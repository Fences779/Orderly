using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Orderly.App.Helpers;

public static class ScrollViewerHelper
{
    public static readonly DependencyProperty BindFadeEdgesProperty =
        DependencyProperty.RegisterAttached(
            "BindFadeEdges",
            typeof(bool),
            typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnBindFadeEdgesChanged));

    public static bool GetBindFadeEdges(DependencyObject obj)
    {
        return (bool)obj.GetValue(BindFadeEdgesProperty);
    }

    public static void SetBindFadeEdges(DependencyObject obj, bool value)
    {
        obj.SetValue(BindFadeEdgesProperty, value);
    }

    private static void OnBindFadeEdgesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            if ((bool)e.NewValue)
            {
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
                scrollViewer.Loaded += ScrollViewer_Loaded;
                
                // If it is already loaded, update immediately
                if (scrollViewer.IsLoaded)
                {
                    UpdateFadeEdges(scrollViewer);
                }
            }
            else
            {
                scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
                scrollViewer.Loaded -= ScrollViewer_Loaded;
            }
        }
    }

    private static void ScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            UpdateFadeEdges(scrollViewer);
        }
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            UpdateFadeEdges(scrollViewer);
        }
    }

    private static void UpdateFadeEdges(ScrollViewer scrollViewer)
    {
        var opacityMask = scrollViewer.OpacityMask as LinearGradientBrush;
        if (opacityMask == null)
        {
            return;
        }

        if (opacityMask.IsFrozen)
        {
            opacityMask = opacityMask.Clone();
            scrollViewer.OpacityMask = opacityMask;
        }

        double offset = scrollViewer.VerticalOffset;
        double maxOffset = scrollViewer.ScrollableHeight;

        GradientStop? topStop = null;
        GradientStop? bottomStop = null;

        foreach (var stop in opacityMask.GradientStops)
        {
            if (Math.Abs(stop.Offset - 0.0) < 0.001)
            {
                topStop = stop;
            }
            else if (Math.Abs(stop.Offset - 1.0) < 0.001)
            {
                bottomStop = stop;
            }
        }

        if (topStop != null)
        {
            var targetColor = offset <= 0 ? Colors.White : System.Windows.Media.Color.FromArgb(0, 255, 255, 255);
            if (topStop.Color != targetColor)
            {
                topStop.Color = targetColor;
            }
        }

        if (bottomStop != null)
        {
            var targetColor = offset >= maxOffset ? Colors.White : System.Windows.Media.Color.FromArgb(0, 255, 255, 255);
            if (bottomStop.Color != targetColor)
            {
                bottomStop.Color = targetColor;
            }
        }
    }
}
