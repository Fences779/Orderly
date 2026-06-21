using System;
using System.Collections.Generic;
using System.Linq;

namespace Orderly.App.ViewModels;

/// <summary>
/// 设置搜索静态索引（设计文档 §9.4 / Req 2.4）。
/// 在内存中持有一份编译期静态可搜索条目表，覆盖六大分类的设置行项；每条登记标题 /
/// 描述 / 关键字 / 所属分类（六大分类之一）/ 非空锚点 AutomationId（与 XAML 行项一致）。
/// 过滤 / 排序算法与结果上限标志见任务 6.2。
/// </summary>
public sealed class SettingsSearchIndex : ISettingsSearchIndex
{
    /// <summary>搜索结果上限：原始命中超过该值时返回截断结果并置位「结果超限」标志（§9.4 / Req 2.8）。</summary>
    public const int MaxResults = 12;

    // ── 命中位置加权（§9.4） ──
    private const int TitlePrefixScore = 100; // 标题前缀
    private const int TitleContainsScore = 60; // 标题子串
    private const int DescriptionContainsScore = 25; // 描述子串
    private const int KeywordContainsScore = 20; // 关键字子串

    /// <summary>六大分类 key（与左导航项及 <see cref="SettingsSearchEntry.CategoryKey"/> 对齐）。</summary>
    public static readonly IReadOnlyList<string> CategoryKeys = new[]
    {
        "外观与启动",
        "数据与备份",
        "安全与日志",
        "AI 助手",
        "通知提醒",
        "快捷键",
    };

