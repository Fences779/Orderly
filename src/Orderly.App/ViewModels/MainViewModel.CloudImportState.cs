using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Contracts.Commerce;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private LocalImportDryRunRequest? _cloudImportDryRunRequest;

    [ObservableProperty]
    private string cloudImportStatusText = "未执行本地数据导入预检查";

    [ObservableProperty]
    private string cloudImportDetailText = "仅 Owner 在云端模式可用。先预检查，再确认导入。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCloudImportCommand))]
    private LocalImportDryRunResponse? cloudImportDryRun;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCloudImportCommand))]
    private bool isCloudImportCommitConfirmed;

    public bool CanUseCloudImport => IsCurrentUserOwner
        && _remoteImportService is not null
        && _localImportPackageBuilder is not null;

    public bool CanCommitCloudImport => CanUseCloudImport
        && !IsBusy
        && CloudImportDryRun?.CanCommit == true
        && IsCloudImportCommitConfirmed
        && _cloudImportDryRunRequest is not null;

    public string CloudImportWorkflowStatusText => CloudImportDryRun switch
    {
        null when CanUseCloudImport => "待预检查",
        null => "不可用",
        { CanCommit: true } => "可导入",
        _ => "需处理"
    };

    public string CloudImportSummaryText => CloudImportDryRun switch
    {
        null when CanUseCloudImport => "点击“预检查”后显示本机数据量、重复匹配和问题。",
        null => "当前账号或运行模式不能执行云端导入。",
        { CanCommit: true } => $"预检查通过，新建 {CloudImportDryRun.Counts.NewRecords} 条，匹配已有 {CloudImportDryRun.Counts.ExistingMapped} 条。",
        _ => CloudImportDryRun.Issues.Count == 0
            ? "没有可导入的数据。"
            : $"发现 {CloudImportDryRun.Issues.Count} 个问题，需处理后重新预检查。"
    };

    public string CloudImportCountsText => CloudImportDryRun is null
        ? "等待预检查"
        : $"商品:{CloudImportDryRun.Counts.Products} / 客户:{CloudImportDryRun.Counts.Customers} / 库存:{CloudImportDryRun.Counts.InventoryItems} / 订单:{CloudImportDryRun.Counts.Orders} / 明细:{CloudImportDryRun.Counts.OrderItems} / 现金流:{CloudImportDryRun.Counts.CashFlowEntries}";

    public string CloudImportIssuesText => CloudImportDryRun is null || CloudImportDryRun.Issues.Count == 0
        ? "无"
        : string.Join("\n", CloudImportDryRun.Issues.Take(5).Select(static issue =>
            $"{issue.EntityType}:{issue.SourceLocalEntityId} {issue.Message}"));

    public string CloudImportPrimaryActionText => CloudImportDryRun switch
    {
        null => "先预检查",
        { CanCommit: false } => "无法导入",
        _ when !IsCloudImportCommitConfirmed => "确认后导入",
        _ => "确认导入"
    };

    public string CloudImportConfirmationText => CloudImportDryRun?.CanCommit == true
        ? "我已确认：按预检查结果把本机旧数据导入当前云端工作区。"
        : "预检查通过后才能确认导入。";

    partial void OnCloudImportDryRunChanged(LocalImportDryRunResponse? value)
    {
        NotifyCloudImportStateChanged();
    }

    partial void OnIsCloudImportCommitConfirmedChanged(bool value)
    {
        NotifyCloudImportStateChanged();
    }

    private void NotifyCloudImportStateChanged()
    {
        OnPropertyChanged(nameof(CanUseCloudImport));
        OnPropertyChanged(nameof(CanCommitCloudImport));
        OnPropertyChanged(nameof(CloudImportWorkflowStatusText));
        OnPropertyChanged(nameof(CloudImportSummaryText));
        OnPropertyChanged(nameof(CloudImportCountsText));
        OnPropertyChanged(nameof(CloudImportIssuesText));
        OnPropertyChanged(nameof(CloudImportPrimaryActionText));
        OnPropertyChanged(nameof(CloudImportConfirmationText));
    }
}
