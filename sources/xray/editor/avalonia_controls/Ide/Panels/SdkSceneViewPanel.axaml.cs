using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using xray.editor.avalonia_controls.Controls;
using xray.editor.avalonia_controls.Ide.Dialogs;
using xray.editor.avalonia_controls.Shell;
using xray.editor.avalonia_controls.Shell.Docking;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Avalonia 3D scene viewport dock panel replacing WinForms SceneViewPanel.
/// </summary>
public partial class SdkSceneViewPanel : UserControl, ISdkSceneViewOptionsTarget
{
    public const string PersistId = SdkDockPanelIds.SceneView;
    public const string LegacyPersistId = SdkDockPanelIds.LegacySceneView;

    public static readonly string[] ViewModeNames =
    {
        "wireframe",
        "wireframe_two_sided",
        "unlit",
        "lit",
        "normals",
        "specular_intencity",
        "specular_power",
        "miplevel",
        "lighting",
        "lighting_diffuse",
        "lighting_specular",
        "shader_complexity",
        "texture_density",
        "geometry_complexity",
        "emissive_only",
        "distortion_only",
        "ambient_occlusion_only",
        "unlit_with_ao",
        "lit_with_histogram_view_mode",
        "overdraw_view_mode",
        "indirect_lighting_mode"
    };

    private readonly Dictionary<string, ToggleButton> m_toggleButtons = new(StringComparer.Ordinal);
    private readonly Button m_render_modes_button;
    private Control m_viewport;

    private bool m_use_postprocess = true;
    private bool m_render_grid;
    private bool m_camera_light;
    private bool m_sun_active = true;
    private bool m_wsad_mode_enabled;
    private bool m_culling_view_frozen;
    private bool m_debug_renderer_enabled;
    private bool m_selection_bounds = true;
    private bool m_selection_outlines = true;
    private bool m_gizmos_visible = true;
    private int m_render_view_mode = 3; // "lit"
    private EditorTransformMode m_transform_mode = EditorTransformMode.None;

    private float m_near_plane = 0.1f;
    private float m_far_plane = 300.0f;
    private float m_focus_distance = 70.7f;
    private float m_mouse_sensitivity = 0.3f;
    private float m_selection_rate = 1.0f;
    private float m_gizmo_size = 0.7f;
    private float m_grid_step = 1.0f;
    private uint m_grid_dim = 100;
    private SdkColor m_selection_color = new(255, 85, 0, 255);

    private Matrix4x4 m_inverted_view = Matrix4x4.CreateTranslation(0f, 2f, -10f);
    private Matrix4x4 m_projection = CreatePerspectiveProjection(MathF.PI / 3.0f, 4.0f / 3.0f, 0.1f, 300.0f);

    public SdkSceneViewPanel()
        : this(null, showCameraLightActions: true)
    {
    }

    public SdkSceneViewPanel(Control? existingViewport = null, bool showCameraLightActions = true)
    {
        InitializeComponent();

        m_viewport = existingViewport ?? new Border
        {
            Background = Brushes.Black,
            Focusable = true
        };
        m_viewport_container.Content = m_viewport;
        m_viewport.SizeChanged += (_, _) => RecalculateProjection();

        m_render_modes_button = BuildToolbar(showCameraLightActions);
        ApplyRenderViewMode(textOnly: true);
        RecalculateProjection();
    }

    public event Action? CameraMoved;
    public event Action<string>? StatusChanged;
    public Func<int, bool>? RenderModeSupported { get; set; }

    public Control ViewportControl => m_viewport;
    public AvaloniaViewportHost? ViewportHost => m_viewport as AvaloniaViewportHost;
    public WrapPanel ToolbarPanel => m_toolbar_panel;
    public Button RenderModesButton => m_render_modes_button;
    public IReadOnlyDictionary<string, ToggleButton> ToggleButtons => m_toggleButtons;

    public bool UsePostProcess
    {
        get => m_use_postprocess;
        set { m_use_postprocess = value; SyncToggle("toggle post process", value); }
    }

    public bool SsaoEnabled { get; set; }
    public bool DistortionEnabled { get; set; }
    public bool FxaaEnabled { get; set; }
    public bool SunShadowsEnabled { get; set; } = true;

