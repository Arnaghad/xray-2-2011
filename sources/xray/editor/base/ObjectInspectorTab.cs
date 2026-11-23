using System;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using PropertyHost = xray.editor.wpf_controls.property_grid.host;

namespace xray.editor_base
{
    // C# port of world/sources/object_inspector_tab.{h,cpp}
    public class ObjectInspectorTab : DockContent
    {
        public const string PersistIdentity = "xray.editor.object_inspector_tab";
        public const string SharedControlsAlias = "xray.editor.controls.item_properties_panel_base";

        private readonly PropertyHost m_property_grid_host;

        public ObjectInspectorTab()
        {
            m_property_grid_host = new PropertyHost
            {
                Dock = DockStyle.Fill,
                auto_update = false
            };
            SuspendLayout();
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(284, 476);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            Controls.Add(m_property_grid_host);
            Name = "object_inspector_tab";
            Text = "Object Inspector";
            TabText = Text;
            ResumeLayout(false);
        }

        protected override string GetPersistString() => PersistIdentity;

        public PropertyHost PropertyGridHost => m_property_grid_host;

        public void show_properties(object? obj, bool readOnly = false)
        {
            m_property_grid_host.read_only_mode = readOnly;
            if (obj is object[] arr)
                m_property_grid_host.selected_objects = arr;
            else
                m_property_grid_host.selected_object = obj;
        }

        public void ShowProperties(object? obj) => show_properties(obj, false);

        public void update() => m_property_grid_host.update();
        public void UpdateGrid() => m_property_grid_host.update();

        public void reset()
        {
            m_property_grid_host.selected_object = null;
        }
        public void Reset() => reset();
    }
}
