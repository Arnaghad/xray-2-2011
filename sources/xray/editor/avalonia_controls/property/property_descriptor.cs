using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using xray.editor.avalonia_controls.control_containers;
using xray.editor.avalonia_controls.hypergraph;
using xray.editor.avalonia_controls.property_editors.attributes;
using property_item = xray.editor.avalonia_controls.property_editors.property;

namespace xray.editor.avalonia_controls;

public class property_descriptor : PropertyDescriptor
{
    public property_descriptor(string property_name) : base(property_name, Array.Empty<Attribute>())
    {
        m_type = typeof(object);
        m_property_value = null;
    }

    public property_descriptor(property_descriptor other_descriptor) : base(other_descriptor.Name, Array.Empty<Attribute>())
    {
        m_type = other_descriptor.m_type;
        m_property_value = other_descriptor.m_property_value;
        m_attributes = new attribute_collection(other_descriptor.m_attributes);
        m_inner_properties = other_descriptor.inner_properties;
    }

    public property_descriptor(string property_name, Type property_type, i_property_value value) : base(property_name, Array.Empty<Attribute>())
    {
        m_type = property_type;
        m_property_value = value;
    }

    public property_descriptor(string property_name, i_property_value value) : base(property_name, Array.Empty<Attribute>())
    {
        m_type = value.value_type;
        m_property_value = value;
    }

    public property_descriptor(i_property_info_value value) : base(value.property_info.Name, Array.Empty<Attribute>())
    {
        m_type = value.value_type;
        m_property_value = value;
    }

    public property_descriptor(string display_name, string category, string description, object? default_value, i_property_value value)
        : base(display_name, Array.Empty<Attribute>())
    {
        m_type = value.value_type;
        m_property_value = value;
        this.category = category;
        this.display_name = display_name;
        this.description = description;
        this.default_value = default_value;
    }

    public property_descriptor(string display_name, string category, string description, i_property_container container)
        : base(display_name, Array.Empty<Attribute>())
    {
        var value = new object_property_value<i_property_container>(container);
        m_type = value.value_type;
        m_property_value = value;
        this.category = category;
        this.display_name = display_name;
        this.description = description;
    }

    public property_descriptor(string display_name, string category, string description, property_container container)
        : base(display_name, Array.Empty<Attribute>())
    {
        var value = new object_property_value<property_container>(container);
        m_type = value.value_type;
        m_property_value = value;
        this.category = category;
        this.display_name = display_name;
        this.description = description;
    }

