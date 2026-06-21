using System.Windows;
using Orderly.Core.Models;
using Orderly.App.Views;

namespace Orderly.App;

public partial class App
{
    private static void ApplyWindowBounds(MainWindow window, AppPreferences preferences)
    {
        if (!preferences.RememberWindowBounds
            || double.IsNaN(preferences.WindowLeft)
            || double.IsNaN(preferences.WindowTop)
            || double.IsNaN(preferences.WindowWidth)
            || double.IsNaN(preferences.WindowHeight)
            || preferences.WindowWidth < 320
            || preferences.WindowHeight < 240)
        {
            return;
        }

        window.Width = preferences.WindowWidth;
        window.Height = preferences.WindowHeight;
        window.Left = preferences.WindowLeft;
        window.Top = preferences.WindowTop;
        EnsureWindowVisible(window);
    }

    private static void EnsureWindowVisible(Window window)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        if (window.Left + window.Width < virtualLeft
            || window.Top + window.Height < virtualTop
            || window.Left > virtualRight
            || window.Top > virtualBottom)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
