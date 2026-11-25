using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors;

public class property : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public property()
    {
        m_index = s_count++;
        descriptors = new List<PropertyDescriptor>();
        property_owners = new List<object>();
        default_values = new List<object?>();
        is_valid = true;
        is_read_only = false;
    }

    private static int s_count;

    private readonly int m_index;
    private bool m_is_collection_item;
    private bool m_is_expandable_item;
    private bool m_is_multiple_values;
    private Type m_type = typeof(object);

    private property? m_property_parent;
    private List<property>? m_inner_properties;
    private type_converter_base? m_converter;
    private property_editor_base? m_owner_editor;

    public string name { get; set; } = string.Empty;

    public Type type
    {
        get => m_type;
        set => m_type = value;
    }

    public List<PropertyDescriptor> descriptors { get; protected set; }
    public PropertyDescriptor descriptor => descriptors[0];
    public bool is_read_only { get; set; }
    public bool is_valid { get; internal set; }
    public List<property>? sub_properties { get; internal set; }

    public List<property>? inner_properties
    {
        get
        {
            if (m_inner_properties == null && descriptors.Count > 0 && descriptors[0] is property_descriptor)
                m_inner_properties = property_extractor.extract(descriptors.Cast<object>().ToArray(), this, extract_settings ?? new property_extractor.settings());
            return m_inner_properties;
        }
    }

    public type_converter_base? converter
    {
        get => m_converter;
        set => m_converter = value;
    }

    public property_editor_base? owner_editor
    {
        get => m_owner_editor;
        internal set => m_owner_editor = value;
    }

    public virtual bool is_selected { get; set; }
    public bool is_collection { get; internal set; }

    public List<object?> default_values { get; protected set; }
    public List<object> property_owners { get; protected set; }

    public property? property_parent
    {
        get => m_property_parent;
        internal set
        {
            m_property_parent = value;
            if (value != null)
                is_collection_item = value.is_collection;
        }
    }

    public bool is_multiple_values
    {
        get => m_is_multiple_values;
        internal set
        {
            m_is_multiple_values = value;
            on_property_changed(nameof(is_multiple_values));
        }
    }

    public bool is_expandable_item
    {
        get => m_is_expandable_item;
        internal set
        {
            m_is_expandable_item = value;
            on_property_changed(nameof(is_expandable_item));
        }
    }

    public bool is_expanded
    {
        get => descriptors.Count > 0 && descriptor is property_descriptor pd && pd.is_expanded;
        internal set
        {
            if (descriptors.Count > 0 && descriptor is property_descriptor pd)
                pd.is_expanded = value;
            on_property_changed(nameof(is_expanded));
        }
    }

    public bool is_collection_item
    {
        get => m_is_collection_item;
        internal set
        {
            m_is_collection_item = value;
            on_property_changed(nameof(is_collection_item));
        }
    }

    public bool is_default_value
    {
        get
        {
            var vls = values;
            for (int i = 0; i < vls.Length; i++)
            {
                if (vls[i] == null)
                {
                    if (default_values[i] != null)
                        return false;
                }
                else if (!vls[i]!.Equals(default_values[i]))
                {
                    return false;
                }
            }
            return true;
        }
        internal set => on_property_changed(nameof(is_default_value));
    }

    public virtual object? value
    {
        get => is_multiple_values || !is_valid || property_owners.Count == 0 ? null : get_descriptor_value(property_owners[0], descriptors[0]);
        set
        {
            for (int i = 0; i < property_owners.Count; ++i)
                set_descriptor_value(property_owners[i], descriptors[i], value);

            on_property_changed(nameof(value));
            on_property_changed(nameof(is_default_value));
        }
    }

    public object?[] values
    {
        get
        {
            var ret_values = new object?[property_owners.Count];
            for (int i = 0; i < property_owners.Count; ++i)
                ret_values[i] = get_descriptor_value(property_owners[i], descriptors[i]);
            return ret_values;
        }
    }

    public property_extractor.settings? extract_settings { get; set; }

    public override string ToString() => $"{name} = {(value != null ? value.ToString() : "null")} (idx={m_index})";

    public void destroy() => destroy(true);

    public void destroy(bool is_hierarchical)
    {
        if (is_hierarchical)
        {
            if (sub_properties != null)
            {
                foreach (var p in sub_properties)
                    p.destroy();
            }
            if (m_inner_properties != null)
            {
                foreach (var p in m_inner_properties)
                    p.destroy();
            }
        }

        descriptors.Clear();
        owner_editor = null;
        property_owners.Clear();
        default_values.Clear();
        m_inner_properties = null;
        sub_properties = null;
        property_parent = null;
        is_valid = false;
    }

    internal static object? get_property_value(PropertyDescriptor descriptor, object obj)
    {
        try
        {
            return descriptor.GetValue(obj);
        }
        catch (System.Reflection.TargetInvocationException)
        {
            if (descriptor.PropertyType.IsValueType)
                return Activator.CreateInstance(descriptor.PropertyType);
            return null;
        }
    }

    internal static object? create_property_value(Type type)
    {
        try
        {
            if (type == typeof(string))
                return string.Empty;
            return Activator.CreateInstance(type);
        }
        catch (System.Reflection.TargetInvocationException)
        {
            return null;
        }
        catch (MissingMethodException)
        {
            return null;
        }
    }

    internal void invalidate_value()
    {
        on_property_changed(nameof(value));
        on_property_changed(nameof(is_default_value));
    }

    internal void update_owner_editor() => owner_editor?.update();

    internal void set_descriptor_value(object owner, PropertyDescriptor descriptor, object? value)
    {
        if (m_converter != null && descriptor.PropertyType != m_type)
            descriptor.SetValue(owner, m_converter.convert_back(value));
        else
            descriptor.SetValue(owner, value?.cast_to(descriptor.PropertyType));
    }

    internal object? get_descriptor_value(object owner, PropertyDescriptor descriptor)
    {
        if (m_converter != null && descriptor.PropertyType != m_type)
            return m_converter.convert(descriptor.GetValue(owner));
        return descriptor.GetValue(owner);
    }

    protected void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
}