    public property_descriptor(object obj, string property_name) : base(property_name, Array.Empty<Attribute>())
    {
        var prop_info = obj.GetType().GetProperty(property_name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var val = new property_property_value(obj, prop_info);

        var dattrs = (DisplayNameAttribute[])prop_info.GetCustomAttributes(typeof(DisplayNameAttribute), true);
        if (dattrs.Length > 0)
            display_name = dattrs[0].DisplayName;

        m_type = val.value_type;
        m_property_value = val;
        default_value = val.get_value();
        dynamic_attributes.add_range(prop_info.GetCustomAttributes(true));
    }

    public property_descriptor(object obj, string display_name, string property_name) : base(property_name, Array.Empty<Attribute>())
    {
        var prop_info = obj.GetType().GetProperty(property_name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var val = new property_property_value(obj, prop_info);
        this.display_name = display_name;
        m_type = val.value_type;
        m_property_value = val;
        default_value = val.get_value();
        dynamic_attributes.add_range(prop_info.GetCustomAttributes(true));
    }

    public property_descriptor(string display_name, object obj, string property_name) : base(display_name, Array.Empty<Attribute>())
    {
        var prop_info = obj.GetType().GetProperty(property_name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var val = new property_property_value(obj, prop_info);
        this.display_name = display_name;
        m_type = val.value_type;
        m_property_value = val;
        default_value = val.get_value();
        dynamic_attributes.add_range(prop_info.GetCustomAttributes(true));
    }

    public property_descriptor(string display_name, string category, string description, object? default_value, i_property_container container)
        : base(display_name, Array.Empty<Attribute>())
    {
        var val = new object_property_value<i_property_container>(container);
        m_type = val.value_type;
        m_property_value = val;
        this.category = category;
        this.display_name = display_name;
        this.description = description;
        this.default_value = default_value;
    }

    protected attribute_collection m_attributes = new();
    protected Type m_type;
    private i_property_value? m_property_value;
    protected Dictionary<string, property_descriptor> m_inner_properties = new();

    public property_container_base? owner { get; set; }
    public attribute_collection dynamic_attributes => m_attributes;

    public override AttributeCollection Attributes
    {
        get
        {
            if (m_attributes.need_recreate)
            {
                AttributeArray = m_attributes.to_array();
                m_attributes.reset_recreate();
            }
            return base.Attributes;
        }
    }

    public object? tag { get; set; }
    public Dictionary<string, property_descriptor> inner_properties => m_inner_properties;
    public bool is_expanded { get; set; }
    public property_item? owner_property { get; set; }

    public override Type PropertyType => m_type;
    public override Type ComponentType => typeof(object);

    public override bool IsReadOnly =>
        dynamic_attributes[typeof(ReadOnlyAttribute)] is ReadOnlyAttribute attr && attr.IsReadOnly;

    public override string DisplayName =>
        dynamic_attributes[typeof(DisplayNameAttribute)] is DisplayNameAttribute attr ? attr.DisplayName : Name;

    public override string Category =>
        dynamic_attributes[typeof(CategoryAttribute)] is CategoryAttribute attr ? attr.Category : string.Empty;

    public override string Description =>
        dynamic_attributes[typeof(DescriptionAttribute)] is DescriptionAttribute attr ? attr.Description : string.Empty;

    public override bool IsBrowsable =>
        dynamic_attributes[typeof(BrowsableAttribute)] is not BrowsableAttribute attr || attr.Browsable;

    public bool is_read_only
    {
        get => IsReadOnly;
        set
        {
            if (dynamic_attributes[typeof(ReadOnlyAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new ReadOnlyAttribute(value));
        }
    }

    public string display_name
    {
        get => DisplayName;
        set
        {
            if (DisplayName == value)
                return;

            var owner_ref = owner;
            if (owner is property_container pc)
            {
                if (pc.properties.contains(category + "/" + value))
                    throw new ArgumentException($"Can't change display name. Descriptor with that category and display name already exists in parent container.\n {category}/{value}");
                owner.properties.remove(this);
            }

            if (dynamic_attributes[typeof(DisplayNameAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new DisplayNameAttribute(value));

            owner_ref?.properties.add(this);
        }
    }

    public string category
    {
        get => Category;
        set
        {
            if (Category == value)
                return;

            var owner_ref = owner;
            if (owner is property_container pc)
            {
                if (pc.properties.contains(value + "/" + display_name))
                    throw new ArgumentException($"Can't change category. Descriptor with that category and display name already exists in parent container.\n {value}/{display_name}");
                owner.properties.remove(this);
            }

            if (dynamic_attributes[typeof(CategoryAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new CategoryAttribute(value));

            owner_ref?.properties.add(this);
        }
    }

    public string description
    {
        get => Description;
        set
        {
            if (dynamic_attributes[typeof(DescriptionAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new DescriptionAttribute(value));
        }
    }

    public string full_name => category + "/" + display_name;

    public bool is_browsable
    {
        get => IsBrowsable;
        set
        {
            if (dynamic_attributes[typeof(BrowsableAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new BrowsableAttribute(value));
        }
    }

    public object? default_value
    {
        get => dynamic_attributes[typeof(DefaultValueAttribute)] is DefaultValueAttribute attr ? attr.Value : null;
        set
        {
            if (dynamic_attributes[typeof(DefaultValueAttribute)] is Attribute existing)
                dynamic_attributes.remove(existing);
            dynamic_attributes.add(new DefaultValueAttribute(value));
        }
    }

    public object? value
    {
        get => m_property_value?.get_value();
        set => m_property_value?.set_value(value);
    }

    public i_property_value? property_value => m_property_value;
    public select_behavior select_behavior { get; set; }

    public bool is_selected
    {
        get => owner_property?.is_selected ?? false;
        set
        {
            if (owner_property != null)
                owner_property.is_selected = value;
        }
    }

    public event Action<bool>? selection_changed;

    internal void raise_selection_changed(bool selected) => selection_changed?.Invoke(selected);

    public static List<property_descriptor> from_obj(object obj)
    {
        var descriptors = TypeDescriptor.GetProperties(obj);
        var list = new List<property_descriptor>(descriptors.Count);
        foreach (PropertyDescriptor descriptor in descriptors)
        {
            var desc = new property_descriptor(descriptor.DisplayName, new property_property_value(obj, descriptor.Name));
            desc.dynamic_attributes.add_range(descriptor.Attributes);
            list.Add(desc);
        }
        return list;
    }

    public static property_descriptor from_obj(object obj, string property_name)
    {
        var prop_info = obj.GetType().GetProperty(property_name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var val = new property_property_value(obj, prop_info);
        return from_value(prop_info, val);
    }

    public static property_descriptor from_obj(object obj, PropertyInfo property_info) =>
        from_value(property_info, new property_property_value(obj, property_info));

    public static property_descriptor from_value(i_property_info_value value) => new(value);

    public static property_descriptor from_value(PropertyInfo prop_info, i_property_value value)
    {
        string display_name = prop_info.Name;
        var dattrs = (DisplayNameAttribute[])prop_info.GetCustomAttributes(typeof(DisplayNameAttribute), true);
        if (dattrs.Length > 0)
            display_name = dattrs[0].DisplayName;

        var desc = new property_descriptor(display_name, value.value_type, value) { default_value = value.get_value() };
        desc.dynamic_attributes.add_range(prop_info.GetCustomAttributes(true));
        return desc;
    }

    public static property_descriptor from_value(string display_name, i_property_value value) =>
        new(display_name, value.value_type, value);

    public static property_descriptor from_value(string display_name, string category, i_property_value value) =>
        new(display_name, value.value_type, value) { category = category };

    public static property_descriptor from_value(string display_name, string category, string description, object? default_value, i_property_value value) =>
        new(display_name, value.value_type, value)
        {
            category = category,
            display_name = display_name,
            description = description,
            default_value = default_value
        };

    public static property_descriptor from_container(string display_name, string category, string description, i_property_container container) =>
        container is property_container pc
            ? new property_descriptor(display_name, new object_property_value<property_container>(pc))
            {
                category = category,
                display_name = display_name,
                description = description
            }
            : new property_descriptor(display_name, new object_property_value<i_property_container>(container))
            {
                category = category,
                display_name = display_name,
                description = description
            };

    public static List<property_descriptor> from_container(i_property_container container) => container.ToList();

    public override bool CanResetValue(object component) => false;

    public override object? GetValue(object? component) => m_property_value?.get_value();

    public override void SetValue(object? component, object? set_value) => m_property_value?.set_value(set_value);

    public override void ResetValue(object component)
    {
        if (default_value != null)
            m_property_value?.set_value(default_value);
    }

    public override bool ShouldSerializeValue(object component) => false;

    public void set_source(i_property_value new_value) => m_property_value = new_value;

    public button add_button(string caption, string description, Action<button> callback)
    {
        var desc = new property_descriptor(caption) { display_name = caption, description = description };
        var btn = new button(caption) { content = caption };
        btn.click += callback;
        desc.dynamic_attributes.add(new control_attribute(btn));
        inner_properties.Add(caption, desc);
        return btn;
    }

    public property_descriptor set_compo_box_style(string[] combo_box_items)
    {
        dynamic_attributes.add(new combo_box_items_attribute(combo_box_items));
        return this;
    }

    public property_descriptor set_compo_box_style(object[] combo_box_associatons)
    {
        dynamic_attributes.add(new combo_box_items_attribute(combo_box_associatons));
        return this;
    }

    public property_descriptor set_compo_box_style(string argument, Func<string, IEnumerable<string>> get_items)
    {
        dynamic_attributes.add(new combo_box_items_attribute(argument, get_items));
        return this;
    }

    public property_descriptor set_compo_box_style(Func<int> items_count_func, Func<int, string> get_item_func)
    {
        dynamic_attributes.add(new combo_box_items_attribute(items_count_func, get_item_func));
        return this;
    }

    public property_descriptor set_external_editor_style(Type editor_type)
    {
        dynamic_attributes.add(new external_editor_attribute(editor_type));
        return this;
    }

    public property_descriptor set_external_editor_style(Type editor_type, bool can_directly_set)
    {
        dynamic_attributes.add(new external_editor_attribute(editor_type, can_directly_set));
        return this;
    }

    public property_descriptor set_external_editor_style(external_editor_event_handler run_editor_callback)
    {
        dynamic_attributes.add(new external_editor_attribute(run_editor_callback));
        return this;
    }

    public property_descriptor set_external_editor_style(external_editor_event_handler run_editor_callback, bool is_clear_visible)
    {
        dynamic_attributes.add(new external_editor_attribute(run_editor_callback, is_clear_visible));
        return this;
    }

    public property_descriptor set_link_points(bool input_enabled, bool output_enabled)
    {
        dynamic_attributes.add(new node_property_attribute(input_enabled, output_enabled));
        return this;
    }

    public void update() => owner_property?.owner_editor?.item_editor?.parent_container?.update_hierarchy();

    public class attribute_collection : IEnumerable<Attribute>
    {
        internal attribute_collection() { }

        internal attribute_collection(attribute_collection other_collection)
        {
            m_attributes = new List<Attribute>(other_collection.m_attributes.Count);
            foreach (var attr in other_collection.m_attributes)
            {
                var new_attr = (Attribute)RuntimeHelpers.GetUninitializedObject(attr.GetType());
                foreach (var field_info in attr.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
                    field_info.SetValue(new_attr, field_info.GetValue(attr));
                m_attributes.Add(new_attr);
            }
            m_need_recreate = true;
        }

        private readonly List<Attribute> m_attributes = new();
        private bool m_need_recreate;

        public Attribute? this[Type attribute_type]
        {
            get
            {
                var list = m_attributes.Where(attr => attr.GetType() == attribute_type).ToList();
                return list.Count > 0 ? list[0] : null;
            }
        }

        public Attribute this[int index]
        {
            get => m_attributes[index];
            set
            {
                m_attributes[index] = value;
                m_need_recreate = true;
            }
        }

        public bool need_recreate => m_need_recreate;

        internal void reset_recreate() => m_need_recreate = false;

        public void add(Attribute attribute)
        {
            m_attributes.Add(attribute);
            m_need_recreate = true;
        }

        public void add_range(IEnumerable<Attribute> attributes)
        {
            m_attributes.AddRange(attributes);
            m_need_recreate = true;
        }

        public void add_range(IEnumerable attributes)
        {
            foreach (Attribute attribute in attributes)
                m_attributes.Add(attribute);
            m_need_recreate = true;
        }

        public void add_range(AttributeCollection attributes)
        {
            foreach (Attribute attribute in attributes)
                m_attributes.Add(attribute);
            m_need_recreate = true;
        }

        public void set_range(IEnumerable<Attribute> attributes)
        {
            m_attributes.Clear();
            add_range(attributes);
        }

        public void remove_at(int index)
        {
            m_attributes.RemoveAt(index);
            m_need_recreate = true;
        }

        public void remove(Attribute attribute)
        {
            m_attributes.Remove(attribute);
            m_need_recreate = true;
        }

        public void remove<TType>(Attribute attribute) where TType : Attribute
        {
            for (int i = m_attributes.Count - 1; i >= 0; --i)
            {
                if (m_attributes[i] is TType)
                    m_attributes.RemoveAt(i);
            }
            m_need_recreate = true;
        }

        public void clear()
        {
            m_attributes.Clear();
            m_need_recreate = true;
        }

        public Attribute[] to_array() => m_attributes.ToArray();

        public List<Attribute>? get_attributes(Type attributes_type)
        {
            var list = m_attributes.Where(attr => attr.GetType() == attributes_type).ToList();
            return list.Count > 0 ? list : null;
        }

        public IEnumerator<Attribute> GetEnumerator() => m_attributes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
