using System.Windows;
using System.Windows.Media;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
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
}
