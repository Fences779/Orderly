using Orderly.Contracts.Permissions;
using Orderly.Server.Models;
using Orderly.Server.Services;
using Xunit;

namespace Orderly.Tests.Server;

public sealed class CloudPermissionServiceTests
{
    private readonly CloudPermissionService _permissions = new();

    [Fact]
    public void Admin_can_view_costs_export_manage_users_and_archive_any_entity()
    {
        var admin = Member(CloudRole.Admin);

        Assert.True(_permissions.IsAdmin(admin));
        Assert.False(_permissions.IsEmployee(admin));
        Assert.True(_permissions.CanViewCosts(admin));
        Assert.True(_permissions.CanExport(admin));
        Assert.True(_permissions.CanManageUsers(admin));
        Assert.True(_permissions.CanManageCashFlow(admin));
        Assert.True(_permissions.CanApprovePriceChange(admin));
        Assert.True(_permissions.CanRecordInventoryMovement(admin));
        Assert.True(_permissions.CanArchive(admin, EntityType.Order, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Employee_cannot_view_costs_or_export_or_manage_users()
    {
        var employee = Member(CloudRole.Employee);

        Assert.False(_permissions.IsAdmin(employee));
        Assert.True(_permissions.IsEmployee(employee));
        Assert.False(_permissions.CanViewCosts(employee));
        Assert.False(_permissions.CanExport(employee));
        Assert.False(_permissions.CanManageUsers(employee));
        Assert.False(_permissions.CanManageCashFlow(employee));
        Assert.False(_permissions.CanApprovePriceChange(employee));
        Assert.True(_permissions.CanRecordInventoryMovement(employee));
    }

    [Fact]
    public void Admin_with_staff_label_cannot_manage_users()
    {
        var admin = Member(CloudRole.Admin, businessLabel: BusinessLabel.Staff);

        Assert.True(_permissions.IsAdmin(admin));
        Assert.False(_permissions.CanManageUsers(admin));
    }

    [Theory]
    [InlineData(EntityType.Order)]
    [InlineData(EntityType.Customer)]
    [InlineData(EntityType.BusinessTask)]
    public void Employee_can_archive_own_or_assigned_entities(string entityType)
    {
        var employee = Member(CloudRole.Employee);

        Assert.True(_permissions.CanArchive(employee, entityType, employee.UserId, null));
        Assert.True(_permissions.CanArchive(employee, entityType, Guid.NewGuid(), employee.UserId));
    }

    [Theory]
    [InlineData(EntityType.Order)]
    [InlineData(EntityType.Customer)]
    [InlineData(EntityType.BusinessTask)]
    public void Employee_cannot_archive_others_entities(string entityType)
    {
        var employee = Member(CloudRole.Employee);
        var other = Guid.NewGuid();

        Assert.False(_permissions.CanArchive(employee, entityType, other, null));
        Assert.False(_permissions.CanArchive(employee, entityType, other, other));
    }

    [Theory]
    [InlineData(EntityType.Product)]
    [InlineData(EntityType.InventoryItem)]
    [InlineData(EntityType.CashFlowEntry)]
    public void Employee_cannot_archive_non_ownable_entity_types(string entityType)
    {
        var employee = Member(CloudRole.Employee);

        Assert.False(_permissions.CanArchive(employee, entityType, employee.UserId, null));
    }

    [Fact]
    public void Null_membership_denies_everything()
    {
        CloudWorkspaceMemberRecord? membership = null;

        Assert.False(_permissions.IsAdmin(membership!));
        Assert.False(_permissions.IsEmployee(membership!));
        Assert.False(_permissions.CanViewCosts(membership!));
        Assert.False(_permissions.CanExport(membership!));
        Assert.False(_permissions.CanArchive(membership!, EntityType.Order, Guid.NewGuid(), null));
    }

    private static CloudWorkspaceMemberRecord Member(string role, Guid? userId = null, string businessLabel = BusinessLabel.Operator) => new()
    {
        UserId = userId ?? Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        CloudRole = role,
        BusinessLabel = businessLabel,
        IsEnabled = true
    };
}
