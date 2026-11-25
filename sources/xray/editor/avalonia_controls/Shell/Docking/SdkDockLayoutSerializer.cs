using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;

namespace xray.editor.avalonia_controls.Shell.Docking;

public static class SdkDockLayoutSerializer
{
    public const int MaxLayoutBytes = 1024 * 1024;

    private static DockSerializer CreateSerializer() => new();

    public static byte[] Serialize(IRootDock layout)
    {
        if (layout == null)
            throw new ArgumentNullException(nameof(layout));

        var savedContexts = new List<(IDockable Dockable, object? Context)>();
        DetachContexts(layout, savedContexts);
        string json;
        try
        {
            json = CreateSerializer().Serialize(layout);
        }
        finally
        {
            foreach (var (dockable, context) in savedContexts)
                dockable.Context = context;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxLayoutBytes)
            throw new InvalidDataException("SDK layout exceeds 1 MiB.");

        return bytes;
    }

    private static void DetachContexts(IDockable? node, List<(IDockable Dockable, object? Context)> saved)
    {
        if (node == null)
            return;

        if (node.Context != null)
        {
            saved.Add((node, node.Context));
            node.Context = null;
        }

        if (node is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
                DetachContexts(child, saved);
        }

        if (node is IRootDock rootDock)
        {
            if (rootDock.HiddenDockables != null)
            {
                foreach (var hidden in rootDock.HiddenDockables)
                    DetachContexts(hidden, saved);
            }
            if (rootDock.Windows != null)
            {
                foreach (var win in rootDock.Windows)
                    DetachContexts(win.Layout, saved);
            }
        }
    }

    public static IRootDock ValidateAndDeserialize(
        byte[] jsonBytes,
        SdkDockFactory factory,
        bool backgroundMode,
        IReadOnlyCollection<string> requiredPanelIds,
        bool strictUnknownPanels = true)
    {
        if (jsonBytes == null)
            throw new ArgumentNullException(nameof(jsonBytes));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (jsonBytes.Length == 0)
            throw new InvalidDataException("SDK layout JSON is empty.");
        if (jsonBytes.Length > MaxLayoutBytes)
            throw new InvalidDataException("SDK layout exceeds 1 MiB.");

        string json = Encoding.UTF8.GetString(jsonBytes);
        var root = CreateSerializer().Deserialize<RootDock>(json)
            ?? throw new InvalidDataException("Invalid SDK Dock.Avalonia layout JSON.");

        ValidateRootDock(root, backgroundMode, requiredPanelIds, strictUnknownPanels);

        factory.BackgroundMode = backgroundMode;
        factory.InitLayout(root);
        return root;
    }

    public static IRootDock RestoreWithRollback(
        IRootDock currentLayout,
        byte[] candidateJsonBytes,
        SdkDockFactory factory,
        bool backgroundMode,
        IReadOnlyCollection<string> requiredPanelIds,
        bool strictUnknownPanels = true)
    {
        byte[] snapshot = Serialize(currentLayout);
        try
        {
            return ValidateAndDeserialize(candidateJsonBytes, factory, backgroundMode, requiredPanelIds, strictUnknownPanels);
        }
        catch
        {
            factory.InitLayout(currentLayout);
            _ = ValidateAndDeserialize(snapshot, factory, backgroundMode, requiredPanelIds, strictUnknownPanels: false);
            throw;
        }
    }

