using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace xray.editor.avalonia_controls.property_grid;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class refresh_grid_on_change_attribute : Attribute
{
}

public class value_changed_event_args : EventArgs
{
    public value_changed_event_args(property prop, object? old_value)
    {
        changed_property = prop;
        this.old_value = old_value;
    }

    public property changed_property { get; }
    public object? old_value { get; }
    public object? new_value => changed_property.value;
}

public delegate void value_changed_event_handler(object sender, value_changed_event_args e);

internal class hierarchy_node
{
    public string name { get; set; } = string.Empty;
    public readonly List<hierarchy_node> sub_nodes = new();

    public override string ToString() => name;
}

public sealed class property_category_group : INotifyPropertyChanged
{
    private bool m_is_expanded = true;

    public property_category_group(string name)
    {
        Name = name;
        Items = new ObservableCollection<property_grid_item>();
    }

    public string Name { get; }
    public ObservableCollection<property_grid_item> Items { get; }

    public bool IsExpanded
    {
        get => m_is_expanded;
        set
        {
            if (m_is_expanded == value)
                return;
            m_is_expanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
