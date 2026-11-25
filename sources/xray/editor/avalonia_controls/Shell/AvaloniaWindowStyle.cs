using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.Shell;

public static class AvaloniaWindowStyle
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);

    public static void StyleTitleBar(Window window)
    {
        if (window == null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
            return;

        SetWindowColor(handle, DwmwaBorderColor, SdkWorkspaceTheme.Divider);
        SetWindowColor(handle, DwmwaCaptionColor, SdkWorkspaceTheme.Surface);
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
        if (brush is not ISolidColorBrush solid)
            return;

        Color c = solid.Color;
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, in colorRef, sizeof(int));
    }
}
