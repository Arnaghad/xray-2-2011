#pragma warning disable WFO1000

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace xray.editor_base;

public sealed class SdkNavigationPanel : DockContent
{
    public const string PersistIdentity = "xray.editor.sdk_navigation_panel";

    private readonly Project _project;
    private readonly CommandEngine _commands;
    private readonly NumericUpDown _cellSizeBox = new() { DecimalPlaces = 2, Minimum = 0.5M, Maximum = 10.0M, Increment = 0.25M, Value = 1.5M, Width = 70 };
    private readonly NumericUpDown _agentRadiusBox = new() { DecimalPlaces = 2, Minimum = 0.1M, Maximum = 3.0M, Increment = 0.05M, Value = 0.45M, Width = 70 };
    private readonly NumericUpDown _maxSlopeBox = new() { DecimalPlaces = 1, Minimum = 5M, Maximum = 85M, Increment = 5M, Value = 45M, Width = 70 };
    private readonly TextBox _startBox = new() { Text = "-4.5, 0, 0", Width = 110 };
    private readonly TextBox _goalBox = new() { Text = "4.5, 0, 0", Width = 110 };
    private readonly ListBox _waypointsList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _statsLabel = new() { Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Bottom, Height = 26, AutoEllipsis = true };

    private bool _disposed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SdkNavPathResult? LastPathResult { get; private set; }

    public SdkNavigationPanel(Project project, CommandEngine commands)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        Text = "Navigation mesh";
        ClientSize = new Size(760, 500);
        DockAreas = DockAreas.Float | DockAreas.DockRight | DockAreas.DockLeft | DockAreas.DockBottom | DockAreas.Document;
        HideOnClose = true;

        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        void AddBtn(string caption, System.Action act)
        {
            var b = new Button { Text = caption, AutoSize = true };
            b.Click += (_, _) =>
            {
                try { act(); }
                catch (Exception ex) { _statusLabel.Text = ex.Message; }
            };
            topBar.Controls.Add(b);
        }

