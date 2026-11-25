using System;
using System.Collections.Generic;
using xray.editor.avalonia_controls.control_containers;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls;

public class property_container : property_container_base
{
    public property_container()
    {
        properties = new property_collection(this);
    }

    private int m_containers_count;

    public new property_collection properties
    {
        get => (property_collection)base.properties;
        private set => base.properties = value;
    }

    public event Action<string>? property_added;
    public event Action<string>? property_removed;

    private void on_property_added(string key) => property_added?.Invoke(key);
    private void on_property_removed(string key) => property_removed?.Invoke(key);

    public control_container add_uniform_container()
    {
        var desc = new property_descriptor("control_container_" + m_containers_count)
        {
            display_name = "control_container_" + m_containers_count++
        };
        var grid = new uniform_control_container(desc);
        desc.dynamic_attributes.add(new control_container_attribute(grid));
        properties.add(desc);
        return grid;
    }

    public control_container add_stack_container()
    {
        var desc = new property_descriptor("control_container_" + m_containers_count)
        {
            display_name = "control_container_" + m_containers_count++
        };
        var stack_panel = new stack_control_container(desc);
        desc.dynamic_attributes.add(new control_container_attribute(stack_panel));
        properties.add(desc);
        return stack_panel;
    }

    public control_container add_dock_container(bool dock_left)
    {
        var desc = new property_descriptor("control_container_" + m_containers_count)
        {
            display_name = "control_container_" + m_containers_count++
        };
        var dock_panel = new dock_control_container(desc) { is_dock_left = dock_left };
        desc.dynamic_attributes.add(new control_container_attribute(dock_panel));
        properties.add(desc);
        return dock_panel;
    }

    public class property_collection : property_collection_base
    {
        internal property_collection(property_container owner) : base(owner)
        {
            m_properties = new Dictionary<string, property_descriptor>();
        }

        private readonly Dictionary<string, property_descriptor> m_properties;

        public property_descriptor this[string property_id]
        {
            get
            {
                var parts = property_id.Split('.');
                var descriptor = m_properties[parts[0]];
                if (parts.Length > 1)
                    descriptor = descriptor.inner_properties[parts[1]];
                return descriptor;
            }
            set
            {
                var parts = property_id.Split('.');
                if (parts.Length == 1)
                {
                    m_properties[parts[0]] = value;
                    return;
                }
                m_properties[parts[0]].inner_properties[parts[1]] = value;
            }
        }

        public override int count => m_properties.Count;

        public override property_descriptor add(property_descriptor desc)
        {
            var key = desc.Category + "/" + desc.DisplayName;
            if (contains(key))
                throw new ArgumentException($"Can't add descriptor. Descriptor with that category and display name already exists in parent container.\n {key}");

            m_properties.Add(key, desc);
            desc.owner = m_owner;
            ((property_container)m_owner).on_property_added(key);
            return desc;
        }

        public override void remove(property_descriptor desc) => remove(desc.Category + "/" + desc.DisplayName);

        public void remove(string property_id)
        {
            var desc = m_properties[property_id];
            desc.owner = null;
            desc.owner_property = null;
            m_properties.Remove(property_id);
            ((property_container)m_owner).on_property_removed(property_id);
        }

        public override void clear()
        {
            foreach (var descriptor in m_properties.Values)
            {
                descriptor.owner = null;
                descriptor.owner_property = null;
            }
            m_properties.Clear();
            m_owner.refresh_properties();
        }

        public bool contains(string property_path) => m_properties.ContainsKey(property_path);

        public override bool contains(property_descriptor desc) =>
            m_properties.ContainsKey(desc.category + "/" + desc.display_name);

        public override IEnumerator<property_descriptor> GetEnumerator() => m_properties.Values.GetEnumerator();
    }
}
