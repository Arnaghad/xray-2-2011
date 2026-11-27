using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace xray.editor.avalonia_controls.Shell.Docking;

public sealed class SdkDockControlRecycling : IControlRecycling
{
    private readonly SdkDockFactory m_factory;

    public SdkDockControlRecycling(SdkDockFactory factory)
    {
        m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public bool TryToUseIdAsKey { get; set; } = true;

    private static bool TryExtractPanelIdentity(object? data, out string panelId, out string? title)
    {
        switch (data)
        {
            case IDocument doc when !string.IsNullOrEmpty(doc.Id):
                panelId = doc.Id;
                title = doc.Title;
                return true;
            case ITool tool when !string.IsNullOrEmpty(tool.Id):
                panelId = tool.Id;
                title = tool.Title;
                return true;
            case SdkDockPanelContext ctx when !string.IsNullOrEmpty(ctx.PanelId):
                panelId = ctx.PanelId;
                title = ctx.Title;
                return true;
            default:
                panelId = string.Empty;
                title = null;
                return false;
        }
    }

    public bool TryGetValue(object? data, out object? control)
    {
        if (TryExtractPanelIdentity(data, out string panelId, out _))
        {
            var view = m_factory.GetPanelContent(panelId);
            if (view != null)
            {
                control = view;
                return true;
            }
        }

        control = null;
        return false;
    }

    public object? Build(object? data, object? existing, object? parent)
    {
        if (!TryExtractPanelIdentity(data, out string panelId, out string? title))
            return null;

        if (existing is Control existingCtrl &&
            ReferenceEquals(existingCtrl, m_factory.GetPanelContent(panelId)) &&
            existingCtrl.Parent != null &&
            ReferenceEquals(existingCtrl.Parent, parent))
        {
            return existingCtrl;
        }

        return m_factory.ResolveViewForPanelId(panelId, title);
    }

    public void Add(object data, object control)
    {
    }

    public void Clear()
    {
    }
}

public sealed class SdkDockPanelContext
{
    public string PanelId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
}

public sealed class SdkDockViewDataTemplate : IDataTemplate
{
    private readonly SdkDockFactory m_factory;

    public SdkDockViewDataTemplate(SdkDockFactory factory)
    {
        m_factory = factory;
    }

    public bool Match(object? data) =>
        data is SdkDockPanelContext ctx && !string.IsNullOrEmpty(ctx.PanelId);

    public Control? Build(object? param)
    {
        if (param is not SdkDockPanelContext ctx)
            return new Border();

        return m_factory.ResolveViewForPanelId(ctx.PanelId, ctx.Title);
    }
}

public sealed class SdkDockFactory : Factory
{
    private readonly Dictionary<string, Control> m_panelViews = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDockable> m_dockables = new(StringComparer.Ordinal);
    private readonly SdkDockControlRecycling m_controlRecycling;
    private bool m_backgroundMode;

