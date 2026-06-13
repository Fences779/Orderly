using System.Globalization;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Repositories;
using Orderly.Core.Commerce.Services;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// Universal business insight service implementation (Req 4.1, 4.14, 4.15). Aggregates
/// <see cref="BusinessInsight"/> records produced from deterministic local rules across the
/// Universal_Domain_Model and merges in insights from any registered reserved
/// <see cref="IBusinessInsightProvider"/> extension points.
///
/// <para><b>Deterministic local rules only (Req 4.14).</b> Built-in insights come from two local
/// rule sets, both evaluated against an explicit <c>asOfUtc</c> instant with no hidden wall-clock
/// dependency:
/// <list type="bullet">
///   <item>Inventory — out-of-stock (critical) and low-stock (warning) items, sourced from
///   <see cref="IInventoryService.GenerateInventoryInsightsAsync"/> so the inventory rules live in a
///   single place.</item>
///   <item>Cash flow — an unsettled receivable or payable whose due date is on or before
///   <c>asOfUtc</c> raises an overdue warning.</item>
/// </list>
/// The service never calls a large language model; identical inputs always yield an identical,
/// stably ordered set of insights.</para>
///
/// <para><b>Reserved provider hook (Req 4.15).</b> Any registered <see cref="IBusinessInsightProvider"/>
/// is invoked and its insights merged in. In V1 the provider set is empty (the reserved hook is not
/// wired to any runtime implementation), so only the built-in local rules contribute.</para>
///
/// <para>This type is industry-agnostic and free of any Forbidden_Term, and reads only through the
/// Commerce repositories and services so the P0_Security_System (C-2) is unaffected.</para>
/// </summary>
public sealed class CommerceBusinessInsightService : IBusinessInsightService
{
    /// <summary>Category label applied to generated cash-flow insights.</summary>
    private const string CashFlowInsightCategory = "现金流";

    private readonly IInventoryService _inventoryService;
    private readonly ICashFlowEntryRepository _cashFlowEntryRepository;
    private readonly IReadOnlyList<IBusinessInsightProvider> _reservedProviders;
    private readonly IBusinessInsightRepository? _insightRepository;

    /// <summary>
    /// Creates the service over the inventory service (for inventory insights), the cash-flow entry
    /// repository (for cash-flow insights), the reserved provider extension point, and the optional
    /// insight repository used by <see cref="PersistInsightsAsync"/>. In V1
    /// <paramref name="reservedProviders"/> is empty; passing <c>null</c> is treated as an empty set.
    /// When <paramref name="insightRepository"/> is omitted the generate-only slice still works, but
    /// <see cref="PersistInsightsAsync"/> then throws because it has nowhere to persist.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inventoryService"/> or <paramref name="cashFlowEntryRepository"/> is null.
    /// </exception>
    public CommerceBusinessInsightService(
        IInventoryService inventoryService,
        ICashFlowEntryRepository cashFlowEntryRepository,
        IEnumerable<IBusinessInsightProvider>? reservedProviders = null,
        IBusinessInsightRepository? insightRepository = null)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _cashFlowEntryRepository = cashFlowEntryRepository ?? throw new ArgumentNullException(nameof(cashFlowEntryRepository));
        _reservedProviders = reservedProviders?.ToArray() ?? Array.Empty<IBusinessInsightProvider>();
        _insightRepository = insightRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BusinessInsight>> GenerateInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        var insights = new List<BusinessInsight>();

        // Rule set 1: inventory insights (out-of-stock / low-stock) from deterministic local rules.
        IReadOnlyList<BusinessInsight> inventoryInsights = await _inventoryService
            .GenerateInventoryInsightsAsync(asOfUtc, cancellationToken)
            .ConfigureAwait(false);
        insights.AddRange(inventoryInsights);

        // Rule set 2: cash-flow insights (overdue receivable / payable) from deterministic local rules.
        insights.AddRange(await GenerateCashFlowInsightsAsync(asOfUtc, cancellationToken).ConfigureAwait(false));

        // Reserved extension point (Req 4.15): merge in any registered provider's insights. Empty in V1.
        foreach (IBusinessInsightProvider provider in _reservedProviders)
        {
            IReadOnlyList<BusinessInsight> providerInsights = await provider
                .GenerateAsync(asOfUtc, cancellationToken)
                .ConfigureAwait(false);
            if (providerInsights is { Count: > 0 })
            {
                insights.AddRange(providerInsights);
            }
        }

