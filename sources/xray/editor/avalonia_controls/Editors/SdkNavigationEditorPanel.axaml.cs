using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Editors;

public interface ISdkNavigationProject
{
    bool IsOpen { get; }
    SdkNavMesh? NavMesh { get; set; }
    string GetNavMeshPath(bool forWrite);
    SdkNavMesh GenerateNavMesh(SdkNavMeshGenerationSettings settings, IReadOnlyCollection<Guid>? excludedObjectGuids);
}

public partial class SdkNavigationEditorPanel : UserControl
{
    private ISdkNavigationProject? _project;
    private CommandEngine? _commands;
    private SdkNavMesh? _displayedMesh;

    public SdkNavigationEditorPanel()
    {
        InitializeComponent();
        GenerateButton.Click += (_, _) => Run(() => GenerateForProject());
        SaveButton.Click += (_, _) => Run(SaveProjectNavMesh);
        LoadButton.Click += (_, _) => Run(() => LoadProjectNavMesh());
        UndoButton.Click += (_, _) => Run(() => { if (Commands.CanUndo) Commands.rollback(-1); });
        RedoButton.Click += (_, _) => Run(() => { if (Commands.CanRedo) Commands.rollback(1); });
        QueryButton.Click += (_, _) => Run(() =>
        {
            if (!TryParseCoordinates(StartBox.Text, out var start) || !TryParseCoordinates(GoalBox.Text, out var goal))
                throw new FormatException("Invalid XYZ coordinate.");
            QueryPath(start, goal);
        });
    }

    public SdkNavigationEditorPanel(ISdkNavigationProject project, CommandEngine commands) : this()
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        RefreshStats();
    }

    private ISdkNavigationProject Project => _project ?? throw new InvalidOperationException("No project is attached.");
    private CommandEngine Commands => _commands ?? throw new InvalidOperationException("No command history is attached.");

    public SdkNavPathResult? LastPathResult { get; private set; }

    public SdkNavMesh GenerateForProject(IReadOnlyCollection<Guid>? excludedObjectGuids = null)
    {
        if (!Project.IsOpen)
            throw new InvalidOperationException("Open or create a project before generating navigation mesh.");

        var settings = new SdkNavMeshGenerationSettings
        {
            CellSize = (float)(CellSizeBox.Value ?? throw new FormatException("Enter a cell size.")),
            AgentRadius = (float)(AgentRadiusBox.Value ?? throw new FormatException("Enter an agent radius.")),
            MaxSlopeDegrees = (float)(MaxSlopeBox.Value ?? throw new FormatException("Enter a maximum slope."))
        };
        return ApplyNavMesh(Project.GenerateNavMesh(settings, excludedObjectGuids), "Generated navmesh");
    }

    public void SaveProjectNavMesh()
    {
        if (Project.NavMesh is not { TriangleCount: > 0 } mesh)
            throw new InvalidOperationException("No generated navigation mesh to save.");

        string path = Project.GetNavMeshPath(forWrite: true);
        mesh.SaveToFile(path);
        RefreshStats();
        StatusLabel.Text = $"Saved navmesh: {path} ({mesh.TriangleCount} tris)";
    }

    public SdkNavMesh LoadProjectNavMesh(string? explicitPath = null)
    {
        string path = string.IsNullOrWhiteSpace(explicitPath) ? Project.GetNavMeshPath(forWrite: false) : explicitPath;
        return ApplyNavMesh(SdkNavMesh.LoadFromFile(path), "Loaded navmesh.lua");
    }

    public SdkNavPathResult QueryPath(Vector3 start, Vector3 goal)
    {
        if (Project.NavMesh is not { TriangleCount: > 0 } mesh)
            throw new InvalidOperationException("Generate or load a navigation mesh before querying paths.");

        RefreshStats();
        var result = SdkNavMeshPathfinder.FindPath(mesh, start, goal);
        LastPathResult = result;
        if (result.Success)
        {
            var waypoints = new List<string>
            {
                $"Path found: {result.TriangleCorridor.Count} tris, {result.Waypoints.Count} waypoints, length={result.TotalLength:F2}m"
            };
            waypoints.AddRange(result.Waypoints.Select(point => $"  ({point.X:F2}, {point.Y:F2}, {point.Z:F2})"));
            WaypointsList.ItemsSource = waypoints;
            StatusLabel.Text = $"Path length {result.TotalLength:F2}m ({result.Waypoints.Count} waypoints)";
        }
        else
        {
            WaypointsList.ItemsSource = new[] { "Blocked / unreachable: " + result.FailureReason };
            StatusLabel.Text = result.FailureReason;
        }
        return result;
    }

    public void RefreshStats()
    {
        var mesh = Project.NavMesh;
        if (!ReferenceEquals(_displayedMesh, mesh))
        {
            _displayedMesh = mesh;
            LastPathResult = null;
            WaypointsList.ItemsSource = null;
            StatusLabel.Text = string.Empty;
        }
        StatsLabel.Text = mesh == null
            ? "NavMesh: (none)"
            : $"NavMesh: {mesh.Vertices.Count} vertices | {mesh.TriangleCount} triangles | source={(string.IsNullOrEmpty(mesh.SourceFilePath) ? "<memory>" : Path.GetFileName(mesh.SourceFilePath))}";
    }

    private SdkNavMesh ApplyNavMesh(SdkNavMesh mesh, string status)
    {
        if (Commands.EditingEnabled && Project.IsOpen)
            Commands.run(new NavMeshCommand(Project, mesh));
        else
            Project.NavMesh = mesh.Clone();

        RefreshStats();
        StatusLabel.Text = $"{status}: {mesh.TriangleCount} triangles, {mesh.Vertices.Count} vertices";
        return Project.NavMesh!;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }

    private static bool TryParseCoordinates(string? text, out Vector3 coordinates)
    {
        coordinates = default;
        var parts = (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            return false;

        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            return false;
        coordinates = new Vector3(x, y, z);
        return true;
    }

    private sealed class NavMeshCommand(ISdkNavigationProject project, SdkNavMesh mesh) : Command
    {
        private readonly SdkNavMesh? _before = project.NavMesh?.Clone();
        private readonly SdkNavMesh _after = mesh.Clone();

        public override bool commit()
        {
            project.NavMesh = _after.Clone();
            return true;
        }

        public override void rollback() => project.NavMesh = _before?.Clone();
    }
}
