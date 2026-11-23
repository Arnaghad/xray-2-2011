// C# port of IDE panels and dialogs from sources/xray/editor/base/sources/ and sources/xray/editor/world/sources/
// Copyright (C) GSC Game World - 2009-2011

#pragma warning disable WFO1000

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WeifenLuo.WinFormsUI.Docking;
using xray.editor.controls;
using xray.editor.controls.hypergraph;
using Controls = xray.editor.wpf_controls;

namespace xray.editor_base
{
    public enum EditorTransformMode
    {
        None,
        Translate,
        Rotate,
        Scale
    }

    // C# port of base/sources/scene_view_panel.{h,cpp}
    public class SceneViewPanel : DockContent
    {
        public const string PersistIdentity = "xray.editor_base.scene_view_panel";
        public const string LegacyPersistIdentity = "xray.editor.window_view";

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

        public readonly Panel top_panel = new Panel();
        public readonly ToolStripPanel m_top_toolstrip_panel = new ToolStripPanel();
        private Control m_view_panel;
        public ToolStripButton? m_render_modes_button;

        private bool m_use_postprocess = true;
        private bool m_render_grid;
        private bool m_camera_light;
        private bool m_sun_active = true;
        private bool m_wsad_mode_enabled;
        private bool m_culling_view_frozen;
        private bool m_debug_renderer_enabled;
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
        private Color m_selection_color = Color.FromArgb(255, 85, 0, 255);

        private Matrix4x4 m_inverted_view = Matrix4x4.CreateTranslation(0f, 2f, -10f);
        private Matrix4x4 m_projection = CreatePerspectiveProjection(MathF.PI / 3.0f, 4.0f / 3.0f, 0.1f, 300.0f);

        public event System.Action? CameraMoved;
        public event Action<string>? StatusChanged;
        public Func<int,bool>? RenderModeSupported { get; set; }
        public bool SelectionBounds { get; set; } = true;
        public bool SelectionOutlines {get;set;}=true;
        public float? SelectionTimeOverride {get;set;}
        public bool GizmosVisible { get; set; } = true;

        public SceneViewPanel(Control? existingViewport = null)
        {
            m_view_panel = existingViewport ?? new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Name = "view_panel" };
            m_view_panel.Dock = DockStyle.Fill;

            SuspendLayout();
            top_panel.Dock = DockStyle.Top;
            top_panel.AutoSize = true;
            top_panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top_panel.MinimumSize = new Size(0, 25);
            top_panel.Name = "top_panel";
            top_panel.BackColor = SdkWindowStyle.Chrome;
            m_top_toolstrip_panel.Dock = DockStyle.Top;
            m_top_toolstrip_panel.BackColor = SdkWindowStyle.Chrome;
            top_panel.Controls.Add(m_top_toolstrip_panel);

            Controls.Add(m_view_panel);
            Controls.Add(top_panel);

            DockAreas = DockAreas.Document | DockAreas.Float;
            CloseButton = false;
            CloseButtonVisible = false;
            HideOnClose = true;
            Name = "scene_view_panel";
            Text = "View";
            TabText = "View";

            create_tool_strip("RenderOptionsStrip", 0);
            m_view_panel.Resize += (_, _) => RecalculateProjection();
            RecalculateProjection();
            ResumeLayout(false);
            PerformLayout();
        }

        protected override string GetPersistString() => PersistIdentity;

        public Control view_panel => m_view_panel;

        public void AttachViewport(Control viewport)
        {
            if (ReferenceEquals(m_view_panel, viewport)) return;
            Controls.Remove(m_view_panel);
            m_view_panel = viewport;
            m_view_panel.Dock = DockStyle.Fill;
            Controls.Add(m_view_panel);
            Controls.SetChildIndex(m_view_panel, 0);
            RecalculateProjection();
        }

        public bool UsePostProcess { get => m_use_postprocess; set => m_use_postprocess = value; }
        public bool SsaoEnabled { get; set; }
        public bool DistortionEnabled { get; set; }
        public bool FxaaEnabled { get; set; }
        public bool SunShadowsEnabled { get; set; } = true;
        public bool RenderGrid { get => m_render_grid; set => m_render_grid = value; }
        public bool CameraLight { get => m_camera_light; set => m_camera_light = value; }
        public bool SunActive { get => m_sun_active; set => m_sun_active = value; }
        public bool WsadMode { get => m_wsad_mode_enabled; set => m_wsad_mode_enabled = value; }
        public bool CullingViewFrozen { get => m_culling_view_frozen; set => m_culling_view_frozen = value; }
        public bool DebugRendererEnabled { get => m_debug_renderer_enabled; set => m_debug_renderer_enabled = value; }
        public EditorTransformMode TransformMode { get => m_transform_mode; set => m_transform_mode = value; }
        public int RenderViewModeIndex => m_render_view_mode;
        public string RenderViewModeName => ViewModeNames[m_render_view_mode];

        public float m_near_plane_val { get => m_near_plane; set { m_near_plane = Math.Max(0.01f, value); RecalculateProjection(); } }
        public float m_far_plane_val { get => m_far_plane; set { m_far_plane = Math.Max(m_near_plane * 2f, value); RecalculateProjection(); } }
        public float view_point_distance { get => m_focus_distance; set => m_focus_distance = Math.Max(0.1f, value); }
        public float mouse_sensitivity { get => m_mouse_sensitivity; set => m_mouse_sensitivity = Math.Max(0.01f, value); }
        public float m_selection_rate_val { get => m_selection_rate; set => m_selection_rate = Math.Clamp(value, 0f, 10f); }
        public float gizmo_size { get => m_gizmo_size; set => m_gizmo_size = Math.Clamp(value, 0.05f, 5f); }
        public float m_grid_step_val { get => m_grid_step; set { if(!float.IsFinite(value) || value>10000)throw new ArgumentOutOfRangeException(nameof(value));m_grid_step=Math.Max(.1f,value); } }
        public uint m_grid_dim_val { get => m_grid_dim; set {if(value>10000)throw new ArgumentOutOfRangeException(nameof(value));m_grid_dim=Math.Max(1u,value);} }
        public Color selection_color { get => m_selection_color; set => m_selection_color = value; }

        public ToolStrip create_tool_strip(string name, int prio)
        {
            int idx = m_top_toolstrip_panel.Controls.IndexOfKey(name);
            if (idx != -1) return (ToolStrip)m_top_toolstrip_panel.Controls[idx];

            var strip = new ToolStrip
            {
                Name = name,
                Tag = prio,
                ImageScalingSize = new Size(16, 16)
            };
            SdkWindowStyle.StyleStrip(strip);
            m_top_toolstrip_panel.Join(strip);
            return strip;
        }

        public ToolStrip get_tool_strip(string name)
        {
            int idx = m_top_toolstrip_panel.Controls.IndexOfKey(name);
            if (idx == -1)
                return create_tool_strip(name, 0);
            return (ToolStrip)m_top_toolstrip_panel.Controls[idx];
        }

        public ToolStripButton add_action_button_item(GuiBinder binder, string actionName, string stripKey, int prio)
        {
            ToolStrip strip = get_tool_strip(stripKey);
            var button = new ToolStripButton
            {
                Name = actionName,
                Tag = prio
            };
            strip.Items.Add(button);
            binder.bind(button, actionName);
            var img = binder.get_image(actionName);
            if (img != null)
            {
                button.Image = img;
                button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                button.ImageScaling = ToolStripItemImageScaling.SizeToFit;
            }
            else
            {
                button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            }
            return button;
        }

