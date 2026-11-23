using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using xray.editor.controls;
using Palette = xray.editor.wpf_controls.SdkWorkspaceTheme;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace xray.editor_base;

internal static class SdkWindowStyle
{
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    internal static readonly Color Surface = DrawingColor(Palette.Surface);
    internal static readonly Color Canvas = DrawingColor(Palette.Canvas);
    internal static readonly Color Chrome = DrawingColor(Palette.Chrome);
    internal static readonly Color Divider = DrawingColor(Palette.Divider);
    internal static readonly Color Text = DrawingColor(Palette.Text);
    internal static readonly Color MutedText = DrawingColor(Palette.MutedText);
    internal static readonly Color Accent = DrawingColor(Palette.Accent);
    internal static readonly Color Hover = DrawingColor(Palette.Hover);
    internal static readonly Color Selection = DrawingColor(Palette.Selection);
    internal static readonly Color SelectionText = DrawingColor(Palette.SelectionText);
    internal static readonly Font UiFont = new(Palette.FontFamilyName, (float)(Palette.FontSize * 72 / 96));

    private static readonly ToolStripRenderer StripRenderer = SystemInformation.HighContrast
        ? new ToolStripSystemRenderer()
        : new WorkspaceStripRenderer { RoundedEdges = false };

    private static Color DrawingColor(System.Windows.Media.Brush brush)
    {
        var color = ((SolidColorBrush)brush).Color;
        return Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    internal static VS2015LightTheme CreateDockTheme(bool backgroundMode)
    {
        var theme = new VS2015LightTheme();
        theme.Extender.FloatWindowFactory = new SdkFloatWindowFactory(backgroundMode);
        if (SystemInformation.HighContrast)
            return theme;

        var palette = theme.ColorPalette;
        palette.MainWindowActive.Background = Canvas;
        palette.ToolWindowBorder = Divider;
        palette.ToolWindowSeparator = Divider;
        palette.ToolWindowCaptionActive.Background = Selection;
        palette.ToolWindowCaptionActive.Text = SelectionText;
        palette.ToolWindowCaptionActive.Button = SelectionText;
        palette.ToolWindowCaptionActive.Grip = Divider;
        palette.ToolWindowCaptionInactive.Background = Chrome;
        palette.ToolWindowCaptionInactive.Text = Text;
        palette.ToolWindowCaptionInactive.Button = MutedText;
        palette.ToolWindowCaptionInactive.Grip = Divider;
        palette.TabSelectedActive.Background = Selection;
        palette.TabSelectedActive.Text = SelectionText;
        palette.TabSelectedActive.Button = SelectionText;
        palette.TabSelectedInactive.Background = Surface;
        palette.TabSelectedInactive.Text = Text;
        palette.TabUnselected.Background = Canvas;
        palette.TabUnselected.Text = MutedText;
        palette.TabUnselectedHovered.Background = Hover;
        palette.TabUnselectedHovered.Text = Text;
        palette.ToolWindowTabSelectedActive.Background = Selection;
        palette.ToolWindowTabSelectedActive.Text = SelectionText;
        palette.ToolWindowTabSelectedInactive.Background = Surface;
        palette.ToolWindowTabSelectedInactive.Text = Text;
        palette.ToolWindowTabUnselected.Background = Canvas;
        palette.ToolWindowTabUnselected.Text = MutedText;
        palette.ToolWindowTabUnselectedHovered.Background = Hover;
        palette.ToolWindowTabUnselectedHovered.Text = Text;
        palette.AutoHideStripDefault.Background = Canvas;
        palette.AutoHideStripDefault.Border = Divider;
        palette.AutoHideStripDefault.Text = MutedText;
        palette.AutoHideStripHovered.Background = Selection;
        palette.AutoHideStripHovered.Border = Accent;
        palette.AutoHideStripHovered.Text = SelectionText;
        theme.Skin.DockPaneStripSkin.TextFont = UiFont;
        return theme;
    }

    internal static void StylePane(Control pane)
    {
        pane.Font = UiFont;
        pane.BackColor = Surface;
        pane.ForeColor = Text;
    }

    internal static void StyleStrip(ToolStrip strip)
    {
        strip.Font = UiFont;
        strip.Renderer = StripRenderer;
        strip.BackColor = strip is MenuStrip or ToolStripDropDown ? Surface : Chrome;
        strip.ForeColor = Text;
    }

    internal static void StyleTree(tree_view tree)
    {
        StylePane(tree);
        tree.BorderStyle = BorderStyle.None;
        tree.LineColor = Divider;
        tree.SelectionBackground = Selection;
        tree.SelectionForeground = SelectionText;

        var filter = tree.m_filter_panel;
        filter.BackColor = Canvas;
        filter.ForeColor = MutedText;
        filter.m_filter_text_box.BackColor = Surface;
        filter.m_filter_text_box.ForeColor = Text;
        var search = tree.m_search_panel;
        search.BackColor = Canvas;
        search.ForeColor = MutedText;
        search.m_search_text_box.BackColor = Surface;
        search.m_search_text_box.ForeColor = Text;
    }

    internal static void StyleTitleBar(nint handle)
    {
        if (SystemInformation.HighContrast || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        SetWindowColor(handle, DwmBorderColor, Divider);
        SetWindowColor(handle, DwmCaptionColor, Surface);
        SetWindowColor(handle, DwmTextColor, Text);
    }

    private static void SetWindowColor(nint handle, int attribute, Color color)
    {
        var colorRef = ColorTranslator.ToWin32(color);
        _ = DwmSetWindowAttribute(handle, attribute, in colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);

    private sealed class WorkspaceStripRenderer() : ToolStripProfessionalRenderer(new WorkspaceStripColors())
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderToolStripBorder(e);
                return;
            }

            var bounds = e.AffectedBounds;
            var y = e.ToolStrip is StatusStrip ? bounds.Top : bounds.Bottom - 1;
            using var border = new Pen(Divider);
            e.Graphics.DrawLine(border, bounds.Left, y, bounds.Right, y);
        }
    }

    private sealed class WorkspaceStripColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ToolStripGradientBegin => Chrome;
        public override Color ToolStripGradientMiddle => Chrome;
        public override Color ToolStripGradientEnd => Chrome;
        public override Color StatusStripGradientBegin => Chrome;
        public override Color StatusStripGradientEnd => Chrome;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Canvas;
        public override Color ImageMarginGradientMiddle => Canvas;
        public override Color ImageMarginGradientEnd => Canvas;
        public override Color MenuBorder => Divider;
        public override Color MenuItemBorder => Divider;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientMiddle => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color SeparatorDark => Divider;
        public override Color SeparatorLight => Chrome;
        public override Color ButtonSelectedBorder => Divider;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonCheckedGradientBegin => Selection;
        public override Color ButtonCheckedGradientMiddle => Selection;
        public override Color ButtonCheckedGradientEnd => Selection;
        public override Color ButtonCheckedHighlight => Selection;
        public override Color ButtonCheckedHighlightBorder => Accent;
        public override Color ButtonPressedBorder => Accent;
        public override Color ButtonPressedGradientBegin => Selection;
        public override Color ButtonPressedGradientMiddle => Selection;
        public override Color ButtonPressedGradientEnd => Selection;
        public override Color CheckBackground => Selection;
        public override Color CheckSelectedBackground => Selection;
        public override Color OverflowButtonGradientBegin => Chrome;
        public override Color OverflowButtonGradientMiddle => Chrome;
        public override Color OverflowButtonGradientEnd => Chrome;
    }
}
