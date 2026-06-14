using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Orderly.App.Helpers;

public static class DwmHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static void UpdateTitleBarColor(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            if (window.TryFindResource("PageBackgroundBrush") is SolidColorBrush brush)
            {
                var color = brush.Color;
                // COLORREF format is 0x00BBGGRR
                int colorRef = color.R | (color.G << 8) | (color.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));

                // Calculate brightness to set text color and dark mode flag
                double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
                int textColorRef = brightness > 0.5 ? 0x00000000 : 0x00FFFFFF; // Black for light, White for dark
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColorRef, sizeof(int));

                int useDark = brightness > 0.5 ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            }
        }
        catch
        {
            // Ignore if DWM attribute is not supported or dwmapi.dll is missing
        }
    }
}
