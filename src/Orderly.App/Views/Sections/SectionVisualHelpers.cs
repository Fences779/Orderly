using System.Windows;

namespace Orderly.App.Views.Sections;

/// <summary>
/// Shared visual-tree helpers for extracted section UserControls. These mirror the
/// helpers that previously lived on <see cref="MainWindow"/> so relocated event
/// handlers keep byte-for-byte identical behaviour.
/// </summary>
internal static class SectionVisualHelpers
{
    public static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
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

    /// <summary>
    /// Surfaces the shared window-level copy toast from inside an extracted control,
    /// preserving the original <c>CopyText_Click</c> behaviour (clipboard + toast).
    /// </summary>
    public static void ShowCopyToast(DependencyObject child, string message)
    {
        if (Window.GetWindow(child) is MainWindow main)
        {
            main.ShowCopyToastMessage(message);
        }
    }
}