    public bool RenderGrid
    {
        get => m_render_grid;
        set { m_render_grid = value; SyncToggle("render grid", value); }
    }

    public bool CameraLight
    {
        get => m_camera_light;
        set { m_camera_light = value; SyncToggle("camera light", value); }
    }

    public bool SunActive
    {
        get => m_sun_active;
        set { m_sun_active = value; SyncToggle("sun", value); }
    }

    public bool WsadMode
    {
        get => m_wsad_mode_enabled;
        set { m_wsad_mode_enabled = value; SyncToggle("toggle WSAD mode", value); }
    }

    public bool CullingViewFrozen
    {
        get => m_culling_view_frozen;
        set { m_culling_view_frozen = value; SyncToggle("toggle culling view", value); }
    }

    public bool DebugRendererEnabled
    {
        get => m_debug_renderer_enabled;
        set => m_debug_renderer_enabled = value;
    }

    public bool SelectionBounds
    {
        get => m_selection_bounds;
        set { m_selection_bounds = value; SyncToggle("selection bounds", value); }
    }

    public bool SelectionOutlines
    {
        get => m_selection_outlines;
        set { m_selection_outlines = value; SyncToggle("selection outlines", value); }
    }

    public bool GizmosVisible
    {
        get => m_gizmos_visible;
        set { m_gizmos_visible = value; SyncToggle("transform gizmos", value); }
    }

    public float? SelectionTimeOverride { get; set; }
    public EditorTransformMode TransformMode
    {
        get => m_transform_mode;
        set => m_transform_mode = value;
    }

    public int RenderViewModeIndex => m_render_view_mode;
    public string RenderViewModeName => ViewModeNames[m_render_view_mode];

    public float NearPlane
    {
        get => m_near_plane;
        set { m_near_plane = Math.Max(0.01f, value); RecalculateProjection(); }
    }

    public float FarPlane
    {
        get => m_far_plane;
        set { m_far_plane = Math.Max(m_near_plane * 2f, value); RecalculateProjection(); }
    }

    public float ViewPointDistance
    {
        get => m_focus_distance;
        set => m_focus_distance = Math.Max(0.1f, value);
    }

    public float MouseSensitivity
    {
        get => m_mouse_sensitivity;
        set => m_mouse_sensitivity = Math.Max(0.01f, value);
    }

    public float SelectionRate
    {
        get => m_selection_rate;
        set => m_selection_rate = Math.Clamp(value, 0f, 10f);
    }

    public float GizmoSize
    {
        get => m_gizmo_size;
        set => m_gizmo_size = Math.Clamp(value, 0.05f, 5f);
    }

    public float GridStep
    {
        get => m_grid_step;
        set
        {
            if (!float.IsFinite(value) || value > 10000f)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_grid_step = Math.Max(0.1f, value);
        }
    }

    public uint GridDim
    {
        get => m_grid_dim;
        set
        {
            if (value > 10000u)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_grid_dim = Math.Max(1u, value);
        }
    }

    public SdkColor SelectionColor
    {
        get => m_selection_color;
        set => m_selection_color = value;
    }

    public void AttachViewport(Control viewport)
    {
        if (ReferenceEquals(m_viewport, viewport))
            return;

        m_viewport_container.Content = null;
        m_viewport = viewport;
        m_viewport_container.Content = m_viewport;
        m_viewport.SizeChanged += (_, _) => RecalculateProjection();
        RecalculateProjection();
    }

    public void CycleRenderViewMode()
    {
        for (int i = 0; i < ViewModeNames.Length; i++)
        {
            m_render_view_mode = (m_render_view_mode + 1) % ViewModeNames.Length;
            if (RenderModeSupported?.Invoke(m_render_view_mode) != false)
                break;
        }
        ApplyRenderViewMode(textOnly: false);
    }

    public void SetRenderViewMode(string modeName)
    {
        int idx = Array.IndexOf(ViewModeNames, modeName);
        if (idx < 0)
            throw new ArgumentException("Unknown render mode: " + modeName, nameof(modeName));
        if (RenderModeSupported?.Invoke(idx) == false)
            throw new NotSupportedException("Renderer does not support " + modeName);

        m_render_view_mode = idx;
        ApplyRenderViewMode(textOnly: false);
    }