    private static readonly IReadOnlyList<SettingsSearchEntry> StaticEntries = new[]
    {
        // ── 外观与启动 ──
        new SettingsSearchEntry("主题模式", "在浅色、深色或跟随系统主题间切换", new[] { "主题", "皮肤", "深色", "浅色", "theme" }, "外观与启动", "List_ThemeMode"),
        new SettingsSearchEntry("字体大小", "调整界面字号预设", new[] { "字体", "字号", "大小", "font" }, "外观与启动", "List_FontSizePreset"),
        new SettingsSearchEntry("强调色", "选择界面主强调色", new[] { "强调色", "主题色", "颜色", "accent" }, "外观与启动", "List_AccentColor"),
        new SettingsSearchEntry("启动默认页面", "设置应用启动后默认进入的页面", new[] { "启动", "默认页", "startup" }, "外观与启动", "List_StartupDefaultSection"),
        new SettingsSearchEntry("默认窗口模式", "设置应用启动时的窗口显示模式", new[] { "窗口", "模式", "window" }, "外观与启动", "List_DefaultWindowMode"),
        new SettingsSearchEntry("记住上次所在页面", "重新打开时回到上次停留的页面", new[] { "记住", "上次", "页面" }, "外观与启动", "Chk_RememberLastSection"),
        new SettingsSearchEntry("开机自启动", "随 Windows 启动自动运行", new[] { "开机", "自启", "启动", "startup" }, "外观与启动", "Chk_StartWithWindows"),
        new SettingsSearchEntry("启动时显示悬浮窗", "启动后自动呼出桌面悬浮窗", new[] { "悬浮窗", "浮窗", "启动" }, "外观与启动", "Chk_ShowFloatingWindow"),
        new SettingsSearchEntry("关闭窗口后最小化到托盘", "点击关闭按钮后隐藏到系统托盘", new[] { "托盘", "最小化", "关闭" }, "外观与启动", "Chk_StartMinimizedToTray"),
        new SettingsSearchEntry("记住窗口位置与大小", "重新打开时恢复上次的窗口位置与尺寸", new[] { "窗口", "位置", "大小" }, "外观与启动", "Chk_RememberWindowBounds"),

        // ── 数据与备份 ──
        new SettingsSearchEntry("自动备份", "启用定时自动备份本地数据", new[] { "备份", "自动", "backup" }, "数据与备份", "Chk_AutoBackupEnabled"),
        new SettingsSearchEntry("自动备份频率", "设置自动备份的执行周期", new[] { "备份", "频率", "周期" }, "数据与备份", "List_AutoBackupFrequency"),
        new SettingsSearchEntry("备份保留数量", "设置本地保留的历史备份份数", new[] { "备份", "保留", "数量" }, "数据与备份", "Txt_BackupRetentionCount"),
        new SettingsSearchEntry("备份目录", "更改本地备份文件的存放目录", new[] { "备份", "目录", "路径" }, "数据与备份", "Btn_BrowseBackupDirectory"),
        new SettingsSearchEntry("导出备份", "立即导出一份完整数据备份", new[] { "导出", "备份", "export" }, "数据与备份", "Btn_ExportBackup"),
        new SettingsSearchEntry("选择备份文件", "选择用于验证或恢复的备份文件", new[] { "备份", "文件", "选择" }, "数据与备份", "Btn_SelectBackupFile"),
        new SettingsSearchEntry("验证备份", "校验所选备份文件的完整性", new[] { "备份", "验证", "校验" }, "数据与备份", "Btn_ValidateBackup"),
        new SettingsSearchEntry("执行安全恢复", "从备份文件恢复数据", new[] { "恢复", "备份", "restore" }, "数据与备份", "Btn_RestoreBackup"),

        // ── 安全与日志 ──
        new SettingsSearchEntry("默认脱敏手机号", "默认对手机号进行脱敏展示", new[] { "脱敏", "手机号", "隐私" }, "安全与日志", "Chk_MaskPhoneByDefault"),
        new SettingsSearchEntry("默认脱敏地址", "默认对收货地址进行脱敏展示", new[] { "脱敏", "地址", "隐私" }, "安全与日志", "Chk_MaskAddressByDefault"),
        new SettingsSearchEntry("导出包含敏感信息", "导出数据时包含敏感字段", new[] { "导出", "敏感", "脱敏" }, "安全与日志", "Chk_IncludeSensitiveInExport"),
        new SettingsSearchEntry("复制订单摘要时脱敏", "复制订单摘要时自动脱敏", new[] { "复制", "脱敏", "订单" }, "安全与日志", "Chk_MaskOrderSummaryOnCopy"),
        new SettingsSearchEntry("操作日志记录", "启用系统操作日志记录", new[] { "日志", "操作", "审计" }, "安全与日志", "Chk_OperationLogEnabled"),
        new SettingsSearchEntry("数据库健康检查", "检查本地数据库完整性与健康状况", new[] { "数据库", "健康", "检查" }, "安全与日志", "Btn_CheckDatabaseHealth"),

        // ── AI 助手 ──
        new SettingsSearchEntry("启用 AI 助手", "开启 AI 智能辅助能力", new[] { "AI", "助手", "智能" }, "AI 助手", "Chk_EnableAiAssistant"),
        new SettingsSearchEntry("允许 AI 读取订单上下文", "允许 AI 使用订单信息辅助生成", new[] { "AI", "订单", "上下文" }, "AI 助手", "Chk_AllowAiOrderContext"),
        new SettingsSearchEntry("允许 AI 读取客户档案", "允许 AI 使用客户档案辅助生成", new[] { "AI", "客户", "档案" }, "AI 助手", "Chk_AllowAiCustomerProfileContext"),
        new SettingsSearchEntry("默认 AI 模型", "设置默认使用的 AI 模型", new[] { "AI", "模型", "model" }, "AI 助手", "Txt_DefaultAiModel"),
        new SettingsSearchEntry("AI 超时时间", "设置 AI 请求的超时秒数", new[] { "AI", "超时", "timeout" }, "AI 助手", "Txt_AiTimeoutSeconds"),
        new SettingsSearchEntry("发送前自动脱敏", "调用 AI 前自动脱敏敏感信息", new[] { "AI", "脱敏", "隐私" }, "AI 助手", "Chk_AiAutoRedactBeforeSend"),
        new SettingsSearchEntry("屏蔽手机号", "发送给 AI 时屏蔽手机号", new[] { "AI", "手机号", "屏蔽" }, "AI 助手", "Chk_AiBlockPhone"),
        new SettingsSearchEntry("屏蔽完整地址", "发送给 AI 时屏蔽完整地址", new[] { "AI", "地址", "屏蔽" }, "AI 助手", "Chk_AiBlockFullAddress"),
        new SettingsSearchEntry("屏蔽支付交易号", "发送给 AI 时屏蔽支付交易号", new[] { "AI", "支付", "交易号" }, "AI 助手", "Chk_AiBlockPaymentTransactionId"),
        new SettingsSearchEntry("AI 回复语气", "设置 AI 回复的语气风格", new[] { "AI", "语气", "风格" }, "AI 助手", "List_AiReplyTone"),
        new SettingsSearchEntry("AI 回复长度", "设置 AI 回复的篇幅长度", new[] { "AI", "长度", "篇幅" }, "AI 助手", "List_AiReplyLength"),
        new SettingsSearchEntry("自动生成订单摘要", "由 AI 自动生成订单摘要", new[] { "AI", "订单", "摘要" }, "AI 助手", "Chk_AiAutoGenerateOrderSummary"),

        // ── 通知提醒 ──
        new SettingsSearchEntry("新订单提醒", "有新订单拉取成功时提醒", new[] { "通知", "订单", "提醒" }, "通知提醒", "Chk_NotifyNewOrder"),
        new SettingsSearchEntry("异常订单提醒", "发现异常订单时提醒", new[] { "通知", "异常", "提醒" }, "通知提醒", "Chk_NotifyExceptionOrder"),
        new SettingsSearchEntry("待办超期提醒", "待办任务超期未处理提醒", new[] { "通知", "超期", "待办" }, "通知提醒", "Chk_NotifyOverdueUnhandled"),
        new SettingsSearchEntry("同步失败提醒", "网关同步失败时提醒", new[] { "通知", "同步", "失败" }, "通知提醒", "Chk_NotifySyncFailed"),
        new SettingsSearchEntry("未确认订单催促提醒", "已支付订单超时未确认提醒（小时）", new[] { "通知", "催促", "小时" }, "通知提醒", "Txt_NotifyPaidUnconfirmedHours"),
        new SettingsSearchEntry("待制作工单催促提醒", "已确认订单超时未制作提醒（小时）", new[] { "通知", "工单", "催促" }, "通知提醒", "Txt_NotifyPendingProductionHours"),
        new SettingsSearchEntry("待发货催促提醒", "工单完成超时未发货提醒（小时）", new[] { "通知", "发货", "催促" }, "通知提醒", "Txt_NotifyPendingShipmentHours"),
        new SettingsSearchEntry("地址缺失提醒", "收货地址缺失时提醒", new[] { "通知", "地址", "缺失" }, "通知提醒", "Chk_NotifyMissingAddress"),
        new SettingsSearchEntry("免打扰时间段", "设置系统免打扰时间段", new[] { "免打扰", "通知", "时间" }, "通知提醒", "Txt_NotifyDoNotDisturbRange"),
        new SettingsSearchEntry("仅推送高优先级待办", "仅推送高优先级待办通知", new[] { "通知", "优先级", "高" }, "通知提醒", "Chk_NotifyHighPriorityOnly"),
        new SettingsSearchEntry("测试桌面通知", "发送一条测试桌面通知", new[] { "通知", "测试", "桌面" }, "通知提醒", "Btn_TestDesktopNotification"),

        // ── 快捷键 ──
        new SettingsSearchEntry("显示/隐藏主窗口", "激活并恢复主窗口至最前", new[] { "快捷键", "主窗口", "hotkey" }, "快捷键", "Txt_MainWindowHotkey"),
        new SettingsSearchEntry("显示/隐藏桌面悬浮窗", "呼出桌面悬浮按钮面板", new[] { "快捷键", "悬浮窗", "hotkey" }, "快捷键", "Txt_FloatingWindowHotkey"),
        new SettingsSearchEntry("全局客户与订单搜索", "一键聚焦全局搜索框", new[] { "快捷键", "搜索", "hotkey" }, "快捷键", "Txt_GlobalSearchHotkey"),
        new SettingsSearchEntry("导航至今日工作台", "快速切换至今日工作台", new[] { "快捷键", "工作台", "hotkey" }, "快捷键", "Txt_TodayWorkbenchHotkey"),
        new SettingsSearchEntry("一键复制订单摘要", "复制当前选中订单摘要", new[] { "快捷键", "复制", "订单" }, "快捷键", "Txt_CopyOrderSummaryHotkey"),
        new SettingsSearchEntry("打开待制作工单", "快速查看待制作工单", new[] { "快捷键", "工单", "hotkey" }, "快捷键", "Txt_OpenProductionSheetHotkey"),
        new SettingsSearchEntry("标记当前订单异常", "将订单快速标记为异常", new[] { "快捷键", "异常", "订单" }, "快捷键", "Txt_MarkOrderExceptionHotkey"),
        new SettingsSearchEntry("推进订单履约", "推进订单履约流转", new[] { "快捷键", "履约", "hotkey" }, "快捷键", "Txt_AdvanceFulfillmentHotkey"),
        new SettingsSearchEntry("打开客户档案", "快速打开客户档案", new[] { "快捷键", "客户", "档案" }, "快捷键", "Txt_OpenCustomerProfileHotkey"),
        new SettingsSearchEntry("新建客户备注", "快速新建客户备注", new[] { "快捷键", "客户", "备注" }, "快捷键", "Txt_NewCustomerNoteHotkey"),
        new SettingsSearchEntry("复制客户偏好摘要", "复制客户偏好摘要", new[] { "快捷键", "客户", "偏好" }, "快捷键", "Txt_CopyCustomerPreferenceSummaryHotkey"),
    };

