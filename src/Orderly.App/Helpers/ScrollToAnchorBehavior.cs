using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;

namespace Orderly.App.Helpers;

/// <summary>
/// 轻量附加行为（任务 17.2，设计 §9.4 / Req 2.3）：监听 <see cref="SettingsViewModel"/> 的
/// <c>PendingScrollAnchorId</c> 命中导航信号，在右侧内容区按 AutomationId 定位目标元素，
/// 执行 <see cref="FrameworkElement.BringIntoView()"/> + 短暂背景高亮脉冲（~800ms），随后清空信号。
/// </summary>
/// <remarks>
/// 用法：在右内容区根元素上设置
/// <c>helpers:ScrollToAnchorBehavior.AnchorId="{Binding Settings.PendingScrollAnchorId, Mode=TwoWay}"</c>。
/// <para>
/// 设计要点：
/// </para>
/// <list type="bullet">
/// <item>保持 ViewModel 不引用控件（P5）：定位与高亮纯在 View 侧完成。</item>
/// <item>命令已先设置 <c>SelectedCategoryKey</c> 切换分类，故以 <see cref="DispatcherPriority.Background"/>
/// 延迟到布局完成后再查找目标元素，确保新分类内容已可见且已排版。</item>
/// <item>处理完成后将附加属性回写为 <c>null</c>（经 TwoWay 绑定清空 <c>PendingScrollAnchorId</c>），
/// 使再次激活同一条目能重新触发。</item>
/// <item>空 / 空白信号不触发任何跳转或高亮（Req 2.7）。</item>
/// <item>查找 / 高亮全程 best-effort：定位失败或动画异常均静默跳过，不影响搜索可用性。</item>
/// </list>
/// </remarks>
public static class ScrollToAnchorBehavior
{
    /// <summary>高亮脉冲持续时间（设计 §9.4：~800ms）。</summary>
    private static readonly Duration HighlightDuration = new(TimeSpan.FromMilliseconds(800));

    /// <summary>防重入标记：回写 <c>null</c> 清空信号时，避免再次进入定位逻辑。</summary>
    private static readonly DependencyProperty IsClearingProperty =
        DependencyProperty.RegisterAttached(
            "IsClearing",
            typeof(bool),
            typeof(ScrollToAnchorBehavior),
            new PropertyMetadata(false));

