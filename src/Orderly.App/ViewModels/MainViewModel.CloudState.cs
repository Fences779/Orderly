using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Contracts.Offline;

namespace Orderly.App.ViewModels;

public sealed record EmergencyDraftListItem(
    string Id,
    string EntityType,
    string EntityId,
    string OperationType,
    string Status,
    string CreatedAtText,
    string LastSubmitError)
{
    public string Summary => $"{EntityType}/{OperationType} -> {EntityId}";
    public string StatusText => string.IsNullOrWhiteSpace(LastSubmitError)
        ? Status
        : $"{Status}：{LastSubmitError}";
}

public partial class MainViewModel
{
    private IEmergencyDraftSubmitter? _emergencyDraftSubmitter;
    private Dispatcher? _uiDispatcher;

    [ObservableProperty]
    private bool _isCloudOnline = true;

    [ObservableProperty]
    private int _pendingDraftCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudStatusBarText))]
    private bool _isSynchronizingDrafts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloudStatusBarText))]
    private string _cloudSyncStatusText = "缓存可看";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitSelectedEmergencyDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardSelectedEmergencyDraftCommand))]
    private EmergencyDraftListItem? _selectedEmergencyDraft;

    public ObservableCollection<EmergencyDraftListItem> EmergencyDrafts { get; } = new();

    public bool IsCloudMode => _emergencyDraftSubmitter is not null;

    public string EmergencyDraftReviewText => EmergencyDrafts.Count == 0
        ? "暂无待确认草稿。断网时只会保留低风险草稿。"
        : $"待处理 {EmergencyDrafts.Count} 条。选中后提交或放弃。";

    public string CloudStatusBarText
    {
        get
        {
            if (_emergencyDraftSubmitter is null)
            {
                return StatusMessage;
            }

            if (IsSynchronizingDrafts)
            {
                return $"[正在同步 {PendingDraftCount} 条草稿] {StatusMessage}";
            }

            if (!IsCloudOnline || PendingDraftCount > 0)
            {
                var suffix = PendingDraftCount > 0
                    ? $"[离线，{PendingDraftCount} 条草稿待确认]"
                    : "[离线]";
                return $"{suffix} {StatusMessage}";
            }

            if (!string.IsNullOrWhiteSpace(CloudSyncStatusText)
                && !string.Equals(CloudSyncStatusText, "缓存可看", StringComparison.Ordinal))
            {
                return $"[{CloudSyncStatusText}] {StatusMessage}";
            }

            return StatusMessage;
        }
    }

    partial void OnIsCloudOnlineChanged(bool value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnPendingDraftCountChanged(int value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnIsSynchronizingDraftsChanged(bool value) { }

    partial void OnCloudSyncStatusTextChanged(string value) => OnPropertyChanged(nameof(CloudStatusBarText));

    public void SetCloudSyncStatus(string status)
    {
        CloudSyncStatusText = string.IsNullOrWhiteSpace(status) ? "缓存可看" : status;
    }

    public void SetCloudConnectivity(bool isOnline)
    {
        ApplyConnectivity(isOnline);
    }

    public void ConfigureEmergencyDraftSubmitter(IEmergencyDraftSubmitter? submitter)
    {
        if (_emergencyDraftSubmitter is not null)
        {
            _emergencyDraftSubmitter.DraftCountChanged -= OnDraftCountChanged;
            _emergencyDraftSubmitter.ConnectivityChanged -= OnConnectivityChanged;
        }

        _emergencyDraftSubmitter = submitter;
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        if (submitter is null)
        {
            IsCloudOnline = true;
            PendingDraftCount = 0;
            IsSynchronizingDrafts = false;
            OnPropertyChanged(nameof(IsCloudMode));
            EmergencyDrafts.Clear();
            OnPropertyChanged(nameof(EmergencyDraftReviewText));
            return;
        }

        submitter.DraftCountChanged += OnDraftCountChanged;
        submitter.ConnectivityChanged += OnConnectivityChanged;
        OnPropertyChanged(nameof(IsCloudMode));
        _ = RefreshEmergencyDraftsAsync();
    }

    private void OnDraftCountChanged(int count)
    {
        if (_uiDispatcher is null)
        {
            ApplyDraftCount(count);
            return;
        }

        _uiDispatcher.Invoke(() => ApplyDraftCount(count));
    }

    private void ApplyDraftCount(int count)
    {
        PendingDraftCount = count;
        IsSynchronizingDrafts = false;
    }

    private void OnConnectivityChanged(bool isOnline)
    {
        if (_uiDispatcher is null)
        {
            ApplyConnectivity(isOnline);
            return;
        }

        _uiDispatcher.Invoke(() => ApplyConnectivity(isOnline));
    }

    private void ApplyConnectivity(bool isOnline)
    {
        IsCloudOnline = isOnline;
        IsSynchronizingDrafts = false;
    }

    [RelayCommand(CanExecute = nameof(CanUseEmergencyDraftReview))]
    private async Task RefreshEmergencyDraftsAsync()
    {
        if (_emergencyDraftSubmitter is null)
        {
            return;
        }

        var drafts = await _emergencyDraftSubmitter.ListDraftsAsync();
        ApplyEmergencyDrafts(drafts);
    }

    [RelayCommand(CanExecute = nameof(CanOperateSelectedEmergencyDraft))]
    private async Task SubmitSelectedEmergencyDraftAsync()
    {
        var selected = SelectedEmergencyDraft;
        if (selected is null || _emergencyDraftSubmitter is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在提交选中的离线草稿...",
            successMessage: "离线草稿已提交",
            errorTitle: "草稿提交失败",
            errorStatusPrefix: "草稿提交失败",
            action: async () =>
            {
                await _emergencyDraftSubmitter.SubmitDraftAsync(selected.Id);
                var drafts = await _emergencyDraftSubmitter.ListDraftsAsync();
                ApplyEmergencyDrafts(drafts);
            });
    }

    [RelayCommand(CanExecute = nameof(CanOperateSelectedEmergencyDraft))]
    private async Task DiscardSelectedEmergencyDraftAsync()
    {
        var selected = SelectedEmergencyDraft;
        if (selected is null || _emergencyDraftSubmitter is null)
        {
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在放弃选中的离线草稿...",
            successMessage: "离线草稿已放弃",
            errorTitle: "放弃草稿失败",
            errorStatusPrefix: "放弃草稿失败",
            action: async () =>
            {
                await _emergencyDraftSubmitter.DiscardDraftAsync(selected.Id);
                var drafts = await _emergencyDraftSubmitter.ListDraftsAsync();
                ApplyEmergencyDrafts(drafts);
            });
    }

    private bool CanUseEmergencyDraftReview()
    {
        return _emergencyDraftSubmitter is not null && !IsBusy;
    }

    private bool CanOperateSelectedEmergencyDraft()
    {
        return _emergencyDraftSubmitter is not null && SelectedEmergencyDraft is not null && !IsBusy;
    }

    private void ApplyEmergencyDrafts(IReadOnlyList<EmergencyDraftDto> drafts)
    {
        EmergencyDrafts.Clear();
        foreach (var draft in drafts.Where(static draft =>
                     string.Equals(draft.Status, EmergencyDraftStatus.Pending, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(draft.Status, EmergencyDraftStatus.Failed, StringComparison.OrdinalIgnoreCase)))
        {
            EmergencyDrafts.Add(new EmergencyDraftListItem(
                draft.Id,
                draft.EntityType,
                draft.EntityId ?? "-",
                draft.OperationType,
                draft.Status,
                draft.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                draft.LastSubmitError ?? string.Empty));
        }

        if (SelectedEmergencyDraft is not null && EmergencyDrafts.All(draft => draft.Id != SelectedEmergencyDraft.Id))
        {
            SelectedEmergencyDraft = null;
        }

        PendingDraftCount = EmergencyDrafts.Count;
        OnPropertyChanged(nameof(EmergencyDraftReviewText));
        SubmitSelectedEmergencyDraftCommand.NotifyCanExecuteChanged();
        DiscardSelectedEmergencyDraftCommand.NotifyCanExecuteChanged();
    }
}
