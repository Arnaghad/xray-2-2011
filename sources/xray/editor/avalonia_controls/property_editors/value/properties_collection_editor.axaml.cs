using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class properties_collection_editor : value_collection_editor_base
{
    private Func<bool>? m_add_property_func;
    private Func<object, bool>? m_remove_property_func;
    private Func<int, string, bool>? m_move_property_func;

    public properties_collection_editor()
    {
        InitializeComponent();
        m_add_button.Click += add_click;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;
            m_property.is_expandable_item = true;
            m_property.is_collection = true;

            if (m_property.descriptors[0].Attributes[typeof(collection_attribute)] is collection_attribute attribute)
            {
                m_add_property_func = attribute.add_func;
                m_remove_property_func = attribute.remove_func;
                m_move_property_func = attribute.move_func;
            }

            if (item_editor?.parent_container != null)
            {
                item_editor.parent_container.is_expand_visible = true;
                item_editor.parent_container.fill_sub_properties = fill_sub_properties;
                item_editor.parent_container.decorate_container = decorate_container;

                if (m_property.value is property_container_collection pcc)
                {
                    pcc.property_added += indexed_property_added;
                    pcc.property_removed += indexed_property_removed;
                    pcc.updated += collection_updated;
                    pcc.refreshed += collection_refreshed;
                }
                else if (m_property.value is property_container pc)
                {
                    pc.property_added += property_added;
                    pc.property_removed += property_removed;
                    pc.updated += collection_updated;
                    pc.refreshed += collection_refreshed;
                }

                item_editor.parent_container.is_expanded = true;
            }
        };
    }

    private void collection_refreshed() => item_editor?.parent_container?.reset_sub_properties();
    private void collection_updated() => item_editor?.parent_container?.update_hierarchy();

    private void indexed_property_removed(int index)
    {
        if (item_editor?.parent_container != null && m_property?.sub_properties != null)
        {
            item_editor.parent_container.remove_sub_container(index);
            m_property.sub_properties.RemoveAt(index);
        }
        else
        {
            item_editor?.parent_container?.reset_sub_properties();
        }
    }

    private void indexed_property_added(int index)
    {
        if (item_editor?.parent_container != null && m_property?.sub_properties != null)
        {
            var property = property_extractor.extract_from_collection(index, m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
            if (property != null)
            {
                item_editor.parent_container.insert_container_for_property(index, property);
                m_property.sub_properties.Insert(index, property);
            }
        }
        else
        {
            item_editor?.parent_container?.reset_sub_properties();
        }
    }

    private void property_removed(string name)
    {
        if (item_editor?.parent_container == null || m_property?.sub_properties == null)
            return;

        for (int i = 0; i < item_editor.parent_container.Items.Count; i++)
        {
            if (item_editor.parent_container.Items[i] is property_items_control pic && pic.m_property?.name == name)
            {
                item_editor.parent_container.remove_sub_container(i);
                m_property.sub_properties.RemoveAt(i);
                return;
            }
        }
    }

    private void property_added(string name)
    {
        if (m_property?.sub_properties == null || item_editor?.parent_container == null)
            return;

        var collection = property_extractor.extract<List<property>>(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
        var property = collection.First(p => p.name == name);
        int insert_index = m_property.sub_properties.Count;
        item_editor.parent_container.insert_container_for_property(insert_index, property);
        m_property.sub_properties.Insert(insert_index, property);
    }

    private void add_click(object? sender, RoutedEventArgs e) => m_add_property_func?.Invoke();

    private void fill_sub_properties()
    {
        if (m_property == null)
            return;
        if (m_property.sub_properties == null || m_property.sub_properties.Count == 0)
            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }

    private void decorate_container(expandable_items_control container)
    {
        var geom = this.FindResource("remove_button_path_geometry") as Geometry;
        var button = new Button
        {
            Width = 14,
            Height = 14,
            Padding = new(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            Content = new Path
            {
                Data = geom,
                Stroke = SdkWorkspaceTheme.MutedText,
                StrokeThickness = 1
            }
        };

        button.Click += remove_item_click;
        container.add_right_pocket_inner(button);
    }

    private void remove_item_click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is property subProp)
            remove_sub_property(subProp);
    }

    internal override void remove_sub_property(property sub_property)
    {
        if (m_property?.sub_properties == null)
            return;

        if (sub_property.descriptors[0] is item_property_descriptor && sub_property.property_parent?.sub_properties != null)
        {
            var properties = sub_property.property_parent.sub_properties;
            int index = properties.IndexOf(sub_property);
            m_remove_property_func?.Invoke(index);

            for (int i = index; i < m_property.sub_properties.Count; ++i)
            {
                foreach (item_property_descriptor descriptor in m_property.sub_properties[i].descriptors)
                    descriptor.decrease_item_index();
            }

            item_editor?.parent_container?.reset_sub_properties();
            return;
        }

        if (m_remove_property_func?.Invoke(sub_property.name) == true)
            item_editor?.parent_container?.reset_sub_properties();
    }

    internal override void move_up_sub_property(property sub_property)
    {
        if (sub_property.descriptors[0] is item_property_descriptor && sub_property.property_parent?.sub_properties != null)
        {
            int index = sub_property.property_parent.sub_properties.IndexOf(sub_property);
            if (m_move_property_func?.Invoke(index, "up") == true)
                item_editor?.parent_container?.reset_sub_properties();
        }
    }

    internal override void move_down_sub_property(property sub_property)
    {
        if (sub_property.descriptors[0] is item_property_descriptor && sub_property.property_parent?.sub_properties != null)
        {
            int index = sub_property.property_parent.sub_properties.IndexOf(sub_property);
            if (m_move_property_func?.Invoke(index, "down") == true)
                item_editor?.parent_container?.reset_sub_properties();
        }
    }
}
