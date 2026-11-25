using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace xray.editor.avalonia_controls.property_grid;

public sealed class property : property_editors.property
{
    public property() { }

    internal property(property_grid_control owner) : this()
    {
        owner_property_grid = owner;
    }

    internal property(
        string name,
        PropertyDescriptor descriptor,
        object? value,
        object? default_value,
        object property_owner)
    {
        is_valid = true;
        this.name = name;
        descriptors = new List<PropertyDescriptor> { descriptor };
        default_values = new List<object?> { default_value };
        property_owners = new List<object> { property_owner };
        this.value = value;
    }

    private bool m_is_selected;

    public property_grid_control? owner_property_grid { get; private set; }

    public override bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (descriptors.Count == 0 ||
                descriptor is not property_descriptor pd ||
                pd.select_behavior == select_behavior.select)
            {
                bool old_selected = m_is_selected;
                m_is_selected = value;
                if (old_selected != m_is_selected)
                {
                    if (owner_property_grid != null)
                    {
                        if (value)
                            owner_property_grid.selected_properties.Add(this);
                        else
                            owner_property_grid.selected_properties.Remove(this);
                    }
                    process_selection_changed();
                    on_property_changed(nameof(is_selected));
                }
            }
            else if (pd.select_behavior == select_behavior.select_parent && property_parent != null)
            {
                property_parent.is_selected = value;
            }
        }
    }

    public override object? value
    {
        get => is_multiple_values || !is_valid || property_owners.Count == 0
            ? null
            : get_descriptor_value(property_owners[0], descriptors[0]);
        set
        {
            if (owner_property_grid != null &&
                owner_property_grid.selected_properties.Count > 1 &&
                !owner_property_grid.setting_values)
            {
                owner_property_grid.set_properties_values(value);
                process_important_property();
            }
            else
            {
                object? old_value = this.value;
                for (int i = 0; i < property_owners.Count; ++i)
                    set_descriptor_value(property_owners[i], descriptors[i], value);

                is_multiple_values = false;
                on_property_changed(nameof(value));
                on_property_changed(nameof(is_default_value));
                owner_property_grid?.on_property_changed(this, old_value);
                if (owner_property_grid == null || !owner_property_grid.setting_values)
                    process_important_property();
            }
        }
    }

    private void process_selection_changed()
    {
        foreach (var desc in descriptors.OfType<property_descriptor>())
            desc.raise_selection_changed(m_is_selected);
    }

    private void process_important_property()
    {
        if (descriptors.Count > 0 &&
            descriptor.Attributes[typeof(refresh_grid_on_change_attribute)] is refresh_grid_on_change_attribute &&
            owner_property_grid != null)
        {
            owner_property_grid.on_refresh_property_changed();
            owner_property_grid.update();
        }
    }
}
