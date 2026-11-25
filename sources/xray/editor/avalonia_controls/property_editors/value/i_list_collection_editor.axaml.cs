using System;
using System.Collections;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class IList_collection_editor : value_collection_editor_base
{
    private int m_last_sub_properties_count;

    public IList_collection_editor()
    {
        InitializeComponent();
        m_add_button.Click += add_click;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;
            m_property.is_collection = true;
            m_property.is_expandable_item = true;

            if (item_editor?.parent_container != null)
            {
                item_editor.parent_container.is_expand_visible = true;
                item_editor.parent_container.fill_sub_properties = fill_sub_properties;
                if (!m_property.is_read_only)
                    item_editor.parent_container.decorate_container = decorate_container;
            }
        };
    }

    private void add_click(object? sender, RoutedEventArgs e)
    {
        if ((m_property ?? DataContext as property) is not property collection_property)
            return;

        var itemProp = new property
        {
            name = "item",
            is_collection_item = true,
            property_parent = collection_property
        };

        foreach (var obj in collection_property.values)
        {
            if (obj is not IList list)
                continue;

            var generic_types = list.GetType().GetGenericArguments();
            list.Add(generic_types.Length > 0 ? Activator.CreateInstance(generic_types[0]) : new object());
            itemProp.descriptors.Add(new item_property_descriptor(itemProp.name, list.Count - 1, list));
            if (collection_property.value != null)
                itemProp.property_owners.Add(collection_property.value);
        }

        collection_property.sub_properties ??= new();
        collection_property.sub_properties.Add(itemProp);
        item_editor?.parent_container?.reset_sub_properties();
    }

    private void fill_sub_properties()
    {
        if (m_property == null)
            return;
        if (m_property.sub_properties == null || m_property.sub_properties.Count == 0)
        {
            m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
            m_last_sub_properties_count = m_property.sub_properties.Count;
        }
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
            Content = new Path
            {
                Data = geom,
                Stroke = Brushes.Black,
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
        int count = sub_property.property_owners.Count;
        for (int i = 0; i < count; ++i)
        {
            ((IList)sub_property.property_owners[i]).RemoveAt(
                ((item_property_descriptor)sub_property.descriptors[i]).item_index);
        }

        var properties = sub_property.property_parent?.sub_properties;
        if (properties != null)
        {
            int index = properties.IndexOf(sub_property);
            if (index >= 0)
            {
                for (int i = index + 1; i < properties.Count; ++i)
                {
                    foreach (item_property_descriptor desc in properties[i].descriptors)
                        desc.decrease_item_index();
                }
                properties.RemoveAt(index);
            }
        }
        update();
    }

    public override void update()
    {
        if (item_editor?.parent_container != null &&
            m_property?.value is IList list &&
            m_last_sub_properties_count != list.Count)
        {
            item_editor.parent_container.reset_sub_properties();
        }
    }
}

internal class collection_editor_is_fixed_size_checker : IValueConverter
{
    public object Convert(object? value, Type target_type, object? parameter, CultureInfo culture) =>
        value is IList list && !list.IsFixedSize;

    public object ConvertBack(object? value, Type target_type, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
