#pragma warning disable WFO1000

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfGraph = xray.editor.wpf_controls.hypergraph;
using WpfPropertyHost = xray.editor.wpf_controls.property_grid.host;

namespace xray.editor_base;

public sealed class SdkDialogNodePropertyAdapter
{
    private readonly SdkDialogDocument _doc;
    private readonly uint _nodeId;

    public SdkDialogNodePropertyAdapter(SdkDialogDocument doc, uint nodeId)
    {
        _doc = doc;
        _nodeId = nodeId;
    }

    private SdkDialogNode Node => _doc.Nodes[_nodeId];

    [Category("Identity"), ReadOnly(true)]
    public uint Id => Node.Id;

    [Category("Node")]
    public bool IsRoot
    {
        get => Node.IsRoot;
        set => _doc.SetNodeProperties(_nodeId, value, Node.IsDialog, Node.StringTableId);
    }

    [Category("Node")]
    public bool IsDialogContainer
    {
        get => Node.IsDialog;
        set => _doc.SetNodeProperties(_nodeId, Node.IsRoot, value, Node.StringTableId);
    }

    [Category("Localization")]
    public string StringTableOrSubDialog
    {
        get => Node.StringTableId;
        set => _doc.SetNodeProperties(_nodeId, Node.IsRoot, Node.IsDialog, value);
    }

    [Category("Localization")]
    public string Text
    {
        get => Node.IsDialog ? string.Empty : _doc.Localization.GetText(Node.StringTableId);
        set
        {
            if (!Node.IsDialog && !string.IsNullOrEmpty(Node.StringTableId))
                _doc.SetLocalizedText(Node.StringTableId, value);
        }
    }

    [Category("Localization")]
    public SdkDialogTextCategory Category
    {
        get => Node.IsDialog ? SdkDialogTextCategory.None : _doc.Localization.GetCategory(Node.StringTableId);
        set
        {
            if (!Node.IsDialog && !string.IsNullOrEmpty(Node.StringTableId))
                _doc.Localization.SetCategory(Node.StringTableId, value);
        }
    }
}

public sealed class SdkDialogEditor : DockContent
{
    public const string PersistIdentity = "xray.editor.sdk_dialog_editor";