        topBar.Controls.Add(new Label { Text = "Cell:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        topBar.Controls.Add(_cellSizeBox);
        topBar.Controls.Add(new Label { Text = "Radius:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        topBar.Controls.Add(_agentRadiusBox);
        topBar.Controls.Add(new Label { Text = "Slope:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        topBar.Controls.Add(_maxSlopeBox);

        AddBtn("Generate NavMesh", () => _ = GenerateForProject());
        AddBtn("Save navmesh.lua", SaveProjectNavMesh);
        AddBtn("Load navmesh.lua", () => _ = LoadProjectNavMesh());
        AddBtn("Undo", () => { if (_commands.CanUndo) _commands.rollback(-1); });
        AddBtn("Redo", () => { if (_commands.CanRedo) _commands.rollback(1); });

        var queryBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        queryBar.Controls.Add(new Label { Text = "Start:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        queryBar.Controls.Add(_startBox);
        queryBar.Controls.Add(new Label { Text = "Goal:", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
        queryBar.Controls.Add(_goalBox);
        var qBtn = new Button { Text = "Query Path", AutoSize = true };
        qBtn.Click += (_, _) =>
        {
            try
            {
                if (TryParseVec3(_startBox.Text, out var s) && TryParseVec3(_goalBox.Text, out var g))
                    QueryPath(s, g);
                else
                    _statusLabel.Text = "Invalid XYZ coordinate.";
            }
            catch (Exception ex) { _statusLabel.Text = ex.Message; }
        };
        queryBar.Controls.Add(qBtn);

        Controls.Add(_waypointsList);
        Controls.Add(_statsLabel);
        Controls.Add(queryBar);
        Controls.Add(topBar);
        Controls.Add(_statusLabel);

        _project.SessionChanged += RefreshStats;
        _project.ProjectStateChanged += RefreshStats;
        RefreshStats();
    }

    protected override string GetPersistString() => PersistIdentity;

    public SdkNavMesh GenerateForProject(IReadOnlyCollection<Guid>? excludedObjectGuids = null)
    {
        if (!_project.IsProjectOpen)
            throw new InvalidOperationException("Open or create a project before generating navigation mesh.");

        var settings = new SdkNavMeshGenerationSettings
        {
            CellSize = (float)_cellSizeBox.Value,
            AgentRadius = (float)_agentRadiusBox.Value,
            MaxSlopeDegrees = (float)_maxSlopeBox.Value
        };

        var generated = SdkNavMeshGenerator.Generate(_project, settings, excludedObjectGuids);
        ApplyNavMeshCommand(generated, "Generated navmesh");
        return _project.NavMesh!;
    }

    public void SaveProjectNavMesh()
    {
        if (_project.NavMesh == null || _project.NavMesh.TriangleCount == 0)
            throw new InvalidOperationException("No generated navigation mesh to save.");
        string dir = !string.IsNullOrWhiteSpace(_project.ProjectFilePath)
            ? Path.GetDirectoryName(Path.GetFullPath(_project.ProjectFilePath))!
            : SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{_project.Name}", forWrite: true);
        string navPath = Path.Combine(dir, "navmesh.lua");
        _project.NavMesh.SaveToFile(navPath);
        _statusLabel.Text = $"Saved navmesh: {navPath} ({_project.NavMesh.TriangleCount} tris)";
    }

    public SdkNavMesh LoadProjectNavMesh(string? explicitPath = null)
    {
        string path = explicitPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            string dir = !string.IsNullOrWhiteSpace(_project.ProjectFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(_project.ProjectFilePath))!
                : SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{_project.Name}", forWrite: false);
            path = Path.Combine(dir, "navmesh.lua");
        }
        var loaded = SdkNavMesh.LoadFromFile(path);
        ApplyNavMeshCommand(loaded, "Loaded navmesh.lua");
        return _project.NavMesh!;
    }

    public SdkNavPathResult QueryPath(Vector3 start, Vector3 goal)
    {
        if (_project.NavMesh == null || _project.NavMesh.TriangleCount == 0)
            throw new InvalidOperationException("Generate or load a navigation mesh before querying paths.");

        var res = SdkNavMeshPathfinder.FindPath(_project.NavMesh, start, goal);
        LastPathResult = res;
        _waypointsList.Items.Clear();
        if (res.Success)
        {
            _waypointsList.Items.Add($"Path found: {res.TriangleCorridor.Count} tris, {res.Waypoints.Count} waypoints, length={res.TotalLength:F2}m");
            foreach (var wp in res.Waypoints)
                _waypointsList.Items.Add($"  ({wp.X:F2}, {wp.Y:F2}, {wp.Z:F2})");
            _statusLabel.Text = $"Path length {res.TotalLength:F2}m ({res.Waypoints.Count} waypoints)";
        }
        else
        {
            _waypointsList.Items.Add("Blocked / unreachable: " + res.FailureReason);
            _statusLabel.Text = res.FailureReason;
        }
        return res;
    }

    public void RefreshStats()
    {
        if (_disposed || IsDisposed) return;
        var nm = _project.NavMesh;
        _statsLabel.Text = nm == null
            ? "NavMesh: (none)"
            : $"NavMesh: {nm.Vertices.Count} vertices | {nm.TriangleCount} triangles | source={(string.IsNullOrEmpty(nm.SourceFilePath) ? "<memory>" : Path.GetFileName(nm.SourceFilePath))}";
    }

    private void ApplyNavMeshCommand(SdkNavMesh next, string statusMessage)
    {
        var previous = _project.NavMesh?.Clone();
        if (_commands.EditingEnabled && _project.IsProjectOpen)
        {
            _commands.run(new SdkNavMeshCommand(_project, this, previous, next.Clone()));
        }
        else
        {
            _project.NavMesh = next.Clone();
            _project.SetChanged();
            RefreshStats();
        }
        _statusLabel.Text = $"{statusMessage}: {next.TriangleCount} triangles, {next.Vertices.Count} vertices";
    }

    private static bool TryParseVec3(string text, out Vector3 vec)
    {
        vec = Vector3.Zero;
        var parts = (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            vec = new Vector3(x, y, z);
            return true;
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _project.SessionChanged -= RefreshStats;
            _project.ProjectStateChanged -= RefreshStats;
        }
        base.Dispose(disposing);
    }

    private sealed class SdkNavMeshCommand : Command
    {
        private readonly Project _project;
        private readonly SdkNavigationPanel _panel;
        private readonly SdkNavMesh? _before;
        private readonly SdkNavMesh _after;

        public SdkNavMeshCommand(Project project, SdkNavigationPanel panel, SdkNavMesh? before, SdkNavMesh after)
        {
            _project = project;
            _panel = panel;
            _before = before;
            _after = after;
        }

        public override bool commit()
        {
            _project.NavMesh = _after.Clone();
            _project.SetChanged();
            _panel.RefreshStats();
            return true;
        }

        public override void rollback()
        {
            _project.NavMesh = _before?.Clone();
            _project.SetChanged();
            _panel.RefreshStats();
        }
    }
}

public partial class WindowIde
{
    private readonly List<SdkNavigationPanel> navigationPanels = new();

    public IReadOnlyList<SdkNavigationPanel> NavigationPanels => navigationPanels;

    public SdkNavigationPanel ShowNavigationPanel()
    {
        var existing = navigationPanels.FirstOrDefault(p => !p.IsDisposed);
        if (existing != null)
        {
            existing.RefreshStats();
            if (!BackgroundMode && Visible)
                existing.Activate();
            SetStatus("Navigation mesh panel opened");
            return existing;
        }

        var panel = new SdkNavigationPanel(m_project, Commands);
        navigationPanels.Add(panel);
        panel.Disposed += (_, _) => navigationPanels.Remove(panel);
        if (!BackgroundMode && Visible)
            panel.Show(m_dock_panel, DockState.Float);

        SetStatus("Navigation mesh panel opened");
        return panel;
    }
}
