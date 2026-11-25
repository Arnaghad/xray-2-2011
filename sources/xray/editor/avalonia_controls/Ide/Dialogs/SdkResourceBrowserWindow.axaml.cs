using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public sealed class SdkVfsTreeNode : INotifyPropertyChanged
{
    private readonly bool m_isDirectory;
    private bool m_isExpanded;
    private bool m_populated;

    public SdkVfsTreeNode(string name, string virtualPath, bool isDirectory)
    {
        Name = name;
        VirtualPath = virtualPath;
        m_isDirectory = isDirectory;
        if (isDirectory)
            Children.Add(new SdkVfsTreeNode("...", string.Empty, isDirectory: false) { m_populated = true });
        else
            m_populated = true;
    }

    public string Name { get; }
    public string VirtualPath { get; }
    public bool IsDirectory => m_isDirectory;
    public ObservableCollection<SdkVfsTreeNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => m_isExpanded;
        set
        {
            if (m_isExpanded == value)
                return;
            m_isExpanded = value;
            if (value)
                EnsurePopulated();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void EnsurePopulated()
    {
        if (m_populated || !m_isDirectory || string.IsNullOrEmpty(VirtualPath))
            return;

        m_populated = true;
        Children.Clear();

        foreach (string dir in SdkVfsManager.EnumerateDirectories(VirtualPath))
        {
            string leaf = Path.GetFileName(dir);
            Children.Add(new SdkVfsTreeNode(leaf, VirtualPath + "/" + leaf, isDirectory: true));
        }

        foreach (string file in SdkVfsManager.EnumerateFiles(VirtualPath))
        {
            string leaf = Path.GetFileName(file);
            Children.Add(new SdkVfsTreeNode(leaf, VirtualPath + "/" + leaf, isDirectory: false));
        }
    }
}

/// <summary>
/// VFS & dependency browser dialog replacing WinForms SdkResourceBrowser.
/// </summary>
public partial class SdkResourceBrowserWindow : Window
{
    private readonly ObservableCollection<SdkVfsTreeNode> m_rootNodes = new();
    private bool m_loading;
    private bool m_closed;
    private Guid? m_activeQueryId;

    public SdkResourceBrowserWindow()
    {
        InitializeComponent();

        m_resource_tree.ItemsSource = m_rootNodes;
        m_resource_tree.SelectionChanged += (_, _) => OnNodeSelected();

        m_dependencies_button.Click += (_, _) => ShowDependencies();
        m_load_button.Click += (_, _) =>
        {
            if (SelectedVirtualPath is { } path)
                LoadPath(path);
        };
        m_cancel_query_button.Click += (_, _) => CancelLoading();
        m_preview_button.Click += (_, _) =>
        {
            if (SelectedVirtualPath is { } path)
                RequestPreview(path);
        };
        m_edit_source_button.Click += (_, _) =>
        {
            if (SelectedVirtualPath is { } path)
                RequestEditSource(path);
        };
        m_dcc_button.Click += (_, _) =>
        {
            if (SelectedVirtualPath is { } path)
                DccRequested?.Invoke(path);
        };

        Closed += (_, _) =>
        {
            m_closed = true;
            CancelLoading();
        };

        PopulateMounts();
    }

    public ObservableCollection<SdkVfsTreeNode> RootNodes => m_rootNodes;
    public SdkCookedCacheEntry? LastResult { get; private set; }
    public string DetailsText => m_details_box.Text ?? string.Empty;
    public string? SelectedVirtualPath => (m_resource_tree.SelectedItem as SdkVfsTreeNode)?.VirtualPath;

    public Func<string, (Guid QueryId, SdkCookedCacheEntry Result)>? LoadResourceHandler { get; set; }
    public Action<Guid>? CancelLoadingHandler { get; set; }
    public Func<SdkDependencyReport>? InspectDependenciesHandler { get; set; }
    public Func<string, string>? DccSourceDetails { get; set; }
    public event Action<string>? EditSourceRequested;
    public event Action<string>? DccRequested;
    public event Action<string, SdkOutputKind>? PreviewRequested;

    public void PopulateMounts()
    {
        m_rootNodes.Clear();
        foreach (string prefix in SdkVfsManager.GetMounts()
                     .Where(m => !m.IsDynamicRuntimeMount)
                     .Select(m => m.VirtualPrefix)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            m_rootNodes.Add(new SdkVfsTreeNode(prefix, prefix, isDirectory: true));
        }
    }

    public void LoadPath(string path)
    {
        if (m_loading || string.IsNullOrWhiteSpace(path))
            return;

        if (path.EndsWith(".model", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase))
        {
            path += "/render";
        }

        m_loading = true;
        m_load_button.IsEnabled = false;
        m_cancel_query_button.IsEnabled = true;
        LastResult = null;

        try
        {
            if (LoadResourceHandler == null)
                throw new InvalidOperationException("Resource loading handler is not attached.");

            var (queryId, result) = LoadResourceHandler(path);
            m_activeQueryId = queryId;
            if (!m_closed)
            {
                LastResult = result;
                m_details_box.Text = path + Environment.NewLine
                    + "Loaded: " + result.NativeBytes + " bytes; " + result.NativeUnits + " units" + Environment.NewLine
                    + (result.CacheHit ? "Cache hit" : "Loaded from source") + Environment.NewLine
                    + "Receipt: " + result.CookedPhysicalPath;
            }
        }
        catch (Exception error)
        {
            if (!m_closed)
                m_details_box.Text = "Resource load failed: " + error.Message;
        }
        finally
        {
            m_activeQueryId = null;
            m_loading = false;
            if (!m_closed)
            {
                m_load_button.IsEnabled = true;
                m_cancel_query_button.IsEnabled = false;
            }
        }
    }

    public void ShowDependencies()
    {
        if (InspectDependenciesHandler == null)
        {
            m_details_box.Text = "Dependencies: 0; missing: 0";
            return;
        }

        var report = InspectDependenciesHandler();
        m_details_box.Text = "Dependencies: " + report.TotalCount + "; missing: " + report.MissingCount + Environment.NewLine
            + string.Join(Environment.NewLine, report.Entries.Select(e =>
                (e.Exists ? "OK  " : "Missing  ") + e.Category + ": " + e.AuthoredReference));
    }

    public void RequestPreview(string path)
    {
        try
        {
            SdkOutputKind kind =
                path.EndsWith(".model", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".model/render", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".skinned_model/render", StringComparison.OrdinalIgnoreCase)
                    ? SdkOutputKind.Model
                : path.EndsWith(".material", StringComparison.OrdinalIgnoreCase)
                    ? SdkOutputKind.Material
                : path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                  path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                  path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? SdkOutputKind.Texture
                : throw new NotSupportedException("This resource does not have a viewport preview.");

            if (PreviewRequested == null)
                throw new NotSupportedException("Preview services are unavailable.");
            PreviewRequested(path, kind);
        }
        catch (Exception error)
        {
            m_details_box.Text = "Preview failed: " + error.Message;
        }
    }

    public void RequestEditSource(string path)
    {
        try
        {
            if (!path.EndsWith(".avs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".anim_clip", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".anim_collection", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".material", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".model", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".dlg", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".template", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".particle", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".raw_options", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".single_sound_options", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".sound_collection", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".composite_sound", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".editor_sound_scene", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Select a supported asset source (model, texture, material, animation, dialog, particle, or sound).");
            }

            if (EditSourceRequested == null)
                throw new NotSupportedException("Texture authoring services are unavailable.");
            EditSourceRequested(path);
        }
        catch (Exception error)
        {
            m_details_box.Text = "Source editor failed: " + error.Message;
        }
    }

    private void CancelLoading()
    {
        if (m_activeQueryId.HasValue)
            CancelLoadingHandler?.Invoke(m_activeQueryId.Value);
    }

    private void OnNodeSelected()
    {
        string path = SelectedVirtualPath ?? string.Empty;
        m_details_box.Text = path;
        try
        {
            if (!string.IsNullOrEmpty(path) && DccSourceDetails != null)
                m_details_box.Text += Environment.NewLine + DccSourceDetails(path);
        }
        catch (Exception error)
        {
            m_details_box.Text += Environment.NewLine + error.Message;
        }
    }
}
