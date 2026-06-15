using System.Windows;

namespace Orderly.App.Helpers;

public static class SettingsHelper
{
    public static readonly DependencyProperty IsSelectingStartupSectionProperty =
        DependencyProperty.RegisterAttached(
            "IsSelectingStartupSection",
            typeof(bool),
            typeof(SettingsHelper),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsSelectingStartupSection(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsSelectingStartupSectionProperty);
    }

    public static void SetIsSelectingStartupSection(DependencyObject obj, bool value)
    {
        obj.SetValue(IsSelectingStartupSectionProperty, value);
    }
}
