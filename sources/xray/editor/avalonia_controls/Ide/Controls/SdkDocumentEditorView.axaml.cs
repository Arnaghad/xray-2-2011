using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Controls;

/// <summary>
/// Multi-document Dock.Avalonia editor host replacing WinForms document_editor_base.
/// </summary>
public partial class SdkDocumentEditorView : UserControl
{
    private readonly List<SdkDocumentBase> m_openedDocuments = new();
    private readonly DocumentEditorDockFactory m_factory;
    private readonly IRootDock m_layout;
    private readonly DocumentDock m_documentDock;
    private readonly ToolDock m_leftToolDock;
    private readonly ToolDock m_rightToolDock;
    private readonly SdkFileViewPanel m_viewPanel;
    private readonly SdkItemPropertiesPanel m_propertiesPanel;
    private readonly SdkToolbarPanel m_toolbarPanel;

    private SdkDocumentBase? m_activeDocument;
    private bool m_suppressFactoryCloseHook;

    public SdkDocumentEditorView()
        : this("document_editor", createBasePanels: true, backgroundMode: false)
    {
    }

    public SdkDocumentEditorView(string editorName, bool createBasePanels = true, bool backgroundMode = false)
    {
        InitializeComponent();

        EditorName = editorName;
        BackgroundMode = backgroundMode;

        m_viewPanel = new SdkFileViewPanel(this);
        m_propertiesPanel = new SdkItemPropertiesPanel(this);
        m_toolbarPanel = new SdkToolbarPanel(this);

        m_factory = new DocumentEditorDockFactory(this, backgroundMode);
        m_factory.RegisterPanelView(SdkFileViewPanel.PersistId, m_viewPanel);
        m_factory.RegisterPanelView(SdkItemPropertiesPanel.PersistId, m_propertiesPanel);
        m_factory.RegisterPanelView(SdkToolbarPanel.PersistId, m_toolbarPanel);

        m_layout = m_factory.BuildLayout(
            createBasePanels,
            out m_leftToolDock,
            out m_documentDock,
            out m_rightToolDock);

        m_documentDock.PropertyChanged += OnDocumentDockPropertyChanged;

        m_dock_control.DataTemplates.Insert(0, new DocumentEditorViewTemplate(m_factory));
        m_dock_control.Factory = m_factory;
        m_dock_control.Layout = m_layout;

        m_save_menu_item.Click += (_, _) => SaveActive();
        m_save_all_menu_item.Click += (_, _) => SaveAll();
        m_close_all_menu_item.Click += (_, _) => CloseAllDocuments();
        m_undo_menu_item.Click += (_, _) => Undo();
        m_redo_menu_item.Click += (_, _) => Redo();
        m_cut_menu_item.Click += (_, _) => Cut();
        m_copy_menu_item.Click += (_, _) => Copy();
        m_paste_menu_item.Click += (_, _) => Paste();
        m_delete_menu_item.Click += (_, _) => Delete();
        m_select_all_menu_item.Click += (_, _) => SelectAll();

        m_files_view_menu_item.Click += (_, _) => ToggleToolPanel(SdkFileViewPanel.PersistId);
        m_properties_menu_item.Click += (_, _) => ToggleToolPanel(SdkItemPropertiesPanel.PersistId);
        m_toolbar_menu_item.Click += (_, _) => ToggleToolPanel(SdkToolbarPanel.PersistId);
    }

    public string EditorName { get; }
    public bool BackgroundMode { get; }
    public bool IsSingleDocument { get; set; }
    public bool IsReuseSingleDocument { get; set; } = true;

    public IReadOnlyList<SdkDocumentBase> OpenedDocuments => m_openedDocuments;
    public SdkDocumentBase? ActiveDocument => m_activeDocument;
    public SdkFileViewPanel ViewPanel => m_viewPanel;
    public SdkItemPropertiesPanel PropertiesPanel => m_propertiesPanel;
    public SdkToolbarPanel ToolbarPanel => m_toolbarPanel;
    public IRootDock DockLayout => m_layout;
    public Menu MenuBar => m_menu_bar;

