using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class LoginViewModel
{
    public void UpdateSignInUsernameInput(string username)
    {
        var normalizedUsername = username.Trim();
        SignInAccountErrorMessage = string.Empty;

        if (ConfirmedSignInAccount is not null
            && !string.Equals(ConfirmedSignInAccount.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
        {
            ConfirmedSignInAccount = null;
        }

        ApplySignInAccountFilter(normalizedUsername);
    }

    public bool TryConfirmSignInAccount(string username)
    {
        var normalizedUsername = username.Trim();
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        SignInAccountErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            ConfirmedSignInAccount = null;
            ApplySignInAccountFilter(string.Empty);
            return false;
        }

        var account = _availableSignInAccounts.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            ConfirmedSignInAccount = null;
            SignInAccountErrorMessage = "账号不存在，请检查后重试。";
            ApplySignInAccountFilter(normalizedUsername);
            return false;
        }

        ConfirmedSignInAccount = account;
        ReplaceFilteredSignInAccounts([]);
        return true;
    }

    private void LoadSignInAccounts(IEnumerable<LocalAccountSummary> accounts)
    {
        _availableSignInAccounts.Clear();
        _availableSignInAccounts.AddRange(accounts
            .Where(account => account.IsEnabled)
            .OrderByDescending(account => account.LastLoginAt ?? DateTime.MinValue)
            .ThenBy(account => account.CreatedAt));

        ApplySignInAccountFilter(string.Empty);
    }

    private void ApplySignInAccountFilter(string username)
    {
        if (ConfirmedSignInAccount is not null)
        {
            ReplaceFilteredSignInAccounts([]);
            return;
        }

        var candidates = string.IsNullOrWhiteSpace(username)
            ? _availableSignInAccounts
            : _availableSignInAccounts
                .Where(account => account.Username.StartsWith(username, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ReplaceFilteredSignInAccounts(candidates);
    }

    private void ReplaceFilteredSignInAccounts(IEnumerable<LocalAccountSummary> accounts)
    {
        FilteredSignInAccounts.Clear();
        foreach (var account in accounts)
        {
            FilteredSignInAccounts.Add(account);
        }

        OnPropertyChanged(nameof(HasFilteredSignInAccounts));
    }

    private void ResetSignInAccountState()
    {
        SignInAccountErrorMessage = string.Empty;
        ConfirmedSignInAccount = null;
        ApplySignInAccountFilter(string.Empty);
    }
}
