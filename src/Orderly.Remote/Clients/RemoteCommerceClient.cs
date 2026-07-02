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
            throw new RemoteConflictException(content);
        }

        var error = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Remote request failed: {(int)response.StatusCode} {response.StatusCode}. {error}");
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class RemoteConflictException : Exception
{
    public RemoteConflictException(string message) : base(message) { }
}