    public Func<SdkDocumentEditorView, SdkDocumentBase>? CreateDocumentFactory { get; set; }
    public event EventHandler<SdkDocumentBase>? DocumentCreated;
    public event EventHandler<SdkDocumentBase?>? ActiveDocumentChanged;

    public bool IsOpened(string docName) =>
        m_openedDocuments.Any(d => string.Equals(d.Name, docName, StringComparison.Ordinal));

    public SdkDocumentBase? LoadDocument(string fullPath, bool activate = true)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        var existing = m_openedDocuments.FirstOrDefault(d => string.Equals(d.Name, fullPath, StringComparison.Ordinal));
        if (existing != null)
        {
            if (activate)
                ActivateDocument(existing);
            return existing;
        }

        if (IsSingleDocument && m_activeDocument != null)
        {
            if (IsReuseSingleDocument)
            {
                if (!m_activeDocument.IsSaved)
                {
                    var choice = m_activeDocument.CheckSaved();
                    if (choice == SdkDocumentSaveChoice.Cancel)
                        return m_activeDocument;
                    if (choice == SdkDocumentSaveChoice.Yes)
                        m_activeDocument.Save();
                }

                m_activeDocument.Name = fullPath;
                m_activeDocument.Context = CreateDocumentContext(m_activeDocument);
                if (!m_activeDocument.SuppressLoading)
                    m_activeDocument.Load();
                m_activeDocument.IsSaved = true;
                if (activate)
                    ActivateDocument(m_activeDocument);
                return m_activeDocument;
            }

            if (!TryCloseDocument(m_activeDocument))
                return m_activeDocument;
        }

        var doc = CreateDocument();
        doc.Editor = this;
        doc.Name = fullPath;
        doc.Context = CreateDocumentContext(doc);
        if (!doc.SuppressLoading)
            doc.Load();
        doc.IsSaved = true;

