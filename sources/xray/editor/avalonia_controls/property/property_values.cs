using System;
using System.Collections.Generic;
using System.Reflection;

namespace xray.editor.avalonia_controls;

public interface i_property_value
{
    object? get_value();
    void set_value(object? value);
    Type value_type { get; }
}

public interface i_property_info_value : i_property_value
{
    PropertyInfo property_info { get; }
}

public interface i_property_container : IEnumerable<property_descriptor>
{
    IEnumerable<property_descriptor> get_properties();
}

public class callback_property_value<TType> : i_property_value
{
    public callback_property_value(Func<TType> get_value_callback, Action<TType> set_value_callback)
    {
        m_get_value_callback = get_value_callback ?? throw new ArgumentNullException(nameof(get_value_callback));
        m_set_value_callback = set_value_callback ?? throw new ArgumentNullException(nameof(set_value_callback));
    }

    private readonly Func<TType> m_get_value_callback;
    private readonly Action<TType> m_set_value_callback;

    public Type value_type => typeof(TType);

    public object? get_value() => m_get_value_callback();

    public void set_value(object? value) => m_set_value_callback((TType)value!);
}

public class dictionary_item_property_value<TKey, TValue> : i_property_value where TKey : notnull
{
    public dictionary_item_property_value(Dictionary<TKey, TValue> dictionary, TKey key)
    {
        m_key = key;
        m_dictionary = dictionary;
    }

    private readonly TKey m_key;
    private readonly Dictionary<TKey, TValue> m_dictionary;

    public object? get_value() => m_dictionary[m_key];

    public void set_value(object? value) => m_dictionary[m_key] = (TValue)value!;

    public Type value_type => typeof(TValue);
}

public class object_property_value<TType> : i_property_value
{
    public object_property_value(TType obj) => m_obj = obj;

    private TType m_obj;

    public object? get_value() => m_obj;

    public void set_value(object? value) => m_obj = (TType)value!;

    public Type value_type => typeof(TType);
}

public class property_property_value : i_property_value
{
    public property_property_value(object obj, string property_name)
    {
        m_obj = obj;
        m_property = obj.GetType().GetProperty(property_name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            ?? throw new ArgumentException($"Property '{property_name}' not found on '{obj.GetType().FullName}'.");
    }

    public property_property_value(object obj, PropertyInfo property)
    {
        m_obj = obj;
        m_property = property;
    }

    private readonly object m_obj;
    private readonly PropertyInfo m_property;

    public Type value_type => m_property.PropertyType;
    public PropertyInfo property_info => m_property;

    public object? get_value() => m_property.GetValue(m_obj, null);

    public void set_value(object? value) => m_property.SetValue(m_obj, value, null);
}
