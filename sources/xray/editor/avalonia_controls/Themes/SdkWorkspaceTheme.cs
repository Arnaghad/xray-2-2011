using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace xray.editor.avalonia_controls.Themes;

public static class SdkWorkspaceTheme
{
    public const string FontFamilyName = "Segoe UI";
    public const double FontSize = 12;

    public static FontFamily FontFamily { get; } = new(FontFamilyName);

    public static Color SurfaceColor { get; } = Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static Color CanvasColor { get; } = Color.FromRgb(0xF5, 0xF8, 0xFB);
    public static Color ChromeColor { get; } = Color.FromRgb(0xEA, 0xF0, 0xF5);
    public static Color DividerColor { get; } = Color.FromRgb(0xD6, 0xE0, 0xE9);
    public static Color GridLineColor { get; } = Color.FromRgb(0xE3, 0xEA, 0xF0);
    public static Color AxisColor { get; } = Color.FromRgb(0x80, 0x93, 0xA5);
    public static Color TextColor { get; } = Color.FromRgb(0x23, 0x34, 0x47);
    public static Color MutedTextColor { get; } = Color.FromRgb(0x4D, 0x61, 0x75);
    public static Color AccentColor { get; } = Color.FromRgb(0x17, 0x6B, 0x80);
    public static Color HoverColor { get; } = Color.FromRgb(0xEE, 0xF5, 0xF8);
    public static Color SelectionColor { get; } = Color.FromRgb(0xD9, 0xEB, 0xF2);
    public static Color SelectionTextColor { get; } = Color.FromRgb(0x18, 0x3B, 0x4C);
    public static Color WarningColor { get; } = Color.FromRgb(0xAB, 0x41, 0x3F);

    public static IBrush Surface { get; } = new ImmutableSolidColorBrush(SurfaceColor);
    public static IBrush Canvas { get; } = new ImmutableSolidColorBrush(CanvasColor);
    public static IBrush Chrome { get; } = new ImmutableSolidColorBrush(ChromeColor);
    public static IBrush Divider { get; } = new ImmutableSolidColorBrush(DividerColor);
    public static IBrush GridLine { get; } = new ImmutableSolidColorBrush(GridLineColor);
    public static IBrush Axis { get; } = new ImmutableSolidColorBrush(AxisColor);
    public static IBrush Text { get; } = new ImmutableSolidColorBrush(TextColor);
    public static IBrush MutedText { get; } = new ImmutableSolidColorBrush(MutedTextColor);
    public static IBrush Accent { get; } = new ImmutableSolidColorBrush(AccentColor);
    public static IBrush Hover { get; } = new ImmutableSolidColorBrush(HoverColor);
    public static IBrush Selection { get; } = new ImmutableSolidColorBrush(SelectionColor);
    public static IBrush SelectionText { get; } = new ImmutableSolidColorBrush(SelectionTextColor);
    public static IBrush Warning { get; } = new ImmutableSolidColorBrush(WarningColor);

    public const string SurfaceBrushKey = "SdkSurfaceBrush";
    public const string CanvasBrushKey = "SdkCanvasBrush";
    public const string ChromeBrushKey = "SdkChromeBrush";
    public const string DividerBrushKey = "SdkDividerBrush";
    public const string TextBrushKey = "SdkTextBrush";
    public const string MutedTextBrushKey = "SdkMutedTextBrush";
    public const string AccentBrushKey = "SdkAccentBrush";
    public const string SelectionBrushKey = "SdkSelectionBrush";
    public const string WarningBrushKey = "SdkWarningBrush";
}
