using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.property_grid;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public partial class SdkDialogEditorPanel : UserControl, IDisposable
{
    private static readonly FilePickerFileType DialogFiles = new("Dialog files") { Patterns = new[] { "*.dlg", "*.template" } };
    private static readonly FilePickerFileType ExcelFiles = new("Excel localization") { Patterns = new[] { "*.xlsx", "*.xls" } };
    private static readonly FilePickerFileType XlsxFiles = new("Excel workbook") { Patterns = new[] { "*.xlsx" } };

    private bool _refreshing;
    private bool _disposed;
    private bool _pickerActive;
    private string? _rowsLanguage;
    private SdkDialogLocalizationRow[] _rows = [];

    public SdkDialogEditorPanel()
    {
        InitializeComponent();
        Properties.auto_update = false;
        Graph.is_nodes_moveable = true;
        Graph.is_linkable = false;
        Graph.selecting += (_, args) =>
        {
            if (uint.TryParse(args.selected_node_ids.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out uint id))
                SelectNode(id);
        };

        OpenButton.Click += async (_, _) => await PromptOpenFileAsync();
        SaveButton.Click += (_, _) => Run(SaveCurrentDocument);
        UndoButton.Click += (_, _) => Run(() => Document?.Commands.rollback(-1));
        RedoButton.Click += (_, _) => Run(() => Document?.Commands.rollback(1));
        AddPhraseButton.Click += (_, _) => Run(() => AddNode(isDialog: false));
        AddDialogButton.Click += (_, _) => Run(() => AddNode(isDialog: true));
        CompileButton.Click += (_, _) => Run(() => CompileAndResetRuntime());
        ExportButton.Click += async (_, _) => await ExportLocalizationAsync();
        ImportButton.Click += async (_, _) => await ImportLocalizationAsync();
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && LanguageBox.SelectedItem is string language && Document != null)
            {
                Document.Localization.CurrentLanguage = language;
                RebuildAll();
            }
        };

        RefreshStatus();
    }

    public SdkDialogDocument? Document { get; private set; }
    public SdkDialogLocalizationStorage LocalizationStorage { get; private set; } = SdkDialogLocalizationStorage.CreateEmpty();
    public uint SelectedNodeId { get; private set; }
    public SdkCompiledGameDialog? LastCompiledDialog { get; private set; }
    public SdkDialogRuntimeState RuntimeState { get; private set; } = new();
    public hypergraph_control Hypergraph => Graph;
    public property_grid_control PropertyGrid => Properties;

    public event Action<string>? OpenFileRequested;
    public event Action<string>? TitleChanged;
    public event Action<SdkDialogDocument>? DocumentSaved;

    public void LoadDocument(SdkDialogDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(Document, document))
            return;

        ReleaseDocument();
        Document = document;
        LocalizationStorage = document.Localization;
        SelectedNodeId = document.Nodes.Keys.FirstOrDefault();
        LastCompiledDialog = null;
        RuntimeState = new SdkDialogRuntimeState();
        CompiledList.ItemsSource = null;
        document.Changed += RebuildAll;
        RebuildAll();
    }

    public SdkDialogDocument CreateNew(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var document = SdkDialogDocument.CreateNew(pathOrVirtual, localization);
        LoadDocument(document);
        return document;
    }

    public SdkDialogDocument OpenFile(string pathOrVirtual, SdkDialogLocalizationStorage? localization = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var document = SdkDialogDocument.Load(pathOrVirtual, localization);
        LoadDocument(document);
        return document;
    }

    public void SaveCurrentDocument()
    {
        CommitFocusedLocalizationText();
        if (Document is not { } document)
            return;

        document.Save();
        DocumentSaved?.Invoke(document);
        RefreshStatus();
    }

    public bool ConfirmClose(Func<string, SdkDocumentSaveChoice> prompt)
    {
        CommitFocusedLocalizationText();
        if (Document is not { IsDirty: true } document)
            return true;

        switch (prompt($"Save changes to '{Path.GetFileName(document.FilePath)}'?"))
        {
            case SdkDocumentSaveChoice.Yes:
                SaveCurrentDocument();
                return true;
            case SdkDocumentSaveChoice.No:
                return true;
            default:
                return false;
        }
    }

    public void SelectNode(uint id)
    {
        if (Document is not { } document || !document.Nodes.ContainsKey(id))
            return;

        SelectedNodeId = id;
        Properties.selected_object = new SdkDialogNodePropertyAdapter(document, id);
        Graph.set_selected_nodes(new List<string> { NodeKey(id) });
    }

    public SdkCompiledGameDialog CompileAndResetRuntime(SdkDialogRuntimeState? state = null)
    {
        var document = Document ?? throw new InvalidOperationException("No dialog document is loaded.");
        var compiled = SdkCompiledGameDialog.Compile(document, LocalizationStorage);
        LastCompiledDialog = compiled;
        RuntimeState = state ?? new SdkDialogRuntimeState();

        var log = new List<string>
        {
            $"Compiled '{compiled.Name}': {compiled.Nodes.Count} nodes, {compiled.Links.Count} links, {compiled.RootIds.Count} root(s)"
        };
        foreach (uint root in compiled.RootIds)
        {
            log.Add($"  Root #{root}: \"{compiled.GetText(root)}\"");
            foreach (var link in compiled.GetAvailableLinks(root, RuntimeState))
                log.Add($"    -> #{link.DstRuntimeId}: \"{compiled.GetText(link.DstRuntimeId)}\"");
        }
        CompiledList.ItemsSource = log;
        RefreshStatus();
        return compiled;
    }

    public Task PromptOpenFileAsync(string? initialDirectory = null) => WithStorageAsync(async storage =>
    {
        var document = Document;
        initialDirectory ??= Path.GetDirectoryName(document?.FilePath);
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open dialog graph",
            SuggestedStartLocation = await GetStartLocationAsync(storage, initialDirectory),
            FileTypeFilter = new[] { DialogFiles }
        });
        if (_disposed || !ReferenceEquals(Document, document) || files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;

        if (OpenFileRequested != null)
            OpenFileRequested(path);
        else if (Document?.IsDirty != true)
            OpenFile(path);
    });

    private Task ExportLocalizationAsync() => WithStorageAsync(async storage =>
    {
        if (Document is not { } document)
            return;

        var localization = document.Localization;
        string language = localization.Languages.FirstOrDefault(lang => !string.Equals(lang, "origin", StringComparison.OrdinalIgnoreCase)) ?? "english";
        string directory = string.IsNullOrWhiteSpace(localization.TextsDirectory)
            ? Path.GetTempPath()
            : Path.Combine(localization.TextsDirectory, "batches");
        Directory.CreateDirectory(directory);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export dialog localization",
            SuggestedFileName = $"dialog_{language}.xlsx",
            DefaultExtension = "xlsx",
            SuggestedStartLocation = await GetStartLocationAsync(storage, directory),
            FileTypeChoices = new[] { XlsxFiles }
        });
        if (!_disposed && ReferenceEquals(Document, document) && file?.TryGetLocalPath() is { } path)
        {
            var result = SdkDialogExcelExchange.Export(localization, language, path);
            StatusLabel.Text = $"Exported {result.RowCount} strings to {Path.GetFileName(result.WorkbookPath)}";
        }
    });

    private Task ImportLocalizationAsync() => WithStorageAsync(async storage =>
    {
        if (Document is not { } document)
            return;

        var localization = document.Localization;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import dialog localization",
            SuggestedStartLocation = await GetStartLocationAsync(storage, localization.TextsDirectory),
            FileTypeFilter = new[] { ExcelFiles }
        });
        if (_disposed || !ReferenceEquals(Document, document) || files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
            return;

        string language = localization.Languages.FirstOrDefault(lang =>
            Path.GetFileNameWithoutExtension(path).EndsWith("_" + lang, StringComparison.OrdinalIgnoreCase)) ?? localization.CurrentLanguage;
        var result = SdkDialogExcelExchange.Import(localization, language, path);
        if (!string.IsNullOrWhiteSpace(localization.TextsDirectory))
            localization.Save();
        RebuildAll();
        StatusLabel.Text = $"Imported {result.UpdatedCount} localized strings for '{language}'";
    });

    private static async Task<IStorageFolder?> GetStartLocationAsync(IStorageProvider storage, string? directory)
    {
        return string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
            ? null
            : await storage.TryGetFolderFromPathAsync(directory);
    }

    private async Task WithStorageAsync(Func<IStorageProvider, Task> action)
    {
        if (_disposed || _pickerActive || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        _pickerActive = true;
        try { await action(storage); }
        catch (Exception error)
        {
            if (!_disposed)
                ShowError(error.Message);
        }
        finally { _pickerActive = false; }
    }

    private void AddNode(bool isDialog)
    {
        if (Document is not { } document)
            return;

        var node = document.AddNode(!isDialog && document.Nodes.Count == 0, isDialog,
            isDialog ? "zaton/test_zaton" : "New dialog phrase");
        SelectNode(node.Id);
    }

    private void RebuildAll()
    {
        if (Document is not { } document)
            return;

        _refreshing = true;
        try
        {
            var localization = document.Localization;
            if (LanguageBox.ItemsSource is not string[] languages || !languages.SequenceEqual(localization.Languages))
                LanguageBox.ItemsSource = localization.Languages.ToArray();
            LanguageBox.SelectedItem = localization.CurrentLanguage;

            RebuildGraph(document);
            LinksList.ItemsSource = document.Links.Select(link =>
            {
                var expression = link.Preconditions;
                string preconditions = expression == null ? "always" : $"{expression.Type}:{expression.Func}({expression.Param1})";
                string actions = link.Actions.Count == 0 ? "none" : string.Join(", ", link.Actions.Select(action => $"{action.Func}({action.Param1})"));
                return $"#{link.SrcId} -> #{link.DstId} | if [{preconditions}] | do [{actions}]";
            }).ToArray();
            RebuildLocalizationRows(document);
            if (document.Nodes.Count == 0)
            {
                SelectedNodeId = 0;
                Properties.selected_object = null;
            }
            else
            {
                SelectNode(document.Nodes.ContainsKey(SelectedNodeId) ? SelectedNodeId : document.Nodes.Keys.First());
            }
            RefreshStatus();
        }
        finally { _refreshing = false; }
    }

    private void RebuildGraph(SdkDialogDocument document)
    {
        SdkEditorGraph.Clear(Graph);
        int index = 0;
        foreach (var node in document.Nodes.Values)
        {
            string preview = node.IsDialog
                ? $"[Dialog] {node.StringTableId}"
                : $"#{node.Id} {(node.IsRoot ? "(Root) " : string.Empty)}{document.Localization.GetText(node.StringTableId)}";
            if (preview.Length > 42)
                preview = preview[..42] + "…";
            var position = node.HasLayoutPosition
                ? node.LayoutPosition
                : new Vector2((index % 4) * 200f + 40f, (index / 4) * 100f + 40f);
            var color = node.IsDialog ? Brushes.DarkSlateGray : node.IsRoot ? Brushes.DarkOliveGreen : Brushes.SteelBlue;
            SdkEditorGraph.AddNode(Graph, NodeKey(node.Id), preview, new Point(position.X, position.Y), color);
            index++;
        }
        foreach (var link in document.Links)
            SdkEditorGraph.AddEdge(Graph, NodeKey(link.SrcId), NodeKey(link.DstId));
    }

    private void RebuildLocalizationRows(SdkDialogDocument document)
    {
        var localization = document.Localization;
        string language = localization.CurrentLanguage;
        var ids = localization.GetAllStringIds().ToArray();
        if (!string.Equals(_rowsLanguage, language, StringComparison.OrdinalIgnoreCase) ||
            !ids.SequenceEqual(_rows.Select(row => row.Id)))
        {
            _rows = ids.Select(id => new SdkDialogLocalizationRow(id, language, (text, lang) =>
            {
                if (ReferenceEquals(Document, document))
                    Run(() => document.SetLocalizedText(id, text, lang));
            })).ToArray();
            _rowsLanguage = language;
            foreach (var row in _rows)
                row.Refresh(localization);
            StringsList.ItemsSource = _rows;
            return;
        }

        foreach (var row in _rows)
            row.Refresh(localization);
    }

    private void RefreshStatus()
    {
        var document = Document;
        if (document == null)
        {
            StatusLabel.Text = "No dialog document loaded.";
            return;
        }

        string name = string.IsNullOrWhiteSpace(document.FilePath) ? "untitled.dlg" : Path.GetFileName(document.FilePath);
        TitleChanged?.Invoke(name + (document.IsDirty ? " *" : "") + " · Dialog editor");
        string compiled = LastCompiledDialog == null ? "pending" : $"{LastCompiledDialog.Nodes.Count} nodes";
        StatusLabel.Text = $"Nodes: {document.Nodes.Count} | Links: {document.Links.Count} | Lang: {LocalizationStorage.CurrentLanguage} ({LocalizationStorage.Languages.Count}) | Compiled: {compiled}";
    }

    public void ShowError(string message) => StatusLabel.Text = message;

    private void CommitFocusedLocalizationText()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox { DataContext: SdkDialogLocalizationRow })
            SaveButton.Focus();
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { ShowError(error.Message); }
    }

    private static string NodeKey(uint id) => id.ToString(CultureInfo.InvariantCulture);

    private void ReleaseDocument()
    {
        if (Document is { } document)
        {
            Document = null;
            document.Changed -= RebuildAll;
            document.Dispose();
        }
        _rows = [];
        _rowsLanguage = null;
        StringsList.ItemsSource = null;
        Properties.selected_object = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseDocument();
        SdkEditorGraph.Clear(Graph);
    }
}

