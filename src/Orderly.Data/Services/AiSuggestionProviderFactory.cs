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
            "openai-compatible" => new OpenAiCompatibleSuggestionProvider(CreateHttpClient(options), options),
            _ => localProvider
        };
    }

    private static HttpClient CreateHttpClient(AiProviderOptions options)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
