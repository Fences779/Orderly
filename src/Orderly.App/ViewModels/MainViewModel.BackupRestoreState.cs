using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private string recentBackupStatusText = "暂无本地备份";

    [ObservableProperty]
    private string recentBackupDetailText = "导出后会在这里显示最近一次本地备份状态。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ValidateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private string selectedBackupPath = string.Empty;

    [ObservableProperty]
    private string restoreStatusText = "未选择恢复备份";

    [ObservableProperty]
    private string restoreDetailText = "先选择备份文件，再执行校验或恢复。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private BackupRestorePreviewResult? restorePreview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private bool isRestoreRiskConfirmed;

    public bool HasRestorePreview => RestorePreview is not null;

    public bool CanConfirmRestoreRisk => RestorePreview?.CanRestore == true;

    public string RestorePreviewFileName => string.IsNullOrWhiteSpace(RestorePreview?.FileName)
        ? "未生成"
        : RestorePreview.FileName;

    public string RestorePreviewExportedAtText => RestorePreview?.ExportedAt is DateTimeOffset exportedAt && exportedAt != default
        ? exportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "未知";

    public string RestorePreviewSchemaVersionText => RestorePreview?.SchemaVersion?.ToString() ?? "未知";

    public string RestorePreviewChecksumText => string.IsNullOrWhiteSpace(RestorePreview?.Checksum)
        ? "无"
        : RestorePreview.Checksum;

    public string RestorePreviewChecksumStatusText => RestorePreview is null
        ? "未校验"
        : RestorePreview.IsChecksumValid
            ? "Valid"
            : "Invalid";

    public string RestorePreviewCountsText => FormatBackupCounts(RestorePreview?.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal));

    public string RestorePreviewTargetCountsText => FormatBackupCounts(RestorePreview?.TargetCounts ?? new Dictionary<string, int>(StringComparer.Ordinal));

    public string RestorePreviewTargetStateCodeText => GetRestoreTargetCode(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewTargetStateText => GetRestoreTargetLabel(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewWillClearQaDataText => RestorePreview is { WillClearQaData: true } ? "是" : "否";

    public string RestorePreviewCanRestoreText => RestorePreview is { CanRestore: true } ? "是" : "否";

    public string RestorePreviewRefuseReasonText => string.IsNullOrWhiteSpace(RestorePreview?.RefuseReason)
        ? "无"
        : RestorePreview.RefuseReason;

    public string RestoreRiskPromptText => RestorePreview switch
    {
        null => "先选择备份并生成恢复预览，再确认风险。",
        { CanRestore: false } => "当前预览已拒绝恢复，禁止继续执行。",
        { WillClearQaData: true } => "恢复会先清理当前 QA/测试数据，再按备份完整覆盖恢复。",
        _ => "恢复会按预览结果覆盖当前空库；不会合并数据，也不会覆盖已有生产库。"
    };

    public string RestoreRiskConfirmationText => RestorePreview is { WillClearQaData: true }
        ? "我已确认：将先清理当前 QA/测试数据，再执行恢复。"
        : "我已确认：已阅读预览和风险提示，并继续恢复。";

    public bool CanRestoreWithConfirmation => RestorePreview?.CanRestore == true && IsRestoreRiskConfirmed;
}