        return OrderDeterministically(insights);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BusinessInsight>> PersistInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (_insightRepository is null)
        {
            throw new InvalidOperationException(
                "This CommerceBusinessInsightService was created without the insight repository required to persist insights; supply an IBusinessInsightRepository to the constructor.");
        }

        IReadOnlyList<BusinessInsight> generated =
            await GenerateInsightsAsync(asOfUtc, cancellationToken).ConfigureAwait(false);

        // Persist each insight idempotently by Business_Key (Req 4.20, 18.6): an insight whose
        // Business_Key already exists is reused rather than inserted again, so re-running insight
        // generation produces no duplicate insight records. Insights with no Business_Key are created.
        var persisted = new List<BusinessInsight>(generated.Count);
        foreach (BusinessInsight insight in generated)
        {
            BusinessInsight stored = await BusinessKeyIdempotency
                .CreateIdempotentAsync(_insightRepository, insight, i => i.BusinessKey, cancellationToken)
                .ConfigureAwait(false);
            persisted.Add(stored);
        }

        return persisted;
    }
    /// receivable (income) or payable (expense) entry whose due date is on or before
    /// <paramref name="asOfUtc"/> raises an overdue warning. Settled entries, transfers, and entries
    /// without a due date never raise an insight. Each insight carries a stable business key for
    /// later idempotent persistence (Req 4.20, 18.6).
    /// </summary>
    private async Task<IReadOnlyList<BusinessInsight>> GenerateCashFlowInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CashFlowEntry> entries = await _cashFlowEntryRepository
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        var insights = new List<BusinessInsight>();
        foreach (CashFlowEntry entry in entries)
        {
            if (!IsOverdue(entry, asOfUtc))
            {
                continue;
            }

            // Receivable = income still owed to the business; payable = expense still owed by it.
            bool isReceivable = entry.Direction == CashFlowDirection.Income;
            string keySuffix = isReceivable ? "receivable-overdue" : "payable-overdue";
            string title = isReceivable ? "应收逾期" : "应付逾期";
            string label = isReceivable ? "应收款项" : "应付款项";
            string dueDate = entry.DueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            insights.Add(new BusinessInsight
            {
                WorkspaceId = entry.WorkspaceId,
                Severity = InsightSeverity.Warning,
                Title = title,
                Message = $"{label} {Format(entry.Amount.Amount)} 已于 {dueDate} 到期且尚未结清，请及时跟进。",
                Category = CashFlowInsightCategory,
                GeneratedAt = asOfUtc,
                BusinessKey = $"cashflow:{keySuffix}:{entry.Id}",
            });
        }

        return insights;
    }

    /// <summary>
    /// A cash-flow entry is overdue when it is a receivable or payable (income or expense — never a
    /// transfer), is not fully settled, has a due date, and that due date is on or before the
    /// evaluation instant. The check is purely a function of the entry and <paramref name="asOfUtc"/>,
    /// so it is deterministic and independent of the persisted settlement status's freshness.
    /// </summary>
    private static bool IsOverdue(CashFlowEntry entry, DateTime asOfUtc)
        => entry.Direction != CashFlowDirection.Transfer
            && entry.SettlementStatus != CashFlowSettlementStatus.Settled
            && entry.DueDate is DateTime dueDate
            && dueDate <= asOfUtc;

    /// <summary>
    /// Orders insights deterministically so identical inputs always yield an identical sequence:
    /// most severe first, then by category, business key, title, and message.
    /// </summary>
    private static IReadOnlyList<BusinessInsight> OrderDeterministically(List<BusinessInsight> insights)
        => insights
            .OrderByDescending(insight => (int)insight.Severity)
            .ThenBy(insight => insight.Category, StringComparer.Ordinal)
            .ThenBy(insight => insight.BusinessKey, StringComparer.Ordinal)
            .ThenBy(insight => insight.Title, StringComparer.Ordinal)
            .ThenBy(insight => insight.Message, StringComparer.Ordinal)
            .ToList();

    private static string Format(decimal amount)
        => amount.ToString("0.##", CultureInfo.InvariantCulture);
}