    public ContextMenu BuildRenderModesContextMenu()
    {
        var menu = new ContextMenu();
        for (int mode = 0; mode < ViewModeNames.Length; ++mode)
        {
            int captured = mode;
            var item = new MenuItem
            {
                Header = ViewModeNames[mode],
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = mode == m_render_view_mode,
                IsEnabled = RenderModeSupported?.Invoke(mode) != false
            };
            item.Click += (_, _) => SetRenderViewMode(ViewModeNames[captured]);
            menu.Items.Add(item);
        }
        return menu;
    }

    public PixelSize GetViewSize()
    {
        if (m_viewport is AvaloniaViewportHost host && host.SurfacePixelWidth > 0 && host.SurfacePixelHeight > 0)
            return new PixelSize(host.SurfacePixelWidth, host.SurfacePixelHeight);

        int w = (int)Math.Round(m_viewport.Bounds.Width);
        int h = (int)Math.Round(m_viewport.Bounds.Height);
        if (w <= 0 && !double.IsNaN(m_viewport.Width))
            w = (int)Math.Round(m_viewport.Width);
        if (h <= 0 && !double.IsNaN(m_viewport.Height))
            h = (int)Math.Round(m_viewport.Height);
        return new PixelSize(Math.Max(1, w), Math.Max(1, h));
    }

    public Vector3 ScreenTo3D(Vector2 screenCenterRelative, float zDist)
    {
        RecalculateProjection();
        PixelSize vs = GetViewSize();
        float halfW = Math.Max(0.5f, vs.Width * 0.5f);
        float halfH = Math.Max(0.5f, vs.Height * 0.5f);
        float x = screenCenterRelative.X * (zDist / m_projection.M11 / halfW);
        float y = screenCenterRelative.Y * (zDist / m_projection.M22 / halfH);
        return new Vector3(x, y, zDist);
    }

    public void GetScreenRay(Vector2 centerRelativeXY, out Vector3 origin, out Vector3 direction)
    {
        Vector3 localDir = ScreenTo3D(new Vector2(centerRelativeXY.X, -centerRelativeXY.Y), 1.0f);
        Vector3 worldDir = Vector3.TransformNormal(localDir, m_inverted_view);
        direction = Vector3.Normalize(worldDir);
        origin = m_inverted_view.Translation;
    }

    public void GetMouseRayFromNormalizedViewportPoint(float nx, float ny, out Vector3 origin, out Vector3 direction)
    {
        RecalculateProjection();
        PixelSize vs = GetViewSize();
        float halfW = vs.Width * 0.5f;
        float halfH = vs.Height * 0.5f;
        var exactRel = new Vector2(nx * halfW, -ny * halfH);
        Vector3 localDir = ScreenTo3D(exactRel, 1.0f);
        direction = Vector3.Normalize(Vector3.TransformNormal(localDir, m_inverted_view));
        origin = m_inverted_view.Translation;
    }

    public void SetViewMatrix(Matrix4x4 viewMatrix)
    {
        if (!Matrix4x4.Invert(viewMatrix, out var inv))
            throw new ArgumentException("Camera view must be invertible.", nameof(viewMatrix));

        if (viewMatrix.M14 == 0f && viewMatrix.M24 == 0f && viewMatrix.M34 == 0f && viewMatrix.M44 == 1f
            && Math.Abs(inv.M14) < 1e-5f && Math.Abs(inv.M24) < 1e-5f && Math.Abs(inv.M34) < 1e-5f && Math.Abs(inv.M44 - 1f) < 1e-5f)
        {
            inv.M14 = 0f;
            inv.M24 = 0f;
            inv.M34 = 0f;
            inv.M44 = 1f;
        }
        SetCameraWorld(inv);
    }

