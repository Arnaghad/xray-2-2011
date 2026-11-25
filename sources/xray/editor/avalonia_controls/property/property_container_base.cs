using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace xray.editor.avalonia_controls;

public abstract class property_container_base : ICustomTypeDescriptor, i_property_container
{
    private string? m_string_view;

    public property_collection_base properties { get; protected set; } = null!;
    public object? owner { get; set; }

    public string? string_view
    {
        get => m_string_view;
        set => m_string_view = value;
    }

    public property_descriptor? inner_value { get; set; }
    public bool is_return_inner_property { get; set; }

    AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);
    string? ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);
    string? ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);
    TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);
    EventDescriptor? ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
    PropertyDescriptor? ICustomTypeDescriptor.GetDefaultProperty() => null;
    object? ICustomTypeDescriptor.GetEditor(Type editor_base_type) => TypeDescriptor.GetEditor(this, editor_base_type, true);
    EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);
    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[]? attributes) => TypeDescriptor.GetEvents(this, attributes, true);
    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() => ((ICustomTypeDescriptor)this).GetProperties(Array.Empty<Attribute>());

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[]? attributes)
    {
        var descriptors = new PropertyDescriptor[properties.count];
        int i = 0;
        foreach (var descriptor in properties)
            descriptors[i++] = descriptor;
        return new PropertyDescriptorCollection(descriptors);
    }

    object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor? pd) => this;

    public override string? ToString() => m_string_view;

    public void update_properties() => updated?.Invoke();
    public void refresh_properties() => refreshed?.Invoke();

    IEnumerable<property_descriptor> i_property_container.get_properties()
    {
        if (is_return_inner_property)
            return inner_value == null ? Array.Empty<property_descriptor>() : new[] { inner_value };
        return properties;
    }

    public IEnumerator<property_descriptor> GetEnumerator() => properties.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public event Action? updated;
    public event Action? refreshed;

    public abstract class property_collection_base : IEnumerable<property_descriptor>
    {
        internal property_collection_base(property_container_base owner) => m_owner = owner;

        protected readonly property_container_base m_owner;

        public abstract int count { get; }

        public List<property_descriptor> add_from_obj(object obj)
        {
            var list = property_descriptor.from_obj(obj);
            foreach (var desc in list)
                add(desc);
            return list;
        }

        public property_descriptor add_from_obj(object obj, string property_name) =>
            add(property_descriptor.from_obj(obj, property_name));

        public property_descriptor add_from_obj(object obj, PropertyInfo property_info) =>
            add(property_descriptor.from_obj(obj, property_info));

        public property_descriptor add(i_property_info_value value) => add(new property_descriptor(value));

        public property_descriptor add(PropertyInfo prop_info, i_property_value value) =>
            add(property_descriptor.from_value(prop_info, value));

        public property_descriptor add(string display_name, i_property_value value) =>
            add(new property_descriptor(display_name, value.value_type, value));

        public property_descriptor add(string display_name, string category, i_property_value value) =>
            add(new property_descriptor(display_name, value.value_type, value) { category = category });

        public property_descriptor add(string display_name, string category, string description, object? default_value, i_property_value value) =>
            add(new property_descriptor(display_name, value.value_type, value)
            {
                category = category,
                display_name = display_name,
                description = description,
                default_value = default_value
            });

        public property_descriptor add_container(string display_name, string category, string description, i_property_container container) =>
            add(property_descriptor.from_container(display_name, category, description, container));

        public List<property_descriptor> add_from_container(IEnumerable<property_descriptor> container) =>
            container.Select(add).ToList();

        public abstract property_descriptor add(property_descriptor desc);
        public abstract void remove(property_descriptor desc);
        public abstract void clear();
        public abstract bool contains(property_descriptor desc);

        public abstract IEnumerator<property_descriptor> GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