        public void register_actions(InputEngine ie, GuiBinder binder, bool cameraLight)
        {
            RegisterToggleAction(ie, binder, "toggle post process",
                () => { m_use_postprocess = !m_use_postprocess; StatusChanged?.Invoke($"Post process: {(m_use_postprocess ? "on" : "off")}"); },
                () => m_use_postprocess, (int)images16x16.toggle_post_process, "RenderOptionsStrip", 0);

            RegisterToggleAction(ie, binder, "render grid",
                () => { m_render_grid = !m_render_grid; StatusChanged?.Invoke($"Render grid: {(m_render_grid ? "on" : "off")}"); },
                () => m_render_grid, (int)images16x16.toggle_draw_grid, "RenderOptionsStrip", 2);
            RegisterToggleAction(ie,binder,"selection bounds",()=>{SelectionBounds=!SelectionBounds;StatusChanged?.Invoke($"Selection bounds: {(SelectionBounds?"on":"off")}");},
                ()=>SelectionBounds,(int)images16x16.node_mesh,"RenderOptionsStrip",3);
            RegisterToggleAction(ie,binder,"transform gizmos",()=>{GizmosVisible=!GizmosVisible;StatusChanged?.Invoke($"Transform gizmos: {(GizmosVisible?"on":"off")}");},
                ()=>GizmosVisible,(int)images16x16.tool_translate,"RenderOptionsStrip",4);
            RegisterToggleAction(ie,binder,"selection outlines",()=>{SelectionOutlines=!SelectionOutlines;StatusChanged?.Invoke($"Selection outlines: {(SelectionOutlines?"on":"off")}");},
                ()=>SelectionOutlines,(int)images16x16.node_mesh,"RenderOptionsStrip",5);

            if (cameraLight)
            {
                RegisterToggleAction(ie, binder, "camera light",
                    () => { m_camera_light = !m_camera_light; StatusChanged?.Invoke($"Camera light: {(m_camera_light ? "on" : "off")}"); },
                    () => m_camera_light, (int)images16x16.turn_camera_light, "RenderOptionsStrip", 3);

                RegisterToggleAction(ie, binder, "sun",
                    () => { m_sun_active = !m_sun_active; StatusChanged?.Invoke($"Sun light: {(m_sun_active ? "on" : "off")}"); },
                    () => m_sun_active, (int)images16x16.sun_light, "RenderOptionsStrip", 3);
            }

            RegisterToggleAction(ie, binder, "toggle culling view",
                () => { m_culling_view_frozen = !m_culling_view_frozen; StatusChanged?.Invoke($"Culling view: {(m_culling_view_frozen ? "frozen" : "live")}"); },
                () => m_culling_view_frozen, (int)images16x16.terrain_debug_mode, "RenderOptionsStrip", 10);

            RegisterToggleAction(ie, binder, "toggle WSAD mode",
                () => { m_wsad_mode_enabled = !m_wsad_mode_enabled; StatusChanged?.Invoke($"WSAD mode: {(m_wsad_mode_enabled ? "on" : "off")}"); },
                () => m_wsad_mode_enabled, (int)images16x16.camera_wasd_move, "RenderOptionsStrip", 11);

            m_render_modes_button = new ToolStripButton
            {
                Name = "m_render_modes_button",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Render view mode (Left-click: cycle, Right-click: select)"
            };
            apply_render_view_mode(true);
            get_tool_strip("RenderOptionsStrip").Items.Add(m_render_modes_button);
            m_render_modes_button.Click += render_mode_btn_click;
            m_render_modes_button.MouseDown += render_mode_btn_mouse_down;
        }

        private void RegisterToggleAction(
            InputEngine ie,
            GuiBinder binder,
            string actionName,
            ExecuteDelegate exec,
            CheckedDelegate check,
            int imageIdx,
            string stripKey,
            int prio)
        {
            var a = new ActionDelegate(actionName, exec);
            a.set_checked(check);
            if (!ie.action_exists(actionName))
                ie.register_action(a, string.Empty);
            binder.register_image("base", actionName, imageIdx);
            add_action_button_item(binder, actionName, stripKey, prio);
        }

        private void render_mode_btn_click(object? sender, EventArgs e)
        {
            for(int i=0;i<ViewModeNames.Length;i++) {
                m_render_view_mode = (m_render_view_mode + 1) % ViewModeNames.Length;
                if(RenderModeSupported?.Invoke(m_render_view_mode)!=false)break;
            }
            apply_render_view_mode(false);
        }

        public void SetRenderViewMode(string modeName)
        {
            int idx = Array.IndexOf(ViewModeNames, modeName);
            if(idx<0)throw new ArgumentException("Unknown render mode: "+modeName,nameof(modeName));
            if(idx>=0 && RenderModeSupported?.Invoke(idx)==false)throw new NotSupportedException("Renderer does not support "+modeName);
            m_render_view_mode = idx;
            apply_render_view_mode(false);
        }

        public void apply_render_view_mode(bool textOnly)
        {
            if (m_render_modes_button != null)
                m_render_modes_button.Text = ViewModeNames[m_render_view_mode];
            if (!textOnly)
                StatusChanged?.Invoke($"Render mode: {ViewModeNames[m_render_view_mode]}");
        }

        public ContextMenuStrip BuildRenderModesContextMenu()
        {
            var menu = new ContextMenuStrip();
            SdkWindowStyle.StyleStrip(menu);
            for (int mode = 0; mode < ViewModeNames.Length; ++mode)
            {
                int captured = mode;
                var item = new ToolStripMenuItem(ViewModeNames[mode], null, (_, _) =>
                {
                    SetRenderViewMode(ViewModeNames[captured]);
                })
                {
                    Tag = mode,
                    Checked = mode == m_render_view_mode
                    ,Enabled = RenderModeSupported?.Invoke(mode)!=false
                };
                menu.Items.Add(item);
            }
            return menu;
        }

