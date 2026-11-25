using System.Linq;
using Avalonia.Controls;
using xray.editor.avalonia_controls.control_containers;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class control_editor : value_editor_base
{
    public control_editor()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            update();
        };
    }

    public override void update()
    {
        if (m_property == null)
            return;

        m_panel.Children.Clear();
        bool is_first = true;
        Control? uiCtrl = null;
        control? first_control = null;

        foreach (var attr in m_property.descriptors
                     .Select(desc => desc.Attributes[typeof(control_attribute)] as control_attribute)
                     .Where(attr => attr != null))
        {
            if (is_first)
            {
                first_control = attr!.m_control;
                uiCtrl = attr.m_control.generate_control();
                DockPanel.SetDock(uiCtrl, global::Avalonia.Controls.Dock.Left);
                m_panel.Children.Add(uiCtrl);
                is_first = false;
            }
            else if (first_control != null && uiCtrl != null && attr!.m_control.same_as(first_control))
            {
                attr.m_control.merge_to(uiCtrl);
            }
        }

        m_panel.IsEnabled = !m_property.is_read_only;
    }
}
