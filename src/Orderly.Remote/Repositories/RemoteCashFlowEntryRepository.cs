using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteCashFlowEntryRepository : RemoteCommerceRepositoryBase<CashFlowEntry, CloudCashFlowEntryDto>, ICashFlowEntryRepository
{
    public RemoteCashFlowEntryRepository(RemoteCommerceClient client, CloudAuthSession session) : base(client, session) { }

    protected override string EntityPath => "cashflow/entries";
    protected override CashFlowEntry Map(CloudCashFlowEntryDto dto) => dto.ToEntity();

    public override async Task<CashFlowEntry> CreateAsync(CashFlowEntry entity, CancellationToken cancellationToken = default)
    {
        var (kind, direction) = ResolveKind(entity);

        var command = new CashFlowEntryCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            Direction = direction,
            Amount = entity.Amount.Amount,
            OccurredAtUtc = entity.OccurredAt,
            DueDateUtc = entity.DueDate,
            CategoryName = entity.CategoryName ?? string.Empty,
            OrderId = entity.OrderId,
            BusinessKey = entity.BusinessKey
        };

        var dto = await Client.PostAsync<CashFlowEntryCommand, CloudCashFlowEntryDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/cashflow/{kind}",
            command,
            cancellationToken).ConfigureAwait(false);

        return dto?.ToEntity() ?? entity;
    }

    public override Task UpdateAsync(CashFlowEntry entity, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Cloud cash-flow entries cannot be updated directly; use settlement or archive instead.");

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudCashFlowEntryDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/cashflow/entries/{id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new ArchiveCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            EntityType = EntityType.CashFlowEntry,
            EntityId = id,
            ArchiveReason = "Remote soft delete"
        };

        await Client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/archive/{EntityType.CashFlowEntry}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    private static (string Kind, CashFlowDirection Direction) ResolveKind(CashFlowEntry entry)
    {
        if (entry.Direction == CashFlowDirection.Income)
        {
            return entry.SettlementStatus == CashFlowSettlementStatus.Settled
                ? ("income", CashFlowDirection.Income)
                : ("receivable", CashFlowDirection.Income);
        }

        if (entry.Direction == CashFlowDirection.Expense)
        {
            return entry.SettlementStatus == CashFlowSettlementStatus.Settled
                ? ("expense", CashFlowDirection.Expense)
                : ("payable", CashFlowDirection.Expense);
        }

        throw new NotSupportedException("Cloud cash-flow entries only support Income and Expense directions.");
    }
}
