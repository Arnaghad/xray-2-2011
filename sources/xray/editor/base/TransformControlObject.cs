using System;
using System.Collections.Generic;

namespace xray.editor_base
{
    public struct CollisionObjectWrapper
    {
        public IntPtr m_collision_object;
    }

    public abstract class TransformControlObject
    {
        public enum Mode { EditTransform, EditPivot }

        private static Mode m_edit_mode = Mode.EditTransform;

        public virtual bool attach(TransformControlBase control) => true;
        public virtual void detach(TransformControlBase control) { }
        public abstract void start_modify(TransformControlBase control);
        public abstract void execute_preview(TransformControlBase control);
        public abstract void end_modify(TransformControlBase control);
        public abstract System.Numerics.Matrix4x4 get_anchor_transform();

        public virtual uint get_collision(ref List<CollisionObjectWrapper> list) => 0;

        public Mode current_mode() => m_edit_mode;
        public static bool is_edit_pivot_mode() => m_edit_mode == Mode.EditPivot;
        public static void switch_edit_pivot_mode()
        {
            m_edit_mode = is_edit_pivot_mode() ? Mode.EditTransform : Mode.EditPivot;
        }

        public virtual bool translation_ignore_rotation() => true;
    }
}
