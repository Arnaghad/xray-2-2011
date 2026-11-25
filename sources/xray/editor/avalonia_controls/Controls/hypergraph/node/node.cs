using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.hypergraph;

public class node : ContentControl
{
    public static readonly StyledProperty<bool> is_selectedProperty =
        AvaloniaProperty.Register<node, bool>(nameof(is_selected));

    public static readonly AttachedProperty<node?> parent_nodeProperty =
        AvaloniaProperty.RegisterAttached<node, AvaloniaObject, node?>("parent_node", default, inherits: true);

    protected override Type StyleKeyOverride => typeof(node);

    public node()
    {
        m_link_points = new Dictionary<string, link_point>();
        SizeChanged += node_size_changed;
        AddHandler(PointerPressedEvent, node_mouse_down, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, node_mouse_up, RoutingStrategies.Tunnel);
        set_parent_node(this, this);
    }

    public node(hypergraph_control hypergraph) : this()
    {
        this.hypergraph = hypergraph;
    }

    private string? m_id;
    private readonly Dictionary<string, link_point> m_link_points;
    private bool m_is_node_moved;

    public string id
    {
        get => m_id ?? string.Empty;
        set
        {
            if (value == m_id)
                return;

            if (m_id == null)
            {
                m_id = value;
                return;
            }

            if (hypergraph != null && hypergraph.nodes.ContainsKey(m_id))
                hypergraph.nodes.Remove(m_id);

            m_id = value;
            if (hypergraph != null)
                hypergraph.nodes[m_id] = this;
        }
    }

    public hypergraph_control? hypergraph { get; set; }

    public bool is_selected
    {
        get => GetValue(is_selectedProperty);
        set => SetValue(is_selectedProperty, value);
    }

    public Point position
    {
        get
        {
            double x = Canvas.GetLeft(this);
            double y = Canvas.GetTop(this);
            return new Point(double.IsNaN(x) ? 0 : x, double.IsNaN(y) ? 0 : y);
        }
        set
        {
            Canvas.SetLeft(this, value.X);
            Canvas.SetTop(this, value.Y);
            hypergraph?.recalculate_field(this);
            update_link_points_positions();
        }
    }

    public bool is_movable { get; set; } = true;
    public bool is_selectable { get; set; } = true;

    public bool is_node_moved
    {
        get => m_is_node_moved;
        set => m_is_node_moved = value;
    }

    public Func<bool>? is_header_captured_callback;

    private void node_size_changed(object? sender, SizeChangedEventArgs e)
    {
        hypergraph?.recalculate_field(this);
        update_link_points_positions();
    }

    private void node_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (hypergraph != null && hypergraph.is_nodes_moveable && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (is_header_captured_callback != null && is_header_captured_callback())
            {
                hypergraph.start_nodes_moveing(e);
                m_is_node_moved = false;
                e.Handled = true;
            }
        }
    }

    private void node_mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (!m_is_node_moved && is_selectable && hypergraph != null)
            hypergraph.fire_node_selecting(new selection_event_args(new List<string> { id }, new List<link_id>()), e.KeyModifiers);
    }

    internal void update_link_points_positions()
    {
        if (hypergraph == null)
            return;

        foreach (var lp in m_link_points.Values)
            lp.update_field_position();
    }

    internal void destruct()
    {
        if (m_link_points.Values.Any(lp => lp.is_connected))
            throw new InvalidOperationException("Removing node must contains no linked link points. Unlink node before removing");
    }

    public void add_link_point(string link_id, link_point link_point) => m_link_points[link_id] = link_point;
    public link_point get_link_point(string link_id) => m_link_points[link_id];
    public void remove_link_point(string link_id) => m_link_points.Remove(link_id);

    public static node? get_parent_node(AvaloniaObject obj) => obj.GetValue(parent_nodeProperty);
    public static void set_parent_node(AvaloniaObject obj, node? value) => obj.SetValue(parent_nodeProperty, value);
}