    public static IRootDock ConvertLegacyDockPanelXml(
        byte[] xmlBytes,
        SdkDockFactory factory,
        bool backgroundMode,
        IReadOnlyCollection<string>? requiredPanelIds = null,
        bool strictUnknownPanels = false)
    {
        if (xmlBytes == null)
            throw new ArgumentNullException(nameof(xmlBytes));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (xmlBytes.Length == 0)
            throw new InvalidDataException("Legacy DockPanelSuite XML is empty.");
        if (xmlBytes.Length > MaxLayoutBytes)
            throw new InvalidDataException("Legacy DockPanelSuite XML exceeds 1 MiB.");

        XDocument doc;
        using (var stream = new MemoryStream(xmlBytes))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
        {
            doc = XDocument.Load(reader);
        }

        if (doc.Root == null || doc.Root.Name.LocalName != "DockPanel")
            throw new InvalidDataException("Legacy XML root must be <DockPanel>.");

        var contents = doc.Root.Element("Contents")?.Elements("Content").ToList()
            ?? throw new InvalidDataException("Legacy DockPanel XML missing <Contents>.");

        var knownIds = new List<string>();
        foreach (var el in contents)
        {
            string rawId = (string?)el.Attribute("PersistString") ?? string.Empty;
            if (!SdkDockPanelIds.IsKnownPanelId(rawId))
            {
                if (strictUnknownPanels)
                    throw new InvalidDataException($"Unknown legacy panel ID '{rawId}'.");
                continue;
            }
            knownIds.Add(SdkDockPanelIds.NormalizeId(rawId));
        }

        var panesById = new Dictionary<int, XElement>();
        foreach (var p in doc.Root.Element("Panes")?.Elements("Pane") ?? Enumerable.Empty<XElement>())
        {
            if (int.TryParse((string?)p.Attribute("ID"), out int paneId))
                panesById[paneId] = p;
        }

        bool PaneHasVisibleContent(XElement p) =>
            p.Element("Contents")?.Elements("Content").Any(c =>
                int.TryParse((string?)c.Attribute("RefID"), out int cid) &&
                cid >= 0 && cid < contents.Count &&
                !string.Equals((string?)contents[cid].Attribute("IsHidden"), "True", StringComparison.OrdinalIgnoreCase)) ?? false;

        bool NestedRefIsVisible(XElement nested)
        {
            if (int.TryParse((string?)nested.Attribute("RefID"), out int refId) &&
                panesById.TryGetValue(refId, out XElement? targetPane))
            {
                return PaneHasVisibleContent(targetPane);
            }
            return PaneHasVisibleContent(nested);
        }

        bool hasFloatPane =
            (doc.Root.Element("Panes")?.Elements("Pane").Any(p =>
                string.Equals((string?)p.Attribute("DockState"), "Float", StringComparison.OrdinalIgnoreCase) &&
                PaneHasVisibleContent(p)) ?? false) ||
            (doc.Root.Element("DockWindows")?.Elements("DockWindow").Any(dw =>
                string.Equals((string?)dw.Attribute("DockState"), "Float", StringComparison.OrdinalIgnoreCase) &&
                dw.Descendants().Any(n => (n.Name.LocalName == "Pane" || n.Name.LocalName == "NestedPane") && NestedRefIsVisible(n))) ?? false) ||
            (doc.Root.Element("FloatWindows")?.Elements("FloatWindow").Any(fw =>
                fw.Descendants().Any(n => (n.Name.LocalName == "Pane" || n.Name.LocalName == "NestedPane") && NestedRefIsVisible(n))) ?? false);

        if (backgroundMode && hasFloatPane)
            throw new InvalidDataException("Floating layout is unavailable in background runs.");

        var contentIdsByIndex = new Dictionary<int, string>();
        for (int i = 0; i < contents.Count; ++i)
        {
            string rawId = (string?)contents[i].Attribute("PersistString") ?? string.Empty;
            if (SdkDockPanelIds.IsKnownPanelId(rawId))
                contentIdsByIndex[i] = SdkDockPanelIds.NormalizeId(rawId);
        }

        var leftIds = new List<string>();
        var rightIds = new List<string>();
        var documentIds = new List<string>();
        var hiddenIds = new List<string>();

        void AssignPaneContents(XElement pane, string effectiveState)
        {
            foreach (var c in pane.Element("Contents")?.Elements("Content") ?? Enumerable.Empty<XElement>())
            {
                if (!int.TryParse((string?)c.Attribute("RefID"), out int refId) ||
                    !contentIdsByIndex.TryGetValue(refId, out string? panelId))
                {
                    continue;
                }

                bool isHiddenContent = refId >= 0 && refId < contents.Count &&
                    string.Equals((string?)contents[refId].Attribute("IsHidden"), "True", StringComparison.OrdinalIgnoreCase);
                if (isHiddenContent ||
                    string.Equals(effectiveState, "Unknown", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(effectiveState, "Hidden", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(hiddenIds, panelId);
                }
                else if (string.Equals(effectiveState, "Document", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(documentIds, panelId);
                }
                else if (effectiveState.Contains("Left", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(leftIds, panelId);
                }
                else if (effectiveState.Contains("Right", StringComparison.OrdinalIgnoreCase) ||
                         effectiveState.Contains("Top", StringComparison.OrdinalIgnoreCase) ||
                         effectiveState.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(rightIds, panelId);
                }
                else
                {
                    AddUnique(hiddenIds, panelId);
                }
            }
        }

        var visitedPaneIds = new HashSet<int>();
        foreach (var dw in doc.Root.Element("DockWindows")?.Elements("DockWindow") ?? Enumerable.Empty<XElement>())
        {
            string dockState = (string?)dw.Attribute("DockState") ?? string.Empty;
            foreach (var paneRef in dw.Descendants().Where(n => n.Name.LocalName == "Pane" || n.Name.LocalName == "NestedPane"))
            {
                if (int.TryParse((string?)paneRef.Attribute("RefID"), out int refPaneId) &&
                    panesById.TryGetValue(refPaneId, out XElement? referencedPane))
                {
                    visitedPaneIds.Add(refPaneId);
                    string paneState = (string?)referencedPane.Attribute("DockState") ?? dockState;
                    AssignPaneContents(referencedPane, paneState);
                }
                else if (paneRef.Element("Contents") != null)
                {
                    string paneState = (string?)paneRef.Attribute("DockState") ?? dockState;
                    AssignPaneContents(paneRef, paneState);
                }
            }
        }

        foreach (var (paneId, pane) in panesById)
        {
            if (visitedPaneIds.Contains(paneId))
                continue;

            string paneState = (string?)pane.Attribute("DockState") ?? string.Empty;
            AssignPaneContents(pane, paneState);
        }

        // Handle hidden contents declared without a visible DockWindow assignment.
        foreach (int idx in contentIdsByIndex.Keys)
        {
            string id = contentIdsByIndex[idx];
            bool isHiddenAttr = string.Equals((string?)contents[idx].Attribute("IsHidden"), "True", StringComparison.OrdinalIgnoreCase);
            if (!documentIds.Contains(id) && !leftIds.Contains(id) && !rightIds.Contains(id) && !hiddenIds.Contains(id))
            {
                if (id == SdkDockPanelIds.SceneView && !isHiddenAttr)
                    AddUnique(documentIds, id);
                else if (isHiddenAttr)
                    AddUnique(hiddenIds, id);
                else
                    AddUnique(rightIds, id);
            }
        }

        if (!documentIds.Contains(SdkDockPanelIds.SceneView))
            documentIds.Insert(0, SdkDockPanelIds.SceneView);

        factory.BackgroundMode = backgroundMode;

        var docDockables = new List<IDockable>();
        foreach (string id in documentIds)
        {
            docDockables.Add(id == SdkDockPanelIds.SceneView
                ? factory.GetOrCreateSceneDocument()
                : factory.GetOrCreateTool(id, TitleForId(id)));
        }

        var centerDocDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            CanCloseLastDockable = false,
            Proportion = leftIds.Count > 0 && rightIds.Count > 0 ? 0.54 : 0.78,
            VisibleDockables = factory.CreateList(docDockables.ToArray()),
            ActiveDockable = docDockables[0],
            DefaultDockable = docDockables[0]
        };

        var rootChildren = new List<IDockable>();
        if (leftIds.Count > 0)
        {
            var leftTools = leftIds.Select(id => (IDockable)factory.GetOrCreateTool(id, TitleForId(id))).ToArray();
            rootChildren.Add(new ToolDock
            {
                Id = "LeftToolDock",
                Alignment = Alignment.Left,
                Proportion = 0.24,
                VisibleDockables = factory.CreateList(leftTools),
                ActiveDockable = leftTools[0],
                DefaultDockable = leftTools[0]
            });
            rootChildren.Add(new ProportionalDockSplitter());
        }

        rootChildren.Add(centerDocDock);

        if (rightIds.Count > 0)
        {
            rootChildren.Add(new ProportionalDockSplitter());
            var rightTools = rightIds.Select(id => (IDockable)factory.GetOrCreateTool(id, TitleForId(id))).ToArray();
            rootChildren.Add(new ToolDock
            {
                Id = "RightToolDock",
                Alignment = Alignment.Right,
                Proportion = 0.22,
                VisibleDockables = factory.CreateList(rightTools),
                ActiveDockable = rightTools[0],
                DefaultDockable = rightTools[0]
            });
        }

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = factory.CreateList(rootChildren.ToArray())
        };

        var rootDock = factory.CreateRootDock();
        rootDock.Id = "Root";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = factory.CreateList<IDockable>(mainLayout);
        rootDock.ActiveDockable = mainLayout;
        rootDock.DefaultDockable = mainLayout;
        if (hiddenIds.Count > 0)
        {
            rootDock.HiddenDockables = factory.CreateList(
                hiddenIds.Select(id => (IDockable)factory.GetOrCreateTool(id, TitleForId(id))).ToArray());
        }

        var reqs = requiredPanelIds ?? SdkDockPanelIds.MinimalRequiredPanelIds;
        ValidateRootDock(rootDock, backgroundMode, reqs, strictUnknownPanels);
        factory.InitLayout(rootDock);
        return rootDock;
    }

    public static IRootDock LoadOrMigrateLayoutFile(
        string jsonPath,
        string? legacyXmlPath,
        SdkDockFactory factory,
        bool backgroundMode,
        IReadOnlyCollection<string>? requiredPanelIds = null)
    {
        var reqs = requiredPanelIds ?? SdkDockPanelIds.IdeRequiredPanelIds;
        if (File.Exists(jsonPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(jsonPath);
                return ValidateAndDeserialize(bytes, factory, backgroundMode, reqs, strictUnknownPanels: false);
            }
            catch
            {
                // Fall back to legacy XML or reference default layout cleanly.
            }
        }

        if (!string.IsNullOrWhiteSpace(legacyXmlPath) && File.Exists(legacyXmlPath))
        {
            try
            {
                byte[] xmlBytes = File.ReadAllBytes(legacyXmlPath);
                var converted = ConvertLegacyDockPanelXml(xmlBytes, factory, backgroundMode, reqs, strictUnknownPanels: false);
                SaveLayoutFileAtomic(jsonPath, Serialize(converted));
                return converted;
            }
            catch
            {
                // Fall back to default reference layout cleanly.
            }
        }

        return factory.CreateDefaultWorkspaceLayout();
    }

    public static void SaveLayoutFileAtomic(string path, byte[] jsonBytes)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, jsonBytes);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void ValidateRootDock(
        IRootDock root,
        bool backgroundMode,
        IReadOnlyCollection<string> requiredPanelIds,
        bool strictUnknownPanels)
    {
        if (backgroundMode && root.Windows != null && root.Windows.Count > 0)
            throw new InvalidDataException("Floating layout is unavailable in background runs.");

        var foundPanelIds = new HashSet<string>(StringComparer.Ordinal);
        bool sceneInDocumentDock = false;
        CollectAndValidate(root, insideDocumentDock: false, foundPanelIds, ref sceneInDocumentDock, strictUnknownPanels);

        if (!sceneInDocumentDock)
            throw new InvalidDataException("Scene document must remain visible inside a DocumentDock.");

        foreach (string req in requiredPanelIds)
        {
            string normalized = SdkDockPanelIds.NormalizeId(req);
            if (!foundPanelIds.Contains(normalized))
                throw new InvalidDataException($"Required SDK panel '{normalized}' is missing from layout.");
        }
    }

    private static void CollectAndValidate(
        IDockable? node,
        bool insideDocumentDock,
        HashSet<string> foundPanelIds,
        ref bool sceneInDocumentDock,
        bool strictUnknownPanels)
    {
        if (node == null)
            return;

        if (node is IDocument or ITool)
        {
            string normalized = SdkDockPanelIds.NormalizeId(node.Id);
            if (strictUnknownPanels && !SdkDockPanelIds.IsKnownPanelId(node.Id))
                throw new InvalidDataException($"Unknown SDK panel identity '{node.Id}'.");

            if (!string.IsNullOrEmpty(normalized))
            {
                foundPanelIds.Add(normalized);
                if (normalized == SdkDockPanelIds.SceneView && insideDocumentDock)
                    sceneInDocumentDock = true;
            }
        }

        bool childIsDocDock = insideDocumentDock || node is IDocumentDock;
        if (node is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
                CollectAndValidate(child, childIsDocDock, foundPanelIds, ref sceneInDocumentDock, strictUnknownPanels);
        }

        if (node is IRootDock rootDock)
        {
            if (rootDock.HiddenDockables != null)
            {
                foreach (var hidden in rootDock.HiddenDockables)
                    CollectAndValidate(hidden, insideDocumentDock: false, foundPanelIds, ref sceneInDocumentDock, strictUnknownPanels);
            }
            if (rootDock.Windows != null)
            {
                foreach (var win in rootDock.Windows)
                    CollectAndValidate(win.Layout, insideDocumentDock: false, foundPanelIds, ref sceneInDocumentDock, strictUnknownPanels);
            }
        }
    }

    private static void AddUnique(List<string> list, string item)
    {
        if (!list.Contains(item))
            list.Add(item);
    }

    private static string TitleForId(string normalizedId) => normalizedId switch
    {
        SdkDockPanelIds.SceneView => "Scene",
        SdkDockPanelIds.Project => "Project",
        SdkDockPanelIds.ObjectInspector => "Item properties",
        SdkDockPanelIds.Tools => "Tools",
        SdkDockPanelIds.SceneGraph => "Scene graph",
        SdkDockPanelIds.CameraView => "camera view",
        SdkDockPanelIds.AnimationCurveEditor => "Animation curves",
        SdkDockPanelIds.TriangulationTester => "Triangulation",
        _ => normalizedId
    };
}
