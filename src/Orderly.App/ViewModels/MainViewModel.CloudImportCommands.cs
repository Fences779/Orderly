using CommunityToolkit.Mvvm.Input;
using Orderly.Contracts.Commerce;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanPreviewCloudImport))]
    private async Task PreviewCloudImportAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在预检查本地数据导入...",
            successMessage: "本地数据导入预检查完成",
            errorTitle: "预检查失败",
            errorStatusPrefix: "预检查失败",
            action: async () =>
            {
                var builder = _localImportPackageBuilder ?? throw new InvalidOperationException("本机导入包生成器不可用。");
                var service = _remoteImportService ?? throw new InvalidOperationException("云端导入服务不可用。");

                _cloudImportDryRunRequest = await builder.BuildDryRunRequestAsync();
                var dryRun = await service.DryRunAsync(_cloudImportDryRunRequest)
                    ?? throw new InvalidOperationException("云端未返回预检查结果。");

                CloudImportDryRun = dryRun;
                IsCloudImportCommitConfirmed = false;
                CloudImportStatusText = dryRun.CanCommit ? "预检查通过" : "预检查未通过";
                CloudImportDetailText = CloudImportSummaryText;
            });
    }

    [RelayCommand(CanExecute = nameof(CanCommitCloudImport))]
    private async Task CommitCloudImportAsync()
    {
        await ExecuteSaveActionAsync(
            busyMessage: "正在导入本机旧数据...",
            successMessage: "本机旧数据已导入云端",
            errorTitle: "导入失败",
            errorStatusPrefix: "导入失败",
            action: async () =>
            {
                var service = _remoteImportService ?? throw new InvalidOperationException("云端导入服务不可用。");
                var builder = _localImportPackageBuilder ?? throw new InvalidOperationException("本机导入包生成器不可用。");
                var dryRun = CloudImportDryRun ?? throw new InvalidOperationException("请先执行预检查。");
                var request = _cloudImportDryRunRequest ?? throw new InvalidOperationException("预检查请求已失效，请重新预检查。");

                // Re-read the local database right before commit and verify it hasn't changed since DryRun.
                var currentFingerprint = await builder.ComputeCurrentFingerprintAsync();
                if (!string.Equals(currentFingerprint, dryRun.SourceFingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("本地数据自预检查后已发生变化，请重新执行预检查后再导入。");
                }

                var result = await service.CommitAsync(new LocalImportCommitRequest
                {
                    DryRunBatchId = dryRun.DryRunBatchId,
                    SourceInstanceId = request.SourceInstanceId,
                    SourceFingerprint = currentFingerprint
                }) ?? throw new InvalidOperationException("云端未返回导入结果。");

                CloudImportStatusText = result.Status == "Committed" ? "导入完成" : $"导入失败：{result.Status}";
                CloudImportDetailText = result.Failures.Count == 0
                    ? $"已导入/匹配 {result.Imported.NewRecords + result.Imported.ExistingMapped} 条数据。"
                    : string.Join("\n", result.Failures.Take(5).Select(static failure => $"{failure.EntityType}:{failure.Message}"));

                IsCloudImportCommitConfirmed = false;
                _cloudImportDryRunRequest = null;
                CloudImportDryRun = null;
                MarkAllCommercePagesDirty();
            });
    }

    private bool CanPreviewCloudImport()
    {
        return CanUseCloudImport && !IsBusy;
    }
}
