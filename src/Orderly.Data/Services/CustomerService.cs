using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class CustomerService : ICustomerService
{
    private const int MaxCustomerNameCharacters = 80;
    private const int MaxShortFieldCharacters = 80;
    private const int MaxContactHandleCharacters = 120;
    private const int MaxPhoneCharacters = 40;
    private const int MaxRemarkCharacters = 1000;
    private const int MaxExternalIdCharacters = 160;
    private const int MaxRawPayloadCharacters = 4096;

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
        ArgumentNullException.ThrowIfNull(customer);

        NormalizeCustomer(customer);
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

    private static void NormalizeCustomer(Customer customer)
    {
        if (!Enum.IsDefined(customer.Status))
        {
            throw new InvalidOperationException("客户状态无效。");
        }

        if (!Enum.IsDefined(customer.Priority))
        {
            throw new InvalidOperationException("客户优先级无效。");
        }

        customer.Name = NormalizeRequiredText(customer.Name, MaxCustomerNameCharacters, "客户名称");
        customer.SourcePlatform = NormalizeOptionalText(customer.SourcePlatform, MaxShortFieldCharacters, "客户来源平台");
        customer.Channel = NormalizeOptionalText(customer.Channel, MaxShortFieldCharacters, "客户渠道");
        customer.ContactHandle = NormalizeOptionalText(customer.ContactHandle, MaxContactHandleCharacters, "客户联系方式");
        customer.Phone = NormalizeOptionalText(customer.Phone, MaxPhoneCharacters, "客户手机号");
        customer.Remark = NormalizeOptionalText(customer.Remark, MaxRemarkCharacters, "客户备注");
        customer.ExternalId = NormalizeOptionalText(customer.ExternalId, MaxExternalIdCharacters, "客户外部标识");
        customer.RawPayload = NormalizeOptionalText(customer.RawPayload, MaxRawPayloadCharacters, "客户原始载荷");
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }
}
