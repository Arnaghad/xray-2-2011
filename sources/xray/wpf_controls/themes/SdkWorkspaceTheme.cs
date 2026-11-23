using System.Windows;
using System.Windows.Media;

namespace xray.editor.wpf_controls;

public static class SdkWorkspaceTheme
{
    public const string FontFamilyName = "Segoe UI";
    public const double FontSize = 12;
    public static FontFamily FontFamily { get; } = new(FontFamilyName);

    private static readonly bool highContrast = SystemParameters.HighContrast;

    public static Brush Surface { get; } = Palette(0xFFFFFF, SystemColors.WindowBrush);
    public static Brush Canvas { get; } = Palette(0xF5F8FB, SystemColors.WindowBrush);
    public static Brush Chrome { get; } = Palette(0xEAF0F5, SystemColors.ControlBrush);
    public static Brush Divider { get; } = Palette(0xD6E0E9, SystemColors.WindowTextBrush);
    public static Brush GridLine { get; } = Palette(0xE3EAF0, SystemColors.GrayTextBrush);
    public static Brush Axis { get; } = Palette(0x8093A5, SystemColors.WindowTextBrush);
    public static Brush Text { get; } = Palette(0x233447, SystemColors.WindowTextBrush);
    public static Brush MutedText { get; } = Palette(0x4D6175, SystemColors.WindowTextBrush);
    public static Brush Accent { get; } = Palette(0x176B80, SystemColors.HighlightBrush);
    public static Brush Hover { get; } = Palette(0xEEF5F8, SystemColors.HighlightBrush);
    public static Brush Selection { get; } = Palette(0xD9EBF2, SystemColors.HighlightBrush);
    public static Brush SelectionText { get; } = Palette(0x183B4C, SystemColors.HighlightTextBrush);
    public static Brush Warning { get; } = Palette(0xAB413F, SystemColors.WindowTextBrush);

    internal static Pen Stroke(Brush brush, double thickness = 1)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush Palette(int color, Brush highContrastBrush)
    {
        if (highContrast)
            return highContrastBrush;

        var brush = new SolidColorBrush(Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color));
        brush.Freeze();
        return brush;
    }
}
