using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using WeifenLuo.WinFormsUI.Docking;
using PropertyHost = xray.editor.wpf_controls.property_grid.host;

namespace xray.editor_base;

// C# adaptation of controls/sources/item_properties_panel_base.{h,cpp}
// (GSC Game World, 2010): DockContent + fill-docked original WPF host,
// selection/read-only/update operations and tool-panel docking areas.
internal sealed class SdkPropertyPanel : DockContent
{
    internal const string Identity = "xray.editor.controls.item_properties_panel_base";
    internal readonly PropertyHost Grid = new() { Dock = DockStyle.Fill, auto_update = false, Enabled = false };
    internal SdkPropertyPanel()
    {
        Text = "Item properties"; Name = "item_properties_panel_base";
        DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
        HideOnClose = true; MaximizeBox = false; MinimizeBox = false;
        Controls.Add(Grid);
    }
    protected override string GetPersistString() => Identity;
    internal void ShowProperties(object? value, bool readOnly = false)
    {
        Grid.read_only_mode = readOnly;
        if (value is object[] values) Grid.selected_objects = values; else Grid.selected_object = value;
    }
}

internal sealed class SdkViewPanel : DockContent
{
    internal const string Identity = "xray.editor_base.scene_view_panel";
    internal const string LegacyIdentity = "xray.editor.window_view";
    internal SdkViewPanel() { Text = "Scene"; DockAreas = DockAreas.Document; CloseButton = false; CloseButtonVisible = false; }
    protected override string GetPersistString() => Identity;
}

internal sealed class SdkFloatWindow : FloatWindow
{
    private readonly bool suppressActivation;
    internal SdkFloatWindow(DockPanel panel, DockPane pane, bool hiddenOwner) : base(panel, pane)
    {
        suppressActivation = hiddenOwner;
        if (hiddenOwner) { ShowInTaskbar = false; Opacity = 0; }
    }
    internal SdkFloatWindow(DockPanel panel, DockPane pane, System.Drawing.Rectangle bounds, bool hiddenOwner) : base(panel, pane, bounds)
    {
        suppressActivation = hiddenOwner;
        if (hiddenOwner) { ShowInTaskbar = false; Opacity = 0; }
    }
    protected override bool ShowWithoutActivation => suppressActivation || base.ShowWithoutActivation;
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SdkWindowStyle.StyleTitleBar(Handle);
    }
    protected override void SetVisibleCore(bool value) {
        if(suppressActivation){_=Handle;base.SetVisibleCore(false);}
        else base.SetVisibleCore(value);
    }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (suppressActivation) cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00080000; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED
            return cp;
        }
    }
}

internal sealed class SdkFloatWindowFactory : DockPanelExtender.IFloatWindowFactory
{
    private readonly bool background;
    internal SdkFloatWindowFactory(bool backgroundMode) => background = backgroundMode;
    public FloatWindow CreateFloatWindow(DockPanel dockPanel, DockPane pane) => new SdkFloatWindow(dockPanel, pane, background);
    public FloatWindow CreateFloatWindow(DockPanel dockPanel, DockPane pane, System.Drawing.Rectangle bounds) => new SdkFloatWindow(dockPanel, pane, bounds, background);
}

