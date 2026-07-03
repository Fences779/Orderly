using System.Net;
using System.Net.Http.Json;
using Orderly.Contracts.Auth;
using Orderly.Remote.Auth;

namespace Orderly.Remote.Clients;

/// <summary>
/// Client-side cloud authentication gateway. Handles login, silent refresh, logout,
/// and secure local storage of refresh tokens via <see cref="ICloudTokenStorage"/>.
/// All server communication is over HTTPS and never touches the local SQLCipher database.
/// </summary>
public sealed class RemoteAuthClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly CloudAuthSession _session;
    private readonly ICloudTokenStorage _tokenStorage;

    public RemoteAuthClient(string baseUrl, CloudAuthSession session, ICloudTokenStorage tokenStorage)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public CloudAuthSession Session => _session;

    /// <summary>
    /// Attempts to sign in with username/password. On success populates <see cref="Session"/>
    /// and persists the refresh token locally. Returns <c>true</c> when authenticated.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("用户名不能为空", nameof(username));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("密码不能为空", nameof(password));

        var request = new LoginRequest
        {
            Username = username.Trim(),
            Password = password,
            ClientRequestId = Guid.NewGuid().ToString("N")
        };

        var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }

        await EnsureSuccessOrThrowAsync(response);
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(ct).ConfigureAwait(false);
        if (loginResponse is null)
        {
            throw new InvalidOperationException("登录接口返回空响应。");
        }

        ApplyLoginResponse(loginResponse);
        await SaveRefreshTokenAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Attempts a silent sign-in using a previously stored refresh token.
    /// Returns <c>true</c> when a new access token was obtained.
    /// </summary>
    public async Task<bool> TrySilentSignInAsync(CancellationToken ct = default)
    {
        var workspaceId = _session.WorkspaceId;
        string? refreshToken = null;

        if (workspaceId != Guid.Empty)
        {
            refreshToken = await _tokenStorage.LoadAsync(GetRefreshTokenKey(workspaceId), ct).ConfigureAwait(false);
        }

        refreshToken ??= await _tokenStorage.LoadAsync(DefaultRefreshTokenKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        return await RefreshAsync(refreshToken, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the access token using the supplied refresh token. The new tokens are stored locally.
    /// </summary>
    public async Task<bool> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var request = new RefreshRequest
        {
            RefreshToken = refreshToken,
            ClientRequestId = Guid.NewGuid().ToString("N")
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/refresh", request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await ClearStoredTokensAsync(ct).ConfigureAwait(false);
                return false;
            }

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(ct).ConfigureAwait(false);
            if (loginResponse is null)
            {
                return false;
            }

            ApplyLoginResponse(loginResponse);
            await SaveRefreshTokenAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException)
        {
            // Network failure during refresh is not a logout; caller may retry later.
            return false;
        }
    }

    /// <summary>
    /// Clears the server-side refresh token (best effort) and removes locally stored tokens.
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_session.AccessToken))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
                await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort logout; local tokens are cleared regardless.
        }
        finally
        {
            await ClearStoredTokensAsync(ct).ConfigureAwait(false);
            ClearSession();
        }
    }

    /// <summary>
    /// Loads the locally cached workspace data key, or generates and stores a new 256-bit key.
    /// The data key is used for the SQLCipher local cache database when running in Cloud mode.
    /// </summary>
    public async Task<byte[]> GetOrCreateDataKeyAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var key = await _tokenStorage.LoadAsync(GetDataKeyKey(workspaceId), ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(key))
        {
            try
            {
                var existing = Convert.FromBase64String(key);
                if (existing.Length == 32)
                {
                    return existing;
                }
            }
            catch (FormatException)
            {
                // Treat as missing and generate a new key below.
            }
        }

        var newKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await _tokenStorage.SaveAsync(GetDataKeyKey(workspaceId), Convert.ToBase64String(newKey), ct).ConfigureAwait(false);
        return newKey;
    }

    public void Dispose() => _httpClient.Dispose();

    private void ApplyLoginResponse(LoginResponse response)
    {
        _session.AccessToken = response.AccessToken;
        _session.RefreshToken = response.RefreshToken;
        _session.User = response.User;
        _session.WorkspaceMembership = response.WorkspaceMembership;
        _session.ServerTimeUtc = response.ServerTimeUtc;
        _session.TokenAcquiredAtUtc = DateTime.UtcNow;
    }

    private async Task SaveRefreshTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_session.RefreshToken))
        {
            return;
        }

        await _tokenStorage.SaveAsync(DefaultRefreshTokenKey, _session.RefreshToken, ct).ConfigureAwait(false);
        if (_session.WorkspaceId != Guid.Empty)
        {
            await _tokenStorage.SaveAsync(GetRefreshTokenKey(_session.WorkspaceId), _session.RefreshToken, ct).ConfigureAwait(false);
        }
    }

    private async Task ClearStoredTokensAsync(CancellationToken ct)
    {
        await _tokenStorage.DeleteAsync(DefaultRefreshTokenKey, ct).ConfigureAwait(false);
        if (_session.WorkspaceId != Guid.Empty)
        {
            await _tokenStorage.DeleteAsync(GetRefreshTokenKey(_session.WorkspaceId), ct).ConfigureAwait(false);
            await _tokenStorage.DeleteAsync(GetDataKeyKey(_session.WorkspaceId), ct).ConfigureAwait(false);
        }
    }

    private void ClearSession()
    {
        _session.AccessToken = string.Empty;
        _session.RefreshToken = string.Empty;
        _session.User = new CloudUserDto();
        _session.WorkspaceMembership = new CloudWorkspaceMembershipDto();
        _session.ServerTimeUtc = DateTime.MinValue;
        _session.TokenAcquiredAtUtc = DateTime.MinValue;
    }

    private const string DefaultRefreshTokenKey = "refresh-default";
    private static string GetRefreshTokenKey(Guid workspaceId) => $"refresh-{workspaceId:N}";
    private static string GetDataKeyKey(Guid workspaceId) => $"datakey-{workspaceId:N}";

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"Remote auth request failed: {(int)response.StatusCode} {response.StatusCode}. {content}");
    }
}
