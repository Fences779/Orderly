using CommunityToolkit.Mvvm.ComponentModel;
using Orderly.Core.Models;
using System.IO;

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

    public bool HasSelectedBackupPath => !string.IsNullOrWhiteSpace(SelectedBackupPath);

    public bool CanConfirmRestoreRisk => RestorePreview?.CanRestore == true;

    public string RestoreWorkflowStatusText => RestorePreview switch
    {
        { CanRestore: true } => "校验通过",
        { CanRestore: false } => "已拒绝",
        null when HasSelectedBackupPath => "待校验",
        _ => "未选择"
    };

    public string RestoreWorkflowStatusDetailText => RestorePreview switch
    {
        { CanRestore: true } => "备份文件已通过检查，确认风险后可执行恢复。",
        { CanRestore: false } => "备份文件或当前数据库状态不满足恢复条件。",
        null when HasSelectedBackupPath => "已选择备份文件，请先检查内容和恢复条件。",
        _ => "从本地备份恢复数据前，会先检查完整性、目标库状态和恢复风险。"
    };

    public string RestoreSelectedBackupFileNameText => HasSelectedBackupPath
        ? Path.GetFileName(SelectedBackupPath)
        : "尚未选择备份文件";

    public string RestoreSelectedBackupPathText => HasSelectedBackupPath
        ? SelectedBackupPath
        : "选择后会显示文件路径，并清空旧的检查结果。";

    public string RestorePreviewFileName => string.IsNullOrWhiteSpace(RestorePreview?.FileName)
        ? "未生成"
        : RestorePreview.FileName;

    public string RestorePreviewExportedAtText => RestorePreview?.ExportedAt is DateTimeOffset exportedAt && exportedAt != default
        ? exportedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        : "等待检查";

    public string RestorePreviewSchemaVersionText => RestorePreview?.SchemaVersion?.ToString() ?? "未知";

    public string RestorePreviewChecksumText => string.IsNullOrWhiteSpace(RestorePreview?.Checksum)
        ? "无"
        : RestorePreview.Checksum;

    public string RestorePreviewChecksumStatusText => RestorePreview is null
        ? "未校验"
        : RestorePreview.IsChecksumValid
            ? "完整性通过"
            : "完整性失败";

    public string RestorePreviewCountsText
    {
        get
        {
            var countsText = FormatBackupCounts(RestorePreview?.Counts ?? new Dictionary<string, int>(StringComparer.Ordinal));
            return string.IsNullOrWhiteSpace(countsText) ? "等待检查" : countsText;
        }
    }

    public string RestorePreviewTargetCountsText => FormatBackupCounts(RestorePreview?.TargetCounts ?? new Dictionary<string, int>(StringComparer.Ordinal));

    public string RestorePreviewTargetStateCodeText => GetRestoreTargetCode(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewTargetStateText => GetRestoreTargetLabel(RestorePreview?.TargetState ?? BackupRestoreTargetState.Unknown);

    public string RestorePreviewWillClearQaDataText => RestorePreview is { WillClearQaData: true } ? "是" : "否";

    public string RestorePreviewCanRestoreText => RestorePreview switch
    {
        { CanRestore: true } => "可以恢复",
        { CanRestore: false } => "禁止恢复",
        _ => "等待检查"
    };

    public string RestorePreviewRefuseReasonText => string.IsNullOrWhiteSpace(RestorePreview?.RefuseReason)
        ? "无"
        : RestorePreview.RefuseReason;

    public string RestoreRiskPromptText => RestorePreview switch
    {
        null when HasSelectedBackupPath => "先检查备份内容，系统确认可恢复后再继续。",
        null => "先选择备份文件，再检查是否可以恢复。",
        { CanRestore: false } => string.IsNullOrWhiteSpace(RestorePreview.RefuseReason)
            ? "当前检查结果已拒绝恢复，禁止继续执行。"
            : RestorePreview.RefuseReason,
        { WillClearQaData: true } => "恢复会先清理当前 QA/测试数据，再按备份完整覆盖恢复。",
        _ => "恢复会按预览结果覆盖当前空库；不会合并数据，也不会覆盖已有生产库。"
    };

    public string RestoreRiskConfirmationText => RestorePreview is { WillClearQaData: true }
        ? "我已确认：将先清理当前 QA/测试数据，再执行恢复。"
        : "我已确认：已阅读预览和风险提示，并继续恢复。";

    public string RestoreCheckSummaryText => RestorePreview switch
    {
        null when HasSelectedBackupPath => "点击“检查备份”后显示备份时间、包含数据、当前数据库状态和恢复结论。",
        null => "尚未选择备份文件。",
        { CanRestore: true } => string.IsNullOrWhiteSpace(RestorePreview.Summary)
            ? "检查通过，当前备份可以用于恢复。"
            : RestorePreview.Summary,
        _ => string.IsNullOrWhiteSpace(RestorePreview.RefuseReason)
            ? RestorePreview.Summary
            : RestorePreview.RefuseReason
    };

    public string RestoreValidateActionText => HasRestorePreview ? "重新检查" : "检查备份";

    public string RestorePrimaryActionText => RestorePreview switch
    {
        null when !HasSelectedBackupPath => "先选择文件",
        null => "先检查备份",
        { CanRestore: false } => "无法恢复",
        { CanRestore: true } when !IsRestoreRiskConfirmed => "确认后恢复",
        _ => "开始恢复"
    };

    public bool CanRestoreWithConfirmation => RestorePreview?.CanRestore == true && IsRestoreRiskConfirmed;
}
