using Orderly.Contracts.Permissions;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface ICloudPermissionService
{
    bool IsAdmin(CloudWorkspaceMemberRecord membership);
    bool IsEmployee(CloudWorkspaceMemberRecord membership);
    bool CanViewCosts(CloudWorkspaceMemberRecord membership);
    bool CanExport(CloudWorkspaceMemberRecord membership);
    bool CanManageUsers(CloudWorkspaceMemberRecord membership);
    bool CanArchive(CloudWorkspaceMemberRecord membership, string entityType, Guid? createdByUserId, Guid? assignedToUserId);
    bool CanManageCashFlow(CloudWorkspaceMemberRecord membership);
    bool CanApprovePriceChange(CloudWorkspaceMemberRecord membership);
    bool CanRecordInventoryMovement(CloudWorkspaceMemberRecord membership);
}

public sealed class CloudPermissionService : ICloudPermissionService
{
    public bool IsAdmin(CloudWorkspaceMemberRecord membership) =>
        membership != null && string.Equals(membership.CloudRole, CloudRole.Admin, StringComparison.OrdinalIgnoreCase);

    public bool IsEmployee(CloudWorkspaceMemberRecord membership) =>
        membership != null && string.Equals(membership.CloudRole, CloudRole.Employee, StringComparison.OrdinalIgnoreCase);

    public bool CanViewCosts(CloudWorkspaceMemberRecord membership) => IsAdmin(membership);
    public bool CanExport(CloudWorkspaceMemberRecord membership) => IsAdmin(membership);
    public bool CanManageUsers(CloudWorkspaceMemberRecord membership) => IsCloudAdministrator(membership);
    public bool CanManageCashFlow(CloudWorkspaceMemberRecord membership) => IsAdmin(membership);
    public bool CanApprovePriceChange(CloudWorkspaceMemberRecord membership) => IsAdmin(membership);
    public bool CanRecordInventoryMovement(CloudWorkspaceMemberRecord membership) => IsAdmin(membership) || IsEmployee(membership);

    private bool IsCloudAdministrator(CloudWorkspaceMemberRecord membership) =>
        IsAdmin(membership)
        && (string.Equals(membership.BusinessLabel, BusinessLabel.Operator, StringComparison.Ordinal)
            || string.Equals(membership.BusinessLabel, BusinessLabel.Investor, StringComparison.Ordinal));

    public bool CanArchive(CloudWorkspaceMemberRecord membership, string entityType, Guid? createdByUserId, Guid? assignedToUserId)
    {
        if (membership == null) return false;
        if (IsAdmin(membership)) return true;
        if (!IsEmployee(membership)) return false;

        if (!string.Equals(entityType, EntityType.Order, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entityType, EntityType.Customer, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entityType, EntityType.BusinessTask, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (membership.UserId == createdByUserId) return true;
        if (assignedToUserId.HasValue && membership.UserId == assignedToUserId.Value) return true;
        return false;
    }
}
