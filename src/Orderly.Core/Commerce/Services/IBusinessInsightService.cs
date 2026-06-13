namespace Orderly.Core.Commerce.Services;

/// <summary>
/// A reserved, pluggable extension point that contributes additional <see cref="BusinessInsight"/>
/// records to <see cref="IBusinessInsightService"/> (Req 4.15).
///
/// <para><b>Reserved in V1.</b> Mirroring the reserved external connectors (Req 8.3), this provider
/// is a declared extension point that is <i>not</i> wired to any active runtime implementation in
/// V1. No provider is registered by default, so <see cref="IBusinessInsightService"/> generates
/// insights from its built-in deterministic local rules only and never calls any large language
/// model (Req 4.14). The interface exists so future, opt-in providers can be plugged in without
/// changing the service contract; any such provider MUST itself produce insights from deterministic
/// local rules — it is never an entry point for an LLM.</para>
/// </summary>
public interface IBusinessInsightProvider
{
    /// <summary>A neutral, industry-agnostic identifier for the provider.</summary>
    string Name { get; }

    /// <summary>
    /// Produces additional insights as of <paramref name="asOfUtc"/> from the provider's own
    /// deterministic local rules. Implementations MUST be deterministic (identical inputs yield
    /// identical insights) and MUST NOT call any large language model (Req 4.14). Each returned
    /// insight SHOULD carry a stable <see cref="BusinessInsight.BusinessKey"/> so later idempotent
    /// persistence produces no duplicates (Req 4.20, 18.6).
    /// </summary>
    Task<IReadOnlyList<BusinessInsight>> GenerateAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Universal business insight service (Req 4.1, 4.14, 4.15). Aggregates <see cref="BusinessInsight"/>
/// records produced from deterministic local rules across the Universal_Domain_Model — inventory
/// (out-of-stock / low-stock) and cash flow (overdue receivable / payable) — and merges in insights
/// from any registered reserved <see cref="IBusinessInsightProvider"/> extension points.
///
/// <para><b>No LLM dependency (Req 4.14).</b> Every insight is derived from deterministic local rules
/// only; the service never calls a large language model. Because all rules take an explicit
/// <c>asOfUtc</c> instant, results are reproducible with no hidden wall-clock dependency: identical
/// inputs always yield an identical, stably ordered set of insights.</para>
///
/// <para><b>Reserved extension point (Req 4.15).</b> The service exposes the reserved
/// <see cref="IBusinessInsightProvider"/> hook. In V1 no provider is registered, so only the built-in
/// local rules contribute.</para>
///
/// <para>This contract is industry-agnostic and free of any Forbidden_Term.</para>
/// </summary>
public interface IBusinessInsightService
{
    /// <summary>
    /// Generates the aggregated set of business insights as of <paramref name="asOfUtc"/> from the
    /// built-in deterministic local rules plus any registered reserved
    /// <see cref="IBusinessInsightProvider"/> providers (Req 4.14, 4.15). Each insight carries a
    /// stable <see cref="BusinessInsight.BusinessKey"/> so later idempotent persistence produces no
    /// duplicates (Req 4.20, 18.6). The returned list is deterministically ordered and is not
    /// persisted by this slice.
    /// </summary>
    Task<IReadOnlyList<BusinessInsight>> GenerateInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the insights for <paramref name="asOfUtc"/> (see <see cref="GenerateInsightsAsync"/>)
    /// and persists them idempotently by <see cref="BusinessInsight.BusinessKey"/> (Req 4.20, 18.6):
    /// an insight whose Business_Key already exists is reused rather than inserted again, so
    /// re-running insight generation produces no duplicate insight records. The returned list is the
    /// persisted set (existing where a Business_Key matched, newly created otherwise), in the same
    /// deterministic order as <see cref="GenerateInsightsAsync"/>.
    /// </summary>
    Task<IReadOnlyList<BusinessInsight>> PersistInsightsAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}
