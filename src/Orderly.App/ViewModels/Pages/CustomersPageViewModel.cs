using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;
using Orderly.Core.Repositories;
using AppPreferences = Orderly.Core.Models.AppPreferences;

namespace Orderly.App.ViewModels.Pages;

/// <summary>
/// One row of the Customers page: a customer joined with its computed RFM
/// (<see cref="CustomerRfmMetrics"/>) figures.
/// </summary>
public sealed record CustomerRow(
    Guid CustomerId,
    string Name,
    string? Phone,
    string PhoneDisplay,
    int? RecencyDays,
    int Frequency,
    string Monetary);

/// <summary>
/// Dedicated ViewModel for the Customers (客户) page. RFM metrics are sourced from
/// <see cref="ICustomerService"/> and customer descriptors from the Commerce
/// <see cref="ICommerceCustomerRepository"/> (Req 6.6, 7.3); no legacy remote service is invoked.
/// </summary>
public sealed partial class CustomersPageViewModel : CommercePageViewModel
{
    private readonly ICustomerService _customerService;
    private readonly ICommerceCustomerRepository _customerRepository;
    private readonly IAppSettingRepository? _settingRepository;

    /// <summary>Creates the Customers page ViewModel over the customer service and customer repository.</summary>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public CustomersPageViewModel(
        ICustomerService customerService,
        ICommerceCustomerRepository customerRepository,
        IAppSettingRepository? settingRepository = null)
    {
        _customerService = customerService ?? throw new ArgumentNullException(nameof(customerService));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _settingRepository = settingRepository;
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionCustomers;

    /// <summary>The active customers with their computed RFM metrics.</summary>
    public ObservableCollection<CustomerRow> Customers { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Customers.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        DateTime asOfUtc = DateTime.UtcNow;

        IReadOnlyList<Customer> customers = await _customerRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(true);
        IReadOnlyList<CustomerRfmMetrics> metrics = await _customerService
            .GetAllMetricsAsync(asOfUtc, cancellationToken)
            .ConfigureAwait(true);
        AppPreferences preferences = await GetPrivacyPreferencesAsync(cancellationToken)
            .ConfigureAwait(true);

        Dictionary<Guid, CustomerRfmMetrics> metricsById = metrics.ToDictionary(m => m.CustomerId);

        Customers.Clear();
        foreach (Customer customer in customers)
        {
            metricsById.TryGetValue(customer.Id, out CustomerRfmMetrics? metric);
            Customers.Add(new CustomerRow(
                customer.Id,
                customer.Name,
                customer.Phone,
                MaskPhoneForDisplay(customer.Phone, preferences.MaskPhoneByDefault),
                metric?.RecencyDays,
                metric?.Frequency ?? 0,
                (metric?.Monetary ?? CommerceMoney.Zero).ToString()));
        }

        NotifyEmptyStateChanged();
    }

    private async Task<AppPreferences> GetPrivacyPreferencesAsync(CancellationToken cancellationToken)
    {
        if (_settingRepository is null)
        {
            return new AppPreferences();
        }

        try
        {
            return await _settingRepository.GetPreferencesAsync(cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            return new AppPreferences();
        }
    }

    private static string MaskPhoneForDisplay(string? phone, bool shouldMask)
    {
        if (string.IsNullOrWhiteSpace(phone) || !shouldMask)
        {
            return phone ?? string.Empty;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return new string('*', digits.Length);
        }

        if (digits.Length <= 7)
        {
            return digits[..2] + new string('*', digits.Length - 2);
        }

        return digits[..3] + new string('*', digits.Length - 7) + digits[^4..];
    }
}
