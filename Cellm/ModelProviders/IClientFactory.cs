﻿namespace Cellm.ModelProviders;

public interface IClientFactory
{
    IClient GetClient(string clientName);
}


public class ClientFactory : IClientFactory
{
    public IClient GetClient(string modelProvider)
    { 

        return modelProvider switch
        {
            nameof(AnthropicClient) => ServiceLocator.Get<AnthropicClient>(),
            nameof(OpenAiClient) => ServiceLocator.Get<OpenAiClient>(),
            _ => throw new ArgumentException($"Unsupported client type: {modelProvider}")
        };
    }
}