        m_openedDocuments.Add(doc);
        m_factory.AddDockable(m_documentDock, doc);
        if (activate)
            ActivateDocument(doc);
        return doc;
    }

    public void ActivateDocument(SdkDocumentBase doc)
    {
        if (!m_openedDocuments.Contains(doc))
            return;

        m_factory.SetActiveDockable(doc);
        m_factory.SetFocusedDockable(m_documentDock, doc);
        OnDocumentActivated(doc);
    }

    public bool TryCloseDocument(SdkDocumentBase doc)
    {
        if (!m_openedDocuments.Contains(doc))
            return false;

        var choice = doc.CheckSaved();
        if (choice == SdkDocumentSaveChoice.Yes)
            doc.Save();
        else if (choice == SdkDocumentSaveChoice.Cancel)
            return false;

        var cancelArgs = new CancelEventArgs();
        doc.RaiseClosing(cancelArgs);
        if (cancelArgs.Cancel)
            return false;

        m_openedDocuments.Remove(doc);
        m_suppressFactoryCloseHook = true;
        try
        {
            m_factory.CloseDockable(doc);
        }
        finally
        {
            m_suppressFactoryCloseHook = false;
        }

        if (ReferenceEquals(m_activeDocument, doc))
        {
            var next = m_documentDock.ActiveDockable as SdkDocumentBase ?? m_openedDocuments.FirstOrDefault();
            OnDocumentActivated(next);
        }
        return true;
    }

    public void CloseAllDocuments()
    {
        foreach (var doc in m_openedDocuments.ToArray())
            TryCloseDocument(doc);
    }

    public void SaveActive() => m_activeDocument?.Save();

    public void SaveAll()
    {
        foreach (var doc in m_openedDocuments)
            doc.Save();
    }

    public void Undo() => m_activeDocument?.Undo();
    public void Redo() => m_activeDocument?.Redo();
    public void Cut() => m_activeDocument?.Copy(deleteSource: true);
    public void Copy() => m_activeDocument?.Copy(deleteSource: false);
    public void Paste() => m_activeDocument?.Paste();
    public void Delete() => m_activeDocument?.Delete();
    public void SelectAll() => m_activeDocument?.SelectAll();

    public bool ToggleToolPanel(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
            return false;

        var visibleTool = m_factory.FindDockable(m_layout, d => d is ITool && string.Equals(d.Id, panelId, StringComparison.Ordinal));
        if (visibleTool != null && m_layout.HiddenDockables?.Contains(visibleTool) != true)
        {
            m_factory.HideDockable(visibleTool);
            return false;
        }

        if (m_layout.HiddenDockables != null)
        {
            var hidden = m_layout.HiddenDockables.FirstOrDefault(h => string.Equals(h.Id, panelId, StringComparison.Ordinal));
            if (hidden != null)
            {
                m_factory.RestoreDockable(hidden);
                m_factory.SetActiveDockable(hidden);
                return true;
            }
        }

        var targetDock = string.Equals(panelId, SdkFileViewPanel.PersistId, StringComparison.Ordinal)
            ? m_leftToolDock
            : m_rightToolDock;
        var tool = m_factory.CreateToolForPanel(panelId);
        m_factory.AddDockable(targetDock, tool);
        m_factory.SetActiveDockable(tool);
        return true;
    }

    public bool IsToolPanelVisible(string panelId)
    {
        var tool = m_factory.FindDockable(m_layout, d => d is ITool && string.Equals(d.Id, panelId, StringComparison.Ordinal));
        if (tool == null)
            return false;
        return m_layout.HiddenDockables?.Contains(tool) != true;
    }

    protected virtual SdkDocumentBase CreateDocument()
    {
        var doc = CreateDocumentFactory != null ? CreateDocumentFactory(this) : new SdkDocumentBase(this);
        DocumentCreated?.Invoke(this, doc);
        return doc;
    }

    private void OnDocumentActivated(SdkDocumentBase? doc)
    {
        bool changed = !ReferenceEquals(m_activeDocument, doc);
        m_activeDocument = doc;
        if (doc != null)
        {
            m_viewPanel.TrackActiveItem(doc.Name);
            doc.RaiseActivated();
        }
        if (changed)
            ActiveDocumentChanged?.Invoke(this, doc);
    }

    private void OnDocumentDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(IDock.ActiveDockable), StringComparison.Ordinal) &&
            m_documentDock.ActiveDockable is SdkDocumentBase doc &&
            m_openedDocuments.Contains(doc))
        {
            OnDocumentActivated(doc);
        }
    }

    private static SdkDockPanelContext CreateDocumentContext(SdkDocumentBase doc) =>
        new() { PanelId = doc.Id ?? doc.Name, Title = doc.Title ?? doc.Name };

    private Control? ResolveView(string panelId, string? fallbackTitle)
    {
        if (m_factory.TryGetRegisteredPanelView(panelId, out var panelView) && panelView != null)
        {
            DetachFromPreviousParent(panelView);
            return panelView;
        }

        var doc = m_openedDocuments.FirstOrDefault(d =>
            string.Equals(d.Id, panelId, StringComparison.Ordinal) ||
            string.Equals(d.Name, panelId, StringComparison.Ordinal));
        if (doc?.ViewContent != null)
        {
            DetachFromPreviousParent(doc.ViewContent);
            return doc.ViewContent;
        }

        return new Border
        {
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(fallbackTitle) ? panelId : fallbackTitle,
                Margin = new global::Avalonia.Thickness(8)
            }
        };
    }

    private static void DetachFromPreviousParent(Control view)
    {
        DetachParentObject(view.Parent, view);
        if (view.GetVisualParent() is { } visualParent && !ReferenceEquals(visualParent, view.Parent))
            DetachParentObject(visualParent, view);
    }

    private static void DetachParentObject(object? parent, Control view)
    {
        switch (parent)
        {
            case ContentControl cc:
                if (ReferenceEquals(cc.Content, view))
                    cc.Content = null;
                break;
            case ContentPresenter cp:
                if (ReferenceEquals(cp.Content, view))
                    cp.Content = null;
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

    private sealed class DocumentEditorViewTemplate : IDataTemplate
    {
        private readonly DocumentEditorDockFactory m_factory;

        public DocumentEditorViewTemplate(DocumentEditorDockFactory factory) => m_factory = factory;

        public bool Match(object? data) =>
            data is SdkDockPanelContext ctx && !string.IsNullOrEmpty(ctx.PanelId);

        public Control? Build(object? param) =>
            param is SdkDockPanelContext ctx
                ? m_factory.Owner.ResolveView(ctx.PanelId, ctx.Title)
                : new Border();
    }

    private sealed class DocumentEditorDockFactory : Factory
    {
        private readonly SdkDocumentEditorView m_owner;
        private readonly bool m_backgroundMode;
        private readonly Dictionary<string, Control> m_panelViews = new(StringComparer.Ordinal);

        public DocumentEditorDockFactory(SdkDocumentEditorView owner, bool backgroundMode)
        {
            m_owner = owner;
            m_backgroundMode = backgroundMode;
            HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
            {
                [nameof(IDockWindow)] = () => new HostWindow()
            };
        }

        public SdkDocumentEditorView Owner => m_owner;

        public void RegisterPanelView(string panelId, Control view) => m_panelViews[panelId] = view;

        public bool TryGetRegisteredPanelView(string panelId, out Control? view) =>
            m_panelViews.TryGetValue(panelId, out view);

        public Tool CreateToolForPanel(string panelId)
        {
            string title = panelId switch
            {
                SdkFileViewPanel.PersistId => "Files viewer",
                SdkItemPropertiesPanel.PersistId => "Item properties",
                SdkToolbarPanel.PersistId => "Toolbar",
                _ => panelId
            };
            return new Tool
            {
                Id = panelId,
                Title = title,
                CanClose = true,
                CanFloat = !m_backgroundMode,
                CanPin = true,
                Context = new SdkDockPanelContext { PanelId = panelId, Title = title }
            };
        }

        public IRootDock BuildLayout(
            bool createBasePanels,
            out ToolDock leftToolDock,
            out DocumentDock documentDock,
            out ToolDock rightToolDock)
        {
            var filesTool = CreateToolForPanel(SdkFileViewPanel.PersistId);
            var propsTool = CreateToolForPanel(SdkItemPropertiesPanel.PersistId);
            var toolbarTool = CreateToolForPanel(SdkToolbarPanel.PersistId);

            leftToolDock = new ToolDock
            {
                Id = "EditorLeftToolDock",
                Alignment = Alignment.Left,
                Proportion = 0.24,
                VisibleDockables = createBasePanels ? CreateList<IDockable>(filesTool) : CreateList<IDockable>(),
                ActiveDockable = createBasePanels ? filesTool : null,
                DefaultDockable = createBasePanels ? filesTool : null
            };

            documentDock = new DocumentDock
            {
                Id = "EditorDocuments",
                IsCollapsable = false,
                CanCreateDocument = false,
                CanCloseLastDockable = true,
                Proportion = 0.52,
                VisibleDockables = CreateList<IDockable>()
            };

            rightToolDock = new ToolDock
            {
                Id = "EditorRightToolDock",
                Alignment = Alignment.Right,
                Proportion = 0.24,
                VisibleDockables = createBasePanels ? CreateList<IDockable>(propsTool, toolbarTool) : CreateList<IDockable>(),
                ActiveDockable = createBasePanels ? propsTool : null,
                DefaultDockable = createBasePanels ? propsTool : null
            };

            var mainLayout = new ProportionalDock
            {
                Id = "EditorMainLayout",
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(
                    leftToolDock,
                    new ProportionalDockSplitter(),
                    documentDock,
                    new ProportionalDockSplitter(),
                    rightToolDock)
            };

            var rootDock = CreateRootDock();
            rootDock.Id = "EditorRoot";
            rootDock.IsCollapsable = false;
            rootDock.VisibleDockables = CreateList<IDockable>(mainLayout);
            rootDock.ActiveDockable = mainLayout;
            rootDock.DefaultDockable = mainLayout;
            InitLayout(rootDock);
            return rootDock;
        }

        public override void CloseDockable(IDockable? dockable)
        {
            if (dockable is SdkDocumentBase doc && !m_owner.m_suppressFactoryCloseHook)
            {
                m_owner.TryCloseDocument(doc);
                return;
            }
            if (dockable != null)
                base.CloseDockable(dockable);
        }
    }
}
