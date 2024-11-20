using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cellm.AddIn;
using Cellm.AddIn.Exceptions;
using Cellm.Models.OpenAi.Models;
using Cellm.Prompts;
using Cellm.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Cellm.Models.OpenAi;

internal class OpenAiRequestHandler : IModelRequestHandler<OpenAiRequest, OpenAiResponse>
{
    private readonly OpenAiConfiguration _openAiConfiguration;
    private readonly CellmConfiguration _cellmConfiguration;
    private readonly HttpClient _httpClient;
    private readonly ToolRunner _toolRunner;
    private readonly Serde _serde;

    public OpenAiRequestHandler(
        IOptions<OpenAiConfiguration> openAiConfiguration,
        IOptions<CellmConfiguration> cellmConfiguration,
        HttpClient httpClient,
        ToolRunner toolRunner,
        Serde serde)
    {
        _openAiConfiguration = openAiConfiguration.Value;
        _cellmConfiguration = cellmConfiguration.Value;
        _httpClient = httpClient;
        _toolRunner = toolRunner;
        _serde = serde;
    }

    public async Task<OpenAiResponse> Handle(OpenAiRequest request, CancellationToken cancellationToken)
    {
        const string path = "/v1/chat/completions";
        var address = request.BaseAddress is null ? new Uri(path, UriKind.Relative) : new Uri(request.BaseAddress, path);

        // Must instantiate manually because injected OpenAIClient does not allow setting endpoint per-call
        var openAiClientCredentials = new ApiKeyCredential(_openAiConfiguration.ApiKey);
        var openAiClientOptions = new OpenAIClientOptions { 
            Transport = new HttpClientPipelineTransport(_httpClient), 
            Endpoint = address };

        var chatClient = new OpenAIClient(openAiClientCredentials, openAiClientOptions)
            .AsChatClient(request.Prompt.Options.ModelId ?? throw new CellmException($"{nameof(request.Prompt.Options.ModelId)} was null"));
        var chatCompletion = await chatClient.CompleteAsync(request.Prompt.Messages, request.Prompt.Options, cancellationToken);

        // Do not mutate request.Prompt
        var messages = new List<ChatMessage>(request.Prompt.Messages) { chatCompletion.Message };
        var options = request.Prompt.Options.Clone();

        return new OpenAiResponse(new Prompt(messages, options));
    }

    public string Serialize(OpenAiRequest request)
    {
        var openAiPrompt = new PromptBuilder(request.Prompt)
            .AddSystemMessage()
            .Build();

        var chatCompletionRequest = new OpenAiChatCompletionRequest(
            openAiPrompt.Model,
            openAiPrompt.ToOpenAiMessages(),
            _cellmConfiguration.MaxOutputTokens,
            openAiPrompt.Temperature,
            _toolRunner.ToOpenAiTools(),
            "auto");

        return _serde.Serialize(chatCompletionRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    public OpenAiResponse Deserialize(OpenAiRequest request, string responseBodyAsString)
    {
        var responseBody = _serde.Deserialize<OpenAiChatCompletionResponse>(responseBodyAsString, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var choice = responseBody?.Choices?.FirstOrDefault() ?? throw new CellmException("Empty response from OpenAI API");
        var toolCalls = choice.Message.ToolCalls?
            .Select(x => new ToolCall(x.Id, x.Function.Name, x.Function.Arguments, null))
            .ToList();

        var content = choice.Message.Content;
        var message = new Message(content, Roles.Assistant, toolCalls);

        var prompt = new PromptBuilder(request.Prompt)
            .AddMessage(message)
            .Build();

        return new OpenAiResponse(prompt);
    }
}
