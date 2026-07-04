using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Contracts.Offline;

namespace Orderly.App.ViewModels;

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

    public bool IsCloudMode => _emergencyDraftSubmitter is not null;

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
                    ? $"[离线，{PendingDraftCount} 条草稿待同步]"
                    : "[离线]";
                return $"{suffix} {StatusMessage}";
            }

            return StatusMessage;
        }
    }

    partial void OnIsCloudOnlineChanged(bool value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnPendingDraftCountChanged(int value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(CloudStatusBarText));

    partial void OnIsSynchronizingDraftsChanged(bool value) { }

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
            return;
        }

        submitter.DraftCountChanged += OnDraftCountChanged;
        submitter.ConnectivityChanged += OnConnectivityChanged;
        OnPropertyChanged(nameof(IsCloudMode));
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
        IsSynchronizingDrafts = count > 0 && IsCloudOnline;
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
        IsSynchronizingDrafts = PendingDraftCount > 0 && isOnline;
    }
}