    /// <inheritdoc />
    public IReadOnlyList<SettingsSearchEntry> Entries => StaticEntries;

    /// <inheritdoc />
    public IReadOnlyList<SettingsSearchEntry> Query(string text) => Search(text).Entries;

    /// <inheritdoc />
    public SettingsSearchResult Search(string text)
    {
        // 空 / 空白查询不展示结果列表（§9.4 / Req 2.5）。
        var q = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length == 0)
        {
            return new SettingsSearchResult(Array.Empty<SettingsSearchEntry>(), IsTruncated: false);
        }

        // 不区分大小写、子串 + 关键字匹配、命中位置加权（§9.4 / Req 2.1, 2.2）。
        var scored = new List<(SettingsSearchEntry Entry, int Score)>();
        foreach (var entry in StaticEntries)
        {
            var score = ScoreEntry(entry, q);
            if (score > 0)
            {
                scored.Add((entry, score));
            }
        }

        // 命中权重降序、同分按 CategoryKey 稳定排序（LINQ OrderBy/ThenBy 为稳定排序，
        // 同分同分类时保持静态索引原有顺序）（§9.4 / Req 2.2, 2.6）。
        var ordered = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Entry.CategoryKey, StringComparer.Ordinal)
            .Select(item => item.Entry)
            .ToList();

        // 上限 12 条：原始命中数超过上限时截断并置位「结果超限」标志（§9.4 / Req 2.8）。
        var isTruncated = ordered.Count > MaxResults;
        IReadOnlyList<SettingsSearchEntry> entries = isTruncated
            ? ordered.Take(MaxResults).ToList()
            : ordered;

        return new SettingsSearchResult(entries, isTruncated);
    }

    /// <summary>按 §9.4 加权规则为单条目相对查询串 <paramref name="q"/>（已 Trim+小写）打分。</summary>
    private static int ScoreEntry(SettingsSearchEntry entry, string q)
    {
        var score = 0;

        var title = entry.Title.ToLowerInvariant();
        if (title.StartsWith(q, StringComparison.Ordinal))
        {
            score += TitlePrefixScore;
        }
        else if (title.Contains(q, StringComparison.Ordinal))
        {
            score += TitleContainsScore;
        }

        if (entry.Description.ToLowerInvariant().Contains(q, StringComparison.Ordinal))
        {
            score += DescriptionContainsScore;
        }

        foreach (var keyword in entry.Keywords)
        {
            if (keyword.ToLowerInvariant().Contains(q, StringComparison.Ordinal))
            {
                score += KeywordContainsScore;
                break;
            }
        }

        return score;
    }
}
