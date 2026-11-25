using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Editors;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

public sealed class SdkNavigationPanel : IDisposable
{
    public const string PersistIdentity = "xray.editor.sdk_navigation_panel";

    private readonly Project _project;
    private bool _cleanedUp;

    public SdkNavigationPanel(Project project, CommandEngine commands)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkNavigationEditorPanel(new NavigationProject(project), commands);
        Window = new Window
        {
            Title = "Navigation mesh",
            Width = 760,
            Height = 500,
            Background = SdkWorkspaceTheme.Surface,
            Content = View
        };
        Window.Closed += OnWindowClosed;
        _project.SessionChanged += RefreshStats;
        _project.ProjectStateChanged += RefreshStats;
    }

    public Window Window { get; }
    public SdkNavigationEditorPanel View { get; }
    public SdkNavPathResult? LastPathResult => View.LastPathResult;
    public string Text => Window.Title ?? string.Empty;
    public bool Visible => Window.IsVisible;
    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public SdkNavMesh GenerateForProject(IReadOnlyCollection<Guid>? excludedObjectGuids = null) => View.GenerateForProject(excludedObjectGuids);
    public void SaveProjectNavMesh() => View.SaveProjectNavMesh();
    public SdkNavMesh LoadProjectNavMesh(string? explicitPath = null) => View.LoadProjectNavMesh(explicitPath);
    public SdkNavPathResult QueryPath(Vector3 start, Vector3 goal) => View.QueryPath(start, goal);
    public void RefreshStats() => View.RefreshStats();

    public void Show(object? _ = null)
    {
        if (!IsDisposed)
            Window.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            Window.Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => CleanupAndRaiseDisposed();

    private void CleanupAndRaiseDisposed()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;
        IsDisposed = true;
        Window.Closed -= OnWindowClosed;
        _project.SessionChanged -= RefreshStats;
        _project.ProjectStateChanged -= RefreshStats;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        try { Window.Close(); } catch { }
        CleanupAndRaiseDisposed();
    }

    private sealed class NavigationProject(Project project) : ISdkNavigationProject
    {
        public bool IsOpen => project.IsProjectOpen;

        public SdkNavMesh? NavMesh
        {
            get => project.NavMesh;
            set
            {
                project.NavMesh = value;
                project.SetChanged();
            }
        }

        public string GetNavMeshPath(bool forWrite)
        {
            string directory = !string.IsNullOrWhiteSpace(project.ProjectFilePath)
                ? Path.GetDirectoryName(Path.GetFullPath(project.ProjectFilePath))!
                : SdkVfsManager.ResolveVirtualToPhysical($"resources/projects/{project.Name}", forWrite);
            return Path.Combine(directory, "navmesh.lua");
        }

        public SdkNavMesh GenerateNavMesh(SdkNavMeshGenerationSettings settings, IReadOnlyCollection<Guid>? excludedObjectGuids)
            => SdkNavMeshGenerator.Generate(project, settings, excludedObjectGuids);
    }
}

public partial class WindowIde
{
    private readonly List<SdkNavigationPanel> navigationPanels = new();

    public IReadOnlyList<SdkNavigationPanel> NavigationPanels => navigationPanels;

    public SdkNavigationPanel ShowNavigationPanel()
    {
        var existing = navigationPanels.FirstOrDefault(panel => !panel.IsDisposed);
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
            panel.Show();

        SetStatus("Navigation mesh panel opened");
        return panel;
    }
}
