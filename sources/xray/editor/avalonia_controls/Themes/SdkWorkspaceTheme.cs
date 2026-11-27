using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace xray.editor.avalonia_controls.Themes;

public static class SdkWorkspaceTheme
{
    public const string FontFamilyName = "Segoe UI Variable Text, Segoe UI";
    public const double FontSize = 12;

    public static FontFamily FontFamily { get; } = new(FontFamilyName);

    public static Color SurfaceColor { get; } = Color.FromRgb(0x1B, 0x1F, 0x26);
    public static Color CanvasColor { get; } = Color.FromRgb(0x14, 0x17, 0x1C);
    public static Color ChromeColor { get; } = Color.FromRgb(0x21, 0x26, 0x2F);
    public static Color InputColor { get; } = Color.FromRgb(0x15, 0x19, 0x20);
    public static Color ElevatedColor { get; } = Color.FromRgb(0x28, 0x2F, 0x3B);
    public static Color DividerColor { get; } = Color.FromRgb(0x30, 0x38, 0x46);
    public static Color GridLineColor { get; } = Color.FromRgb(0x25, 0x2C, 0x37);
    public static Color AxisColor { get; } = Color.FromRgb(0x64, 0x74, 0x8B);
    public static Color TextColor { get; } = Color.FromRgb(0xE6, 0xEC, 0xF5);
    public static Color MutedTextColor { get; } = Color.FromRgb(0x93, 0xA1, 0xB5);
    public static Color AccentColor { get; } = Color.FromRgb(0x2D, 0x8C, 0xFF);
    public static Color HoverColor { get; } = Color.FromRgb(0x2A, 0x32, 0x40);
    public static Color SelectionColor { get; } = Color.FromRgb(0x1F, 0x3B, 0x5C);
    public static Color SelectionTextColor { get; } = Color.FromRgb(0xF2, 0xF7, 0xFF);
    public static Color WarningColor { get; } = Color.FromRgb(0xE5, 0x53, 0x53);
    public static Color SuccessColor { get; } = Color.FromRgb(0x34, 0xC7, 0x7B);

    public static IBrush Surface { get; } = new ImmutableSolidColorBrush(SurfaceColor);
    public static IBrush Canvas { get; } = new ImmutableSolidColorBrush(CanvasColor);
    public static IBrush Chrome { get; } = new ImmutableSolidColorBrush(ChromeColor);
    public static IBrush Input { get; } = new ImmutableSolidColorBrush(InputColor);
    public static IBrush Elevated { get; } = new ImmutableSolidColorBrush(ElevatedColor);
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
    public static IBrush Success { get; } = new ImmutableSolidColorBrush(SuccessColor);

    public const string SurfaceBrushKey = "SdkSurfaceBrush";
    public const string CanvasBrushKey = "SdkCanvasBrush";
    public const string ChromeBrushKey = "SdkChromeBrush";
    public const string InputBrushKey = "SdkInputBrush";
    public const string ElevatedBrushKey = "SdkElevatedBrush";
    public const string DividerBrushKey = "SdkDividerBrush";
    public const string TextBrushKey = "SdkTextBrush";
    public const string MutedTextBrushKey = "SdkMutedTextBrush";
    public const string AccentBrushKey = "SdkAccentBrush";
    public const string SelectionBrushKey = "SdkSelectionBrush";
    public const string WarningBrushKey = "SdkWarningBrush";
    public const string SuccessBrushKey = "SdkSuccessBrush";
}
