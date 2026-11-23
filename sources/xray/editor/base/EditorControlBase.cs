using System;
using Microsoft.Win32;
using System.ComponentModel;

namespace xray.editor_base
{
    public delegate void ActivateControlEvent(EditorControlBase c);
    public delegate void DeactivateControlEvent(EditorControlBase c);
    public delegate void PropertyChangedEvent(EditorControlBase c);

    public delegate EditorControlBase ActiveControlGetter();

    public abstract class EditorControlBase
    {
        protected string m_control_id;
        public float sensitivity { get; set; }

        public event ActivateControlEvent on_activated;
        public event DeactivateControlEvent on_deactivated;
        public event PropertyChangedEvent on_property_changed;

        protected EditorControlBase()
        {
        }

        public string id() => m_control_id;

        public virtual void activate(bool b_activate)
        {
            if (b_activate) on_activated?.Invoke(this);
            else on_deactivated?.Invoke(this);
        }

        public abstract void update();

        public virtual void start_input(int mode, int button, IntPtr coll, System.Numerics.Vector3 pos) { }
        public virtual void execute_input() { }
        public virtual void end_input() { }

        public virtual void draw(IntPtr scene_view, IntPtr output_window) { }

        public virtual uint acceptable_collision_type() => 0;

        public virtual void load_settings(RegistryKey key) { }
        public virtual void save_settings(RegistryKey key) { }

        public virtual void change_property(string prop_name, float dx, float dy) { }
        public virtual void pin(bool val) { }
        public virtual bool has_properties() => false;

        public string name => id();

        protected virtual void initialize() { }
        protected virtual void destroy() { }
    }

    public abstract class TransformControlBase : EditorControlBase
    {
        protected TransformControlHelper m_helper;
        protected System.Numerics.Matrix4x4 m_matrix = System.Numerics.Matrix4x4.Identity;
        protected System.Numerics.Matrix4x4 m_inv_view = System.Numerics.Matrix4x4.Identity;
        protected TransformAxis m_active_axis = TransformAxis.None;
        protected TransformAxis m_picked_axis = TransformAxis.None;
        protected bool m_shown = false;
        protected bool m_dragging = false;

        protected System.Numerics.Vector3[] m_axes_vectors = new System.Numerics.Vector3[(int)TransformAxis.Count];
        protected bool[] m_axes_visibilities = new bool[(int)TransformAxis.Count];

        public float gizmo_size { get; set; } = 0.5f;

        protected TransformControlBase(TransformControlHelper helper)
        {
            m_helper = helper;
            m_axes_vectors[(int)TransformAxis.Cam] = System.Numerics.Vector3.Zero;
            m_axes_vectors[(int)TransformAxis.X] = new System.Numerics.Vector3(1, 0, 0);
            m_axes_vectors[(int)TransformAxis.Y] = new System.Numerics.Vector3(0, 1, 0);
            m_axes_vectors[(int)TransformAxis.Z] = new System.Numerics.Vector3(0, 0, 1);
            m_axes_vectors[(int)TransformAxis.All] = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(1, 1, 1));
            m_axes_vectors[(int)TransformAxis.None] = System.Numerics.Vector3.Zero;

            for (int i = 0; i < (int)TransformAxis.Count; i++) m_axes_visibilities[i] = true;
        }

        public abstract System.Numerics.Matrix4x4 calculate(System.Numerics.Matrix4x4 m);

        public override void update()
        {
            if (m_helper == null) return;

            if (m_helper.Empty && m_shown) show(false);
            else if (!m_helper.Empty && !m_shown) show(true);

            if (!m_shown) return;

            m_inv_view = m_helper.get_inverted_view_matrix();
            System.Numerics.Matrix4x4 m = m_helper.active_object_transform;
            set_matrix(m);
        }

        public virtual void set_matrix(System.Numerics.Matrix4x4 m)
        {
            m_matrix = m;
        }

        protected void show(bool b_show)
        {
            if (m_shown == b_show) return;
            m_shown = b_show;
            if (m_shown) m_helper.attach(this);
            else m_helper.detach(this);
        }

        protected float calculate_fixed_size_scale(System.Numerics.Vector3 position)
        {
            float min_dist = 10;
            float max_dist = 70;
            float distance = min_dist + (max_dist - min_dist) * (1.0f - gizmo_size);

            System.Numerics.Vector3 camera_pos = new System.Numerics.Vector3(m_inv_view.M41, m_inv_view.M42, m_inv_view.M43);
            System.Numerics.Vector3 camera_to_position = position - camera_pos;
            float camera_to_position_length = camera_to_position.Length();

            if (camera_to_position_length < 0.0001f) return 1.0f;

            float result = camera_to_position_length / distance;
            return Math.Max(result, 0.05f);
        }

        protected System.Numerics.Matrix4x4 calculate_fixed_size_transform(System.Numerics.Matrix4x4 transform)
        {
            float scale = calculate_fixed_size_scale(new System.Numerics.Vector3(transform.M41, transform.M42, transform.M43));
            return System.Numerics.Matrix4x4.CreateScale(scale) * transform;
        }
    }
}
