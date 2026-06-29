using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using Orderly.Core.Models;
using Orderly.App.ViewModels.Helpers;
using CommerceOrder = Orderly.Core.Commerce.Order;
using OrderFulfillmentStage = Orderly.Core.Commerce.OrderFulfillmentStage;
using OrderPaymentStage = Orderly.Core.Commerce.OrderPaymentStage;
using OrderSalesStage = Orderly.Core.Commerce.OrderSalesStage;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    private const string WindowsRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsRunValueName = "Orderly";
    private static readonly TimeSpan AutoBackupPollInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NotificationPollInterval = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, DateTime> _notificationSentAt = new(StringComparer.Ordinal);
    private CancellationTokenSource? _autoBackupLoopCts;
    private CancellationTokenSource? _notificationLoopCts;
    private SettingsSaveOutcome? _lastMainSettingsSaveOutcome;
    private AppPreferences _lastAppliedRuntimePreferences = new();

    private static readonly Regex RuntimePhoneRegex = new(
        @"(?<!\d)(?:\+?86[-\s]?)?1[3-9]\d(?:[-\s]?\d){8}(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex RuntimeAddressRegex = new(
        @"[\u4e00-\u9fa5A-Za-z0-9#\-]{2,}(?:省|市|区|县|镇|乡|街道|路|街|巷|号|栋|幢|单元|室)[\u4e00-\u9fa5A-Za-z0-9#\-]{0,40}",
        RegexOptions.Compiled);

    private static readonly Regex RuntimeOrderSummaryCustomerRegex = new(
        @"(?m)^(客户：).+$",
        RegexOptions.Compiled);

    private static readonly Regex RuntimeOrderSummaryAmountRegex = new(
        @"(?m)^(金额：).+$",
        RegexOptions.Compiled);

    private static readonly Regex OrderExceptionKeywordRegex = new(
        "异常|缺货|退款|退货|支付异常|付款异常|无法发货|延迟发货|投诉|取消",
        RegexOptions.Compiled);

    private async Task ApplyRuntimeSettingsAsync(AppPreferences previous, AppPreferences current)
    {
        ApplyWindowsStartup(current.StartWithWindows);
        Orderly.App.Helpers.ThemeHelper.ApplyAccentColor(current.AccentColor);
        ApplyAutoBackupRuntime(current);
        ApplyNotificationRuntime(current);
        ApplyDebugRuntime(current.DebugModeEnabled);

        if (previous.OperationLogRetentionDays != current.OperationLogRetentionDays)
        {
            _ = CleanupExpiredActivityLogsBestEffortAsync(current.OperationLogRetentionDays);
        }

        _lastAppliedRuntimePreferences = current;
        await Task.CompletedTask;
    }

    private async Task FlushMainSettingsAutoSaveAsync()
    {
        if (_isApplyingSettingsInputs)
        {
            return;
        }

        if (_hasQueuedSettingsAutoSave && !_isRunningSettingsAutoSave)
        {
            _ = ProcessQueuedSettingsAutoSaveAsync();
        }

        while (_isRunningSettingsAutoSave || _hasQueuedSettingsAutoSave)
        {
            await Task.Delay(50);
        }
    }

    private static void ApplyWindowsStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKeyPath);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(WindowsRunValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        key.SetValue(WindowsRunValueName, $"\"{executablePath}\"");
    }

    private void ApplyDebugRuntime(bool enabled)
    {
        AppContext.SetSwitch("Orderly.DebugMode", enabled);
        if (enabled)
        {
            Directory.CreateDirectory(GetDiagnosticsDirectory());
        }
    }

    private void ApplyAutoBackupRuntime(AppPreferences preferences)
    {
        _autoBackupLoopCts?.Cancel();
        _autoBackupLoopCts = null;

        if (!preferences.AutoBackupEnabled || string.Equals(preferences.AutoBackupFrequency, "手动", StringComparison.Ordinal))
        {
            return;
        }

        _autoBackupLoopCts = new CancellationTokenSource();
        _ = RunAutoBackupLoopAsync(_autoBackupLoopCts.Token);
    }

    private async Task RunAutoBackupLoopAsync(CancellationToken cancellationToken)
    {
        await TryRunAutoBackupIfDueAsync(cancellationToken);
        using var timer = new PeriodicTimer(AutoBackupPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await TryRunAutoBackupIfDueAsync(cancellationToken);
        }
    }

    private async Task TryRunAutoBackupIfDueAsync(CancellationToken cancellationToken)
    {
        var preferences = await _settingRepository.GetPreferencesAsync(cancellationToken);
        if (!preferences.AutoBackupEnabled || string.Equals(preferences.AutoBackupFrequency, "手动", StringComparison.Ordinal))
        {
            return;
        }

        if (!IsAutoBackupDue(preferences))
        {
            return;
        }

        var directory = ResolveBackupDirectory(preferences.BackupDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"orderly-auto-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await _backupService.ExportAsync(
            path,
            createdBy: "settings-auto-backup",
            includeSensitivePlaintext: preferences.IncludeSensitiveInExport,
            cancellationToken: cancellationToken);

        preferences.LastAutoBackupAt = DateTime.Now;
        await _settingRepository.SavePreferencesAsync(preferences, cancellationToken);
        await CleanupAutoBackupRetentionAsync(directory, preferences.BackupRetentionCount, cancellationToken);
        UpdateRecentBackupStatus(await _backupService.GetLatestBackupAsync(cancellationToken) ?? new BackupResult { BackupPath = path });
    }

    private static bool IsAutoBackupDue(AppPreferences preferences)
    {
        var last = preferences.LastAutoBackupAt;
        if (last is null)
        {
            return true;
        }

        return preferences.AutoBackupFrequency switch
        {
            "每日" => last.Value.Date < DateTime.Today,
            "每周" => last.Value.Date <= DateTime.Today.AddDays(-7),
            _ => false
        };
    }

    private static Task CleanupAutoBackupRetentionAsync(string directory, int retentionCount, CancellationToken cancellationToken)
    {
        var keep = Math.Clamp(retentionCount, 1, 100);
        var files = Directory.EnumerateFiles(directory, "orderly-auto-backup-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(keep)
            .ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                file.Delete();
            }
            catch
            {
                // Retention cleanup must not fail the backup itself.
            }
        }

        return Task.CompletedTask;
    }

    private void ApplyNotificationRuntime(AppPreferences preferences)
    {
        _notificationLoopCts?.Cancel();
        _notificationLoopCts = null;

        if (!HasAnyNotificationEnabled(preferences) || _trySendDesktopNotification is null)
        {
            RefreshNotificationSettingsRuntimeStatus();
            return;
        }

        _notificationLoopCts = new CancellationTokenSource();
        _ = RunNotificationLoopAsync(_notificationLoopCts.Token);
        RefreshNotificationSettingsRuntimeStatus();
    }

    private async Task RunNotificationLoopAsync(CancellationToken cancellationToken)
    {
        await TrySendDueNotificationsAsync(cancellationToken);
        using var timer = new PeriodicTimer(NotificationPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await TrySendDueNotificationsAsync(cancellationToken);
        }
    }

    private async Task TrySendDueNotificationsAsync(CancellationToken cancellationToken)
    {
        var preferences = await _settingRepository.GetPreferencesAsync(cancellationToken);
        if (!HasAnyNotificationEnabled(preferences) || IsInDoNotDisturb(preferences.NotifyDoNotDisturbRange))
        {
            return;
        }

        var commerceOrders = preferences.NotifyExceptionOrder || preferences.NotifyOverdueUnhandled
            ? await ListCommerceOrdersSafeAsync(cancellationToken)
            : [];

        if (preferences.NotifyOverdueUnhandled)
        {
            foreach (var task in WorkbenchTasks.Where(task => task.Task.Type == WorkbenchTaskType.FollowUpOverdue))
            {
                if (!ShouldSendNotification(preferences, task.Task.Priority))
                {
                    continue;
                }

                SendNotificationOnce($"overdue:{task.Id}", "待办已超期", task.Title);
            }

            SendCommerceOrderThresholdNotifications(preferences, commerceOrders);
            SendLegacyOrderThresholdNotifications(preferences);
        }

        if (preferences.NotifyExceptionOrder)
        {
            SendCommerceOrderExceptionNotifications(preferences, commerceOrders);
            SendLegacyOrderExceptionNotifications(preferences);
        }

        if (preferences.NotifyMissingAddress)
        {
            if (ShouldSendNotification(preferences, WorkbenchTaskPriority.High))
            {
                foreach (var order in _allOrders.Where(order => string.IsNullOrWhiteSpace(order.Order.Customer?.ContactHandle)))
                {
                    SendNotificationOnce($"missing-address:{order.Id}", "订单缺少地址/联系方式", order.TitleDisplay);
                }
            }
        }

        if (preferences.NotifyNewOrder)
        {
            if (ShouldSendNotification(preferences, WorkbenchTaskPriority.Medium))
            {
                foreach (var order in _allOrders.Where(order => order.Order.CreatedAt >= DateTime.Now.AddHours(-24)))
                {
                    SendNotificationOnce($"new-order:{order.Id}:{order.Order.CreatedAt:yyyyMMddHH}", "新订单提醒", order.TitleDisplay);
                }
            }
        }

        if (preferences.NotifySyncFailed)
        {
            await SendSyncFailureNotificationsAsync(preferences, cancellationToken);
        }
    }

    private static bool HasAnyNotificationEnabled(AppPreferences preferences)
    {
        return preferences.NotifyNewOrder
            || preferences.NotifyExceptionOrder
            || preferences.NotifyOverdueUnhandled
            || preferences.NotifySyncFailed
            || preferences.NotifyMissingAddress;
    }

    private static bool IsInDoNotDisturb(string range)
    {
        if (!TryNormalizeDoNotDisturbRange(range, out var normalized) || string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var parts = normalized.Split('-', StringSplitOptions.TrimEntries);
        var now = DateTime.Now.TimeOfDay;
        var start = TimeSpan.Parse(parts[0]);
        var end = TimeSpan.Parse(parts[1]);
        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    private static bool ShouldSendNotification(AppPreferences preferences, WorkbenchTaskPriority priority)
    {
        return !preferences.NotifyHighPriorityOnly || priority >= WorkbenchTaskPriority.High;
    }

    private async Task<IReadOnlyList<CommerceOrder>> ListCommerceOrdersSafeAsync(CancellationToken cancellationToken)
    {
        if (_commerceOrderRepository is null)
        {
            return [];
        }

        try
        {
            return await _commerceOrderRepository.GetAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            WriteDebugException(ex);
            return [];
        }
    }

    private void SendCommerceOrderThresholdNotifications(AppPreferences preferences, IReadOnlyList<CommerceOrder> orders)
    {
        if (orders.Count == 0 || !ShouldSendNotification(preferences, WorkbenchTaskPriority.High))
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        foreach (var order in orders)
        {
            if (IsPaidButUnconfirmed(order)
                && IsOlderThan(order.UpdatedAt, utcNow, preferences.NotifyPaidUnconfirmedHours))
            {
                SendNotificationOnce(
                    $"commerce-paid-unconfirmed:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "已支付订单待确认",
                    $"{BuildCommerceOrderDisplay(order)} 已超过 {preferences.NotifyPaidUnconfirmedHours} 小时未确认。");
            }

            if (IsPendingProduction(order)
                && IsOlderThan(order.UpdatedAt, utcNow, preferences.NotifyPendingProductionHours))
            {
                SendNotificationOnce(
                    $"commerce-pending-production:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "待制作工单未推进",
                    $"{BuildCommerceOrderDisplay(order)} 已超过 {preferences.NotifyPendingProductionHours} 小时未进入制作。");
            }

            if (order.FulfillmentStage == OrderFulfillmentStage.Ready
                && IsOlderThan(order.UpdatedAt, utcNow, preferences.NotifyPendingShipmentHours))
            {
                SendNotificationOnce(
                    $"commerce-pending-shipment:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "待发货订单未发货",
                    $"{BuildCommerceOrderDisplay(order)} 已超过 {preferences.NotifyPendingShipmentHours} 小时未完成发货。");
            }
        }
    }

    private void SendLegacyOrderThresholdNotifications(AppPreferences preferences)
    {
        if (!ShouldSendNotification(preferences, WorkbenchTaskPriority.High))
        {
            return;
        }

        foreach (var item in _allOrders)
        {
            var order = item.Order;
            if (order.Amount > 0
                && order.Status is OrderStatus.PendingCommunication or OrderStatus.PendingQuote
                && IsOlderThan(order.UpdatedAt, DateTime.Now, preferences.NotifyPaidUnconfirmedHours))
            {
                SendNotificationOnce(
                    $"legacy-paid-unconfirmed:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "已支付订单待确认",
                    $"{item.TitleDisplay} 已超过 {preferences.NotifyPaidUnconfirmedHours} 小时未确认。");
            }

            if (order.Status == OrderStatus.PendingFollowUp
                && IsOlderThan(order.UpdatedAt, DateTime.Now, preferences.NotifyPendingProductionHours))
            {
                SendNotificationOnce(
                    $"legacy-pending-production:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "待制作工单未推进",
                    $"{item.TitleDisplay} 已超过 {preferences.NotifyPendingProductionHours} 小时未进入制作。");
            }

            if (order.Status == OrderStatus.Won
                && IsOlderThan(order.UpdatedAt, DateTime.Now, preferences.NotifyPendingShipmentHours))
            {
                SendNotificationOnce(
                    $"legacy-pending-shipment:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                    "待发货订单未发货",
                    $"{item.TitleDisplay} 已超过 {preferences.NotifyPendingShipmentHours} 小时未完成发货。");
            }
        }
    }

    private void SendCommerceOrderExceptionNotifications(AppPreferences preferences, IReadOnlyList<CommerceOrder> orders)
    {
        if (orders.Count == 0 || !ShouldSendNotification(preferences, WorkbenchTaskPriority.High))
        {
            return;
        }

        foreach (var order in orders.Where(IsCommerceExceptionOrder))
        {
            SendNotificationOnce(
                $"commerce-exception:{order.Id}:{order.UpdatedAt:yyyyMMddHH}",
                "异常订单提醒",
                $"{BuildCommerceOrderDisplay(order)}：{BuildCommerceExceptionReason(order)}");
        }
    }

    private void SendLegacyOrderExceptionNotifications(AppPreferences preferences)
    {
        if (!ShouldSendNotification(preferences, WorkbenchTaskPriority.High))
        {
            return;
        }

        foreach (var item in _allOrders.Where(item => HasLegacyOrderExceptionSignal(item.Order)))
        {
            SendNotificationOnce(
                $"legacy-exception:{item.Id}:{item.Order.UpdatedAt:yyyyMMddHH}",
                "异常订单提醒",
                $"{item.TitleDisplay}：发现异常关键词。");
        }
    }

    private async Task SendSyncFailureNotificationsAsync(AppPreferences preferences, CancellationToken cancellationToken)
    {
        if (!ShouldSendNotification(preferences, WorkbenchTaskPriority.Critical))
        {
            return;
        }

        var records = await _syncRecordRepository.ListFailedOrConflictedAsync(cancellationToken);
        foreach (var record in records)
        {
            var message = string.IsNullOrWhiteSpace(record.ErrorMessage)
                ? $"{record.EntityType}#{record.EntityId} 状态：{record.SyncStatus}"
                : $"{record.EntityType}#{record.EntityId}：{TrimRuntimePreview(record.ErrorMessage)}";
            SendNotificationOnce(
                $"sync-failed:{record.Id}:{record.UpdatedAt:yyyyMMddHH}",
                "同步失败提醒",
                message);
        }
    }

    private static bool IsPaidButUnconfirmed(CommerceOrder order)
    {
        return order.PaymentStage is OrderPaymentStage.Paid or OrderPaymentStage.PartiallyPaid
            && order.SalesStage is OrderSalesStage.Draft or OrderSalesStage.Quoted;
    }

    private static bool IsPendingProduction(CommerceOrder order)
    {
        return order.SalesStage == OrderSalesStage.Confirmed
            && order.FulfillmentStage == OrderFulfillmentStage.NotStarted;
    }

    private static bool IsCommerceExceptionOrder(CommerceOrder order)
    {
        return order.PaymentStage == OrderPaymentStage.Refunded
            || order.FulfillmentStage == OrderFulfillmentStage.Returned
            || order.SalesStage == OrderSalesStage.Cancelled
            || OrderExceptionKeywordRegex.IsMatch(order.Note ?? string.Empty);
    }

    private static string BuildCommerceExceptionReason(CommerceOrder order)
    {
        if (order.PaymentStage == OrderPaymentStage.Refunded)
        {
            return "收款阶段为已退款";
        }

        if (order.FulfillmentStage == OrderFulfillmentStage.Returned)
        {
            return "履约阶段为已退货";
        }

        if (order.SalesStage == OrderSalesStage.Cancelled)
        {
            return "销售阶段为已取消";
        }

        return "备注包含异常关键词";
    }

    private static bool HasLegacyOrderExceptionSignal(MerchantOrder order)
    {
        return OrderExceptionKeywordRegex.IsMatch(string.Join(' ', new[]
        {
            order.Title,
            order.Requirement,
            order.SourcePlatform,
            order.Channel,
            order.RawPayload
        }));
    }

    private static bool IsOlderThan(DateTime value, DateTime now, int hours)
    {
        return now - value >= TimeSpan.FromHours(Math.Clamp(hours, 1, 168));
    }

    private static string BuildCommerceOrderDisplay(CommerceOrder order)
    {
        return string.IsNullOrWhiteSpace(order.OrderNo)
            ? $"订单 {order.Id.ToString()[..8]}"
            : $"订单 {order.OrderNo}";
    }

    private static string TrimRuntimePreview(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 80 ? normalized : $"{normalized[..80]}…";
    }

    private void SendNotificationOnce(string key, string title, string message)
    {
        var now = DateTime.Now;
        if (_notificationSentAt.TryGetValue(key, out var lastSent) && now - lastSent < TimeSpan.FromHours(6))
        {
            return;
        }

        if (_trySendDesktopNotification?.Invoke(title, message) == true)
        {
            _notificationSentAt[key] = now;
        }
    }

    private async Task CleanupExpiredActivityLogsBestEffortAsync(int retentionDays)
    {
        try
        {
            await _activityLogService.CleanupExpiredActivitiesAsync(Math.Clamp(retentionDays, 7, 3650));
        }
        catch
        {
            // Retention setting must not break the save path.
        }
    }

    private static string MaskSensitiveText(string value, bool maskPhone, bool maskAddress)
    {
        var result = value ?? string.Empty;
        if (maskPhone)
        {
            result = RuntimePhoneRegex.Replace(result, match => MaskMiddle(match.Value));
        }

        if (maskAddress)
        {
            result = RuntimeAddressRegex.Replace(result, "[地址已隐藏]");
        }

        return result;
    }

    private static string MaskOrderSummaryPrivacy(string value, bool maskPhone, bool maskAddress)
    {
        var result = MaskSensitiveText(value, maskPhone, maskAddress);
        result = RuntimeOrderSummaryCustomerRegex.Replace(result, "$1[客户已隐藏]");
        result = RuntimeOrderSummaryAmountRegex.Replace(result, "$1[金额已隐藏]");
        return result;
    }

    private static string MaskMiddle(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 7)
        {
            return "***";
        }

        return $"{digits[..3]}****{digits[^4..]}";
    }

    private string BuildOrderSummaryForClipboard()
    {
        var order = SelectedOrder;
        if (order is null)
        {
            return string.Empty;
        }

        var lines = new[]
        {
            $"订单：{order.Title}",
            $"客户：{order.Customer?.Name}",
            $"金额：{(order.Amount > 0 ? $"¥{order.Amount:N0}" : "待报价")}",
            $"状态：{OrderStatusCatalog.GetLabel(order.Status)}",
            $"需求：{order.Requirement}",
            $"联系方式：{order.Customer?.Phone} {order.Customer?.ContactHandle}"
        };
        var text = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        return MaskOrderSummaryOnCopyInput
            ? MaskOrderSummaryPrivacy(text, MaskPhoneByDefaultInput, MaskAddressByDefaultInput)
            : text;
    }

    private string BuildCustomerPreferenceSummaryForClipboard()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            return string.Empty;
        }

        var text = string.Join(Environment.NewLine, new[]
        {
            $"客户：{customer.Name}",
            $"优先级：{StatusLabelHelper.GetCustomerPriorityLabel(customer.Priority)}",
            $"来源：{customer.SourcePlatform}/{customer.Channel}",
            $"电话：{customer.Phone}",
            $"联系方式：{customer.ContactHandle}",
            $"备注：{customer.Remark}"
        });

        return MaskSensitiveText(text, MaskPhoneByDefaultInput, MaskAddressByDefaultInput);
    }

    private static string GetDiagnosticsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orderly",
            "Diagnostics");
    }

    private void WriteDebugException(Exception ex)
    {
        if (!DebugModeEnabledInput)
        {
            return;
        }

        try
        {
            var directory = GetDiagnosticsDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"orderly-error-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, ex.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Never fail the original operation because diagnostic writing failed.
        }
    }

    public void HandleRuntimeHotkeyAction(RuntimeHotkeyAction action)
    {
        switch (action)
        {
            case RuntimeHotkeyAction.GlobalSearch:
                SelectedSection = SectionWorkbench;
                StatusMessage = "已聚焦全局搜索，请输入关键词。";
                break;

            case RuntimeHotkeyAction.TodayWorkbench:
                SelectedSection = SectionWorkbench;
                _ = RefreshWorkbenchTasksAsync();
                break;

            case RuntimeHotkeyAction.CopyOrderSummary:
                CopyOrderSummaryToClipboard();
                break;

            case RuntimeHotkeyAction.OpenProductionSheet:
                SelectedSection = SectionOrders;
                StatusMessage = SelectedOrder is null ? "请先选择订单后再打开制作单。" : "已切换到订单页。";
                break;

            case RuntimeHotkeyAction.MarkOrderException:
                SelectedSection = SectionBusinessAdvice;
                StatusMessage = SelectedOrder is null ? "请先选择订单后再标记异常。" : "已切换到经营建议页查看异常风险。";
                break;

            case RuntimeHotkeyAction.AdvanceFulfillment:
                _ = AdvanceSelectedOrderStatusAsync();
                break;

            case RuntimeHotkeyAction.OpenCustomerProfile:
                SelectedSection = SectionCustomers;
                break;

            case RuntimeHotkeyAction.NewCustomerNote:
                _ = AddNoteAsync();
                break;

            case RuntimeHotkeyAction.CopyCustomerPreferenceSummary:
                CopyCustomerPreferenceSummaryToClipboard();
                break;
        }
    }

    public async Task PersistWindowBoundsIfNeededAsync(Window window)
    {
        if (!RememberWindowBoundsInput)
        {
            return;
        }

        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        if (bounds.Width < 320 || bounds.Height < 240)
        {
            return;
        }

        var preferences = Preferences;
        preferences.WindowLeft = bounds.Left;
        preferences.WindowTop = bounds.Top;
        preferences.WindowWidth = bounds.Width;
        preferences.WindowHeight = bounds.Height;
        await _settingRepository.SavePreferencesAsync(preferences);
        Preferences = preferences;
    }

    private void CopyOrderSummaryToClipboard()
    {
        var summary = BuildOrderSummaryForClipboard();
        if (string.IsNullOrWhiteSpace(summary))
        {
            StatusMessage = "请先选择订单后再复制摘要。";
            return;
        }

        _clipboardService.SetText(summary);
        StatusMessage = MaskOrderSummaryOnCopyInput ? "订单摘要已脱敏复制。" : "订单摘要已复制。";
    }

    private void CopyCustomerPreferenceSummaryToClipboard()
    {
        var summary = BuildCustomerPreferenceSummaryForClipboard();
        if (string.IsNullOrWhiteSpace(summary))
        {
            StatusMessage = "请先选择客户后再复制偏好摘要。";
            return;
        }

        _clipboardService.SetText(summary);
        StatusMessage = "客户偏好摘要已复制。";
    }

    private async Task AdvanceSelectedOrderStatusAsync()
    {
        var order = SelectedOrder;
        if (order is null)
        {
            StatusMessage = "请先选择订单后再推进履约状态。";
            return;
        }

        var next = order.Status switch
        {
            OrderStatus.PendingCommunication => OrderStatus.PendingQuote,
            OrderStatus.PendingQuote => OrderStatus.Quoted,
            OrderStatus.Quoted => OrderStatus.PendingFollowUp,
            OrderStatus.PendingFollowUp => OrderStatus.Won,
            _ => order.Status
        };

        if (next == order.Status)
        {
            StatusMessage = "当前订单状态无需继续推进。";
            return;
        }

        await ExecuteSaveActionAsync(
            busyMessage: "正在推进订单状态...",
            successMessage: "订单状态已推进",
            errorTitle: "推进订单状态失败",
            errorStatusPrefix: "推进订单状态失败",
            action: async () =>
            {
                await _orderService.UpdateStatusAsync(order.Id, next);
                await ReloadListDataAsync(selectedCustomerId: order.CustomerId, selectedOrderId: order.Id);
                SelectOrderById(order.Id);
                await ReloadSelectedCustomerDetailsAsync(SelectedCustomer);
            });
    }
}
