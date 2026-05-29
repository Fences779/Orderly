using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Orderly.App.ViewModels;
using Orderly.Core.Models;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    private System.Threading.CancellationTokenSource? _copyToastCts;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        System.Windows.Application.Current.MainWindow = this;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        TrendTooltip.SizeChanged += TrendTooltip_SizeChanged;
        this.SizeChanged += MainWindow_SizeChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false, IsSwitchingSession: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void Btn_SelectDateRange_Click(object sender, RoutedEventArgs e)
    {
        Popup_DateRangePicker.IsOpen = true;
    }

    private void Btn_ClearDateRange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.StartAt = 0;
            vm.EndAt = 0;
            vm.LoadOrders.Execute(null);
        }
        Popup_DateRangePicker.IsOpen = false;
    }

    private void Btn_ApplyDateRange_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.LoadOrders.Execute(null);
        }
        Popup_DateRangePicker.IsOpen = false;
    }

    private void QuickFulfillmentUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && DataContext is MainViewModel vm)
        {
            if (element.DataContext is Orderly.Core.Models.StringNarrationOrderSummary orderSummary)
            {
                vm.SelectedStringNarrationOrder = orderSummary;
            }

            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus))
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void ContactCustomer_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show("正在拉起客户沟通渠道...", "联系客户", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ModifyInfo_Click(object sender, RoutedEventArgs e)
    {
        var textBox = this.FindName("Input_FulfillmentCarrier") as System.Windows.Controls.TextBox;
        if (textBox != null)
        {
            textBox.Focus();
            textBox.BringIntoView();
        }
    }

    private void CancelOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var result = System.Windows.MessageBox.Show("是否确认将该订单设为异常以便后台协调取消？", "取消订单确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                vm.StringNarrationFulfillmentStatusInput = "exception";
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (DetailPanel != null && DetailPanel.RenderTransform is System.Windows.Media.TranslateTransform tt)
            {
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var translateAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 40.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var sb = new System.Windows.Media.Animation.Storyboard();
                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, DetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                System.Windows.Media.Animation.Storyboard.SetTarget(translateAnim, DetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                sb.Children.Add(opacityAnim);
                sb.Children.Add(translateAnim);

                sb.Completed += (s, ev) =>
                {
                    vm.DismissStringNarrationDetailsForSession();
                    DetailPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                };

                sb.Begin();
            }
            else
            {
                vm.DismissStringNarrationDetailsForSession();
            }
        }
    }

    private async void StringNarrationOrdersList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (listBox.SelectedItem is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenStringNarrationOrderDetailAsync(summary);
        }
    }

    private async void ExceptionOrderCard_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBoxItem item || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null)
        {
            return;
        }

        if (item.DataContext is Orderly.Core.Models.StringNarrationOrderSummary summary)
        {
            await vm.OpenExceptionOrderDetailAsync(summary);
            e.Handled = true;
        }
    }

    private void SettingsTextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
        binding?.UpdateSource();

        if (System.Windows.Controls.Validation.GetHasError(textBox))
        {
            var validationError = System.Windows.Controls.Validation.GetErrors(textBox).FirstOrDefault()?.ErrorContent?.ToString();
            vm.ReportDeferredSettingsAutoSaveValidationError(
                string.IsNullOrWhiteSpace(validationError)
                    ? "当前输入无效，未自动保存。"
                    : $"当前输入无效，未自动保存：{validationError}");
            return;
        }

        vm.CommitDeferredSettingsAutoSave();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StringNarrationStatusMessage))
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.StringNarrationStatusMessage) && vm.StringNarrationStatusMessage.StartsWith("已复制"))
            {
                ShowCopyToast(vm.StringNarrationStatusMessage);
            }
        }
        else if (e.PropertyName == "StringNarrationWorkbenchDashboard")
        {
            Dispatcher.InvokeAsync(UpdateTrendChart);
        }
        else if (e.PropertyName == "CashflowHealthDashboardResult" || (e.PropertyName == nameof(MainViewModel.SelectedSection) && DataContext is MainViewModel vm && string.Equals(vm.SelectedSection, "现金流")))
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateCashflowTrendChart();
                UpdateDonutCharts();
            });
        }
    }

    private void ShowCopyToast(string message)
    {
        _copyToastCts?.Cancel();
        _copyToastCts = new System.Threading.CancellationTokenSource();
        var token = _copyToastCts.Token;

        Text_CopyToast.Text = message;
        Popup_CopyToast.IsOpen = true;

        System.Threading.Tasks.Task.Delay(1500, token).ContinueWith(t =>
        {
            if (!token.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    Popup_CopyToast.IsOpen = false;
                });
            }
        }, token);
    }

    private void StepperNode_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && DataContext is MainViewModel vm)
        {
            var targetStatus = element.Tag as string;
            if (!string.IsNullOrEmpty(targetStatus) && vm.SelectedStringNarrationOrderDetail is not null)
            {
                vm.StringNarrationFulfillmentStatusInput = targetStatus;
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
            }
        }
    }

    private void FulfillmentInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (DataContext is MainViewModel vm && vm.UpdateStringNarrationFulfillmentCommand.CanExecute(null))
            {
                if (sender is System.Windows.Controls.TextBox textBox)
                {
                    var binding = textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                vm.UpdateStringNarrationFulfillmentCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void CloseExceptionDetails_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (ExceptionDetailPanel != null && ExceptionDetailPanel.RenderTransform is System.Windows.Media.TranslateTransform tt)
            {
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var translateAnim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 40.0,
                    Duration = new Duration(System.TimeSpan.FromSeconds(0.2)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };

                var sb = new System.Windows.Media.Animation.Storyboard();
                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, ExceptionDetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

                System.Windows.Media.Animation.Storyboard.SetTarget(translateAnim, ExceptionDetailPanel);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                sb.Children.Add(opacityAnim);
                sb.Children.Add(translateAnim);

                sb.Completed += (s, ev) =>
                {
                    vm.DismissExceptionDetailsForSession();
                    ExceptionDetailPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                };

                sb.Begin();
            }
            else
            {
                vm.DismissExceptionDetailsForSession();
            }
        }
    }

    private async void JumpToOrderFulfillment_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedExceptionOrderDetail is not null)
        {
            var targetOrder = vm.SelectedExceptionOrderDetail;
            vm.SelectedSection = MainViewModel.SectionFulfillment;
            await vm.OpenStringNarrationOrderDetailAsync(targetOrder);
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string text && !string.IsNullOrEmpty(text))
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                ShowCopyToast("已复制");
            }
            catch (System.Exception)
            {
                // ignore
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void TrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTrendChart();
    }

    private void TrendTooltip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTrendTooltipPosition();
    }

    private void UpdateTrendChart()
    {
        if (DataContext is not MainViewModel vm) return;

        var trendItems = vm.StringNarrationWorkbenchTrendItems;
        if (trendItems == null || trendItems.Count == 0)
        {
            if (TrendPath != null) TrendPath.Visibility = Visibility.Collapsed;
            for (int i = 0; i < 7; i++)
            {
                var point = FindName($"TrendPoint{i}") as System.Windows.Shapes.Ellipse;
                if (point != null) point.Visibility = Visibility.Collapsed;
            }
            if (TrendTooltip != null) TrendTooltip.Visibility = Visibility.Collapsed;
            return;
        }

        double width = TrendCanvas.ActualWidth;
        double height = TrendCanvas.ActualHeight;

        if (width <= 0 || height <= 0) return;

        int pointCount = trendItems.Count;
        double colWidth = width / pointCount;

        double maxOrders = 0;
        foreach (var item in trendItems)
        {
            if (item.OrderCount > maxOrders)
            {
                maxOrders = item.OrderCount;
            }
        }

        if (maxOrders <= 0) maxOrders = 150;

        double chartHeight = height - 40;
        double topOffset = 25;

        var points = new PointCollection();
        for (int i = 0; i < pointCount && i < 7; i++)
        {
            var item = trendItems[i];
            double x = (i + 0.5) * colWidth;
            double y = height - (item.OrderCount / maxOrders) * chartHeight - 15;

            if (y < topOffset) y = topOffset;
            if (y > height - 10) y = height - 10;

            points.Add(new System.Windows.Point(x, y));

            var pointEllipse = FindName($"TrendPoint{i}") as System.Windows.Shapes.Ellipse;
            if (pointEllipse != null)
            {
                pointEllipse.Visibility = Visibility.Visible;
                System.Windows.Controls.Canvas.SetLeft(pointEllipse, x - pointEllipse.Width / 2);
                System.Windows.Controls.Canvas.SetTop(pointEllipse, y - pointEllipse.Height / 2);
            }
        }

        for (int i = pointCount; i < 7; i++)
        {
            var pointEllipse = FindName($"TrendPoint{i}") as System.Windows.Shapes.Ellipse;
            if (pointEllipse != null)
            {
                pointEllipse.Visibility = Visibility.Collapsed;
            }
        }

        if (TrendPath != null)
        {
            TrendPath.Visibility = Visibility.Visible;
            if (points.Count > 1)
            {
                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = points[0] };

                for (int i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[i];
                    var p1 = points[i + 1];

                    double controlOffset = (p1.X - p0.X) / 2.5;
                    var cp1 = new System.Windows.Point(p0.X + controlOffset, p0.Y);
                    var cp2 = new System.Windows.Point(p1.X - controlOffset, p1.Y);

                    var segment = new BezierSegment(cp1, cp2, p1, true);
                    figure.Segments.Add(segment);
                }

                geometry.Figures.Add(figure);
                TrendPath.Data = geometry;
            }
            else
            {
                TrendPath.Data = null;
            }
        }

        UpdateTrendTooltipPosition();
    }

    private void UpdateTrendTooltipPosition()
    {
        if (DataContext is not MainViewModel vm || TrendCanvas == null || TrendTooltip == null) return;

        var trendItems = vm.StringNarrationWorkbenchTrendItems;
        if (trendItems == null || trendItems.Count == 0)
        {
            TrendTooltip.Visibility = Visibility.Collapsed;
            return;
        }

        double width = TrendCanvas.ActualWidth;
        double height = TrendCanvas.ActualHeight;

        if (width <= 0 || height <= 0) return;

        int pointCount = trendItems.Count;
        double colWidth = width / pointCount;

        double maxOrders = 0;
        foreach (var item in trendItems)
        {
            if (item.OrderCount > maxOrders)
            {
                maxOrders = item.OrderCount;
            }
        }

        if (maxOrders <= 0) maxOrders = 150;

        double chartHeight = height - 40;
        double topOffset = 25;

        int tooltipIndex = 4;
        if (pointCount > tooltipIndex)
        {
            var targetItem = trendItems[tooltipIndex];
            double x = (tooltipIndex + 0.5) * colWidth;
            double y = height - (targetItem.OrderCount / maxOrders) * chartHeight - 15;

            if (y < topOffset) y = topOffset;
            if (y > height - 10) y = height - 10;

            TrendTooltip.Visibility = Visibility.Visible;

            if (Text_TooltipDate != null) Text_TooltipDate.Text = targetItem.Label;
            if (Text_TooltipRevenue != null) Text_TooltipRevenue.Text = $" ¥{targetItem.RevenueAmount:N2}";
            if (Text_TooltipOrders != null) Text_TooltipOrders.Text = $" {targetItem.OrderCount}";

            double tooltipWidth = TrendTooltip.ActualWidth > 0 ? TrendTooltip.ActualWidth : 120;
            double tooltipHeight = TrendTooltip.ActualHeight > 0 ? TrendTooltip.ActualHeight : 68;

            System.Windows.Controls.Canvas.SetLeft(TrendTooltip, x - tooltipWidth / 2);
            System.Windows.Controls.Canvas.SetTop(TrendTooltip, y - tooltipHeight - 12);
        }
        else
        {
            TrendTooltip.Visibility = Visibility.Collapsed;
        }
    }

    private void CashflowTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCashflowTrendChart();
    }

    private void CanvasIncomeDonut_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDonutCharts();
    }

    private void CanvasExpenseDonut_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDonutCharts();
    }

    private void UpdateCashflowTrendChart()
    {
        if (DataContext is not MainViewModel vm || CashflowTrendCanvas == null) return;

        var trendItems = vm.CashflowHealthTrendItems;
        if (trendItems == null || trendItems.Count == 0)
        {
            CashflowTrendCanvas.Children.Clear();
            return;
        }

        CashflowTrendCanvas.Children.Clear();

        double width = CashflowTrendCanvas.ActualWidth;
        double height = CashflowTrendCanvas.ActualHeight;

        if (width <= 0 || height <= 0) return;

        double maxVal = 1000;
        foreach (var item in trendItems)
        {
            double inc = (double)item.IncomeAmount;
            double exp = (double)Math.Abs(item.ExpenseAmount);
            double net = (double)Math.Abs(item.NetCashflowAmount);
            maxVal = Math.Max(maxVal, Math.Max(inc, Math.Max(exp, net)));
        }

        maxVal *= 1.15;

        double bottomPadding = 25;
        double chartHeight = height - bottomPadding;
        double zeroY = chartHeight / 2;
        double scale = (chartHeight / 2 - 10) / maxVal;

        double[] values = { maxVal, maxVal * 0.5, 0, -maxVal * 0.5, -maxVal };
        foreach (var val in values)
        {
            double y = zeroY - val * scale;

            var line = new System.Windows.Shapes.Line
            {
                X1 = 40,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = new SolidColorBrush(val == 0 ? System.Windows.Media.Color.FromRgb(220, 224, 230) : System.Windows.Media.Color.FromRgb(240, 242, 245)),
                StrokeThickness = val == 0 ? 1.5 : 1
            };
            if (val != 0)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 4 };
            }
            CashflowTrendCanvas.Children.Add(line);

            var yText = new System.Windows.Controls.TextBlock
            {
                Text = val == 0 ? "0" : $"{val:N0}",
                FontSize = 9,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(134, 142, 150)),
                Width = 35,
                TextAlignment = TextAlignment.Right
            };
            System.Windows.Controls.Canvas.SetLeft(yText, 0);
            System.Windows.Controls.Canvas.SetTop(yText, y - 6);
            CashflowTrendCanvas.Children.Add(yText);
        }

        int count = trendItems.Count;
        double chartWidth = width - 40;
        double colWidth = chartWidth / count;
        double barWidth = Math.Max(3, colWidth * 0.35);

        var netPoints = new PointCollection();
        var xLabelIndices = new System.Collections.Generic.HashSet<int>();

        if (count > 0)
        {
            int step = Math.Max(1, count / 5);
            for (int k = 0; k < count; k += step)
            {
                xLabelIndices.Add(k);
            }
            xLabelIndices.Add(count - 1);
        }

        for (int i = 0; i < count; i++)
        {
            var item = trendItems[i];
            double x = 40 + (i + 0.5) * colWidth;

            double incVal = (double)item.IncomeAmount;
            if (incVal > 0)
            {
                double barHeight = incVal * scale;
                var rect = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89)),
                    Width = barWidth,
                    Height = barHeight,
                    CornerRadius = new CornerRadius(barWidth / 2, barWidth / 2, 0, 0),
                    Opacity = 0.8
                };
                System.Windows.Controls.Canvas.SetLeft(rect, x - barWidth - 1);
                System.Windows.Controls.Canvas.SetTop(rect, zeroY - barHeight);
                CashflowTrendCanvas.Children.Add(rect);
            }

            double expVal = (double)Math.Abs(item.ExpenseAmount);
            if (expVal > 0)
            {
                double barHeight = expVal * scale;
                var rect = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 159, 10)),
                    Width = barWidth,
                    Height = barHeight,
                    CornerRadius = new CornerRadius(0, 0, barWidth / 2, barWidth / 2),
                    Opacity = 0.8
                };
                System.Windows.Controls.Canvas.SetLeft(rect, x + 1);
                System.Windows.Controls.Canvas.SetTop(rect, zeroY);
                CashflowTrendCanvas.Children.Add(rect);
            }

            double netVal = (double)item.NetCashflowAmount;
            double netY = zeroY - netVal * scale;
            netPoints.Add(new System.Windows.Point(x, netY));

            if (xLabelIndices.Contains(i) && !string.IsNullOrWhiteSpace(item.Date))
            {
                var xText = new System.Windows.Controls.TextBlock
                {
                    Text = item.Date,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(134, 142, 150)),
                    Width = colWidth * 2,
                    TextAlignment = TextAlignment.Center
                };
                System.Windows.Controls.Canvas.SetLeft(xText, x - colWidth);
                System.Windows.Controls.Canvas.SetTop(xText, chartHeight + 6);
                CashflowTrendCanvas.Children.Add(xText);
            }
        }

        if (netPoints.Count > 1)
        {
            var path = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 86, 214)),
                StrokeThickness = 2.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = System.Windows.Media.Brushes.Transparent
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = netPoints[0] };
            for (int i = 1; i < netPoints.Count; i++)
            {
                figure.Segments.Add(new LineSegment(netPoints[i], true));
            }
            geometry.Figures.Add(figure);
            path.Data = geometry;
            CashflowTrendCanvas.Children.Add(path);

            for (int i = 0; i < netPoints.Count; i++)
            {
                var p = netPoints[i];
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 86, 214)),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1.5
                };
                System.Windows.Controls.Canvas.SetLeft(ellipse, p.X - 3);
                System.Windows.Controls.Canvas.SetTop(ellipse, p.Y - 3);
                CashflowTrendCanvas.Children.Add(ellipse);
            }
        }
    }

    private void UpdateDonutCharts()
    {
        if (DataContext is not MainViewModel vm) return;

        UpdateSingleDonutChart(CanvasIncomeDonut, vm.CashflowIncomeBreakdown?.Items, new string[] { "#4D96FF", "#FFD93D", "#9B5DE5", "#FF6B6B" });
        UpdateSingleDonutChart(CanvasExpenseDonut, vm.CashflowExpenseBreakdown?.Items, new string[] { "#6BCB77", "#4D96FF", "#9B5DE5", "#FFD93D", "#FF6B6B" });
    }

    private void UpdateSingleDonutChart(System.Windows.Controls.Canvas canvas, IReadOnlyList<StringNarrationCashflowHealthDashboardBreakdownItem>? items, string[] colors)
    {
        if (canvas == null) return;
        canvas.Children.Clear();

        if (items == null || items.Count == 0)
        {
            DrawPlaceholderDonut(canvas);
            return;
        }

        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) / 2 - 8;
        double strokeThickness = 12;

        double currentAngle = -90;

        double totalPercent = 0;
        foreach (var item in items)
        {
            if (item.Percent > 0) totalPercent += (double)item.Percent;
        }

        if (totalPercent <= 0)
        {
            DrawPlaceholderDonut(canvas);
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double percent = (double)item.Percent;
            if (percent <= 0) continue;

            double sweepAngle = (percent / totalPercent) * 360;

            if (sweepAngle >= 359.9)
            {
                var fullCircle = new System.Windows.Shapes.Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Stroke = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i % colors.Length])),
                    StrokeThickness = strokeThickness
                };
                System.Windows.Controls.Canvas.SetLeft(fullCircle, centerX - radius);
                System.Windows.Controls.Canvas.SetTop(fullCircle, centerY - radius);
                canvas.Children.Add(fullCircle);
                break;
            }

            double nextAngle = currentAngle + sweepAngle;

            double rad1 = currentAngle * Math.PI / 180.0;
            double rad2 = nextAngle * Math.PI / 180.0;

            double x1 = centerX + radius * Math.Cos(rad1);
            double y1 = centerY + radius * Math.Sin(rad1);
            double x2 = centerX + radius * Math.Cos(rad2);
            double y2 = centerY + radius * Math.Sin(rad2);

            var path = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors[i % colors.Length])),
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new System.Windows.Point(x1, y1),
                IsClosed = false
            };

            var arc = new ArcSegment
            {
                Point = new System.Windows.Point(x2, y2),
                Size = new System.Windows.Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = sweepAngle > 180
            };

            figure.Segments.Add(arc);
            geometry.Figures.Add(figure);
            path.Data = geometry;

            canvas.Children.Add(path);
            currentAngle = nextAngle;
        }
    }

    private void DrawPlaceholderDonut(System.Windows.Controls.Canvas canvas)
    {
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double centerX = width / 2;
        double centerY = height / 2;
        double radius = Math.Min(width, height) / 2 - 8;
        double strokeThickness = 12;

        var fullCircle = new System.Windows.Shapes.Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 224, 230)),
            StrokeThickness = strokeThickness
        };
        System.Windows.Controls.Canvas.SetLeft(fullCircle, centerX - radius);
        System.Windows.Controls.Canvas.SetTop(fullCircle, centerY - radius);
        canvas.Children.Add(fullCircle);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCashflowTrendCardVisibility();
    }

    private void UpdateCashflowTrendCardVisibility()
    {
        bool shouldShow = this.WindowState == WindowState.Maximized || this.ActualWidth >= 1600;

        if (CashflowTrendCard != null)
        {
            CashflowTrendCard.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CashflowFirstRow != null)
        {
            CashflowFirstRow.Height = shouldShow ? new GridLength(0, GridUnitType.Auto) : new GridLength(2.2, GridUnitType.Star);
        }

        if (shouldShow)
        {
            if (CashflowTrendRow != null)
                CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            if (CashflowBreakdownRow != null)
                CashflowBreakdownRow.Height = new GridLength(0, GridUnitType.Auto);
        }
        else
        {
            if (CashflowTrendRow != null)
                CashflowTrendRow.Height = new GridLength(1, GridUnitType.Star);
            if (CashflowBreakdownRow != null)
                CashflowBreakdownRow.Height = new GridLength(3, GridUnitType.Star);
        }
    }
}
