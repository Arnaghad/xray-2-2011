using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.property_editors;

namespace xray.editor.avalonia_controls.property_grid;

public class property_grid_item : property_items_control
{
    private readonly hierarchy_node? m_hierarchy_node;
    private Control? m_item_property_editor;
    private Point m_mouse_down_point;
    private bool m_pointer_pressed;

    internal property_grid_item(
        property prop,
        item_editor_selector item_editor_selector,
        value_editor_selector value_editor_selector,
        int level,
        hierarchy_node? node)
        : base(prop, item_editor_selector, value_editor_selector, level)
    {
        is_create_properties = true;
        m_hierarchy_node = node;

        if (prop.sub_properties == null || prop.sub_properties.Count == 0)
            is_expand_visible = false;

        var editor = m_item_editor_selector.select_editor(prop);
        editor.value_editor_selector = m_value_editor_selector;
        editor.parent_container = this;
        editor.DataContext = null;
        item_editor = editor;
        editor.DataContext = prop;

        is_expanded = is_expanded || m_hierarchy_node != null;
        prop.PropertyChanged += on_prop_changed;

        AddHandler(PointerPressedEvent, preview_pointer_pressed, RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(property_grid_item);

    private void on_prop_changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(property.is_selected) && m_item_property_editor != null && m_property is property p)
            m_item_property_editor.Classes.Set("selected", p.is_selected);
    }

    private void preview_pointer_pressed(object? sender, PointerPressedEventArgs e) =>
        m_mouse_down_point = e.GetPosition(this);

    private void editor_pointer_pressed(object? sender, PointerPressedEventArgs e)
    {
        if (m_property is not property prop)
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            m_pointer_pressed = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                prop.is_selected = !prop.is_selected;
            else
                prop.owner_property_grid?.deselect_all_properties();

            e.Handled = true;
        }
    }

    private void editor_pointer_moved(object? sender, PointerEventArgs e)
    {
        if (m_pointer_pressed &&
            m_property is property prop &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Point delta = m_mouse_down_point - e.GetPosition(this);
            if ((delta.X * delta.X + delta.Y * delta.Y) > 4)
                prop.is_selected = true;
        }
    }

    private void editor_pointer_released(object? sender, PointerReleasedEventArgs e) =>
        m_pointer_pressed = false;

    internal override property_items_control create_container_for_property(property_editors.property prop) =>
        new property_grid_item(
            (property)prop,
            m_item_editor_selector,
            m_value_editor_selector,
            level + 1,
            m_hierarchy_node?.sub_nodes.Find(node => node.name == prop.name));

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        m_item_property_editor = e.NameScope.Find<Control>("PART_item_property_editor");
        if (m_item_property_editor != null)
        {
            m_item_property_editor.PointerPressed += editor_pointer_pressed;
            m_item_property_editor.PointerMoved += editor_pointer_moved;
            m_item_property_editor.PointerReleased += editor_pointer_released;
            if (m_property is property p)
                m_item_property_editor.Classes.Set("selected", p.is_selected);
        }
    }
}