        private void render_mode_btn_mouse_down(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                var menu = BuildRenderModesContextMenu();
                menu.Show(Cursor.Position);
            }
        }

        // Original viewport coordinate and 3D picking ray math (scene_view_panel.cpp:516-644)
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

        private void RecalculateProjection()
        {
            Size vs = get_view_size();
            float aspect = (float)Math.Max(1, vs.Width) / Math.Max(1, vs.Height);
            m_projection = CreatePerspectiveProjection(MathF.PI / 3.0f, aspect, m_near_plane, m_far_plane);
        }

        public Size get_view_size()
        {
            Size sz = m_view_panel.ClientSize;
            if (sz.Width <= 0 || sz.Height <= 0)
                sz = m_view_panel.Size;
            return new Size(Math.Max(1, sz.Width), Math.Max(1, sz.Height));
        }

        public Point get_view_center()
        {
            var rect = m_view_panel.DisplayRectangle;
            Point center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            return m_view_panel.PointToScreen(center);
        }

        public Point get_mouse_position() => GetMousePositionFromScreenPoint(Cursor.Position);

        public Point GetMousePositionFromScreenPoint(Point screenPoint)
        {
            Size sz = get_view_size();
            Point clientPt = m_view_panel.PointToClient(screenPoint);
            return new Point(clientPt.X - sz.Width / 2, clientPt.Y - sz.Height / 2);
        }

        public Vector3 screen_to_3d(Vector2 screenCenterRelative, float zDist)
        {
            RecalculateProjection();
            Size vs = get_view_size();
            float halfW = Math.Max(0.5f, vs.Width * 0.5f);
            float halfH = Math.Max(0.5f, vs.Height * 0.5f);
            float x = screenCenterRelative.X * (zDist / m_projection.M11 / halfW);
            float y = screenCenterRelative.Y * (zDist / m_projection.M22 / halfH);
            return new Vector3(x, y, zDist);
        }

        public void get_screen_ray(Point centerRelativeXY, out Vector3 origin, out Vector3 direction)
        {
            Vector3 localDir = screen_to_3d(new Vector2(centerRelativeXY.X, -centerRelativeXY.Y), 1.0f);
            Vector3 worldDir = Vector3.TransformNormal(localDir, m_inverted_view);
            direction = Vector3.Normalize(worldDir);
            origin = m_inverted_view.Translation;
        }

        public void GetMouseRayFromScreenPoint(Point screenPoint, out Vector3 origin, out Vector3 direction)
        {
            Point rel = GetMousePositionFromScreenPoint(screenPoint);
            get_screen_ray(rel, out origin, out direction);
        }

        // Maps a normalized viewport coordinate (nx, ny in [-1, 1]) to a screen point via PointToScreen
        // and then computes the 3D ray via GetMouseRayFromScreenPoint. Under PerMonitorV2 DPI scaling or monitor changes,
        // the resulting 3D ray must remain invariant.
        public void GetMouseRayFromNormalizedViewportPoint(float nx, float ny, out Vector3 origin, out Vector3 direction)
        {
            RecalculateProjection();
            Size vs = get_view_size();
            float halfW = vs.Width * 0.5f;
            float halfH = vs.Height * 0.5f;
            Point clientPoint = new Point(
                (int)MathF.Round(halfW + nx * halfW),
                (int)MathF.Round(halfH + ny * halfH));
            Point screenPoint = m_view_panel.PointToScreen(clientPoint);
            Point rel = GetMousePositionFromScreenPoint(screenPoint);
            // Use exact floating subpixel offset when comparing across discrete integer resolutions
            Vector2 exactRel = new Vector2(rel.X, -rel.Y);
            Vector3 localDir = screen_to_3d(exactRel, 1.0f);
            direction = Vector3.Normalize(Vector3.TransformNormal(localDir, m_inverted_view));
            origin = m_inverted_view.Translation;
        }

        public void set_view_matrix(Matrix4x4 viewMatrix)
        {
            if (!Matrix4x4.Invert(viewMatrix, out var inv))throw new ArgumentException("Camera view must be invertible.",nameof(viewMatrix));
            if (viewMatrix.M14 == 0f && viewMatrix.M24 == 0f && viewMatrix.M34 == 0f && viewMatrix.M44 == 1f
                && Math.Abs(inv.M14) < 1e-5f && Math.Abs(inv.M24) < 1e-5f && Math.Abs(inv.M34) < 1e-5f && Math.Abs(inv.M44 - 1f) < 1e-5f)
            { inv.M14 = 0f; inv.M24 = 0f; inv.M34 = 0f; inv.M44 = 1f; }
            SetCameraWorld(inv);
        }
        public void SetCameraWorld(Matrix4x4 value)
        {
            var entries=System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4,float>(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref value,1));
            foreach(float entry in entries)if(!float.IsFinite(entry))throw new ArgumentException("Camera must be finite.",nameof(value));
            if(value.M14!=0 || value.M24!=0 || value.M34!=0 || value.M44!=1 || !Matrix4x4.Invert(value,out _)
                || Math.Abs(value.M41)>1e6f || Math.Abs(value.M42)>1e6f || Math.Abs(value.M43)>1e6f)throw new ArgumentException("Invalid camera transform.",nameof(value));
            m_inverted_view=value;
            CameraMoved?.Invoke();
        }

        public Matrix4x4 get_inverted_view_matrix() => m_inverted_view;
        public Matrix4x4 get_projection_matrix() { RecalculateProjection(); return m_projection; }

        public void load_settings(IDictionary<string, string> settings)
        {
            if (settings.TryGetValue("draw_grid", out var dg) && bool.TryParse(dg, out bool bGrid))
                m_render_grid = bGrid;
            if (settings.TryGetValue("near_plane", out var np) && float.TryParse(np, NumberStyles.Float, CultureInfo.InvariantCulture, out float fNear))
                m_near_plane_val = Math.Max(0.1f, fNear);
            if (settings.TryGetValue("far_plane", out var fp) && float.TryParse(fp, NumberStyles.Float, CultureInfo.InvariantCulture, out float fFar))
                m_far_plane_val = Math.Max(2f * m_near_plane, fFar);
            if (settings.TryGetValue("selection_rate", out var sr) && float.TryParse(sr, NumberStyles.Float, CultureInfo.InvariantCulture, out float fSel))
                m_selection_rate_val = fSel;
            if (settings.TryGetValue("grid_step", out var gs) && float.TryParse(gs, NumberStyles.Float, CultureInfo.InvariantCulture, out float fStep))
                m_grid_step_val = fStep;
            if (settings.TryGetValue("grid_dim", out var gd) && uint.TryParse(gd, out uint uDim))
                m_grid_dim_val = uDim;
            if (settings.TryGetValue("mouse_sensitivity", out var ms) && float.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out float fSens))
                mouse_sensitivity = fSens;
            if (settings.TryGetValue("render_mode", out var rm) && !string.IsNullOrEmpty(rm))
                SetRenderViewMode(rm);
        }

        public void save_settings(IDictionary<string, string> settings)
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
    }

    // C# port of base/sources/camera_view_panel.{h,cpp}
    public class CameraViewPanel : DockContent
    {
        public const string PersistIdentity = "xray.editor_base.camera_view_panel";
        public readonly SdkViewportHost ViewportPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 24, 28), Name = "camera_viewport" };

        public CameraViewPanel()
        {
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Document;
            HideOnClose = true;
            Name = "camera_view_panel";
            Text = "camera view";
            TabText = "camera view";
            ClientSize = new Size(320, 240);
            Controls.Add(ViewportPanel);
        }

        protected override string GetPersistString() => PersistIdentity;
    }

    // C# port of world/sources/animation_curve_editor_panel.{h,cpp}
    public class AnimationCurveEditorPanel : DockContent
    {
        public const string PersistIdentity = "xray.editor.animation_curve_editor_panel";

        public readonly ElementHost m_host = new ElementHost { Dock = DockStyle.Fill, Name = "m_host" };
        public readonly Controls.curve_editor.curve_editor_panel CurveEditorControl = new Controls.curve_editor.curve_editor_panel();

        public AnimationCurveEditorPanel()
        {
            SuspendLayout();
            m_host.Child = CurveEditorControl;
            Controls.Add(m_host);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Document;
            HideOnClose = true;
            Name = "animation_curve_editor_panel";
            Text = "Animation curves";
            TabText = "Animation curves";
            ClientSize = new Size(480, 260);
            ResumeLayout(false);
        }

        protected override string GetPersistString() => PersistIdentity;
    }

    // C# port of world/sources/scene_graph_tab.{h,cpp}
    public class SceneGraphTab : DockContent
    {
        public const string PersistIdentity = "xray.editor.scene_graph_tab";

        public readonly hypergraph_control hypergraph_control1 = new hypergraph_control();
        public readonly Button button1 = new Button();
        private readonly Project _project;

        public SceneGraphTab(Project project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            SuspendLayout();
            hypergraph_control1.Dock = DockStyle.Fill;
            hypergraph_control1.Name = "hypergraph_control1";

            button1.Location = new Point(5, 4);
            button1.Name = "button1";
            button1.Size = new Size(78, 25);
            button1.Text = "Initialize";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;

            Controls.Add(button1);
            Controls.Add(hypergraph_control1);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Document;
            HideOnClose = true;
            Name = "scene_graph_tab";
            Text = "Scene graph";
            TabText = "Scene graph";
            ClientSize = new Size(375, 323);
            ResumeLayout(false);
        }

        protected override string GetPersistString() => PersistIdentity;

        public void InitializeGraphFromProject()
        {
            hypergraph_control1.clear();
            var items = new List<ProjectItemBase>();
            _project.Root.get_all_items(items, true);
            int y = 20;
            foreach (var item in items.Where(i => i.is_object()))
            {
                var n = new node(item.Name) { Position = new Point(40, y) };
                n.ConnectionPoints.Add(new connection_point(n, "Position", typeof(Vector3), false));
                hypergraph_control1.append_node(n);
                y += 70;
            }
        }

        private void button1_Click(object? sender, EventArgs e) => InitializeGraphFromProject();
    }

    // C# port of world/sources/triangulation_tester.{h,cpp}
    public class TriangulationTester : DockContent
    {
        public const string PersistIdentity = "xray.editor.triangulation_tester";

        public readonly Panel canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Name = "canvas" };
        public readonly Panel top_panel = new Panel { Dock = DockStyle.Top, Height = 25, Name = "top_panel" };
        public readonly Button open_button = new Button { Name = "button_1", Text = "Load Vertices", Size = new Size(80, 24), Location = new Point(0, 0) };
        public readonly Button save_button = new Button { Name = "button_2", Text = "Save Vertices", Size = new Size(80, 24), Location = new Point(90, 0) };
        public readonly ComboBox triangulation_type = new ComboBox { Name = "combo_1", Size = new Size(80, 24), Location = new Point(180, 0), DropDownStyle = ComboBoxStyle.DropDownList };
        public readonly CheckBox show_bounding_triangle = new CheckBox { Name = "check_1", Text = "Show bounding triangle", Size = new Size(150, 24), Location = new Point(270, 0) };
        public readonly Button clear_vertices_button = new Button { Name = "button_3", Text = "Clear Vertices", Size = new Size(80, 24), Location = new Point(430, 0) };
        public readonly CheckBox show_errors = new CheckBox { Name = "check_2", Text = "Visualize Errors", Size = new Size(150, 24), Location = new Point(510, 0) };
        public readonly List<PointF> Vertices = new List<PointF>();

        public TriangulationTester()
        {
            SuspendLayout();
            triangulation_type.Items.AddRange(new object[] { "Regular", "Constrained" });
            triangulation_type.SelectedIndex = 0;
            triangulation_type.SelectedIndexChanged += (_, _) => canvas.Invalidate();
            show_bounding_triangle.CheckedChanged += (_, _) => canvas.Invalidate();
            show_errors.CheckedChanged += (_, _) => canvas.Invalidate();
            clear_vertices_button.Click += (_, _) => { Vertices.Clear(); canvas.Invalidate(); };
            open_button.Click += (_, _) => LoadVerticesFromDialog();
            save_button.Click += (_, _) => SaveVerticesToDialog();
            canvas.MouseClick += Canvas_MouseClick;
            canvas.Paint += Canvas_Paint;
            top_panel.Controls.AddRange(new Control[]
            {
                open_button, save_button, triangulation_type,
                show_bounding_triangle, clear_vertices_button, show_errors
            });
            Controls.Add(canvas);
            Controls.Add(top_panel);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom | DockAreas.Document;
            HideOnClose = true;
            Name = "triangulation_tester";
            Text = "triangulation_tester";
            TabText = "triangulation_tester";
            ClientSize = new Size(555, 369);
            ResumeLayout(false);
        }

        private void Canvas_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Vertices.Add(new PointF(e.X, e.Y));
                canvas.Invalidate();
            }
            else if (e.Button == MouseButtons.Right && Vertices.Count > 0)
            {
                int nearest = -1;
                float bestDistSq = 100f;
                for (int i = 0; i < Vertices.Count; i++)
                {
                    float dx = Vertices[i].X - e.X;
                    float dy = Vertices[i].Y - e.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= bestDistSq) { bestDistSq = d2; nearest = i; }
                }
                if (nearest >= 0) { Vertices.RemoveAt(nearest); canvas.Invalidate(); }
            }
        }

        private void LoadVerticesFromDialog()
        {
            using var dlg = new OpenFileDialog { Filter = "Vertex lists (*.txt;*.pts)|*.txt;*.pts|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var loaded = new List<PointF>();
            foreach (string line in System.IO.File.ReadAllLines(dlg.FileName))
            {
                string[] parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    loaded.Add(new PointF(x, y));
                }
            }
            Vertices.Clear();
            Vertices.AddRange(loaded);
            canvas.Invalidate();
        }

        private void SaveVerticesToDialog()
        {
            using var dlg = new SaveFileDialog { Filter = "Vertex lists (*.txt)|*.txt", DefaultExt = "txt", FileName = "vertices.txt" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            System.IO.File.WriteAllLines(dlg.FileName, Vertices.Select(v => string.Create(CultureInfo.InvariantCulture, $"{v.X} {v.Y}")));
        }

        private static bool CircumcircleContains(PointF a, PointF b, PointF c, PointF p)
        {
            double ax = a.X - p.X, ay = a.Y - p.Y;
            double bx = b.X - p.X, by = b.Y - p.Y;
            double cx = c.X - p.X, cy = c.Y - p.Y;
            double orient = (b.X - a.X) * (double)(c.Y - a.Y) - (b.Y - a.Y) * (double)(c.X - a.X);
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                       - (bx * bx + by * by) * (ax * cy - cx * ay)
                       + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return orient > 0 ? det > 1e-6 : det < -1e-6;
        }

        private static List<(int A, int B, int C)> ComputeDelaunay(IReadOnlyList<PointF> pts, out PointF[] superTri)
        {
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float dMax = Math.Max(maxX - minX, maxY - minY);
            if (dMax < 1f) dMax = 100f;
            float midX = (minX + maxX) * 0.5f, midY = (minY + maxY) * 0.5f;
            superTri = new[]
            {
                new PointF(midX - 20f * dMax, midY - dMax),
                new PointF(midX, midY + 20f * dMax),
                new PointF(midX + 20f * dMax, midY - dMax)
            };
            int n = pts.Count;
            var all = new List<PointF>(n + 3);
            all.AddRange(pts);
            all.AddRange(superTri);
            var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };
            for (int i = 0; i < n; i++)
            {
                PointF p = all[i];
                var bad = new List<(int A, int B, int C)>();
                foreach (var tri in triangles)
                {
                    if (CircumcircleContains(all[tri.A], all[tri.B], all[tri.C], p))
                        bad.Add(tri);
                }
                var polygon = new List<(int U, int V)>();
                foreach (var tri in bad)
                {
                    (int u, int v)[] triEdges = { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) };
                    foreach (var edge in triEdges)
                    {
                        bool shared = false;
                        foreach (var other in bad)
                        {
                            if (other == tri) continue;
                            if ((other.A == edge.u && other.B == edge.v) || (other.B == edge.u && other.A == edge.v) ||
                                (other.B == edge.u && other.C == edge.v) || (other.C == edge.u && other.B == edge.v) ||
                                (other.C == edge.u && other.A == edge.v) || (other.A == edge.u && other.C == edge.v))
                            { shared = true; break; }
                        }
                        if (!shared) polygon.Add(edge);
                    }
                }
                foreach (var tri in bad) triangles.Remove(tri);
                foreach (var edge in polygon) triangles.Add((edge.U, edge.V, i));
            }
            triangles.RemoveAll(t => t.A >= n || t.B >= n || t.C >= n);
            return triangles;
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Vertices.Count < 3)
            {
                using var hintBrush = new SolidBrush(Color.Gray);
                g.DrawString("Left-click to add vertices (Right-click removes nearest).", Font, hintBrush, 8f, 8f);
            }
            if (Vertices.Count >= 3)
            {
                var tris = ComputeDelaunay(Vertices, out var superTri);
                using var edgePen = new Pen(Color.SteelBlue, 1.2f);
                foreach (var t in tris)
                {
                    g.DrawLine(edgePen, Vertices[t.A], Vertices[t.B]);
                    g.DrawLine(edgePen, Vertices[t.B], Vertices[t.C]);
                    g.DrawLine(edgePen, Vertices[t.C], Vertices[t.A]);
                }
                if (triangulation_type.SelectedIndex == 1 && Vertices.Count >= 2)
                {
                    using var chainPen = new Pen(Color.DarkGreen, 1.8f);
                    for (int i = 0; i < Vertices.Count - 1; i++)
                        g.DrawLine(chainPen, Vertices[i], Vertices[i + 1]);
                }
                if (show_bounding_triangle.Checked)
                {
                    using var boundPen = new Pen(Color.Orange, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    g.DrawPolygon(boundPen, superTri);
                }
            }
            using var vBrush = new SolidBrush(Color.Black);
            using var errBrush = new SolidBrush(Color.Red);
            for (int i = 0; i < Vertices.Count; i++)
            {
                var pt = Vertices[i];
                bool dup = show_errors.Checked && Enumerable.Range(0, Vertices.Count).Any(j => j != i && MathF.Abs(Vertices[j].X - pt.X) < 1f && MathF.Abs(Vertices[j].Y - pt.Y) < 1f);
                g.FillEllipse(dup ? errBrush : vBrush, pt.X - 3f, pt.Y - 3f, 6f, 6f);
            }
        }

        protected override string GetPersistString() => PersistIdentity;
    }

    // C# port of world/sources/property_connection_editor.{h,cpp}
    public class PropertyConnectionEditor : Form, connection_editor
    {
        public const string PersistIdentity = "xray.editor.property_connection_editor";

        public readonly MenuStrip top_menu = new MenuStrip();
        public readonly ToolStripMenuItem enuToolStripMenuItem = new ToolStripMenuItem("Menu");
        public readonly ToolStripMenuItem viewTypeToolStripMenuItem = new ToolStripMenuItem("View Mode");
        public readonly ToolStripMenuItem detailsViewMenuItem = new ToolStripMenuItem("Details") { Checked = true, CheckOnClick = true };
        public readonly ToolStripMenuItem listViewMenuItem = new ToolStripMenuItem("List") { CheckOnClick = true };
        public readonly ToolStripMenuItem leftToolStripMenuItem = new ToolStripMenuItem("Left");
        public readonly ToolStripMenuItem rightToolStripMenuItem = new ToolStripMenuItem("Right");

        public readonly Panel top_panel = new Panel { Dock = DockStyle.Top, Height = 24 };
        public readonly Button btn_grab_left = new Button { Text = "grab left", Location = new Point(136, 0), Size = new Size(75, 23) };
        public readonly Button btn_swap = new Button { Text = "<-->", Location = new Point(217, 0), Size = new Size(75, 23) };
        public readonly Button btn_grab_right = new Button { Text = "grab right", Location = new Point(298, 0), Size = new Size(75, 23) };
        public readonly Label left_label = new Label { Text = "None", AutoSize = true, Dock = DockStyle.Left };
        public readonly Label right_label = new Label { Text = "None", AutoSize = true, Dock = DockStyle.Right };

        public readonly Panel bottom_panel = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        public readonly Button disconnect_button = new Button { Text = "Disconnect", Location = new Point(219, 3), Size = new Size(102, 28) };
        public readonly Button make_connection_button = new Button { Text = "Connect", Location = new Point(327, 3), Size = new Size(102, 28) };
        public readonly Button close_button = new Button { Text = "Close", Location = new Point(435, 3), Size = new Size(104, 28) };

        public readonly ListView left_props_view = new ListView { Dock = DockStyle.Left, Width = 250, View = View.Details, FullRowSelect = true, GridLines = true };
        public readonly ListView right_props_view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };

        private Project? _project;
        private CommandEngine? _commands;
        private ProjectItemObject? _leftObject;
        private ProjectItemObject? _rightObject;

        public PropertyConnectionEditor()
        {
            SuspendLayout();
            viewTypeToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { detailsViewMenuItem, listViewMenuItem });
            enuToolStripMenuItem.DropDownItems.Add(viewTypeToolStripMenuItem);
            leftToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Grab selected", null, (_, _) => GrabSelectionEndpoint(isLeft: true)));
            leftToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Clear", null, (_, _) => SetEndpoints(null, _rightObject)));
            rightToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Grab selected", null, (_, _) => GrabSelectionEndpoint(isLeft: false)));
            rightToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem("Clear", null, (_, _) => SetEndpoints(_leftObject, null)));
            top_menu.Items.AddRange(new ToolStripItem[] { enuToolStripMenuItem, leftToolStripMenuItem, rightToolStripMenuItem });

            detailsViewMenuItem.Click += (_, _) =>
            {
                listViewMenuItem.Checked = false;
                detailsViewMenuItem.Checked = true;
                left_props_view.View = View.Details;
                right_props_view.View = View.Details;
            };
            listViewMenuItem.Click += (_, _) =>
            {
                detailsViewMenuItem.Checked = false;
                listViewMenuItem.Checked = true;
                left_props_view.View = View.List;
                right_props_view.View = View.List;
            };

            left_props_view.Columns.Add("Name", 100);
            left_props_view.Columns.Add("Data Type", 138);
            right_props_view.Columns.Add("Name", 100);
            right_props_view.Columns.Add("Data Type", 138);

            btn_grab_left.Click += (_, _) => GrabSelectionEndpoint(isLeft: true);
            btn_grab_right.Click += (_, _) => GrabSelectionEndpoint(isLeft: false);
            btn_swap.Click += (_, _) => SwapEndpoints();
            make_connection_button.Click += (_, _) =>
            {
                if (left_props_view.SelectedItems.Count > 0 && right_props_view.SelectedItems.Count > 0)
                    ConnectSelected(left_props_view.SelectedItems[0].Text, right_props_view.SelectedItems[0].Text);
            };
            disconnect_button.Click += (_, _) =>
            {
                if (left_props_view.SelectedItems.Count > 0 && right_props_view.SelectedItems.Count > 0)
                    DisconnectSelected(left_props_view.SelectedItems[0].Text, right_props_view.SelectedItems[0].Text);
            };

            top_panel.Controls.AddRange(new Control[] { btn_grab_right, btn_grab_left, btn_swap, right_label, left_label });
            bottom_panel.Controls.AddRange(new Control[] { close_button, make_connection_button, disconnect_button });
            close_button.Click += (_, _) => Hide();

            Controls.Add(right_props_view);
            Controls.Add(left_props_view);
            Controls.Add(bottom_panel);
            Controls.Add(top_panel);
            Controls.Add(top_menu);
            MainMenuStrip = top_menu;
            ClientSize = new Size(542, 519);
            Name = "property_connection_editor";
            Text = "property_connection_editor";
            FormClosing += (_, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            ResumeLayout(false);
            PerformLayout();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ProjectItemObject? LeftObject => _leftObject;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ProjectItemObject? RightObject => _rightObject;

        public void BindProject(Project project, CommandEngine commands)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public void GrabSelectionEndpoint(bool isLeft)
        {
            if (_project == null) return;
            var obj = _project.SelectedItems.OfType<ProjectItemObject>().FirstOrDefault();
            if (obj == null) return;
            if (isLeft) SetEndpoints(obj, _rightObject);
            else SetEndpoints(_leftObject, obj);
        }

        public void SwapEndpoints() => SetEndpoints(_rightObject, _leftObject);

        public void SetEndpoints(ProjectItemObject? source, ProjectItemObject? destination)
        {
            _leftObject = source;
            _rightObject = destination;
            left_label.Text = source?.Name ?? "None";
            right_label.Text = destination?.Name ?? "None";
            left_props_view.Items.Clear();
            right_props_view.Items.Clear();
            if (source != null)
            {
                foreach (var (prop, dtype) in SdkScriptingOperations.GetConnectableProperties(source))
                    left_props_view.Items.Add(new ListViewItem(new[] { prop, dtype }));
            }
            if (destination != null)
            {
                foreach (var (prop, dtype) in SdkScriptingOperations.GetConnectableProperties(destination))
                    right_props_view.Items.Add(new ListViewItem(new[] { prop, dtype }));
            }
        }

        public SdkPropertyConnection ConnectSelected(string sourceProperty, string destinationProperty)
        {
            if (_project == null || _commands == null || _leftObject == null || _rightObject == null)
                throw new InvalidOperationException("Select both left and right project objects before connecting properties.");
            return SdkScriptingOperations.ConnectProperties(_project, _commands, _leftObject, sourceProperty, _rightObject, destinationProperty);
        }

        public bool DisconnectSelected(string sourceProperty, string destinationProperty)
        {
            if (_project == null || _commands == null || _leftObject == null || _rightObject == null)
                return false;
            return SdkScriptingOperations.DisconnectProperties(_project, _commands, _leftObject, sourceProperty, _rightObject, destinationProperty);
        }

        public void execute(node source, node destination)
        {
            left_label.Text = source.Id;
            right_label.Text = destination.Id;
            left_props_view.Items.Clear();
            right_props_view.Items.Clear();
            foreach (var cp in source.ConnectionPoints)
                left_props_view.Items.Add(new ListViewItem(new[] { cp.Name, cp.DataType.Name }));
            foreach (var cp in destination.ConnectionPoints)
                right_props_view.Items.Add(new ListViewItem(new[] { cp.Name, cp.DataType.Name }));
        }
    }

    // C# port of world/sources/ide_log_form.{h,cpp}
    public class IdeLogForm : Form
    {
        public readonly ListBox log_list_box = new ListBox();

        public IdeLogForm()
        {
            SuspendLayout();
            log_list_box.Dock = DockStyle.Fill;
            log_list_box.FormattingEnabled = true;
            log_list_box.HorizontalScrollbar = true;
            log_list_box.Location = new Point(0, 0);
            log_list_box.Name = "log_list_box";
            log_list_box.ScrollAlwaysVisible = true;
            log_list_box.SelectionMode = SelectionMode.MultiExtended;
            log_list_box.Size = new Size(494, 264);

            ClientSize = new Size(494, 268);
            Controls.Add(log_list_box);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            Name = "ide_log_form";
            ShowInTaskbar = false;
            Text = "Log view";
            FormClosing += IdeLogForm_FormClosing;
            ResumeLayout(false);
        }

        public void AddMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            log_list_box.Items.Add(message);
        }

        public void AddWarning(string warning) => AddMessage("[WARN] " + warning);
        public void AddError(string error) => AddMessage("[ERROR] " + error);

        public void ClearMessages() => log_list_box.Items.Clear();

        private void IdeLogForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }

    // C# port of world/sources/about_dialog.{h,cpp}
    public class AboutDialog : Form
    {
        public readonly Label label1 = new Label();
        public readonly Label label2 = new Label();
        public readonly Label label3 = new Label();
        public readonly Button button1 = new Button();

        public AboutDialog(uint buildId = 2011, string buildDate = "2011-2026")
        {
            SuspendLayout();
            label1.AutoSize = true;
            label1.Font = new Font("Microsoft Sans Serif", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 204);
            label1.Location = new Point(44, 24);
            label1.Name = "label1";
            label1.Text = "X-Ray Engine 2.0 SDK";

            label2.AutoSize = true;
            label2.Location = new Point(25, 67);
            label2.Name = "label2";
            label2.Text = $" build #{buildId},   built on {buildDate}";

            label3.AutoSize = true;
            label3.Location = new Point(25, 114);
            label3.Name = "label3";
            label3.Text = "Copyright (C) GSC Game World 2010";

            button1.DialogResult = DialogResult.OK;
            button1.Location = new Point(101, 146);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.Text = "OK";
            button1.Click += (_, _) => Close();

            AcceptButton = button1;
            CancelButton = button1;
            ClientSize = new Size(284, 181);
            Controls.Add(button1);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "about_dialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "About X-Ray Engine 2.0 SDK";
            ResumeLayout(false);
            PerformLayout();
        }
        public string BuildSummary => $"{label1.Text} · {label2.Text.Trim()}";
    }
    public interface options_page
    {
        bool accept_changes();
        void cancel_changes();
        void activate_page();
        void deactivate_page();
        bool changed();
        Control get_control();
    }

    public interface options_manager
    {
        void register_page(string fullName, options_page page);
        void unregister_page(string fullName);
    }

    public class OptionsPageGeneral : UserControl, options_page
    {
        private readonly SceneViewPanel _viewPanel;
        private readonly Dictionary<string, object> _backup = new Dictionary<string, object>();

        public readonly NumericUpDown near_plane_box = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.01M, Maximum = 100M, Increment = 0.1M, Location = new Point(127, 13), Size = new Size(81, 20) };
        public readonly NumericUpDown far_plane_box = new NumericUpDown { DecimalPlaces = 1, Minimum = 1M, Maximum = 10000M, Increment = 10M, Location = new Point(127, 39), Size = new Size(81, 20) };
        public readonly NumericUpDown focus_distance_box = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.1M, Maximum = 1000M, Increment = 1M, Location = new Point(127, 65), Size = new Size(81, 20) };
        public readonly TrackBar mouse_sens_track = new TrackBar { Minimum = 1, Maximum = 100, Location = new Point(127, 91), Size = new Size(96, 45) };
        public readonly TrackBar transparency_track = new TrackBar { Minimum = 1, Maximum = 100, Location = new Point(127, 128), Size = new Size(96, 45) };
        public readonly TrackBar gizmo_size_track = new TrackBar { Minimum = 1, Maximum = 100, Location = new Point(127, 165), Size = new Size(96, 45) };
        public readonly CheckBox m_enable_postprocess_check_box = new CheckBox { Text = "Enable Postprocess", Location = new Point(16, 232), AutoSize = true };
        public readonly CheckBox draw_grid_check_box = new CheckBox { Text = "Draw Grid", Location = new Point(16, 255), AutoSize = true };
        public readonly NumericUpDown grid_step_UpDown = new NumericUpDown { DecimalPlaces = 1, Minimum = 0.1M, Maximum = 20M, Increment = 0.5M, Location = new Point(127, 279), Size = new Size(81, 20) };
        public readonly NumericUpDown grid_size_numericUpDown = new NumericUpDown { Minimum = 5, Maximum = 500, Increment = 5, Location = new Point(127, 305), Size = new Size(81, 20) };

        public OptionsPageGeneral(SceneViewPanel viewPanel)
        {
            _viewPanel = viewPanel ?? throw new ArgumentNullException(nameof(viewPanel));
            Name = "options_page_general";
            Size = new Size(346, 356);
            Controls.AddRange(new Control[]
            {
                new Label { Text = "Near plane:", Location = new Point(16, 15), AutoSize = true },
                new Label { Text = "Far plane:", Location = new Point(16, 41), AutoSize = true },
                new Label { Text = "Focus distance:", Location = new Point(16, 67), AutoSize = true },
                new Label { Text = "Mouse sensitivity:", Location = new Point(16, 95), AutoSize = true },
                new Label { Text = "Selection rate:", Location = new Point(16, 132), AutoSize = true },
                new Label { Text = "Gizmo size:", Location = new Point(16, 169), AutoSize = true },
                new Label { Text = "Grid step:", Location = new Point(16, 281), AutoSize = true },
                new Label { Text = "Grid size:", Location = new Point(16, 307), AutoSize = true },
                near_plane_box, far_plane_box, focus_distance_box,
                mouse_sens_track, transparency_track, gizmo_size_track,
                m_enable_postprocess_check_box, draw_grid_check_box,
                grid_step_UpDown, grid_size_numericUpDown
            });

            near_plane_box.ValueChanged += (_, _) => _viewPanel.m_near_plane_val = (float)near_plane_box.Value;
            far_plane_box.ValueChanged += (_, _) => _viewPanel.m_far_plane_val = (float)far_plane_box.Value;
            focus_distance_box.ValueChanged += (_, _) => _viewPanel.view_point_distance = (float)focus_distance_box.Value;
            mouse_sens_track.ValueChanged += (_, _) => _viewPanel.mouse_sensitivity = mouse_sens_track.Value / 100.0f;
            transparency_track.ValueChanged += (_, _) => _viewPanel.m_selection_rate_val = transparency_track.Value / 100.0f;
            gizmo_size_track.ValueChanged += (_, _) => _viewPanel.gizmo_size = gizmo_size_track.Value / 100.0f;
            m_enable_postprocess_check_box.CheckedChanged += (_, _) => _viewPanel.UsePostProcess = m_enable_postprocess_check_box.Checked;
            draw_grid_check_box.CheckedChanged += (_, _) => _viewPanel.RenderGrid = draw_grid_check_box.Checked;
            grid_step_UpDown.ValueChanged += (_, _) => _viewPanel.m_grid_step_val = (float)grid_step_UpDown.Value;
            grid_size_numericUpDown.ValueChanged += (_, _) => _viewPanel.m_grid_dim_val = (uint)grid_size_numericUpDown.Value;
        }

        public void activate_page()
        {
            if (_backup.Count == 0)
            {
                _backup["near_plane"] = _viewPanel.m_near_plane_val;
                _backup["far_plane"] = _viewPanel.m_far_plane_val;
                _backup["focus_distance"] = _viewPanel.view_point_distance;
                _backup["mouse_sens"] = _viewPanel.mouse_sensitivity;
                _backup["selection_rate"] = _viewPanel.m_selection_rate_val;
                _backup["gizmo_size"] = _viewPanel.gizmo_size;
                _backup["postprocess"] = _viewPanel.UsePostProcess;
                _backup["draw_grid"] = _viewPanel.RenderGrid;
                _backup["grid_step"] = _viewPanel.m_grid_step_val;
                _backup["grid_dim"] = _viewPanel.m_grid_dim_val;
            }

            near_plane_box.Value = Math.Clamp((decimal)_viewPanel.m_near_plane_val, near_plane_box.Minimum, near_plane_box.Maximum);
            far_plane_box.Value = Math.Clamp((decimal)_viewPanel.m_far_plane_val, far_plane_box.Minimum, far_plane_box.Maximum);
            focus_distance_box.Value = Math.Clamp((decimal)_viewPanel.view_point_distance, focus_distance_box.Minimum, focus_distance_box.Maximum);
            mouse_sens_track.Value = Math.Clamp((int)(_viewPanel.mouse_sensitivity * 100.0f), mouse_sens_track.Minimum, mouse_sens_track.Maximum);
            transparency_track.Value = Math.Clamp((int)(_viewPanel.m_selection_rate_val * 100.0f), transparency_track.Minimum, transparency_track.Maximum);
            gizmo_size_track.Value = Math.Clamp((int)(_viewPanel.gizmo_size * 100.0f), gizmo_size_track.Minimum, gizmo_size_track.Maximum);
            m_enable_postprocess_check_box.Checked = _viewPanel.UsePostProcess;
            draw_grid_check_box.Checked = _viewPanel.RenderGrid;
            grid_step_UpDown.Value = Math.Clamp((decimal)_viewPanel.m_grid_step_val, grid_step_UpDown.Minimum, grid_step_UpDown.Maximum);
            grid_size_numericUpDown.Value = Math.Clamp((decimal)_viewPanel.m_grid_dim_val, grid_size_numericUpDown.Minimum, grid_size_numericUpDown.Maximum);
            Visible = true;
        }

        public void deactivate_page() => Visible = false;

        public bool changed()
        {
            if (_backup.Count == 0) return false;
            return Math.Abs((float)_backup["near_plane"] - _viewPanel.m_near_plane_val) > 1e-4f ||
                   Math.Abs((float)_backup["far_plane"] - _viewPanel.m_far_plane_val) > 1e-4f ||
                   Math.Abs((float)_backup["focus_distance"] - _viewPanel.view_point_distance) > 1e-4f ||
                   Math.Abs((float)_backup["mouse_sens"] - _viewPanel.mouse_sensitivity) > 1e-4f ||
                   Math.Abs((float)_backup["selection_rate"] - _viewPanel.m_selection_rate_val) > 1e-4f ||
                   Math.Abs((float)_backup["gizmo_size"] - _viewPanel.gizmo_size) > 1e-4f ||
                   (bool)_backup["postprocess"] != _viewPanel.UsePostProcess ||
                   (bool)_backup["draw_grid"] != _viewPanel.RenderGrid ||
                   Math.Abs((float)_backup["grid_step"] - _viewPanel.m_grid_step_val) > 1e-4f ||
                   (uint)_backup["grid_dim"] != _viewPanel.m_grid_dim_val;
        }

        public bool accept_changes()
        {
            _backup.Clear();
            return true;
        }

        public void cancel_changes()
        {
            if (_backup.Count == 0) return;
            _viewPanel.m_near_plane_val = (float)_backup["near_plane"];
            _viewPanel.m_far_plane_val = (float)_backup["far_plane"];
            _viewPanel.view_point_distance = (float)_backup["focus_distance"];
            _viewPanel.mouse_sensitivity = (float)_backup["mouse_sens"];
            _viewPanel.m_selection_rate_val = (float)_backup["selection_rate"];
            _viewPanel.gizmo_size = (float)_backup["gizmo_size"];
            _viewPanel.UsePostProcess = (bool)_backup["postprocess"];
            _viewPanel.RenderGrid = (bool)_backup["draw_grid"];
            _viewPanel.m_grid_step_val = (float)_backup["grid_step"];
            _viewPanel.m_grid_dim_val = (uint)_backup["grid_dim"];
            _backup.Clear();
        }

        public Control get_control() => this;
    }

    public class OptionsPageViewPanel : UserControl, options_page
    {
        private readonly SceneViewPanel _viewPanel;
        private readonly Dictionary<string, float> _backup = new Dictionary<string, float>();

        public readonly NumericUpDown near_plane_box = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.01M, Maximum = 100M, Increment = 0.1M, Location = new Point(127, 13), Size = new Size(81, 20) };
        public readonly NumericUpDown far_plane_box = new NumericUpDown { DecimalPlaces = 1, Minimum = 1M, Maximum = 10000M, Increment = 10M, Location = new Point(127, 39), Size = new Size(81, 20) };

        public OptionsPageViewPanel(SceneViewPanel viewPanel)
        {
            _viewPanel = viewPanel ?? throw new ArgumentNullException(nameof(viewPanel));
            Name = "options_page_view_panel";
            Size = new Size(346, 356);
            Controls.AddRange(new Control[]
            {
                new Label { Text = "Near plane:", Location = new Point(16, 15), AutoSize = true },
                new Label { Text = "Far plane:", Location = new Point(16, 41), AutoSize = true },
                near_plane_box, far_plane_box
            });
            near_plane_box.ValueChanged += (_, _) => _viewPanel.m_near_plane_val = (float)near_plane_box.Value;
            far_plane_box.ValueChanged += (_, _) => _viewPanel.m_far_plane_val = (float)far_plane_box.Value;
        }

        public void activate_page()
        {
            if (_backup.Count == 0)
            {
                _backup["near_plane"] = _viewPanel.m_near_plane_val;
                _backup["far_plane"] = _viewPanel.m_far_plane_val;
            }
            near_plane_box.Value = Math.Clamp((decimal)_viewPanel.m_near_plane_val, near_plane_box.Minimum, near_plane_box.Maximum);
            far_plane_box.Value = Math.Clamp((decimal)_viewPanel.m_far_plane_val, far_plane_box.Minimum, far_plane_box.Maximum);
            Visible = true;
        }

        public void deactivate_page() => Visible = false;

        public bool changed()
            => _backup.Count > 0 &&
               (Math.Abs(_backup["near_plane"] - _viewPanel.m_near_plane_val) > 1e-4f ||
                Math.Abs(_backup["far_plane"] - _viewPanel.m_far_plane_val) > 1e-4f);

        public bool accept_changes() { _backup.Clear(); return true; }

        public void cancel_changes()
        {
            if (_backup.Count == 0) return;
            _viewPanel.m_near_plane_val = _backup["near_plane"];
            _viewPanel.m_far_plane_val = _backup["far_plane"];
            _backup.Clear();
        }

        public Control get_control() => this;
    }

    public class OptionsPageProject : UserControl, options_page
    {
        private readonly Project _project;
        private bool? _backupTrack;
        public readonly CheckBox track_active_item_check = new CheckBox { Text = "Track active item in project tree", Location = new Point(12, 16), AutoSize = true };

        public OptionsPageProject(Project project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            Name = "options_page_project";
            Size = new Size(346, 356);
            Controls.Add(track_active_item_check);
            track_active_item_check.CheckedChanged += (_, _) => _project.TrackActiveItem = track_active_item_check.Checked;
        }

        public void activate_page()
        {
            _backupTrack ??= _project.TrackActiveItem;
            track_active_item_check.Checked = _project.TrackActiveItem;
            Visible = true;
        }

        public void deactivate_page() => Visible = false;
        public bool changed() => _backupTrack.HasValue && _backupTrack.Value != _project.TrackActiveItem;
        public bool accept_changes() { _backupTrack = null; return true; }
        public void cancel_changes() { if (_backupTrack.HasValue) _project.TrackActiveItem = _backupTrack.Value; _backupTrack = null; }
        public Control get_control() => this;
    }

    // C# port of base/sources/options_page_input.{h,cpp}
    public class OptionsPageInput : UserControl, options_page
    {
        private readonly InputEngine _inputEngine;
        private readonly List<(string action, string shortcut)> _actionsToAdd = new List<(string, string)>();
        private readonly List<(string action, string shortcut)> _actionsToRemove = new List<(string, string)>();
        private Dictionary<string, List<string>>? _snapshotBeforeEdit;
        private bool _changesMade;

        public readonly TextBox filter_text_box = new TextBox { Location = new Point(12, 25), Size = new Size(216, 20) };
        public readonly ListView actions_list_view = new ListView { Location = new Point(12, 51), Size = new Size(332, 180), View = View.Details, FullRowSelect = true };
        public readonly ComboBox shortcuts_combo_box = new ComboBox { Location = new Point(110, 237), Size = new Size(161, 21), DropDownStyle = ComboBoxStyle.DropDownList };
        public readonly Button remove_button = new Button { Text = "Remove", Location = new Point(279, 235), Size = new Size(65, 23) };
        public readonly TextBox shortcut_text_box = new TextBox { Location = new Point(88, 281), Size = new Size(183, 20) };
        public readonly Button assign_button = new Button { Text = "Assign", Location = new Point(279, 279), Size = new Size(65, 23) };
        public readonly Button reset_all_button = new Button { Text = "Reset to default", Location = new Point(242, 326), Size = new Size(102, 23) };

        public OptionsPageInput(InputEngine inputEngine)
        {
            _inputEngine = inputEngine ?? throw new ArgumentNullException(nameof(inputEngine));
            Name = "options_page_input";
            Size = new Size(358, 356);
            actions_list_view.Columns.Add("Action", 180);
            actions_list_view.Columns.Add("Shortcut", 130);

            Controls.AddRange(new Control[]
            {
                new Label { Text = "Filter actions:", Location = new Point(12, 8), AutoSize = true },
                new Label { Text = "Current shortcuts:", Location = new Point(12, 240), AutoSize = true },
                new Label { Text = "New shortcut:", Location = new Point(12, 284), AutoSize = true },
                filter_text_box, actions_list_view, shortcuts_combo_box,
                remove_button, shortcut_text_box, assign_button, reset_all_button
            });

            filter_text_box.TextChanged += (_, _) => PopulateActionsList();
            actions_list_view.SelectedIndexChanged += (_, _) => UpdateSelectedActionShortcuts();
            assign_button.Click += (_, _) => AssignShortcut(SelectedActionName, shortcut_text_box.Text);
            remove_button.Click += (_, _) => RemoveShortcut(SelectedActionName, shortcuts_combo_box.SelectedItem as string);
            reset_all_button.Click += (_, _) => ResetAllToDefaults();
        }

        public string? SelectedActionName
            => actions_list_view.SelectedItems.Count > 0 ? actions_list_view.SelectedItems[0].Text : null;

        public void activate_page()
        {
            if (_snapshotBeforeEdit == null)
            {
                _actionsToAdd.Clear();
                _actionsToRemove.Clear();
                _changesMade = false;
                _snapshotBeforeEdit = CaptureShortcutSnapshot();
            }
            PopulateActionsList();
            Visible = true;
        }

        public void deactivate_page() => Visible = false;

        private Dictionary<string, List<string>> CaptureShortcutSnapshot()
        {
            var dict = new Dictionary<string, List<string>>();
            foreach (string name in _inputEngine.get_actions_names_list())
            {
                var action = _inputEngine.get_action(name);
                if (action != null)
                    dict[name] = action.m_shortcuts.Select(s => s.m_combination).ToList();
            }
            return dict;
        }

        private void RestoreShortcutSnapshot(Dictionary<string, List<string>> snapshot)
        {
            foreach (var kv in snapshot)
            {
                var action = _inputEngine.get_action(kv.Key);
                if (action == null) continue;
                action.m_shortcuts.Clear();
                foreach (string comb in kv.Value)
                    _inputEngine.add_action_shortcut(kv.Key, comb);
            }
        }

        private void PopulateActionsList()
        {
            actions_list_view.Items.Clear();
            string filter = filter_text_box.Text.Trim().ToLowerInvariant();
            var names = _inputEngine.get_actions_names_list();
            names.Sort();
            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(filter) && !name.ToLowerInvariant().Contains(filter))
                    continue;
                var keys = _inputEngine.get_action_keys_list_with_contexts(name);
                string firstKey = keys.Count > 0 ? keys[0] : string.Empty;
                actions_list_view.Items.Add(new ListViewItem(new[] { name, firstKey }));
            }
            if (actions_list_view.Items.Count > 0)
                actions_list_view.Items[0].Selected = true;
        }

        private void UpdateSelectedActionShortcuts()
        {
            shortcuts_combo_box.Items.Clear();
            string? name = SelectedActionName;
            if (string.IsNullOrEmpty(name)) return;
            var keys = _inputEngine.get_action_keys_list_with_contexts(name);
            foreach (string k in keys)
                shortcuts_combo_box.Items.Add(k);
            if (shortcuts_combo_box.Items.Count > 0)
                shortcuts_combo_box.SelectedIndex = 0;
        }

        public bool AssignShortcut(string? actionName, string? combination)
        {
            if (string.IsNullOrEmpty(actionName) || string.IsNullOrWhiteSpace(combination))
                return false;
            if (!_inputEngine.add_action_shortcut(actionName, combination))
                return false;
            _actionsToAdd.Add((actionName, combination));
            _changesMade = true;
            UpdateSelectedActionShortcuts();
            return true;
        }

        public bool RemoveShortcut(string? actionName, string? combination)
        {
            if (string.IsNullOrEmpty(actionName) || string.IsNullOrWhiteSpace(combination))
                return false;
            // Convert "Key (Context)" display format to "Key(Context)" if needed
            string normalized = combination.Replace(" (", "(");
            if (!_inputEngine.remove_action_shortcut(actionName, normalized) &&
                !_inputEngine.remove_action_shortcut(actionName, combination))
                return false;
            _actionsToRemove.Add((actionName, normalized));
            _changesMade = true;
            UpdateSelectedActionShortcuts();
            return true;
        }

        public void ResetAllToDefaults()
        {
            _inputEngine.reset_to_defaults();
            _changesMade = true;
            PopulateActionsList();
        }

        public bool changed() => _changesMade || _actionsToAdd.Count > 0 || _actionsToRemove.Count > 0;

        public bool accept_changes()
        {
            _actionsToAdd.Clear();
            _actionsToRemove.Clear();
            _snapshotBeforeEdit = null;
            _changesMade = false;
            return true;
        }

        public void cancel_changes()
        {
            if (_snapshotBeforeEdit != null)
                RestoreShortcutSnapshot(_snapshotBeforeEdit);
            _snapshotBeforeEdit = null;
            _actionsToAdd.Clear();
            _actionsToRemove.Clear();
            _changesMade = false;
        }

        public Control get_control() => this;
    }

    // C# port of base/sources/options_dialog.{h,cpp}
    public class OptionsDialog : Form, options_manager
    {
        public readonly TreeView treeView = new TreeView();
        public readonly Panel page_platform_panel = new Panel();
        public readonly Button ok_button = new Button();
        public readonly Button cancel_button = new Button();
        public readonly Button apply_button = new Button();

        private options_page? m_active_page;

        public OptionsDialog()
        {
            SuspendLayout();
            treeView.Dock = DockStyle.Left;
            treeView.HideSelection = false;
            treeView.Name = "treeView";
            treeView.Size = new Size(176, 360);
            treeView.AfterSelect += tree_view_selection_changed;

            page_platform_panel.Dock = DockStyle.Fill;
            page_platform_panel.Name = "page_platform_panel";

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            ok_button.Text = "OK";
            ok_button.Size = new Size(75, 23);
            ok_button.Location = new Point(310, 9);
            ok_button.Click += ok_button_Click;

            cancel_button.Text = "Cancel";
            cancel_button.Size = new Size(75, 23);
            cancel_button.Location = new Point(391, 9);
            cancel_button.Click += cancel_button_Click;

            apply_button.Text = "Apply";
            apply_button.Size = new Size(75, 23);
            apply_button.Location = new Point(472, 9);
            apply_button.Enabled = false;
            apply_button.Click += apply_button_Click;

            bottomPanel.Controls.AddRange(new Control[] { ok_button, cancel_button, apply_button });

            Controls.Add(page_platform_panel);
            Controls.Add(treeView);
            Controls.Add(bottomPanel);

            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "options_dialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Options";
            Shown += (_, _) => EnsureInitialized();
            ResumeLayout(false);
        }

        public options_page? ActivePage => m_active_page;

        public void register_page(string fullName, options_page page)
        {
            string[] keys = fullName.Split('\\', '/');
            TreeNodeCollection nodes = treeView.Nodes;
            TreeNode? node = null;
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                int idx = nodes.IndexOfKey(key);
                if (idx >= 0)
                    node = nodes[idx];
                else
                    node = nodes.Add(key, key);
                nodes = node.Nodes;
            }
            if (node != null)
            {
                node.Tag = page;
                var ctrl = page.get_control();
                ctrl.Visible = false;
                HookPageChangeNotifications(ctrl);
                page_platform_panel.Controls.Add(ctrl);
            }
        }

        private void HookPageChangeNotifications(Control control)
        {
            switch (control)
            {
                case NumericUpDown nud: nud.ValueChanged += (_, _) => UpdateApplyState(); break;
                case TrackBar tb: tb.ValueChanged += (_, _) => UpdateApplyState(); break;
                case CheckBox cb: cb.CheckedChanged += (_, _) => UpdateApplyState(); break;
                case Button btn: btn.Click += (_, _) => UpdateApplyState(); break;
            }
            foreach (Control child in control.Controls)
                HookPageChangeNotifications(child);
        }

        private static bool AnyPageChanged(TreeNodeCollection nodes)
        {
            foreach (TreeNode item in nodes)
            {
                if (item.Tag is options_page page && page.changed()) return true;
                if (AnyPageChanged(item.Nodes)) return true;
            }
            return false;
        }

        public void UpdateApplyState() => apply_button.Enabled = AnyPageChanged(treeView.Nodes);

        public void unregister_page(string fullName)
        {
            string[] keys = fullName.Split('\\', '/');
            TreeNodeCollection nodes = treeView.Nodes;
            TreeNode? node = null;
            foreach (string key in keys)
            {
                int idx = nodes.IndexOfKey(key);
                if (idx < 0) return;
                node = nodes[idx];
                nodes = node.Nodes;
            }
            if (node?.Tag is options_page page)
            {
                page_platform_panel.Controls.Remove(page.get_control());
                node.Remove();
            }
        }

        public void EnsureInitialized()
        {
            initialize_all(treeView.Nodes);
            if (m_active_page == null && treeView.Nodes.Count > 0)
            {
                treeView.SelectedNode = treeView.Nodes[0];
                SelectPageNode(treeView.Nodes[0]);
            }
            else if (m_active_page != null)
            {
                m_active_page.activate_page();
            }
            UpdateApplyState();
        }

        private static void initialize_all(TreeNodeCollection nodes)
        {
            foreach (TreeNode item in nodes)
            {
                if (item.Tag is options_page page)
                {
                    page.activate_page();
                    page.deactivate_page();
                }
                initialize_all(item.Nodes);
            }
        }

        public void SelectPageByName(string name)
        {
            EnsureInitialized();
            var node = FindNodeRecursive(treeView.Nodes, name);
            if (node != null)
            {
                treeView.SelectedNode = node;
                SelectPageNode(node);
            }
        }

        private static TreeNode? FindNodeRecursive(TreeNodeCollection nodes, string name)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Name == name || n.Text == name) return n;
                var sub = FindNodeRecursive(n.Nodes, name);
                if (sub != null) return sub;
            }
            return null;
        }

        private void SelectPageNode(TreeNode? node)
        {
            m_active_page?.deactivate_page();
            m_active_page = node?.Tag as options_page;
            if (m_active_page != null)
            {
                var ctrl = m_active_page.get_control();
                ctrl.Dock = DockStyle.Fill;
                m_active_page.activate_page();
            }
            UpdateApplyState();
        }

        private void tree_view_selection_changed(object? sender, TreeViewEventArgs e)
            => SelectPageNode(e.Node);

        private static void make_changes(TreeNodeCollection nodes, bool accept)
        {
            foreach (TreeNode item in nodes)
            {
                if (item.Tag is options_page page)
                {
                    if (accept || !page.changed()) page.accept_changes();
                    else page.cancel_changes();
                }
                make_changes(item.Nodes, accept);
            }
        }

        public void AcceptAllChanges()
        {
            make_changes(treeView.Nodes, true);
            m_active_page?.deactivate_page();
            DialogResult = DialogResult.OK;
        }

        public void CancelAllChanges()
        {
            make_changes(treeView.Nodes, false);
            m_active_page?.deactivate_page();
            DialogResult = DialogResult.Cancel;
        }

        private void ok_button_Click(object? sender, EventArgs e)
        {
            AcceptAllChanges();
            Close();
        }

        private void cancel_button_Click(object? sender, EventArgs e)
        {
            CancelAllChanges();
            Close();
        }

        private void apply_button_Click(object? sender, EventArgs e)
        {
            make_changes(treeView.Nodes, true);
            m_active_page?.activate_page();
            UpdateApplyState();
        }
    }
}
