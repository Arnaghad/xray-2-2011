using System.Linq;
using Avalonia.Controls;
using xray.editor.avalonia_controls.control_containers;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.item;

public class control_container_editor : item_editor_base
{
    public control_container_editor()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            create_controls();
            update();
        };
    }

    public void create_controls()
    {
        if (m_property == null)
            return;

        bool is_first_panel = true;
        Panel? ui_panel = null;
        control_container? first_container = null;

        foreach (var attr in m_property.descriptors
                     .Select(desc => desc.Attributes[typeof(control_container_attribute)] as control_container_attribute)
                     .Where(attr => attr != null))
        {
            var container = attr!.m_control_container;
            if (is_first_panel)
            {
                first_container = container;
                ui_panel = container.generate_panel();
                is_first_panel = false;
                continue;
            }

            if (first_container != null)
                container.merge_to(first_container);
        }

        Content = ui_panel;
    }

    public override void update()
    {
        if (Content is Control ctrl && m_property != null)
            ctrl.IsEnabled = !m_property.is_read_only;
    }
}
