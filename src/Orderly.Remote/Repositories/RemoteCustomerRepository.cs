using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;
using Orderly.Remote.Services;

namespace Orderly.Remote.Repositories;

public sealed class RemoteCustomerRepository : RemoteCommerceRepositoryBase<Customer, CloudCustomerDto>, ICommerceCustomerRepository
{
    public RemoteCustomerRepository(RemoteCommerceClient client, CloudAuthSession session, ICloudCacheStore? cacheStore = null)
        : base(client, session, cacheStore) { }

    protected override string EntityPath => "customers";
    protected override string CacheEntityType => EntityType.Customer;
    protected override Customer Map(CloudCustomerDto dto) => dto.ToEntity();

    public override async Task<Customer> CreateAsync(Customer entity, CancellationToken cancellationToken = default)
    {
        var command = new CreateCustomerCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = 0L,
            Name = entity.Name,
            Phone = entity.Phone,
            WeChat = entity.WeChat,
            Email = entity.Email
        };

        var dto = await Client.PostAsync<CreateCustomerCommand, CloudCustomerDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/customers",
            command,
            cancellationToken).ConfigureAwait(false);

        return dto?.ToEntity() ?? entity;
    }

    public override async Task UpdateAsync(Customer entity, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudCustomerDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/customers/{entity.Id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new UpdateCustomerCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            CustomerId = entity.Id,
            Name = entity.Name,
            Phone = entity.Phone,
            WeChat = entity.WeChat,
            Email = entity.Email
        };

        await Client.PutAsync<UpdateCustomerCommand, CloudCustomerDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/customers/{entity.Id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await DeleteAsync(id, null, cancellationToken).ConfigureAwait(false);

    public override async Task DeleteAsync(Guid id, string? archiveReason, CancellationToken cancellationToken = default)
    {
        var latest = await Client.GetAsync<CloudCustomerDto>(
            $"api/workspaces/{Session.WorkspaceId:N}/customers/{id:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new ArchiveCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            EntityType = EntityType.Customer,
            EntityId = id,
            ArchiveReason = archiveReason ?? "Remote soft delete"
        };

        await Client.PostAsync<ArchiveCommand>(
            $"api/workspaces/{Session.WorkspaceId:N}/archive/{EntityType.Customer}/{id:N}",
            command,
            cancellationToken).ConfigureAwait(false);
    }
}
