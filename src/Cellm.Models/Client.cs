﻿using System.Text.Json;
using Cellm.AddIn.Exceptions;
using Cellm.Models.Anthropic;
using Cellm.Models.Llamafile;
using Cellm.Models.Ollama;
using Cellm.Models.OpenAi;
using Cellm.Models.OpenAiCompatible;
using Cellm.Models.Providers;
using Cellm.Models.Prompts;
using MediatR;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Cellm.Models;

internal class Client(ISender sender, IOptions<ProviderConfiguration> providerConfiguration)
{
    private readonly ProviderConfiguration _providerConfiguration = providerConfiguration.Value;

    public async Task<Prompt> Send(Prompt prompt, string? provider, Uri? baseAddress, CancellationToken cancellationToken)
    {
        try
        {
            provider ??= _providerConfiguration.DefaultProvider;

            if (!Enum.TryParse<Provider>(provider, true, out var parsedProvider))
            {
                throw new ArgumentException($"Unsupported provider: {provider}");
            }

            IModelResponse response = parsedProvider switch
            {
                Provider.Anthropic => await sender.Send(new AnthropicRequest(prompt, provider, baseAddress), cancellationToken),
                Provider.Llamafile => await sender.Send(new LlamafileRequest(prompt), cancellationToken),
                Provider.Ollama => await sender.Send(new OllamaRequest(prompt), cancellationToken),
                Provider.OpenAi => await sender.Send(new OpenAiRequest(prompt), cancellationToken),
                Provider.OpenAiCompatible => await sender.Send(new OpenAiCompatibleRequest(prompt, baseAddress), cancellationToken),
                _ => throw new InvalidOperationException($"Provider {parsedProvider} is defined but not implemented")
            };

            return response.Prompt;
        }
        catch (HttpRequestException ex)
        {
            throw new CellmException($"HTTP request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new CellmException($"JSON processing failed: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new CellmException($"Method not supported: {ex.Message}", ex);
        }
        catch (FileReaderException ex)
        {
            throw new CellmException($"File could not be read: {ex.Message}", ex);
        }
        catch (NullReferenceException ex)
        {
            throw new CellmException($"Null reference error: {ex.Message}", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new CellmException($"Request timed out: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not CellmException)
        {
            // Handle any other unexpected exceptions
            throw new CellmException($"An unexpected error occurred: {ex.Message}", ex);
        }
    }
}