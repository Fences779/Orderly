using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ILocalAuthService
{
    Task<bool> HasAnyAccountAsync(CancellationToken cancellationToken = default);
    Task<LegacyDatabaseMigrationPlan> BuildLegacyMigrationPlanAsync(string ownerAccountId, CancellationToken cancellationToken = default);
    Task<CreateFirstOwnerResult> CreateFirstOwnerAsync(CreateFirstOwnerRequest request, CancellationToken cancellationToken = default);
    Task<LocalSignInResult> SignInAsync(string username, string masterPassword, CancellationToken cancellationToken = default);
    Task<bool> VerifyPinAsync(string accountId, string pin, CancellationToken cancellationToken = default);
    Task<bool> VerifyRecoveryKeyAsync(string accountId, string recoveryKey, CancellationToken cancellationToken = default);
}
