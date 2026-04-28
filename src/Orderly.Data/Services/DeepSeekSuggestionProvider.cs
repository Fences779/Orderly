using Orderly.Core.Models;
using Orderly.Core.Services;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class DeepSeekSuggestionProvider : IAiSuggestionProvider
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public DeepSeekSuggestionProvider(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => AiProviderOptions.DeepSeekProviderName;

    public async Task<AiSuggestionProviderResult> GenerateAsync(AiSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var endpoint = ChatCompletionSuggestionSupport.BuildChatCompletionsEndpoint(_options.BaseUrl);
        var payload = new
        {
            model = _options.Model,
            messages = ChatCompletionSuggestionSupport.BuildMessages(request),
            stream = false,
            temperature = 0.5,
            max_tokens = 400
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ChatCompletionSuggestionSupport.ApplyBearerToken(httpRequest, _options.ApiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"DeepSeek provider returned HTTP {(int)response.StatusCode}: {ChatCompletionSuggestionSupport.BuildErrorSnippet(body)}");
        }

        var suggestionText = ChatCompletionSuggestionSupport.ExtractAssistantContent(body, "DeepSeek provider");
        if (string.IsNullOrWhiteSpace(suggestionText))
        {
            throw new InvalidOperationException("DeepSeek provider returned empty assistant content.");
        }

        return new AiSuggestionProviderResult
        {
            Provider = Name,
            Model = _options.Model,
            SuggestionText = suggestionText,
            MetadataJson = JsonSerializer.Serialize(new
            {
                endpoint = endpoint.AbsoluteUri,
                timeoutSeconds = _options.TimeoutSeconds,
                stream = false
            })
        };
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException($"{AiProviderOptions.DeepSeekApiKeyEnvironmentVariableName} is required for deepseek provider.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("DeepSeek model configuration is empty.");
        }
    }
}