    public SdkDockFactory(bool backgroundMode = false)
    {
        m_backgroundMode = backgroundMode;
        m_controlRecycling = new SdkDockControlRecycling(this);

        ContextLocator = new Dictionary<string, Func<object?>>();
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [SdkDockPanelIds.SceneView] = () => GetOrCreateSceneDocument(),
            [SdkDockPanelIds.LegacySceneView] = () => GetOrCreateSceneDocument(),
            [SdkDockPanelIds.Project] = () => GetOrCreateTool(SdkDockPanelIds.Project, "Project"),
            [SdkDockPanelIds.ObjectInspector] = () => GetOrCreateTool(SdkDockPanelIds.ObjectInspector, "Item properties"),
            [SdkDockPanelIds.Tools] = () => GetOrCreateTool(SdkDockPanelIds.Tools, "Tools"),
            [SdkDockPanelIds.SceneGraph] = () => GetOrCreateTool(SdkDockPanelIds.SceneGraph, "Scene graph"),
            [SdkDockPanelIds.CameraView] = () => GetOrCreateTool(SdkDockPanelIds.CameraView, "camera view"),
            [SdkDockPanelIds.AnimationCurveEditor] = () => GetOrCreateTool(SdkDockPanelIds.AnimationCurveEditor, "Animation curves"),
            [SdkDockPanelIds.TriangulationTester] = () => GetOrCreateTool(SdkDockPanelIds.TriangulationTester, "Triangulation")
        };
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () =>
            {
                var host = new HostWindow();
                ControlRecyclingDataTemplate.SetControlRecycling(host, m_controlRecycling);
                host.Opened += (_, _) => AvaloniaWindowStyle.StyleTitleBar(host);
                return host;
            }
        };
    }

    public IControlRecycling ControlRecycling => m_controlRecycling;

    public bool BackgroundMode
    {
        get => m_backgroundMode;
        set
        {
            m_backgroundMode = value;
            foreach (var d in m_dockables.Values)
            {
                d.CanFloat = !m_backgroundMode;
            }
        }
    }

    public void SetPanelContent(string panelId, Control? content)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (string.IsNullOrEmpty(normalized))
            return;

        if (content == null)
            m_panelViews.Remove(normalized);
        else
            m_panelViews[normalized] = content;
    }

    public Control? GetPanelContent(string panelId)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        return m_panelViews.TryGetValue(normalized, out var view) ? view : null;
    }

    internal Control ResolveViewForPanelId(string panelId, string? fallbackTitle = null)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (m_panelViews.TryGetValue(normalized, out var view))
        {
            DetachFromPreviousParent(view);
            return view;
        }

        return new Border
        {
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(fallbackTitle) ? normalized : fallbackTitle,
                Margin = new Avalonia.Thickness(8)
            }
        };
    }

    public void DetachAllPanelViews()
    {
        foreach (var view in m_panelViews.Values)
            DetachFromPreviousParent(view);
    }

    private static void DetachFromPreviousParent(Control view)
    {
        if (view.GetVisualParent() is { } visualParent)
            DetachParentObject(visualParent, view);
        if (view.Parent is { } logicalParent)
            DetachParentObject(logicalParent, view);
    }

    private static void DetachParentObject(object? parent, Control view)
    {
        switch (parent)
        {
            case DeferredContentControl dcc:
                dcc.Content = null;
                dcc.ClearPresentation();
                break;
            case ContentPresenter cp:
                if (ReferenceEquals(cp.Child, view) || ReferenceEquals(cp.Content, view))
                {
                    cp.Content = null;
                    cp.UpdateChild();
                }
                break;
            case ContentControl cc:
                if (ReferenceEquals(cc.Content, view))
                    cc.Content = null;
                break;
            case Decorator dec:
                if (ReferenceEquals(dec.Child, view))
                    dec.Child = null;
                break;
            case Panel panel:
                panel.Children.Remove(view);
                break;
        }
    }

    public Document GetOrCreateSceneDocument()
    {
        if (m_dockables.TryGetValue(SdkDockPanelIds.SceneView, out var existing) && existing is Document existingDoc)
            return existingDoc;

        var doc = new Document
        {
            Id = SdkDockPanelIds.SceneView,
            Title = "Scene",
            CanClose = false,
            CanFloat = !m_backgroundMode,
            CanPin = false
        };
        m_dockables[SdkDockPanelIds.SceneView] = doc;
        return doc;
    }

    public Tool GetOrCreateTool(string panelId, string defaultTitle)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (m_dockables.TryGetValue(normalized, out var existing) && existing is Tool existingTool)
            return existingTool;

        var tool = new Tool
        {
            Id = normalized,
            Title = defaultTitle,
            CanClose = true,
            CanFloat = !m_backgroundMode,
            CanPin = true
        };
        m_dockables[normalized] = tool;
        return tool;
    }

    public override IRootDock CreateLayout() => CreateDefaultWorkspaceLayout();

    public IRootDock CreateDefaultWorkspaceLayout()
    {
        var sceneDoc = GetOrCreateSceneDocument();
        var projectTool = GetOrCreateTool(SdkDockPanelIds.Project, "Project");
        var inspectorTool = GetOrCreateTool(SdkDockPanelIds.ObjectInspector, "Item properties");
        var toolsTool = GetOrCreateTool(SdkDockPanelIds.Tools, "Tools");

        var projectDock = new ToolDock
        {
            Id = "LeftTopToolDock",
            Alignment = Alignment.Left,
            Proportion = 0.75,
            VisibleDockables = CreateList<IDockable>(projectTool),
            ActiveDockable = projectTool,
            DefaultDockable = projectTool
        };

        var inspectorDock = new ToolDock
        {
            Id = "LeftBottomToolDock",
            Alignment = Alignment.Bottom,
            Proportion = 0.25,
            VisibleDockables = CreateList<IDockable>(inspectorTool),
            ActiveDockable = inspectorTool,
            DefaultDockable = inspectorTool
        };

        var leftPane = new ProportionalDock
        {
            Id = "LeftPane",
            Orientation = Orientation.Vertical,
            Proportion = 0.24,
            VisibleDockables = CreateList<IDockable>(
                projectDock,
                new ProportionalDockSplitter(),
                inspectorDock)
        };

        var centerDocDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            CanCloseLastDockable = false,
            Proportion = 0.54,
            VisibleDockables = CreateList<IDockable>(sceneDoc),
            ActiveDockable = sceneDoc,
            DefaultDockable = sceneDoc
        };

        var rightToolDock = new ToolDock
        {
            Id = "RightToolDock",
            Alignment = Alignment.Right,
            Proportion = 0.22,
            VisibleDockables = CreateList<IDockable>(toolsTool),
            ActiveDockable = toolsTool,
            DefaultDockable = toolsTool
        };

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftPane,
                new ProportionalDockSplitter(),
                centerDocDock,
                new ProportionalDockSplitter(),
                rightToolDock)
        };

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
        rootDock.ActiveDockable = mainLayout;
        rootDock.DefaultDockable = mainLayout;
        InitLayout(rootDock);
        return rootDock;
    }

    public IRootDock CreateMinimalSdkLayout()
    {
        var sceneDoc = GetOrCreateSceneDocument();
        var inspectorTool = GetOrCreateTool(SdkDockPanelIds.ObjectInspector, "Item properties");

        var centerDocDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            CanCloseLastDockable = false,
            Proportion = 0.78,
            VisibleDockables = CreateList<IDockable>(sceneDoc),
            ActiveDockable = sceneDoc,
            DefaultDockable = sceneDoc
        };

        var rightToolDock = new ToolDock
        {
            Id = "RightToolDock",
            Alignment = Alignment.Right,
            Proportion = 0.22,
            VisibleDockables = CreateList<IDockable>(inspectorTool),
            ActiveDockable = inspectorTool,
            DefaultDockable = inspectorTool
        };

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                centerDocDock,
                new ProportionalDockSplitter(),
                rightToolDock)
        };

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
        rootDock.ActiveDockable = mainLayout;
        rootDock.DefaultDockable = mainLayout;
        InitLayout(rootDock);
        return rootDock;
    }

    public override void InitLayout(IDockable layout)
    {
        NormalizeAndRegisterSubtree(layout);
        base.InitLayout(layout);
    }

    private void NormalizeAndRegisterSubtree(IDockable? root)
    {
        if (root == null)
            return;

        if (root is IDocument or ITool)
        {
            string normalized = SdkDockPanelIds.NormalizeId(root.Id);
            if (!string.IsNullOrEmpty(normalized))
            {
                root.Id = normalized;
                root.CanFloat = !m_backgroundMode;
                if (normalized == SdkDockPanelIds.SceneView)
                    root.CanClose = false;
                root.Context = new SdkDockPanelContext
                {
                    PanelId = normalized,
                    Title = root.Title ?? normalized
                };
                m_dockables[normalized] = root;
            }
        }

        if (root is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
                NormalizeAndRegisterSubtree(child);
        }

        if (root is IRootDock rootDock)
        {
            if (rootDock.HiddenDockables != null)
            {
                foreach (var hidden in rootDock.HiddenDockables)
                    NormalizeAndRegisterSubtree(hidden);
            }
            if (rootDock.Windows != null)
            {
                foreach (var win in rootDock.Windows)
                    NormalizeAndRegisterSubtree(win.Layout);
            }
        }
    }

    public IDockable? FindDockableById(IRootDock? root, string panelId)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (root == null || string.IsNullOrEmpty(normalized))
            return null;

        return FindDockable(root, d => string.Equals(SdkDockPanelIds.NormalizeId(d.Id), normalized, StringComparison.Ordinal));
    }

    public bool ShowPanel(IRootDock root, string panelId, string defaultTitle)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (string.IsNullOrEmpty(normalized))
            return false;

        var existing = FindDockableById(root, normalized);
        if (existing != null)
        {
            SetActiveDockable(existing);
            if (existing.Owner is IDock ownerDock)
                SetFocusedDockable(ownerDock, existing);
            return true;
        }

        if (root.HiddenDockables != null)
        {
            for (int i = 0; i < root.HiddenDockables.Count; ++i)
            {
                var hidden = root.HiddenDockables[i];
                if (string.Equals(SdkDockPanelIds.NormalizeId(hidden.Id), normalized, StringComparison.Ordinal))
                {
                    RestoreDockable(hidden);
                    SetActiveDockable(hidden);
                    return true;
                }
            }
        }

        var toolDock = FindDockable(root, d => d is IToolDock) as IToolDock;
        if (toolDock == null)
            return false;

        var tool = GetOrCreateTool(normalized, defaultTitle);
        AddDockable(toolDock, tool);
        SetActiveDockable(tool);
        return true;
    }

    public bool HidePanel(IRootDock root, string panelId)
    {
        string normalized = SdkDockPanelIds.NormalizeId(panelId);
        if (string.IsNullOrEmpty(normalized) || normalized == SdkDockPanelIds.SceneView)
            return false;

        var existing = FindDockableById(root, normalized);
        if (existing == null)
            return false;

        HideDockable(existing);
        return true;
    }
}
