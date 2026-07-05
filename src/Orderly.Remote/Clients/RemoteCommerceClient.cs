using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orderly.Remote.Auth;

namespace Orderly.Remote.Clients;

public sealed class RemoteCommerceClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CloudAuthSession _session;

    public RemoteCommerceClient(string baseUrl, CloudAuthSession session)
    {
        _session = session;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public void UpdateToken(string accessToken)
    {
        _session.AccessToken = accessToken;
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.PutAsJsonAsync(path, request, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
    }

    public async Task<TResponse?> PostAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.PostAsync(path, null, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
    }

    public async Task PostAsync<TRequest>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.PostAsJsonAsync(path, request, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
    }

    public async Task PutAsync<TRequest>(string path, TRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.PutAsJsonAsync(path, request, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var response = await _httpClient.DeleteAsync(path, cancellationToken);
        await EnsureSuccessOrThrowAsync(response);
    }

    private void EnsureAuthorized()
    {
        if (!string.IsNullOrEmpty(_session.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
        }
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw RemoteConflictException.FromResponseContent(content);
        }

        var error = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Remote request failed: {(int)response.StatusCode} {response.StatusCode}. {error}");
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class RemoteConflictException : Exception
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string? Detail { get; }
    public string? ActorDisplayName { get; }
    public DateTime? UpdatedAt { get; }
    public long? LatestRevision { get; }
    public string RawContent { get; }

    public RemoteConflictException(string message) : this(message, null, null, null, null, message) { }

    private RemoteConflictException(
        string message,
        string? detail,
        string? actorDisplayName,
        DateTime? updatedAt,
        long? latestRevision,
        string rawContent)
        : base(message)
    {
        Detail = detail;
        ActorDisplayName = actorDisplayName;
        UpdatedAt = updatedAt;
        LatestRevision = latestRevision;
        RawContent = rawContent;
    }

    public static RemoteConflictException FromResponseContent(string content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ConflictErrorPayload>(content, JsonOptions);
                if (!string.IsNullOrWhiteSpace(payload?.Error))
                {
                    return new RemoteConflictException(
                        payload.Error,
                        payload.Detail,
                        payload.ActorDisplayName,
                        payload.UpdatedAt,
                        payload.LatestRevision,
                        content);
                }
            }
            catch (JsonException)
            {
                // Fall back to a stable user-facing message below.
            }
        }

        return new RemoteConflictException(
            "云端数据已经被其他人更新，你的修改没有覆盖对方内容。请刷新后重新确认。",
            content,
            null,
            null,
            null,
            content);
    }

    private sealed class ConflictErrorPayload
    {
        public string? Error { get; set; }
        public string? Detail { get; set; }
        public string? ActorDisplayName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public long? LatestRevision { get; set; }
    }
}
