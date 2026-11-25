using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Media;
using Dock.Model.Controls;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Shell;

public partial class SdkAvaloniaShellWindow : Window
{
    private readonly SdkDockFactory m_factory;
    private readonly AvaloniaGuiBinder m_guiBinder;
    private IRootDock m_layout;
    private int m_errorCount;

    public SdkAvaloniaShellWindow()
        : this(backgroundMode: false)
    {
    }

    public SdkAvaloniaShellWindow(bool backgroundMode, SdkDockFactory? factory = null, AvaloniaImageAtlas? atlas = null)
    {
        InitializeComponent();

        m_factory = factory ?? new SdkDockFactory(backgroundMode);
        m_guiBinder = new AvaloniaGuiBinder(atlas);

        if (backgroundMode)
        {
            ShowInTaskbar = false;
            ShowActivated = false;
        }

        ControlRecyclingDataTemplate.SetControlRecycling(m_dock_control, m_factory.ControlRecycling);
        m_dock_control.DataTemplates.Insert(0, new SdkDockViewDataTemplate(m_factory));
        m_layout = m_factory.CreateDefaultWorkspaceLayout();
        m_dock_control.Factory = m_factory;
        m_dock_control.Layout = m_layout;

        Opened += (_, _) => AvaloniaWindowStyle.StyleTitleBar(this);
        m_error_button.Click += (_, _) => ErrorLogRequested?.Invoke();
    }

    public SdkDockFactory DockFactory => m_factory;
    public AvaloniaGuiBinder GuiBinder => m_guiBinder;
    public Menu MainMenu => m_main_menu;
    public StackPanel MainToolbar => m_main_toolbar;
    public IRootDock DockLayout => m_layout;
    public int ErrorCount => m_errorCount;

    public event Action? ErrorLogRequested;

    public void SetPanelContent(string panelId, Control? content)
    {
        m_factory.SetPanelContent(panelId, content);
        if (m_dock_control.Layout is IRootDock root)
            m_factory.InitLayout(root);
    }

    public void SetLayout(IRootDock layout)
    {
        m_layout = layout ?? throw new ArgumentNullException(nameof(layout));
        m_dock_control.Layout = null;
        m_factory.DetachAllPanelViews();
        m_factory.InitLayout(m_layout);
        m_dock_control.Layout = m_layout;
    }

    public void ResetToDefaultLayout()
    {
        m_dock_control.Layout = null;
        m_factory.DetachAllPanelViews();
        SetLayout(m_factory.CreateDefaultWorkspaceLayout());
        SetStatus("Reference workspace layout active");
    }

    public byte[] SaveLayoutJson() => SdkDockLayoutSerializer.Serialize(m_layout);

    public void LoadLayoutJson(
        byte[] jsonBytes,
        IReadOnlyCollection<string>? requiredPanelIds = null,
        bool strictUnknownPanels = true)
    {
        var reqs = requiredPanelIds ?? SdkDockPanelIds.IdeRequiredPanelIds;
        var next = SdkDockLayoutSerializer.RestoreWithRollback(
            m_layout,
            jsonBytes,
            m_factory,
            m_factory.BackgroundMode,
            reqs,
            strictUnknownPanels);
        m_dock_control.Layout = null;
        m_factory.DetachAllPanelViews();
        m_layout = next;
        m_dock_control.Layout = m_layout;
        SetStatus("Workspace layout restored");
    }

    public void SetStatus(string text) => m_status_label.Text = text;
    public void SetCameraStatus(string text) => m_camera_label.Text = text;
    public void SetGridStatus(string text) => m_grid_label.Text = text;
    public Button ErrorButton => m_error_button;
    public string StatusText => m_status_label.Text ?? string.Empty;
    public string CameraStatusText => m_camera_label.Text ?? string.Empty;
    public string GridStatusText => m_grid_label.Text ?? string.Empty;

    public void SetErrorCount(int count)
    {
        m_errorCount = Math.Max(0, count);
        m_error_button.Content = $"Errors: {m_errorCount}";
    }

    public void SetErrorStatus(string text, IBrush? background = null)
    {
        m_error_button.Content = text;
        if (background != null)
            m_error_button.Background = background;
        else
            m_error_button.ClearValue(Button.BackgroundProperty);
    }

    public bool IsPanelVisible(string panelId) => m_factory.FindDockableById(m_layout, panelId) != null;
    public bool ShowPanel(string panelId, string defaultTitle) => m_factory.ShowPanel(m_layout, panelId, defaultTitle);
    public bool HidePanel(string panelId) => m_factory.HidePanel(m_layout, panelId);

    public MenuItem? FindMenuItem(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        MenuItem? Search(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is not MenuItem item)
                    continue;
                if (string.Equals(item.Name, name, StringComparison.Ordinal))
                    return item;
                if (item.Items.Count > 0 && Search(item.Items) is { } nested)
                    return nested;
            }
            return null;
        }

        return Search(m_main_menu.Items);
    }
}
