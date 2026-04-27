using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public CustomerService(ICustomerRepository customerRepository, IActivityLogRepository activityLogRepository)
    {
        _customerRepository = customerRepository;
        _activityLogRepository = activityLogRepository;
    }

    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        return _customerRepository.GetAllAsync(cancellationToken);
    }

    public Task<Customer?> GetCustomerAsync(int id, CancellationToken cancellationToken = default)
    {
        return _customerRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Customer> SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        if (customer.Id <= 0)
        {
            var created = await _customerRepository.CreateAsync(customer, cancellationToken);
            await AddActivityAsync(ActivityType.CustomerCreated, created.Id, null, null, "新增客户", created.Name, cancellationToken);
            return created;
        }

        await _customerRepository.UpdateAsync(customer, cancellationToken);
        await AddActivityAsync(ActivityType.CustomerUpdated, customer.Id, null, null, "更新客户", customer.Name, cancellationToken);
        return customer;
    }

    public async Task UpdateStatusAsync(int id, CustomerStatus status, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetByIdAsync(id, cancellationToken);
        if (customer is null || customer.Status == status)
        {
            return;
        }

        var oldStatus = customer.Status;
        customer.Status = status;
        await _customerRepository.UpdateAsync(customer, cancellationToken);
        await AddActivityAsync(
            ActivityType.CustomerStatusChanged,
            customer.Id,
            null,
            null,
            "客户状态变更",
            $"{CustomerStatusCatalog.GetLabel(oldStatus)} -> {CustomerStatusCatalog.GetLabel(status)}",
            cancellationToken);
    }

    public Task DeleteCustomerAsync(int id, CancellationToken cancellationToken = default)
    {
        return _customerRepository.SoftDeleteAsync(id, cancellationToken);
    }

    private Task AddActivityAsync(ActivityType type, int? customerId, int? dealId, int? orderId, string title, string description, CancellationToken cancellationToken)
    {
        return _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = type,
            CustomerId = customerId,
            DealId = dealId,
            OrderId = orderId,
            Title = title,
            Description = description,
            Operator = "local"
        }, cancellationToken);
    }
}