public sealed class SdkDialogLocalizationRow : INotifyPropertyChanged
{
    private readonly string _language;
    private readonly Action<string, string> _update;
    private string _category = string.Empty;
    private string _origin = string.Empty;
    private string _translation = string.Empty;

    internal SdkDialogLocalizationRow(string id, string language, Action<string, string> update)
    {
        Id = id;
        _language = language;
        _update = update;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string Category => _category;

    public string Origin
    {
        get => _origin;
        set
        {
            if (!string.Equals(_origin, value, StringComparison.Ordinal))
                _update(value ?? string.Empty, "origin");
        }
    }

    public string Translation
    {
        get => _translation;
        set
        {
            if (!string.Equals(_translation, value, StringComparison.Ordinal))
                _update(value ?? string.Empty, _language);
        }
    }

    internal void Refresh(SdkDialogLocalizationStorage localization)
    {
        SetDisplayValue(ref _category, SdkDialogLocalizationStorage.CategoryToExcelLabel(localization.GetCategory(Id)), nameof(Category));
        SetDisplayValue(ref _origin, localization.GetExactLanguageText(Id, "origin"), nameof(Origin));
        SetDisplayValue(ref _translation, localization.GetExactLanguageText(Id, _language), nameof(Translation));
    }

    private void SetDisplayValue(ref string field, string value, string property)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

internal sealed class SdkDialogNodePropertyAdapter
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
