using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors;

public static class property_extractor
{
    private static bool is_match_for_attributes(PropertyDescriptor descriptor, settings settings)
    {
        switch (settings.browsable_attribute_state)
        {
            case browsable_state.and:
                return descriptor.Attributes.Matches(settings.browsable_attributes.ToArray());

            case browsable_state.or:
                foreach (var attribute in settings.browsable_attributes)
                {
                    if (descriptor.Attributes.Matches(attribute))
                        return true;
                }
                break;
        }
        return false;
    }

    public static TCollection extract<TCollection>(object obj) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, new[] { obj }, null, new settings());
        return ret_coll;
    }

    public static TCollection extract<TCollection>(object obj, property? parent_property) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, new[] { obj }, parent_property, new settings());
        return ret_coll;
    }

    public static TCollection extract<TCollection>(object obj, property? parent_property, settings settings) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, new[] { obj }, parent_property, settings);
        return ret_coll;
    }

    public static TCollection extract<TCollection>(object[] objects) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, objects, null, new settings());
        return ret_coll;
    }

    public static TCollection extract<TCollection>(object[] objects, property? parent_property) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, objects, parent_property, new settings());
        return ret_coll;
    }

    public static TCollection extract<TCollection>(object[] objects, property? parent_property, settings settings) where TCollection : IList, new()
    {
        var ret_coll = new TCollection();
        extract(ret_coll, objects, parent_property, settings);
        return ret_coll;
    }

    public static List<property> extract(object obj, property? parent_property)
    {
        var ret_coll = new List<property>();
        extract(ret_coll, new[] { obj }, parent_property, new settings());
        return ret_coll;
    }

    public static List<property> extract(object obj, property? parent_property, settings settings)
    {
        var ret_coll = new List<property>();
        extract(ret_coll, new[] { obj }, parent_property, settings);
        return ret_coll;
    }

    public static List<property> extract(object[] objects, property? parent_property)
    {
        var ret_coll = new List<property>();
        extract(ret_coll, objects, parent_property, new settings());
        return ret_coll;
    }

    public static List<property> extract(object[] objects, property? parent_property, settings settings)
    {
        var ret_coll = new List<property>();
        extract(ret_coll, objects, parent_property, settings);
        return ret_coll;
    }

    public static void extract(IList fill_collection, object[] objects, property? parent_property, settings settings)
    {
        if (objects.Length == 0)
            return;

        if (objects[0] is IList || objects[0] is property_container_collection)
        {
            extract_from_collection(fill_collection, objects, parent_property, settings);
            return;
        }
        extract_from_class(fill_collection, objects, parent_property, settings);
    }

    public static void extract_from_class(IList fill_collection, object[] objects, property? parent_property, settings settings)
    {
        var local_properties = fill_collection;
        bool is_first_object = true;
        var properties_dict = new Dictionary<string, property>();
        var new_properties_set = new HashSet<string>();
        var to_remove_props = new List<property>();

        foreach (var obj in objects)
        {
            new_properties_set.Clear();
            to_remove_props.Clear();
            IEnumerable collection = obj switch
            {
                property_descriptor pd => pd.inner_properties.Values,
                i_property_container ipc => ipc.get_properties(),
                _ => TypeDescriptor.GetProperties(obj)
            };

            foreach (PropertyDescriptor descriptor in collection)
            {
                if (descriptor.Attributes[typeof(BrowsableAttribute)] is BrowsableAttribute attr && !attr.Browsable)
                    continue;

                if ((parent_property == null || settings.browsable_attribute_affects_children) &&
                    settings.browsable_attributes.Count > 0 && !is_match_for_attributes(descriptor, settings))
                    continue;

                string dict_key = descriptor.Category + "/" + descriptor.DisplayName;
                if (!is_first_object && !properties_dict.ContainsKey(dict_key))
                    continue;

                property prop;
                if (is_first_object)
                {
                    prop = settings.create_prop_delegate == null
                        ? (property)Activator.CreateInstance(settings.property_type)!
                        : settings.create_prop_delegate();

                    prop.extract_settings = settings;
                    local_properties.Add(prop);
                    properties_dict.Add(dict_key, prop);

                    if (parent_property != null)
                    {
                        prop.property_parent = parent_property;
                        if (parent_property.is_read_only)
                            prop.is_read_only = true;
                    }
                    else if (settings.read_only_mode)
                    {
                        prop.is_read_only = true;
                    }
                }
                else
                {
                    prop = properties_dict[dict_key];
                }

                Type value_type = descriptor.PropertyType;
                if (descriptor.Attributes[typeof(type_converter_attribute)] is type_converter_attribute converter_attribute)
                {
                    value_type = converter_attribute.m_destination_type;
                    prop.converter ??= (type_converter_base)Activator.CreateInstance(converter_attribute.m_converter_type)!;
                }

                if (!is_first_object && prop.type != value_type)
                    continue;

                prop.type = value_type;

                if (descriptor.Attributes[typeof(ReadOnlyAttribute)] is ReadOnlyAttribute read_only_attribute)
                    prop.is_read_only = prop.is_read_only || read_only_attribute.IsReadOnly || settings.read_only_mode;
                else
                    prop.is_read_only = prop.is_read_only || settings.read_only_mode;

                prop.name = descriptor.DisplayName;
                prop.descriptors.Add(descriptor);
                prop.property_owners.Add(obj);

                if (descriptor is property_descriptor pdOwner)
                    pdOwner.owner_property = prop;

                if (descriptor.Attributes[typeof(DefaultValueAttribute)] is DefaultValueAttribute def_attr)
                    prop.default_values.Add(def_attr.Value);
                else
                    prop.default_values.Add(property.get_property_value(descriptor, obj));

                if (!is_first_object && descriptor.Attributes[typeof(expandable_item_attribute)] == null)
                {
                    object? old_val = property.get_property_value(prop.descriptors[0], prop.property_owners[0]);
                    object? new_val = property.get_property_value(descriptor, obj);
                    if ((old_val == null) != (new_val == null) || (old_val != null && !old_val.Equals(new_val)))
                        prop.is_multiple_values = true;
                }

                settings.initialize_prop_delegate?.Invoke(prop);
                new_properties_set.Add(dict_key);
            }

            if (!is_first_object)
            {
                foreach (var entry in properties_dict)
                {
                    if (entry.Value.descriptors[0].Attributes[typeof(MergablePropertyAttribute)] is MergablePropertyAttribute ma && !ma.AllowMerge)
                    {
                        to_remove_props.Add(entry.Value);
                        continue;
                    }
                    if (!new_properties_set.Contains(entry.Key))
                        to_remove_props.Add(entry.Value);
                }

                for (int i = to_remove_props.Count - 1; i >= 0; --i)
                {
                    var rem = to_remove_props[i];
                    properties_dict.Remove(rem.descriptor.Category + "/" + rem.name);
                    local_properties.Remove(rem);
                }
            }
            is_first_object = false;
        }
    }

    public static void extract_from_collection(IList fill_collection, object[] objects, property? parent_property, settings settings)
    {
        var local_properties = fill_collection;
        string str_templ = string.Empty;
        const string c_item_name = "item_{0}";
        bool is_first_object = true;

        foreach (var obj in objects)
        {
            IList collection;
            if (obj is i_property_container ipc)
            {
                collection = (List<property_descriptor>)(property_container_collection.property_collection)ipc.get_properties();
                str_templ = ((property_container_collection)obj).item_name_template;
            }
            else
            {
                collection = (IList)obj;
            }

            for (int i = 0; i < collection.Count; ++i)
            {
                property prop;
                if (is_first_object)
                {
                    prop = settings.create_prop_delegate == null
                        ? (property)Activator.CreateInstance(settings.property_type)!
                        : settings.create_prop_delegate();
                    prop.extract_settings = settings;
                    local_properties.Add(prop);
                    if (parent_property != null)
                    {
                        prop.property_parent = parent_property;
                        if (parent_property.is_read_only)
                            prop.is_read_only = true;
                    }
                }
                else
                {
                    prop = (property)local_properties[i]!;
                }

                if (!is_first_object && prop.descriptors[0].PropertyType != collection[i]?.GetType())
                    continue;

                if (string.IsNullOrEmpty(str_templ))
                    str_templ = c_item_name;

                if (collection[i] is property_descriptor pd)
                    pd.owner_property = prop;

                var descriptor = new item_property_descriptor(str_templ, i, collection);

                prop.name = string.Format(str_templ, i);
                prop.is_expandable_item = true;
                prop.descriptors.Add(descriptor);
                prop.type = descriptor.PropertyType;
                prop.property_owners.Add(collection);
                prop.is_collection_item = true;
                prop.is_read_only = prop.is_read_only || settings.read_only_mode;
                prop.default_values.Add(property.get_property_value(descriptor, collection));

                object? old_val = property.get_property_value(prop.descriptors[0], prop.property_owners[0]);
                object? new_val = property.get_property_value(descriptor, collection);
                if (old_val != null && new_val != null && !old_val.Equals(new_val))
                    prop.is_multiple_values = true;

                settings.initialize_prop_delegate?.Invoke(prop);
            }
            is_first_object = false;
        }
    }

    public static property? extract_from_collection(int index, object[] objects, property? parent_property, settings settings)
    {
        property? local_property = null;
        string str_templ = string.Empty;
        const string c_item_name = "item_{0}";
        bool is_first_object = true;

        foreach (var obj in objects)
        {
            IList collection;
            if (obj is i_property_container ipc)
            {
                collection = (List<property_descriptor>)(property_container_collection.property_collection)ipc.get_properties();
                str_templ = ((property_container_collection)obj).item_name_template;
            }
            else
            {
                collection = (IList)obj;
            }

            property prop;
            if (is_first_object)
            {
                prop = settings.create_prop_delegate == null
                    ? (property)Activator.CreateInstance(settings.property_type)!
                    : settings.create_prop_delegate();
                prop.extract_settings = settings;
                local_property = prop;
                if (parent_property != null)
                {
                    prop.property_parent = parent_property;
                    if (parent_property.is_read_only)
                        prop.is_read_only = true;
                }
            }
            else
            {
                prop = local_property!;
            }

            if (!is_first_object && prop.descriptors[0].PropertyType != collection[index]?.GetType())
                continue;

            if (string.IsNullOrEmpty(str_templ))
                str_templ = c_item_name;

            if (collection[index] is property_descriptor pd)
                pd.owner_property = prop;

            var descriptor = new item_property_descriptor(str_templ, index, collection);

            prop.name = string.Format(str_templ, index);
            prop.is_expandable_item = true;
            prop.descriptors.Add(descriptor);
            prop.type = descriptor.PropertyType;
            prop.property_owners.Add(collection);
            prop.is_collection_item = true;
            prop.is_read_only = prop.is_read_only || settings.read_only_mode;
            prop.default_values.Add(property.get_property_value(descriptor, collection));

            object? old_val = property.get_property_value(prop.descriptors[0], prop.property_owners[0]);
            object? new_val = property.get_property_value(descriptor, collection);
            if (old_val != null && new_val != null && !old_val.Equals(new_val))
                prop.is_multiple_values = true;

            settings.initialize_prop_delegate?.Invoke(prop);
            is_first_object = false;
        }

        return local_property;
    }

    public class settings
    {
        public Func<property>? create_prop_delegate;
        public Action<property>? initialize_prop_delegate;
        public Type property_type = typeof(property);
        public browsable_state browsable_attribute_state;
        public List<Attribute> browsable_attributes = new();
        public bool browsable_attribute_affects_children;
        public bool read_only_mode;
    }
}
