using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace xray.editor.avalonia_controls.control_containers;

public abstract class control
{
    protected control(string name)
    {
        this.name = name;
        width = double.NaN;
        height = double.NaN;
    }

    public string name;
    public object? tag;
    public Control? wpf_control;
    public double width;
    public double height;

    public abstract Control generate_control();

    public bool same_as(control other_control) =>
        other_control.name == name && other_control.GetType() == GetType();

    public abstract void merge_to(Control ui_element);
}

public class button : control
{
    public button(string name) : base(name) { }

    public object? content { get; set; }

    public bool is_enabled
    {
        get => wpf_control?.IsEnabled ?? true;
        set
        {
            if (wpf_control != null)
                wpf_control.IsEnabled = value;
        }
    }

    public event Action<button>? click;

    public override Control generate_control()
    {
        var btn = new Button { Content = content };
        if (!double.IsNaN(width))
            btn.Width = width;
        if (!double.IsNaN(height))
            btn.Height = height;
        wpf_control = btn;
        merge_to(btn);
        return btn;
    }

    public override void merge_to(Control ui_element)
    {
        wpf_control = ui_element;
        ((Button)ui_element).Click += on_click;
    }

    internal void on_click(object? sender, RoutedEventArgs e) => click?.Invoke(this);
}

public class toggle_button : control
{
    public toggle_button(string name) : base(name) { }

    public object? content { get; set; }
    public bool? state;

    public event Action<toggle_button>? toggle;

    internal void on_toggle(object? sender, RoutedEventArgs e)
    {
        state = ((ToggleButton)sender!).IsChecked;
        toggle?.Invoke(this);
    }

    public override Control generate_control()
    {
        var btn = new ToggleButton { Content = content, IsChecked = state };
        if (!double.IsNaN(width))
            btn.Width = width;
        if (!double.IsNaN(height))
            btn.Height = height;
        wpf_control = btn;
        merge_to(btn);
        return btn;
    }

    public override void merge_to(Control ui_element)
    {
        wpf_control = ui_element;
        var tb = (ToggleButton)ui_element;
        tb.IsCheckedChanged += on_toggle;
    }
}

public abstract class control_container
{
    protected control_container(property_descriptor descriptor)
    {
        m_descriptor = descriptor;
        controls = new Dictionary<string, control>();
    }

    private readonly property_descriptor m_descriptor;
    protected readonly Dictionary<string, control> controls;

    public string category
    {
        get => m_descriptor.category;
        set => m_descriptor.category = value;
    }

    public string description
    {
        get => m_descriptor.description;
        set => m_descriptor.description = value;
    }

    public button add_button(string caption, Action<button> click_callback)
    {
        var btn = new button(caption) { content = caption };
        btn.click += click_callback;
        controls.Add(btn.name, btn);
        return btn;
    }

    public toggle_button add_toggle_button(string caption, Action<toggle_button> click_callback)
    {
        var btn = new toggle_button(caption) { content = caption };
        btn.toggle += click_callback;
        controls.Add(btn.name, btn);
        return btn;
    }

    protected internal abstract Panel generate_panel();

    public void merge_to(control_container other)
    {
        foreach (var pair in controls)
        {
            if (other.controls.TryGetValue(pair.Key, out var otherCtrl) &&
                otherCtrl.GetType() == pair.Value.GetType() &&
                otherCtrl.wpf_control != null)
            {
                pair.Value.merge_to(otherCtrl.wpf_control);
            }
        }
    }
}

public class dock_control_container : control_container
{
    public bool is_dock_left;

    public dock_control_container(property_descriptor descriptor) : base(descriptor) { }

    protected internal override Panel generate_panel()
    {
        var panel = new DockPanel { LastChildFill = false };
        foreach (var c in controls)
        {
            var ui = c.Value.generate_control();
            DockPanel.SetDock(ui, is_dock_left ? global::Avalonia.Controls.Dock.Left : global::Avalonia.Controls.Dock.Right);
            panel.Children.Add(ui);
        }
        return panel;
    }
}

public class stack_control_container : control_container
{
    public stack_control_container(property_descriptor descriptor) : base(descriptor) { }

    protected internal override Panel generate_panel()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in controls)
            panel.Children.Add(c.Value.generate_control());
        return panel;
    }
}

public class uniform_control_container : control_container
{
    public uniform_control_container(property_descriptor descriptor) : base(descriptor) { }

    protected internal override Panel generate_panel()
    {
        var grid = new UniformGrid { Columns = controls.Count, Rows = 1 };
        foreach (var c in controls)
            grid.Children.Add(c.Value.generate_control());
        return grid;
    }
}
