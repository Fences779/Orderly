using Orderly.Core.Services;
using System.Net.Http.Headers;

namespace Orderly.Data.Services;

public static class AiSuggestionProviderFactory
{
    public static IAiSuggestionProvider CreatePrimaryProvider(AiProviderOptions options, IAiSuggestionProvider localProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(localProvider);

        return options.RequestedProvider switch
        {
            AiProviderOptions.OpenAiCompatibleProviderName => new OpenAiCompatibleSuggestionProvider(CreateHttpClient(options), options),
            AiProviderOptions.DeepSeekProviderName => new DeepSeekSuggestionProvider(CreateHttpClient(options), options),
            _ => localProvider
        };
    }

    private static HttpClient CreateHttpClient(AiProviderOptions options)
    {
        var client = OutboundHttpClientFactory.Create(TimeSpan.FromSeconds(options.TimeoutSeconds));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
