using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WindowsPoint = System.Windows.Point;
using WindowsSize = System.Windows.Size;

namespace Orderly.App.Views.Sections;

public partial class CashflowBreakdownCards : System.Windows.Controls.UserControl
{
    private MainViewModel? _subscribedViewModel;

    public CashflowBreakdownCards()
    {
        InitializeComponent();
        DataContextChanged += CashflowBreakdownCards_DataContextChanged;
        Loaded += CashflowBreakdownCards_Loaded;
        Unloaded += CashflowBreakdownCards_Unloaded;
    }

    private void CashflowBreakdownCards_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as MainViewModel);
        UpdateDonutCharts();
    }

    private void CashflowBreakdownCards_Unloaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(null);
    }

    private void CashflowBreakdownCards_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeToViewModel(e.NewValue as MainViewModel);
        Dispatcher.InvokeAsync(UpdateDonutCharts);
    }

    private void SubscribeToViewModel(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _subscribedViewModel = viewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "CashflowHealthDashboardResult" || (e.PropertyName == nameof(MainViewModel.SelectedSection) && DataContext is MainViewModel vm && string.Equals(vm.SelectedSection, "现金流")))
        {
            Dispatcher.InvokeAsync(UpdateDonutCharts);
        }
    }

    private void CanvasIncomeDonut_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDonutCharts();
    }

    private void CanvasExpenseDonut_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDonutCharts();
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
                    Stroke = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(colors[i % colors.Length])),
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
                Stroke = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(colors[i % colors.Length])),
                StrokeThickness = strokeThickness,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new WindowsPoint(x1, y1),
                IsClosed = false
            };

            var arc = new ArcSegment
            {
                Point = new WindowsPoint(x2, y2),
                Size = new WindowsSize(radius, radius),
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
            Stroke = new SolidColorBrush(MediaColor.FromRgb(220, 224, 230)),
            StrokeThickness = strokeThickness
        };
        System.Windows.Controls.Canvas.SetLeft(fullCircle, centerX - radius);
        System.Windows.Controls.Canvas.SetTop(fullCircle, centerY - radius);
        canvas.Children.Add(fullCircle);
    }
}
