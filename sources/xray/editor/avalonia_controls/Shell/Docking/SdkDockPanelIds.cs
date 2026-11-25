using System;
using System.Collections.Generic;

namespace xray.editor.avalonia_controls.Shell.Docking;

public static class SdkDockPanelIds
{
    public const string SceneView = "xray.editor_base.scene_view_panel";
    public const string LegacySceneView = "xray.editor.window_view";
    public const string ObjectInspector = "xray.editor.controls.item_properties_panel_base";
    public const string LegacyObjectInspector = "xray.editor.object_inspector_tab";
    public const string Project = "xray.editor.project_tab";
    public const string Tools = "xray.editor.tools_tab";
    public const string SceneGraph = "xray.editor.scene_graph_tab";
    public const string CameraView = "xray.editor_base.camera_view_panel";
    public const string AnimationCurveEditor = "xray.editor.animation_curve_editor_panel";
    public const string TriangulationTester = "xray.editor.triangulation_tester";

    public static readonly IReadOnlySet<string> KnownPanelIds = new HashSet<string>(StringComparer.Ordinal)
    {
        SceneView,
        LegacySceneView,
        ObjectInspector,
        LegacyObjectInspector,
        Project,
        Tools,
        SceneGraph,
        CameraView,
        AnimationCurveEditor,
        TriangulationTester
    };

    public static readonly IReadOnlyList<string> MinimalRequiredPanelIds = new[]
    {
        SceneView,
        ObjectInspector
    };

    public static readonly IReadOnlyList<string> IdeRequiredPanelIds = new[]
    {
        SceneView,
        Project,
        ObjectInspector,
        Tools
    };

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        if (string.Equals(id, LegacySceneView, StringComparison.Ordinal))
            return SceneView;
        if (string.Equals(id, LegacyObjectInspector, StringComparison.Ordinal))
            return ObjectInspector;

        return id;
    }

    public static bool IsKnownPanelId(string? id) =>
        !string.IsNullOrEmpty(id) && KnownPanelIds.Contains(id);
}
