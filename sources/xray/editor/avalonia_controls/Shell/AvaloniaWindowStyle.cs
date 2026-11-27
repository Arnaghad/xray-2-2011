using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.Shell;

public static class AvaloniaWindowStyle
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static class Win32Dwm
    {
        [DllImport("dwmapi.dll", ExactSpelling = true)]
        internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);
    }

    public static void StyleTitleBar(Window window)
    {
        if (window == null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
            return;

        int darkMode = 1;
        _ = Win32Dwm.DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, in darkMode, sizeof(int));
        SetWindowColor(handle, DwmwaBorderColor, SdkWorkspaceTheme.Divider);
        SetWindowColor(handle, DwmwaCaptionColor, SdkWorkspaceTheme.Canvas);
        SetWindowColor(handle, DwmwaTextColor, SdkWorkspaceTheme.Text);
    }

    public static void EnableCustomChrome(Window window)
    {
        if (window == null)
            return;

        window.ExtendClientAreaToDecorationsHint = true;
    }

    private static void SetWindowColor(nint handle, int attribute, IBrush brush)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || brush is not ISolidColorBrush solid)
            return;

        Color c = solid.Color;
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        _ = Win32Dwm.DwmSetWindowAttribute(handle, attribute, in colorRef, sizeof(int));
    }
}