internal sealed class SdkDockLayout : IDisposable
{
    private readonly VS2015LightTheme theme;
    internal readonly DockPanel Panel;
    internal readonly SdkPropertyPanel Properties = new();
    private readonly SdkViewPanel document = new();
    private readonly Control viewport;
    private readonly Form owner;
    private readonly bool background;
    internal SdkDockLayout(Form window, Control view, bool hidden)
    {
        owner = window; viewport = view; background = hidden;
        theme = SdkWindowStyle.CreateDockTheme(hidden);
        Panel = new DockPanel { Dock = DockStyle.Fill, Theme = theme, DockBackColor = SdkWindowStyle.Canvas, DocumentStyle = DocumentStyle.DockingWindow, DockRightPortion = 280 };
        window.Controls.Add(Panel); window.Controls.SetChildIndex(Panel, 0);
        document.Controls.Add(view);
        document.Show(Panel, DockState.Document);
        Properties.Show(Panel, DockState.DockRight);
        // Automated background runs never create floating top-level windows.
        if (hidden) Properties.DockAreas &= ~DockAreas.Float;
        xray.editor.wpf_controls.utils.set_parent_form(Properties.Grid.property_grid, window);
        window.PerformLayout();
    }
    internal byte[] Save()
    {
        using var stream = new MemoryStream();
        Panel.SaveAsXml(stream, Encoding.UTF8, true);
        return stream.ToArray();
    }
    private static XDocument Parse(byte[] layout)
    {
        if (layout.Length > 1024 * 1024) throw new InvalidDataException("SDK layout exceeds 1 MiB");
        using var stream = new MemoryStream(layout);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private void Validate(byte[] layout)
    {
        var xml = Parse(layout);
        var ids = xml.Root?.Element("Contents")?.Elements("Content").Select(e => (string?)e.Attribute("PersistString")).ToArray();
        if (ids == null || ids.Length != 2 || ids.Count(id => id == SdkPropertyPanel.Identity) != 1
            || ids.Count(id => id is SdkViewPanel.Identity or SdkViewPanel.LegacyIdentity) != 1)
            throw new InvalidDataException("Layout must contain exactly the SDK scene and property panel");
        if (background && xml.Descendants().Attributes("DockState").Any(a => a.Value == "Float"))
            throw new InvalidDataException("Floating layout is unavailable in background runs");
        // Exercise the library's full schema/references before touching live HWNDs.
        using var window = new Form();
        using var validationTheme = new VS2015LightTheme();
        validationTheme.Extender.FloatWindowFactory = new SdkFloatWindowFactory(true);
        using var panel = new DockPanel { Dock = DockStyle.Fill, Theme = validationTheme, DocumentStyle = DocumentStyle.DockingWindow };
        using var properties = new SdkPropertyPanel();
        using var view = new SdkViewPanel();
        window.Controls.Add(panel);
        using var stream = new MemoryStream(layout);
        panel.LoadFromXml(stream, id => id switch {
            SdkPropertyPanel.Identity => properties,
            SdkViewPanel.Identity or SdkViewPanel.LegacyIdentity => view,
            _ => throw new InvalidDataException("Unknown SDK layout content")
        });
        foreach (var floatWindow in panel.FloatWindows.ToArray()) floatWindow.Dispose();
        if (view.DockState != DockState.Document || view.IsHidden)
            throw new InvalidDataException("Scene document must remain visible");
    }
    private void Apply(byte[] layout)
    {
        // Detach without Close/Dispose: selection, command history and viewport
        // remain the same instances throughout DockPanel's empty-panel loader.
        Panel.ActiveAutoHideContent = null;
        Properties.DockHandler.DockPanel = null;
        document.DockHandler.DockPanel = null;
        // Moving a tool pane or floating window can leave an empty container until its deferred close.
        // The XML loader indexes Panes and FloatWindows from zero, so remove these containers
        // synchronously after detaching their live contents.
        foreach (var floatWindow in Panel.FloatWindows.ToArray()) floatWindow.Dispose();
        foreach (var pane in Panel.Panes.ToArray()) pane.Dispose();
        using var stream = new MemoryStream(layout);
        Panel.LoadFromXml(stream, id => id switch {
            SdkPropertyPanel.Identity => Properties,
            SdkViewPanel.Identity or SdkViewPanel.LegacyIdentity => document,
            _ => throw new InvalidDataException("Unknown SDK layout content")
        });
        foreach (var floatWindow in Panel.FloatWindows.Where(fw => fw.NestedPanes.Count == 0).ToArray())
            floatWindow.Dispose();
        // DockWindow.OnLayout tests effective Visible, which is always false
        // under a hidden owner. Explicitly reset local visibility so an emptied
        // side window does not keep reserving space in background hosts.
        foreach (DockWindow dockWindow in Panel.DockWindows) {
            dockWindow.PerformLayout();
            dockWindow.Visible = dockWindow.VisibleNestedPanes.Count != 0;
        }
        Panel.PerformLayout();
        owner.PerformLayout();
    }
    internal void Restore(byte[] layout)
    {
        Validate(layout);
        var original = Save();
        var handle = viewport.Handle;
        try { Apply(layout); }
        catch { Apply(original); throw; }
        if (viewport.Handle != handle) throw new InvalidOperationException("Dock layout recreated the renderer HWND");
    }
    internal void SaveFile(string path)
    {
        var bytes = Save();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal void LoadFile(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("SDK layout exceeds 1 MiB");
        Restore(File.ReadAllBytes(path));
    }
    internal void ShowProperties() => Properties.Show(Panel, DockState.DockRight);
    public void Dispose()
    {
        Panel.ActiveAutoHideContent = null;
        foreach (var floatWindow in Panel.FloatWindows.ToArray()) floatWindow.Dispose();
        Properties.Dispose(); document.Dispose(); Panel.Dispose(); theme.Dispose();
    }
}
