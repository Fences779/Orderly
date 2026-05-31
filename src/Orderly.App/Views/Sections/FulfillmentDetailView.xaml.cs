using System.Windows;
using Orderly.App.ViewModels;

namespace Orderly.App.Views.Sections;

public partial class FulfillmentDetailView : System.Windows.Controls.UserControl
{
    public FulfillmentDetailView()
    {
        InitializeComponent();
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
}