    /// <summary>
    /// 命中导航锚点信号（TwoWay 绑定到 <c>SettingsViewModel.PendingScrollAnchorId</c>）。
    /// 变更为非空字符串时触发定位 + 高亮；处理后被回写为 <c>null</c> 以清空信号。
    /// </summary>
    public static readonly DependencyProperty AnchorIdProperty =
        DependencyProperty.RegisterAttached(
            "AnchorId",
            typeof(string),
            typeof(ScrollToAnchorBehavior),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnAnchorIdChanged));

    public static string? GetAnchorId(DependencyObject d) => (string?)d.GetValue(AnchorIdProperty);

    public static void SetAnchorId(DependencyObject d, string? value) => d.SetValue(AnchorIdProperty, value);

    private static bool GetIsClearing(DependencyObject d) => (bool)d.GetValue(IsClearingProperty);

    private static void SetIsClearing(DependencyObject d, bool value) => d.SetValue(IsClearingProperty, value);

    private static void OnAnchorIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host)
        {
            return;
        }

        // 由本行为回写 null（清空信号）触发的变更，跳过避免重入（Req 2.7：空信号不触发跳转）。
        if (GetIsClearing(host))
        {
            return;
        }

        var anchorId = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(anchorId))
        {
            return;
        }

        // 命令已先切换分类（SelectedCategoryKey），延迟到布局完成后再定位，确保目标元素已可见并排版。
        host.Dispatcher.BeginInvoke(
            new Action(() => LocateAndHighlight(host, anchorId!)),
            DispatcherPriority.Background);
    }

    private static void LocateAndHighlight(FrameworkElement host, string anchorId)
    {
        try
        {
            var target = FindByAnchorId(host, anchorId);
            if (target is not null)
            {
                target.BringIntoView();
                PulseHighlight(target);
            }
        }
        catch
        {
            // best-effort：定位 / 高亮失败不影响搜索可用性。
        }
        finally
        {
            ClearSignal(host);
        }
    }

    /// <summary>处理完成后将信号回写为 <c>null</c>（经 TwoWay 绑定清空 <c>PendingScrollAnchorId</c>）。</summary>
    private static void ClearSignal(FrameworkElement host)
    {
        SetIsClearing(host, true);
        try
        {
            SetAnchorId(host, null);
        }
        finally
        {
            SetIsClearing(host, false);
        }
    }

    /// <summary>
    /// 在 <paramref name="root"/> 可视子树中按 AutomationId（优先）或元素 Name 查找目标 <see cref="FrameworkElement"/>。
    /// 跨 UserControl 边界遍历，匹配 <see cref="AutomationProperties"/> 的 AutomationId（与搜索索引锚点一致）。
    /// </summary>
    private static FrameworkElement? FindByAnchorId(DependencyObject root, string anchorId)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                var automationId = AutomationProperties.GetAutomationId(fe);
                if (string.Equals(automationId, anchorId, StringComparison.Ordinal) ||
                    string.Equals(fe.Name, anchorId, StringComparison.Ordinal))
                {
                    return fe;
                }
            }

            var found = FindByAnchorId(child, anchorId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 对目标元素施加短暂背景高亮脉冲（~800ms 后自动复原）。优先高亮就近的 <see cref="Border"/> 行容器，
    /// 否则尝试高亮目标自身（若为可设置背景的容器 / 控件）。全程 best-effort，不抛异常。
    /// </summary>
    private static void PulseHighlight(FrameworkElement target)
    {
        var highlightHost = FindHighlightHost(target);
        if (highlightHost is null)
        {
            return;
        }

        var highlightColor = ResolveHighlightColor(target);

        // 用独立的 SolidColorBrush 承载动画，避免触碰共享 / 冻结的资源画刷；动画结束后复原原背景。
        var brush = new SolidColorBrush(highlightColor);
        var animation = new ColorAnimation
        {
            From = highlightColor,
            To = Colors.Transparent,
            Duration = HighlightDuration,
            FillBehavior = FillBehavior.Stop,
        };

        switch (highlightHost)
        {
            case Border border:
                var originalBorderBg = border.Background;
                border.Background = brush;
                animation.Completed += (_, _) => border.Background = originalBorderBg;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                break;
            case Control control:
                var originalControlBg = control.Background;
                control.Background = brush;
                animation.Completed += (_, _) => control.Background = originalControlBg;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                break;
            case Panel panel:
                var originalPanelBg = panel.Background;
                panel.Background = brush;
                animation.Completed += (_, _) => panel.Background = originalPanelBg;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                break;
        }
    }

    /// <summary>向上查找可承载背景高亮的最近祖先（行容器 <see cref="Border"/> 优先），否则回退到目标自身。</summary>
    private static FrameworkElement? FindHighlightHost(FrameworkElement target)
    {
        DependencyObject? current = target;
        var hops = 0;
        while (current is not null && hops < 8)
        {
            if (current is Border border)
            {
                return border;
            }

            current = VisualTreeHelper.GetParent(current);
            hops++;
        }

        return target as Border ?? (FrameworkElement?)(target as Control) ?? target as Panel ?? target;
    }

    /// <summary>解析高亮色：优先取主题强调画刷颜色，缺失时回退到柔和蓝色，统一施加半透明。</summary>
    private static Color ResolveHighlightColor(FrameworkElement target)
    {
        var color = Color.FromRgb(0x4F, 0x9D, 0xF7);
        if (target.TryFindResource("PrimaryBrush") is SolidColorBrush accent)
        {
            color = accent.Color;
        }

        return Color.FromArgb(0x55, color.R, color.G, color.B);
    }
}
