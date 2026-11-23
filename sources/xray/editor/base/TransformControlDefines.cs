using System;

namespace xray.editor_base
{
    public enum TransformAxis
    {
        Cam,
        X,
        Y,
        Z,
        All,
        None,
        Count
    }

    public enum SnapMode
    {
        NoSnap,
        VertexSnap,
        FaceSnap,
        EdgeSnap,
        StepSnap
    }
    public interface ITransformControlObject
    {
        System.Numerics.Matrix4x4 get_ancor_transform();
        void start_modify(TransformControlBase control);
        void execute_preview(TransformControlBase control);
        void end_modify(TransformControlBase control);
        bool attach(TransformControlBase control);
        void detach(TransformControlBase control);
    }

    public class TransformControlHelper
    {
        public ITransformControlObject m_object { get; set; }
        public bool m_changed { get; set; }
        public IntPtr m_collision_tree { get; set; } // Native pointer
        public IntPtr m_editor_renderer { get; set; } // Native pointer

        public bool Empty => m_object == null;

        public System.Numerics.Matrix4x4 active_object_transform 
            => m_object?.get_ancor_transform() ?? System.Numerics.Matrix4x4.Identity;

        public System.Numerics.Matrix4x4 get_inverted_view_matrix()
        {
            // This would call back to the Engine/Renderer
            return System.Numerics.Matrix4x4.Identity; 
        }

        public void attach(TransformControlBase control) => m_object?.attach(control);
        public void detach(TransformControlBase control) => m_object?.detach(control);
    }
}
