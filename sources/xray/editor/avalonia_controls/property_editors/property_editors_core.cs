using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.item;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors;

public enum browsable_state
{
    and,
    or
}

public interface i_external_property_editor
{
    void run_editor(property prop);
}

public interface i_additional_data
{
    string? additional_data { get; set; }
}

public class editor_picker
{
    public editor_picker(Type editor_type) => this.editor_type = editor_type;

    public Type editor_type { get; }

    protected virtual bool can_edit_internal(property property) => false;

    public bool can_edit(property property) => can_edit_internal(property);
}

public class type_editor_picker : editor_picker
{
    public type_editor_picker(Type edited_type, Type editor_type) : base(editor_type) =>
        this.edited_type = edited_type;

    public Type edited_type { get; set; }

    protected override bool can_edit_internal(property property)
    {
        if (property.type == typeof(object))
        {
            object? val = property.value;
            if (val != null && val.GetType() == edited_type)
                return true;
        }
        return property.type == edited_type;
    }
}

public class assignable_type_editor_picker : editor_picker
{
    public assignable_type_editor_picker(Type edited_type, Type editor_type) : base(editor_type)
    {
        this.edited_type = edited_type;
    }

    public assignable_type_editor_picker(Type edited_type, Type editor_type, bool is_expandable) : base(editor_type)
    {
        this.is_expandable = is_expandable;
        this.edited_type = edited_type;
    }

    public bool is_expandable { get; set; }
    public Type edited_type { get; set; }

    protected override bool can_edit_internal(property property)
    {
        if (property.type == typeof(object))
        {
            object? val = property.value;
            if (val != null && edited_type.IsAssignableFrom(val.GetType()))
            {
                property.is_expandable_item = is_expandable;
                return true;
            }
        }

        if (edited_type.IsAssignableFrom(property.type))
        {
            property.is_expandable_item = is_expandable;
            return true;
        }
        return false;
    }
}

public class attribute_editor_picker : editor_picker
{
    public attribute_editor_picker(Type edited_attribute_type, Type editor_type) : base(editor_type) =>
        this.edited_attribute_type = edited_attribute_type;

    public attribute_editor_picker(Type edited_attribute_type, IValueConverter converter, Type editor_type) : base(editor_type)
    {
        this.edited_attribute_type = edited_attribute_type;
        this.converter = converter;
    }

    public attribute_editor_picker(Type edited_attribute_type, Predicate<object> compare_callback, Type editor_type) : base(editor_type)
    {
        this.edited_attribute_type = edited_attribute_type;
        this.compare_callback = compare_callback;
    }

    public Type edited_attribute_type { get; set; }
    public IValueConverter? converter { get; set; }
    public Predicate<object>? compare_callback { get; set; }

    protected override bool can_edit_internal(property property)
    {
        if (property.descriptors.Count == 0)
            return false;
        var attribute = property.descriptors[0].Attributes[edited_attribute_type];
        if (attribute == null)
            return false;

        if (converter != null)
            return Equals(converter.Convert(attribute, typeof(bool), null, CultureInfo.InvariantCulture), true);
        if (compare_callback != null)
            return compare_callback(attribute);
        return true;
    }
}

public class empty_collection_checher : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var properties = (value as property)?.sub_properties;
        return properties == null || properties.Count != 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public abstract class property_editor_base : UserControl
{
    public property? m_property;
    public item_editor_base? item_editor;

    public abstract void update();
}

public class item_property_descriptor : property_descriptor
{
    public item_property_descriptor(string item_name, int item_index, IList parent_collection)
        : base(item_name)
    {
        m_list = parent_collection;
        if (parent_collection[item_index] is property_descriptor desc)
        {
            is_property_descriptor = true;
            m_type = desc.PropertyType;
            is_expanded = desc.is_expanded;
            m_attributes = desc.dynamic_attributes;
            m_inner_properties = desc.inner_properties;
            tag = desc.tag;
            owner_property = desc.owner_property;
        }
        else
        {
            m_type = parent_collection[item_index]?.GetType() ?? typeof(object);
        }

        m_item_index = item_index;
    }

    private readonly IList m_list;
    private readonly bool is_property_descriptor;
    private int m_item_index;

    public int item_index => m_item_index;

    internal void decrease_item_index() => --m_item_index;
    internal void increase_item_index() => ++m_item_index;

    public bool expanded
    {
        get => is_expanded;
        set
        {
            is_expanded = value;
            if (is_property_descriptor && m_list[item_index] is property_descriptor desc)
                desc.is_expanded = value;
        }
    }

    public override object? GetValue(object? component) =>
        is_property_descriptor
            ? ((property_descriptor)((IList)component!)[m_item_index]!).value
            : ((IList)component!)[m_item_index];

    public override void SetValue(object? component, object? set_value)
    {
        if (is_property_descriptor)
            ((property_descriptor)((IList)component!)[m_item_index]!).value = set_value;
        else
            ((IList)component!)[m_item_index] = set_value;
    }
}
