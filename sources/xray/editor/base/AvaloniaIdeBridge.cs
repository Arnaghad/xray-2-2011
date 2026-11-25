using System;
using System.Collections.Generic;
using System.Linq;
using xray.editor.avalonia_controls.Ide.Dialogs;
using xray.editor.avalonia_controls.Ide.Panels;

namespace xray.editor_base;

/// <summary>
/// Live adapters connecting WindowIde / Project / InputEngine / SdkResourcePipeline
/// to pure-Avalonia UI-5 panels and dialogs.
/// </summary>
public static class AvaloniaIdeBridge
{
    public sealed class SceneViewPanelOptionsAdapter : ISdkSceneViewOptionsTarget
    {
        private readonly SceneViewPanel m_panel;

        public SceneViewPanelOptionsAdapter(SceneViewPanel panel)
        {
            m_panel = panel ?? throw new ArgumentNullException(nameof(panel));
        }

        public float NearPlane
        {
            get => m_panel.m_near_plane_val;
            set => m_panel.m_near_plane_val = value;
        }

        public float FarPlane
        {
            get => m_panel.m_far_plane_val;
            set => m_panel.m_far_plane_val = value;
        }

        public float ViewPointDistance
        {
            get => m_panel.view_point_distance;
            set => m_panel.view_point_distance = value;
        }

        public float MouseSensitivity
        {
            get => m_panel.mouse_sensitivity;
            set => m_panel.mouse_sensitivity = value;
        }

        public float SelectionRate
        {
            get => m_panel.m_selection_rate_val;
            set => m_panel.m_selection_rate_val = value;
        }

        public float GizmoSize
        {
            get => m_panel.gizmo_size;
            set => m_panel.gizmo_size = value;
        }

        public bool UsePostProcess
        {
            get => m_panel.UsePostProcess;
            set => m_panel.UsePostProcess = value;
        }

        public bool RenderGrid
        {
            get => m_panel.RenderGrid;
            set => m_panel.RenderGrid = value;
        }

        public float GridStep
        {
            get => m_panel.m_grid_step_val;
            set => m_panel.m_grid_step_val = value;
        }

        public uint GridDim
        {
            get => m_panel.m_grid_dim_val;
            set => m_panel.m_grid_dim_val = value;
        }
    }

    public sealed class InputEngineOptionsAdapter : ISdkInputOptionsAdapter
    {
        private readonly InputEngine m_input;

        public InputEngineOptionsAdapter(InputEngine input)
        {
            m_input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public List<string> GetActionNames() => m_input.get_actions_names_list();

        public List<string> GetActionShortcutsWithContexts(string actionName) =>
            m_input.get_action_keys_list_with_contexts(actionName);

        public bool AddShortcut(string actionName, string combination) =>
            m_input.add_action_shortcut(actionName, combination);

        public bool RemoveShortcut(string actionName, string combination) =>
            m_input.remove_action_shortcut(actionName, combination);

        public void ResetToDefaults() => m_input.reset_to_defaults();

        public Dictionary<string, List<string>> CaptureSnapshot()
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string name in m_input.get_actions_names_list())
            {
                var action = m_input.get_action(name);
                if (action != null)
                    dict[name] = action.m_shortcuts.Select(s => s.m_combination).ToList();
            }
            return dict;
        }

        public void RestoreSnapshot(Dictionary<string, List<string>> snapshot)
        {
            foreach (var kv in snapshot)
            {
                var action = m_input.get_action(kv.Key);
                if (action == null)
                    continue;
                action.m_shortcuts.Clear();
                foreach (string comb in kv.Value)
                    m_input.add_action_shortcut(kv.Key, comb);
            }
        }
    }

    public static SdkProjectTreeEntry? BuildProjectTreeEntry(Project project)
    {
        if (project.empty())
            return null;

        return BuildItemEntry(project.Root, project);
    }

    private static SdkProjectTreeEntry BuildItemEntry(ProjectItemBase item, Project project)
    {
        bool isFolder = !item.is_object();
        bool isLocked = item is ProjectItemFolder folder && folder.locked;
        string toolName = item is ProjectItemObject obj ? obj.ToolName : string.Empty;
        var children = item.Children.Select(c => BuildItemEntry(c, project)).ToList();
        return new SdkProjectTreeEntry(
            Name: item.Name,
            FullPath: item.FullPath,
            IsFolder: isFolder,
            IsLocked: isLocked,
            IsIgnored: project.is_object_in_ignore_list(item.Id),
            ToolName: toolName,
            Tag: item,
            Children: children);
    }

    public static SdkOptionsWindow CreateOptionsWindow(WindowIde ide)
    {
        var win = new SdkOptionsWindow();
        var viewAdapter = new SceneViewPanelOptionsAdapter(ide.ViewWindow);
        win.RegisterPage("General", new SdkOptionsPageGeneral(viewAdapter));
        win.RegisterPage(@"General\View Panel", new SdkOptionsPageViewPanel(viewAdapter));
        win.RegisterPage(@"General\Project", new SdkOptionsPageProject(
            () => ide.Project.TrackActiveItem,
            v => ide.Project.TrackActiveItem = v));
        win.RegisterPage("Input", new SdkOptionsPageInput(new InputEngineOptionsAdapter(ide.InputEngine)));
        return win;
    }

    public static SdkResourceBrowserWindow CreateResourceBrowserWindow(SdkResourcePipeline pipeline, Project project)
    {
        var win = new SdkResourceBrowserWindow
        {
            LoadResourceHandler = path =>
            {
                var query = pipeline.BeginQuery(path);
                var result = pipeline.QueryOrCookResource(path, query);
                return (query.QueryId, result);
            },
            CancelLoadingHandler = id => pipeline.CancelQuery(id),
            InspectDependenciesHandler = () => SdkResourcePipeline.InspectProjectDependencies(project, project.ToolLibraries)
        };
        win.ShowDependencies();
        return win;
    }

    public static SdkSelectLibraryClassWindow CreateSelectLibraryClassWindow(
        objects_library library,
        string? initial,
        string? filter,
        bool readOnly)
    {
        var entries = library.Items.Select(i => new SdkLibraryClassEntry(
            FullPath: i.full_path_name(),
            BaseClassName: i.m_lib_item_name,
            IconIndex: i.m_image_index,
            Tag: i));
        var win = new SdkSelectLibraryClassWindow();
        win.Initialize(entries, initial, filter, readOnly);
        return win;
    }

    public static SdkAboutWindow CreateAboutWindow(uint buildId = 2011, string buildDate = "2011-2026") =>
        new(buildId, buildDate);
}