    private readonly FlowLayoutPanel _topBar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
    private readonly ComboBox _languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ListBox _linksList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView _stringsGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly ListBox _compiledTestLog = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 26, AutoEllipsis = true };

    private bool _refreshing;
    private bool _disposed;
    private uint _selectedNodeId;
    private SdkDialogDocument? _document;
    private SdkDialogLocalizationStorage _localization = SdkDialogLocalizationStorage.CreateEmpty();
    private SdkCompiledGameDialog? _lastCompiled;
    private SdkDialogRuntimeState _runtimeState = new();

    public WpfGraph.hypergraph_host GraphHost { get; } = new() { Dock = DockStyle.Fill };
    public WpfPropertyHost PropertyGridHost { get; } = new() { Dock = DockStyle.Fill, auto_update = false };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkDialogDocument? Document => _document;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkDialogLocalizationStorage LocalizationStorage => _localization;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public uint SelectedNodeId => _selectedNodeId;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkCompiledGameDialog? LastCompiledDialog => _lastCompiled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkDialogRuntimeState RuntimeState => _runtimeState;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, DialogResult>? SavePromptOverride { get; set; }

    public event System.Action<SdkDialogDocument>? DocumentSaved;

    public SdkDialogEditor()
    {
        Text = "Dialog editor";
        ClientSize = new Size(1240, 780);
        DockAreas = DockAreas.Document | DockAreas.Float;
        HideOnClose = true;

        void AddBtn(string text, System.Action act)
        {
            var btn = new Button { Text = text, AutoSize = true };
            btn.Click += (_, _) =>
            {
                try { act(); }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            };
            _topBar.Controls.Add(btn);
        }

        AddBtn("Save", SaveCurrentDocument);
        AddBtn("Undo", () => _document?.Commands.rollback(-1));
        AddBtn("Redo", () => _document?.Commands.rollback(1));
        AddBtn("+ Phrase", () =>
        {
            if (_document == null) return;
            var added = _document.AddNode(_document.Nodes.Count == 0, false, "New dialog phrase");
            SelectNode(added.Id);
        });
        AddBtn("+ Sub-Dialog", () =>
        {
            if (_document == null) return;
            var added = _document.AddNode(false, true, "zaton/test_zaton");
            SelectNode(added.Id);
        });
        AddBtn("Compile & Test", () => _ = CompileAndResetRuntime());
        AddBtn("Export XLSX", () => ExportCurrentLocalizationPrompt(xlsx: true));
        AddBtn("Import Excel", () => ImportLocalizationPrompt());

        _languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || _languageCombo.SelectedItem is not string lang) return;
            _localization.CurrentLanguage = lang;
            RebuildAll();
        };
        _topBar.Controls.Add(new Label { Text = "Language:", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
        _topBar.Controls.Add(_languageCombo);

        _stringsGrid.Columns.Add("Id", "String ID");
        _stringsGrid.Columns.Add("Category", "Text Type");
        _stringsGrid.Columns.Add("Origin", "Original Text");
        _stringsGrid.Columns.Add("Translation", "Current Language");
        _stringsGrid.Columns[0].ReadOnly = true;
        _stringsGrid.Columns[1].ReadOnly = true;
        _stringsGrid.CellEndEdit += OnStringsGridCellEdited;

        var graph = GraphHost.hypergraph;
        graph.is_nodes_moveable = true;
        graph.is_linkable = false;
        graph.selecting += (_, e) =>
        {
            if (_refreshing || _document == null) return;
            if (e.selected_node_ids.Count > 0 && uint.TryParse(e.selected_node_ids[0], out uint nid) && _document.Nodes.ContainsKey(nid))
                SelectNode(nid);
        };

        var bottomTabs = new TabControl { Dock = DockStyle.Fill };
        var stringsTab = new TabPage("Localization Strings");
        stringsTab.Controls.Add(_stringsGrid);
        var linksTab = new TabPage("Links / Preconditions / Actions");
        linksTab.Controls.Add(_linksList);
        var compileTab = new TabPage("Compiled Game Dialog");
        compileTab.Controls.Add(_compiledTestLog);
        bottomTabs.TabPages.Add(stringsTab);
        bottomTabs.TabPages.Add(linksTab);
        bottomTabs.TabPages.Add(compileTab);

        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 400 };
        leftSplit.Panel1.Controls.Add(GraphHost);
        leftSplit.Panel2.Controls.Add(bottomTabs);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 880 };
        mainSplit.Panel1.Controls.Add(leftSplit);
        mainSplit.Panel2.Controls.Add(PropertyGridHost);

        Controls.Add(mainSplit);
        Controls.Add(_topBar);
        Controls.Add(_statusLabel);
    }

    protected override string GetPersistString() => PersistIdentity;

    public void LoadDocument(SdkDialogDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (_document != null && !ReferenceEquals(_document, doc))
        {
            _document.Changed -= RebuildAll;
            _document.Dispose();
        }
        _document = doc;
        _localization = doc.Localization;
        _document.Changed += RebuildAll;
        _selectedNodeId = _document.Nodes.Keys.FirstOrDefault();
        _lastCompiled = null;
        RebuildAll();
    }

    public SdkDialogDocument CreateNew(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        var doc = SdkDialogDocument.CreateNew(pathOrVirtual, localization);
        LoadDocument(doc);
        return doc;
    }

    public SdkDialogDocument OpenFile(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        var doc = SdkDialogDocument.Load(pathOrVirtual, localization);
        LoadDocument(doc);
        return doc;
    }

    public void SaveCurrentDocument()
    {
        if (_document == null) return;
        _document.Save();
        DocumentSaved?.Invoke(_document);
        RefreshStatus();
    }

    public void SelectNode(uint nodeId)
    {
        if (_document == null || !_document.Nodes.ContainsKey(nodeId)) return;
        _selectedNodeId = nodeId;
        PropertyGridHost.selected_object = new SdkDialogNodePropertyAdapter(_document, nodeId);
        GraphHost.hypergraph.set_selected_nodes(new List<string> { nodeId.ToString() });
    }

    public SdkCompiledGameDialog CompileAndResetRuntime(SdkDialogRuntimeState? state = null)
    {
        if (_document == null)
            throw new InvalidOperationException("No dialog document is loaded.");

        _lastCompiled = SdkCompiledGameDialog.Compile(_document, _localization);
        _runtimeState = state ?? new SdkDialogRuntimeState();

        _compiledTestLog.Items.Clear();
        _compiledTestLog.Items.Add($"Compiled '{_lastCompiled.Name}': {_lastCompiled.Nodes.Count} nodes, {_lastCompiled.Links.Count} links, {_lastCompiled.RootIds.Count} root(s)");
        foreach (uint rootId in _lastCompiled.RootIds)
        {
            _compiledTestLog.Items.Add($"  Root #{rootId}: \"{_lastCompiled.GetText(rootId)}\"");
            foreach (var lnk in _lastCompiled.GetAvailableLinks(rootId, _runtimeState))
                _compiledTestLog.Items.Add($"    -> #{lnk.DstRuntimeId}: \"{_lastCompiled.GetText(lnk.DstRuntimeId)}\"");
        }
        RefreshStatus();
        return _lastCompiled;
    }

    private void ExportCurrentLocalizationPrompt(bool xlsx)
    {
        string targetLang = _localization.Languages.FirstOrDefault(l => !string.Equals(l, "origin", StringComparison.OrdinalIgnoreCase)) ?? "english";
        string baseDir = !string.IsNullOrWhiteSpace(_localization.TextsDirectory) ? Path.Combine(_localization.TextsDirectory, "batches") : Path.GetTempPath();
        Directory.CreateDirectory(baseDir);
        using var sfd = new SaveFileDialog
        {
            InitialDirectory = baseDir,
            FileName = $"dialog_{targetLang}.{(xlsx ? "xlsx" : "xls")}",
            Filter = xlsx ? "Excel Workbook (*.xlsx)|*.xlsx" : "SpreadsheetML (*.xls)|*.xls"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        var res = SdkDialogExcelExchange.Export(_localization, targetLang, sfd.FileName);
        _statusLabel.Text = $"Exported {res.RowCount} strings to {Path.GetFileName(res.WorkbookPath)}";
    }

    private void ImportLocalizationPrompt()
    {
        using var ofd = new OpenFileDialog
        {
            InitialDirectory = !string.IsNullOrWhiteSpace(_localization.TextsDirectory) ? _localization.TextsDirectory : Path.GetTempPath(),
            Filter = "Excel Localization|*.xlsx;*.xls"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        string targetLang = _localization.CurrentLanguage;
        foreach (string lang in _localization.Languages)
        {
            if (Path.GetFileNameWithoutExtension(ofd.FileName).EndsWith("_" + lang, StringComparison.OrdinalIgnoreCase))
            {
                targetLang = lang;
                break;
            }
        }
        var res = SdkDialogExcelExchange.Import(_localization, targetLang, ofd.FileName);
        if (!string.IsNullOrWhiteSpace(_localization.TextsDirectory))
            _localization.Save();
        RebuildAll();
        _statusLabel.Text = $"Imported {res.UpdatedCount} localized strings for '{targetLang}'";
    }

    private void OnStringsGridCellEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing || _document == null || e.RowIndex < 0) return;
        var row = _stringsGrid.Rows[e.RowIndex];
        string? strId = row.Cells[0].Value as string;
        if (string.IsNullOrEmpty(strId)) return;
        if (e.ColumnIndex == 2)
            _document.SetLocalizedText(strId, row.Cells[2].Value?.ToString() ?? string.Empty, "origin");
        else if (e.ColumnIndex == 3)
            _document.SetLocalizedText(strId, row.Cells[3].Value?.ToString() ?? string.Empty, _localization.CurrentLanguage);
    }

    public bool ConfirmClose()
    {
        if (_document != null && _document.IsDirty)
        {
            DialogResult result = SavePromptOverride != null
                ? SavePromptOverride($"Save changes to '{Path.GetFileName(_document.FilePath)}'?")
                : (!Visible ? DialogResult.No : MessageBox.Show(this, $"Save changes to '{Path.GetFileName(_document.FilePath)}'?", "Dialog editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question));

            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes) SaveCurrentDocument();
        }
        return true;
    }

    private void RebuildAll()
    {
        if (_disposed || IsDisposed || _document == null) return;
        _refreshing = true;
        try
        {
            RebuildLanguageList();
            RebuildGraph();
            RebuildLinksList();
            RebuildStringsGrid();
            if (!_document.Nodes.ContainsKey(_selectedNodeId))
                _selectedNodeId = _document.Nodes.Keys.FirstOrDefault();
            PropertyGridHost.selected_object = _document.Nodes.ContainsKey(_selectedNodeId)
                ? new SdkDialogNodePropertyAdapter(_document, _selectedNodeId)
                : null;
            RefreshStatus();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RebuildLanguageList()
    {
        _languageCombo.Items.Clear();
        foreach (string lang in _localization.Languages)
            _languageCombo.Items.Add(lang);
        int idx = _languageCombo.Items.IndexOf(_localization.CurrentLanguage);
        if (idx >= 0) _languageCombo.SelectedIndex = idx;
    }

    private void RebuildGraph()
    {
        if (_document == null) return;
        var graph = GraphHost.hypergraph;
        foreach (var edge in graph.links.Values.ToArray())
            graph.remove_link(edge.id);
        graph.remove_nodes(graph.nodes.Keys.ToArray());

        int autoIdx = 0;
        foreach (var node in _document.Nodes.Values)
        {
            string key = node.Id.ToString();
            var gNode = graph.create_node(key);
            var panel = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
            panel.Children.Add(new WpfGraph.link_point { node = gNode, id = "in", tag = node.Id });
            string preview = node.IsDialog
                ? $"[Dialog] {node.StringTableId}"
                : $"#{node.Id} {(node.IsRoot ? "(Root) " : string.Empty)}{_localization.GetText(node.StringTableId)}";
            if (preview.Length > 42) preview = preview[..42] + "...";
            panel.Children.Add(new WpfControls.TextBlock
            {
                Text = preview,
                Foreground = WpfMedia.Brushes.White,
                Margin = new Wpf.Thickness(6, 3, 6, 3)
            });
            panel.Children.Add(new WpfGraph.link_point { node = gNode, id = "out", tag = node.Id });
            gNode.Content = new WpfControls.Border
            {
                Background = node.IsDialog ? WpfMedia.Brushes.DarkSlateGray : (node.IsRoot ? WpfMedia.Brushes.DarkOliveGreen : WpfMedia.Brushes.SteelBlue),
                Padding = new Wpf.Thickness(4),
                Child = panel
            };
            var pos = node.HasLayoutPosition ? node.LayoutPosition : new Vector2((autoIdx % 4) * 200f + 40f, (autoIdx / 4) * 100f + 40f);
            autoIdx++;
            gNode.position = new Wpf.Point(pos.X, pos.Y);
        }

        graph.Measure(new Wpf.Size(900, 500));
        graph.Arrange(new Wpf.Rect(0, 0, 900, 500));
        graph.UpdateLayout();

        foreach (var lnk in _document.Links)
        {
            if (_document.Nodes.ContainsKey(lnk.SrcId) && _document.Nodes.ContainsKey(lnk.DstId))
                graph.create_link(lnk.SrcId.ToString(), lnk.DstId.ToString(), "out", "in");
        }
    }

    private void RebuildLinksList()
    {
        _linksList.Items.Clear();
        if (_document == null) return;
        foreach (var lnk in _document.Links)
        {
            string pre = lnk.Preconditions == null ? "always" : $"{lnk.Preconditions.Type}:{lnk.Preconditions.Func}({lnk.Preconditions.Param1})";
            string acts = lnk.Actions.Count == 0 ? "none" : string.Join(", ", lnk.Actions.Select(a => $"{a.Func}({a.Param1})"));
            _linksList.Items.Add($"#{lnk.SrcId} -> #{lnk.DstId} | if [{pre}] | do [{acts}]");
        }
    }

    private void RebuildStringsGrid()
    {
        _stringsGrid.Rows.Clear();
        if (_document == null) return;
        foreach (string id in _localization.GetAllStringIds())
        {
            string cat = SdkDialogLocalizationStorage.CategoryToExcelLabel(_localization.GetCategory(id));
            string orig = _localization.GetExactLanguageText(id, "origin");
            string cur = _localization.GetExactLanguageText(id, _localization.CurrentLanguage);
            _stringsGrid.Rows.Add(id, cat, orig, cur);
        }
    }

    private void RefreshStatus()
    {
        if (_document == null) return;
        string name = string.IsNullOrWhiteSpace(_document.FilePath) ? "untitled.dlg" : Path.GetFileName(_document.FilePath);
        Text = name + (_document.IsDirty ? " *" : "") + " · Dialog editor";
        _statusLabel.Text =
            $"Nodes: {_document.Nodes.Count} | Links: {_document.Links.Count} | Lang: {_localization.CurrentLanguage} ({_localization.Languages.Count}) | Compiled: {(_lastCompiled != null ? $"{_lastCompiled.Nodes.Count} nodes" : "pending")}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_document != null)
            {
                _document.Changed -= RebuildAll;
                _document.Dispose();
                _document = null;
            }
            PropertyGridHost.selected_object = null;
        }
        base.Dispose(disposing);
    }
}

public partial class WindowIde
{
    private readonly List<SdkDialogEditor> dialogEditors = new();

    public IReadOnlyList<SdkDialogEditor> DialogEditors => dialogEditors;

    public SdkDialogEditor OpenDialogEditor(string? pathOrVirtual = null, SdkDialogLocalizationStorage? localization = null)
    {
        var existing = dialogEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateDialogEditor();

        if (!string.IsNullOrWhiteSpace(pathOrVirtual))
            editor.OpenFile(pathOrVirtual, localization);
        else if (editor.Document == null)
            editor.CreateNew(SdkVfsManager.IsConfigured ? "resources/dialogs/configs/new_dialog.dlg" : "new_dialog.dlg", localization);

        if (!BackgroundMode && Visible)
        {
            if (existing == null) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Dialog editor opened" + (string.IsNullOrWhiteSpace(pathOrVirtual) ? string.Empty : ": " + pathOrVirtual));
        return editor;
    }

    public SdkDialogEditor CreateNewDialogDocument(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        var existing = dialogEditors.FirstOrDefault(e => !e.IsDisposed);
        var editor = existing ?? CreateDialogEditor();
        editor.CreateNew(pathOrVirtual, localization);
        if (!BackgroundMode && Visible)
        {
            if (existing == null) editor.Show(this);
            else editor.Activate();
        }
        SetStatus("Dialog editor created: " + pathOrVirtual);
        return editor;
    }

    private SdkDialogEditor CreateDialogEditor()
    {
        var editor = new SdkDialogEditor();
        editor.DocumentSaved += _ => ResourcePipeline?.InvalidateAllCache();
        dialogEditors.Add(editor);
        editor.Disposed += (_, _) => dialogEditors.Remove(editor);
        return editor;
    }

    public SdkDialogEditor ShowDialogEditor()
    {
        if (BackgroundMode || !SdkVfsManager.IsConfigured)
            return OpenDialogEditor();

        string initialDir = SdkVfsManager.ResolveVirtualToPhysical("resources/dialogs/configs");
        using var dialog = new OpenFileDialog
        {
            Title = "Open dialog graph",
            Filter = "Dialog files|*.dlg;*.template",
            InitialDirectory = Directory.Exists(initialDir) ? initialDir : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return OpenDialogEditor();

        return OpenDialogEditor(dialog.FileName);
    }
}
