using Orderly.Core.Models;
using Orderly.Core.Services;
using System.Text;
using System.Text.Json;

namespace Orderly.Data.Services;

public sealed class OpenAiCompatibleSuggestionProvider : IAiSuggestionProvider
{
    private const long MaxResponseBodyBytes = 2L * 1024L * 1024L;

    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public OpenAiCompatibleSuggestionProvider(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "openai-compatible";

    public async Task<AiSuggestionProviderResult> GenerateAsync(AiSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var endpoint = ChatCompletionSuggestionSupport.BuildChatCompletionsEndpoint(
            _options.BaseUrl,
            "ORDERLY_AI_BASE_URL is not a valid absolute URL.",
            "ORDERLY_AI_BASE_URL must use HTTPS unless it targets a loopback address.",
            AiProviderOptions.AllowedHostsEnvironmentVariableName);
        var payload = new
        {
            model = _options.Model,
            messages = ChatCompletionSuggestionSupport.BuildMessages(request),
            temperature = 0.5,
            max_tokens = 400
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ChatCompletionSuggestionSupport.ApplyBearerToken(httpRequest, _options.ApiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI-compatible provider returned HTTP {(int)response.StatusCode}.");
        }

        var body = await HttpContentSafety.ReadAsStringWithLimitAsync(
            response.Content,
            MaxResponseBodyBytes,
            "OpenAI-compatible provider",
            cancellationToken);
        var suggestionText = ChatCompletionSuggestionSupport.ExtractAssistantContent(body, "OpenAI-compatible provider");
        if (string.IsNullOrWhiteSpace(suggestionText))
        {
            throw new InvalidOperationException("OpenAI-compatible provider returned empty assistant content.");
        }

        return new AiSuggestionProviderResult
        {
            Provider = Name,
            Model = _options.Model,
            SuggestionText = suggestionText,
            MetadataJson = JsonSerializer.Serialize(new
            {
                timeoutSeconds = _options.TimeoutSeconds
            })
        };
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("ORDERLY_AI_BASE_URL / ORDERLY_AI_API_KEY / ORDERLY_AI_MODEL is required for openai-compatible provider.");
        }
    }
}