    public void SetCameraWorld(Matrix4x4 value)
    {
        var entries = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
        foreach (float entry in entries)
        {
            if (!float.IsFinite(entry))
                throw new ArgumentException("Camera must be finite.", nameof(value));
        }

        if (value.M14 != 0f || value.M24 != 0f || value.M34 != 0f || value.M44 != 1f ||
            !Matrix4x4.Invert(value, out _) ||
            Math.Abs(value.M41) > 1e6f || Math.Abs(value.M42) > 1e6f || Math.Abs(value.M43) > 1e6f)
        {
            throw new ArgumentException("Invalid camera transform.", nameof(value));
        }

        m_inverted_view = value;
        CameraMoved?.Invoke();
    }

    public Matrix4x4 GetInvertedViewMatrix() => m_inverted_view;

    public Matrix4x4 GetProjectionMatrix()
    {
        RecalculateProjection();
        return m_projection;
    }

    public void LoadSettings(IDictionary<string, string> settings)
    {
        if (settings.TryGetValue("draw_grid", out var dg) && bool.TryParse(dg, out bool bGrid))
            RenderGrid = bGrid;
        if (settings.TryGetValue("near_plane", out var np) && float.TryParse(np, NumberStyles.Float, CultureInfo.InvariantCulture, out float fNear))
            NearPlane = Math.Max(0.1f, fNear);
        if (settings.TryGetValue("far_plane", out var fp) && float.TryParse(fp, NumberStyles.Float, CultureInfo.InvariantCulture, out float fFar))
            FarPlane = Math.Max(2f * m_near_plane, fFar);
        if (settings.TryGetValue("selection_rate", out var sr) && float.TryParse(sr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fSel))
            SelectionRate = fSel;
        if (settings.TryGetValue("grid_step", out var gs) && float.TryParse(gs, NumberStyles.Float, CultureInfo.InvariantCulture, out float fStep))
            GridStep = fStep;
        if (settings.TryGetValue("grid_dim", out var gd) && uint.TryParse(gd, out uint uDim))
            GridDim = uDim;
        if (settings.TryGetValue("mouse_sensitivity", out var ms) && float.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out float fSens))
            MouseSensitivity = fSens;
        if (settings.TryGetValue("render_mode", out var rm) && !string.IsNullOrEmpty(rm))
            SetRenderViewMode(rm);
    }

    public void SaveSettings(IDictionary<string, string> settings)
    {
        settings["draw_grid"] = m_render_grid.ToString();
        settings["near_plane"] = m_near_plane.ToString(CultureInfo.InvariantCulture);
        settings["far_plane"] = m_far_plane.ToString(CultureInfo.InvariantCulture);
        settings["selection_rate"] = m_selection_rate.ToString(CultureInfo.InvariantCulture);
        settings["grid_step"] = m_grid_step.ToString(CultureInfo.InvariantCulture);
        settings["grid_dim"] = m_grid_dim.ToString(CultureInfo.InvariantCulture);
        settings["mouse_sensitivity"] = m_mouse_sensitivity.ToString(CultureInfo.InvariantCulture);
        settings["render_mode"] = ViewModeNames[m_render_view_mode];
    }

    private Button BuildToolbar(bool showCameraLightActions)
    {
        AddToggle("toggle post process", images16x16.toggle_post_process, m_use_postprocess, () =>
        {
            UsePostProcess = !m_use_postprocess;
            StatusChanged?.Invoke($"Post process: {(m_use_postprocess ? "on" : "off")}");
        });
        AddToggle("render grid", images16x16.toggle_draw_grid, m_render_grid, () =>
        {
            RenderGrid = !m_render_grid;
            StatusChanged?.Invoke($"Render grid: {(m_render_grid ? "on" : "off")}");
        });
        AddToggle("selection bounds", images16x16.node_mesh, m_selection_bounds, () =>
        {
            SelectionBounds = !m_selection_bounds;
            StatusChanged?.Invoke($"Selection bounds: {(m_selection_bounds ? "on" : "off")}");
        });
        AddToggle("transform gizmos", images16x16.tool_translate, m_gizmos_visible, () =>
        {
            GizmosVisible = !m_gizmos_visible;
            StatusChanged?.Invoke($"Transform gizmos: {(m_gizmos_visible ? "on" : "off")}");
        });
        AddToggle("selection outlines", images16x16.node_mesh, m_selection_outlines, () =>
        {
            SelectionOutlines = !m_selection_outlines;
            StatusChanged?.Invoke($"Selection outlines: {(m_selection_outlines ? "on" : "off")}");
        });

        if (showCameraLightActions)
        {
            AddToggle("camera light", images16x16.turn_camera_light, m_camera_light, () =>
            {
                CameraLight = !m_camera_light;
                StatusChanged?.Invoke($"Camera light: {(m_camera_light ? "on" : "off")}");
            });
            AddToggle("sun", images16x16.sun_light, m_sun_active, () =>
            {
                SunActive = !m_sun_active;
                StatusChanged?.Invoke($"Sun light: {(m_sun_active ? "on" : "off")}");
            });
        }

        AddToggle("toggle culling view", images16x16.terrain_debug_mode, m_culling_view_frozen, () =>
        {
            CullingViewFrozen = !m_culling_view_frozen;
            StatusChanged?.Invoke($"Culling view: {(m_culling_view_frozen ? "frozen" : "live")}");
        });
        AddToggle("toggle WSAD mode", images16x16.camera_wasd_move, m_wsad_mode_enabled, () =>
        {
            WsadMode = !m_wsad_mode_enabled;
            StatusChanged?.Invoke($"WSAD mode: {(m_wsad_mode_enabled ? "on" : "off")}");
        });

        var renderModesBtn = new Button
        {
            Margin = new Thickness(4, 1, 2, 1),
            Padding = new Thickness(9, 3),
            Content = ViewModeNames[m_render_view_mode]
        };
        renderModesBtn.Classes.Add("sdkTool");
        ToolTip.SetTip(renderModesBtn, "Render view mode (Left-click: cycle, Right-click: select)");
        renderModesBtn.Click += (_, _) => CycleRenderViewMode();
        renderModesBtn.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(renderModesBtn).Properties.IsRightButtonPressed)
            {
                var menu = BuildRenderModesContextMenu();
                menu.Open(renderModesBtn);
                e.Handled = true;
            }
        };
        m_toolbar_panel.Children.Add(renderModesBtn);
        return renderModesBtn;
    }

    private void AddToggle(string name, images16x16 icon, bool initialState, Action onToggle)
    {
        var btn = new ToggleButton
        {
            IsChecked = initialState,
            Margin = new Thickness(1),
            Padding = new Thickness(5, 4),
            Content = new Image
            {
                Source = AvaloniaImageAtlas.Shared.GetIcon(icon),
                Width = 16,
                Height = 16
            }
        };
        btn.Classes.Add("sdkIconTool");
        ToolTip.SetTip(btn, name);
        btn.Click += (_, _) => onToggle();
        m_toggleButtons[name] = btn;
        m_toolbar_panel.Children.Add(btn);
    }

    private void SyncToggle(string name, bool state)
    {
        if (m_toggleButtons.TryGetValue(name, out var btn))
            btn.IsChecked = state;
    }

    private void ApplyRenderViewMode(bool textOnly)
    {
        m_render_modes_button.Content = ViewModeNames[m_render_view_mode];
        if (!textOnly)
            StatusChanged?.Invoke($"Render mode: {ViewModeNames[m_render_view_mode]}");
    }

    private void RecalculateProjection()
    {
        PixelSize vs = GetViewSize();
        float aspect = (float)Math.Max(1, vs.Width) / Math.Max(1, vs.Height);
        m_projection = CreatePerspectiveProjection(MathF.PI / 3.0f, aspect, m_near_plane, m_far_plane);
    }

    private static Matrix4x4 CreatePerspectiveProjection(float fovY, float aspect, float nearPlane, float farPlane)
    {
        float yScale = 1.0f / MathF.Tan(fovY * 0.5f);
        float xScale = yScale / Math.Max(0.01f, aspect);
        float q = farPlane / Math.Max(0.01f, farPlane - nearPlane);
        return new Matrix4x4(
            xScale, 0, 0, 0,
            0, yScale, 0, 0,
            0, 0, q, 1,
            0, 0, -q * nearPlane, 0);
    }
}
