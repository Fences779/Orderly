using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Commerce.Services;

namespace Orderly.App.ViewModels.Pages;

/// <summary>One row of the Archive Data page.</summary>
public sealed record ArchiveRow(
    Guid Id,
    string EntityType,
    string Name,
    DateTime? ArchivedAtUtc,
    string? ArchivedByDisplayName,
    string? ArchiveReason,
    long Revision);

/// <summary>
/// Dedicated ViewModel for the Archive Data (归档数据) page. Only administrators can view
/// and recover archived records.
/// </summary>
public sealed partial class ArchivePageViewModel : CommercePageViewModel
{
    private readonly IArchiveService _archiveService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private ArchiveRow? _selectedArchive;

    [ObservableProperty]
    private string _selectedEntityType = "orders";

    public ArchivePageViewModel(IArchiveService archiveService)
    {
        _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
    }

    public override string PageKey => MainViewModel.SectionArchive;

    public ObservableCollection<string> EntityTypes { get; } = new() { "orders", "customers", "products", "inventory", "cashflow", "tasks" };

    public ObservableCollection<ArchiveRow> Archives { get; } = new();

    protected override bool HasNoData => Archives.Count == 0;

    partial void OnSelectedEntityTypeChanged(string value)
    {
        if (IsLoaded)
        {
            _ = LoadAsync();
        }
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchivedEntitySummary> items = await _archiveService
            .ListAsync(SelectedEntityType, cancellationToken)
            .ConfigureAwait(true);

        Archives.Clear();
        foreach (ArchivedEntitySummary item in items)
        {
            Archives.Add(new ArchiveRow(
                item.Id,
                item.EntityType,
                item.Name,
                item.ArchivedAtUtc,
                item.ArchivedByDisplayName,
                item.ArchiveReason,
                item.Revision));
        }

        NotifyEmptyStateChanged();
    }

    private bool CanRecover(ArchiveRow? row) => row is not null;

    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverAsync(ArchiveRow? row)
    {
        var target = row ?? SelectedArchive;
        if (target is null)
            return;

        var result = System.Windows.MessageBox.Show(
            $"确认恢复 {target.EntityType}「{target.Name}」？",
            "恢复归档",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            await _archiveService.RecoverAsync(target.EntityType, target.Id, target.Revision).ConfigureAwait(true);
            Archives.Remove(target);
            NotifyEmptyStateChanged();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"恢复失败：{ex.Message}", "恢复归档", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
