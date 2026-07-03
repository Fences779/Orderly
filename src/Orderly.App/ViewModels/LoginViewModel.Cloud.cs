using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Contracts.Auth;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;
using Orderly.Remote.Clients;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly RemoteAuthClient? _cloudAuthClient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalLoginMode))]
    [NotifyPropertyChangedFor(nameof(IsCloudLoginSectionVisible))]
    private bool _isCloudLoginMode;

    [ObservableProperty]
    private bool _isCloudLoginBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudLoginErrorMessage))]
    private string _cloudLoginErrorMessage = string.Empty;

    [ObservableProperty]
    private string _cloudBaseUrl = string.Empty;

    [ObservableProperty]
    private string _cloudUsername = string.Empty;

    public bool IsCloudLoginAvailable => _cloudAuthClient is not null;

    public bool IsLocalLoginMode => !IsCloudLoginMode;

    public bool IsCloudLoginSectionVisible => IsCloudLoginMode;

    public bool HasCloudLoginErrorMessage => !string.IsNullOrWhiteSpace(CloudLoginErrorMessage);

    public async Task SignInWithCloudAsync(string password, CancellationToken cancellationToken = default)
    {
        CloudLoginErrorMessage = string.Empty;

        if (_cloudAuthClient is null)
        {
            CloudLoginErrorMessage = "云登录未启用。";
            return;
        }

        if (string.IsNullOrWhiteSpace(CloudUsername) || string.IsNullOrWhiteSpace(password))
        {
            CloudLoginErrorMessage = "请输入云账号和密码。";
            return;
        }

        if (string.IsNullOrWhiteSpace(CloudBaseUrl))
        {
            CloudLoginErrorMessage = "请输入云服务器地址。";
            return;
        }

        IsCloudLoginBusy = true;
        try
        {
            var authenticated = await _cloudAuthClient.LoginAsync(CloudUsername.Trim(), password, cancellationToken);
            if (!authenticated)
            {
                CloudLoginErrorMessage = "云账号或密码错误。";
                return;
            }

            var session = _cloudAuthClient.Session;
            if (session.User.Id == Guid.Empty || session.WorkspaceId == Guid.Empty)
            {
                CloudLoginErrorMessage = "云登录返回的数据不完整。";
                return;
            }

            var dataKey = await _cloudAuthClient.GetOrCreateDataKeyAsync(session.WorkspaceId, cancellationToken);
            var localSession = CreateLocalSessionFromCloudSession(session.User, session.WorkspaceId, session.Role, dataKey);
            LoginSucceeded?.Invoke(localSession);
        }
        catch (HttpRequestException ex)
        {
            CloudLoginErrorMessage = $"无法连接到云服务器：{ex.Message}";
        }
        catch (Exception ex)
        {
            CloudLoginErrorMessage = $"云登录失败：{ex.Message}";
        }
        finally
        {
            IsCloudLoginBusy = false;
        }
    }

    public async Task<bool> InitializeCloudLoginAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudAuthClient is null)
        {
            return false;
        }

        IsCloudLoginMode = true;
        IsFirstRunMode = false;
        CloudLoginErrorMessage = string.Empty;

        try
        {
            var silent = await _cloudAuthClient.TrySilentSignInAsync(cancellationToken);
            if (!silent)
            {
                return false;
            }

            var session = _cloudAuthClient.Session;
            var dataKey = await _cloudAuthClient.GetOrCreateDataKeyAsync(session.WorkspaceId, cancellationToken);
            var localSession = CreateLocalSessionFromCloudSession(session.User, session.WorkspaceId, session.Role, dataKey);
            LoginSucceeded?.Invoke(localSession);
            return true;
        }
        catch
        {
            // Silent sign-in failure is not fatal; stay on the cloud login form.
            return false;
        }
    }

    private static LocalSessionContext CreateLocalSessionFromCloudSession(
        CloudUserDto user,
        Guid workspaceId,
        string cloudRole,
        byte[] dataKey)
    {
        return new LocalSessionContext
        {
            AccountId = user.Id.ToString("N"),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = MapCloudRole(cloudRole),
            DatabasePath = DatabasePaths.GetAccountDatabasePath(workspaceId.ToString("N")),
            DataKey = dataKey,
            SignedInAt = DateTime.Now
        };
    }

    private static LocalAccountRole MapCloudRole(string cloudRole)
    {
        if (string.Equals(cloudRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return LocalAccountRole.Owner;
        }

        return LocalAccountRole.Member;
    }
}
