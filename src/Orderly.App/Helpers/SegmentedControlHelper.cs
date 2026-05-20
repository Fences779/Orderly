using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Point = System.Windows.Point;

namespace Orderly.App.Helpers;

public static class SegmentedControlHelper
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SegmentedControlHelper),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject obj, bool value)
    {
        obj.SetValue(IsEnabledProperty, value);
    }

    private static readonly DependencyProperty StatusChangedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "StatusChangedHandler",
            typeof(EventHandler),
            typeof(SegmentedControlHelper),
            new PropertyMetadata(null));

    private static readonly DependencyProperty IsSelectingProperty =
        DependencyProperty.RegisterAttached(
            "IsSelecting",
            typeof(bool),
            typeof(SegmentedControlHelper),
            new PropertyMetadata(false));

    private static bool GetIsSelecting(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsSelectingProperty);
    }

    private static void SetIsSelecting(DependencyObject obj, bool value)
    {
        obj.SetValue(IsSelectingProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox)
        {
            if ((bool)e.NewValue)
            {
                listBox.SelectionChanged += ListBox_SelectionChanged;
                listBox.Loaded += ListBox_Loaded;
                listBox.SizeChanged += ListBox_SizeChanged;
            }
            else
            {
                listBox.SelectionChanged -= ListBox_SelectionChanged;
                listBox.Loaded -= ListBox_Loaded;
                listBox.SizeChanged -= ListBox_SizeChanged;

                var oldHandler = listBox.GetValue(StatusChangedHandlerProperty) as EventHandler;
                if (oldHandler != null)
                {
                    listBox.ItemContainerGenerator.StatusChanged -= oldHandler;
                    listBox.SetValue(StatusChangedHandlerProperty, null);
                }
            }
        }
    }

    private static void ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            listBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateSliderPosition(listBox, false);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            SetIsSelecting(listBox, true);
            UpdateSliderPosition(listBox, true);

            listBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                SetIsSelecting(listBox, false);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static void ListBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            bool isSelecting = GetIsSelecting(listBox);
            UpdateSliderPosition(listBox, isSelecting);
        }
    }

    private static void UpdateSliderPosition(ListBox listBox, bool useAnimation)
    {
        if (listBox.SelectedItem == null)
        {
            var slider = listBox.Template.FindName("PART_Slider", listBox) as Border;
            if (slider != null)
            {
                slider.Visibility = Visibility.Collapsed;
            }
            return;
        }

        var selectedContainer = listBox.ItemContainerGenerator.ContainerFromItem(listBox.SelectedItem) as ListBoxItem;
        if (selectedContainer == null)
        {
            var oldHandler = listBox.GetValue(StatusChangedHandlerProperty) as EventHandler;
            if (oldHandler != null)
            {
                listBox.ItemContainerGenerator.StatusChanged -= oldHandler;
                listBox.SetValue(StatusChangedHandlerProperty, null);
            }

            EventHandler? handler = null;
            handler = (s, e) =>
            {
                if (listBox.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    listBox.ItemContainerGenerator.StatusChanged -= handler;
                    listBox.SetValue(StatusChangedHandlerProperty, null);
                    listBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateSliderPosition(listBox, useAnimation);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };

            listBox.SetValue(StatusChangedHandlerProperty, handler);
            listBox.ItemContainerGenerator.StatusChanged += handler;
            return;
        }

        var partSlider = listBox.Template.FindName("PART_Slider", listBox) as Border;
        if (partSlider == null)
        {
            return;
        }

        partSlider.Visibility = Visibility.Visible;

        var sliderParent = VisualTreeHelper.GetParent(partSlider) as UIElement;
        if (sliderParent == null)
        {
            return;
        }

        try
        {
            var transform = selectedContainer.TransformToAncestor(sliderParent);
            var relativePoint = transform.Transform(new Point(0, 0));

            double targetX = relativePoint.X;
            double targetWidth = selectedContainer.ActualWidth;

            if (targetWidth <= 0)
            {
                return;
            }

            var translateTransform = partSlider.RenderTransform as TranslateTransform;
            if (translateTransform == null)
            {
                translateTransform = new TranslateTransform();
                partSlider.RenderTransform = translateTransform;
            }

            if (useAnimation && partSlider.IsLoaded && partSlider.ActualWidth > 0)
            {
                var animX = new DoubleAnimation
                {
                    To = targetX,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                var animWidth = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                translateTransform.BeginAnimation(TranslateTransform.XProperty, animX);
                partSlider.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
            }
            else
            {
                translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                partSlider.BeginAnimation(FrameworkElement.WidthProperty, null);
                translateTransform.X = targetX;
                partSlider.Width = targetWidth;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
