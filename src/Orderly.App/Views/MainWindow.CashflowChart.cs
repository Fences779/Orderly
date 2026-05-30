using System;
using System.Windows;
using System.Windows.Media;
using Orderly.App.ViewModels;

namespace Orderly.App.Views;

public partial class MainWindow : Window
{
    private void CashflowTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCashflowTrendChart();
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
}
