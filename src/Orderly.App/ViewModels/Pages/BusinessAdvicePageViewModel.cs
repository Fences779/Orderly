using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>One row of the Business Advice page, projected from a generated <see cref="BusinessInsight"/>.</summary>
public sealed record BusinessInsightRow(
    Guid Id,
    InsightSeverity Severity,
    string Title,
    string Message,
    string? Category);

/// <summary>
/// Dedicated ViewModel for the Business Advice (经营建议) page. Insights are sourced exclusively from
/// <see cref="IBusinessInsightService"/>, which derives them from deterministic local rules over the
/// Commerce repositories (Req 6.8, 7.3); no legacy remote service or LLM is invoked.
/// </summary>
public sealed partial class BusinessAdvicePageViewModel : CommercePageViewModel
{
    private readonly IBusinessInsightService _insightService;

    /// <summary>Creates the Business Advice page ViewModel over the business insight service.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="insightService"/> is null.</exception>
    public BusinessAdvicePageViewModel(IBusinessInsightService insightService)
    {
        _insightService = insightService ?? throw new ArgumentNullException(nameof(insightService));
    }

    /// <inheritdoc />
    public override string PageKey => MainViewModel.SectionBusinessAdvice;

    /// <summary>The generated business insights displayed on the page.</summary>
    public ObservableCollection<BusinessInsightRow> Insights { get; } = new();

    /// <inheritdoc />
    protected override bool HasNoData => Insights.Count == 0;

    /// <inheritdoc />
    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BusinessInsight> insights = await _insightService
            .GenerateInsightsAsync(DateTime.UtcNow, cancellationToken)
            .ConfigureAwait(true);

        Insights.Clear();
        foreach (BusinessInsight insight in insights)
        {
            Insights.Add(new BusinessInsightRow(
                insight.Id,
                insight.Severity,
                insight.Title,
                insight.Message,
                insight.Category));
        }

        NotifyEmptyStateChanged();
    }
}
