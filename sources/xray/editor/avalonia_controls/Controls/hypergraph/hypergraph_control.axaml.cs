using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.property_editors;

namespace xray.editor.avalonia_controls.hypergraph;

public partial class hypergraph_control : UserControl
{
    public hypergraph_control()
    {
        InitializeComponent();

        m_field.hypergraph = this;
        m_scale_transform = new ScaleTransform(1, 1);
        m_field.RenderTransform = m_scale_transform;

        m_new_link_line = new LineGeometry { StartPoint = new Point(0, 0), EndPoint = new Point(100, 100) };
        m_new_link_path = new Path
        {
            Data = m_new_link_line,
            Stroke = SdkWorkspaceTheme.Accent,
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 4, 2 },
            ZIndex = 9000000,
            IsHitTestVisible = false
        };

        m_selection_rect = new RectangleGeometry { Rect = new Rect(20, 20, 50, 50) };
        m_selection_path = new Path
        {
            Data = m_selection_rect,
            Stroke = SdkWorkspaceTheme.Accent,
            StrokeThickness = 1,
            StrokeDashArray = new AvaloniaList<double> { 3, 2 },
            ZIndex = 9000000,
            IsHitTestVisible = false
        };

        m_link_cancel_icon = new Path
        {
            IsHitTestVisible = false,
            Stroke = SdkWorkspaceTheme.Warning,
            StrokeThickness = 4,
            StrokeLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M -10,-10 L 10,10 M -10,10 L 10,-10")
        };

        m_link_apply_icon = new Path
        {
            IsHitTestVisible = false,
            Stroke = SdkWorkspaceTheme.Accent,
            StrokeThickness = 4,
            StrokeLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M -10,-10 L 0,0 L 10,-20")
        };

        m_link_icon_container = new Canvas { ZIndex = 9000001, IsHitTestVisible = false };
        m_link_icon_container.Children.Add(m_link_cancel_icon);

        is_nodes_moveable = true;
        is_panable = true;
        is_zoomable = true;
        is_linkable = true;
        is_selectable = true;

        m_item_editor_selector = new item_editor_selector(default_editors_lists.item_editors);
        m_value_editor_selector = new value_editor_selector(default_editors_lists.value_editors);

        left_limit = double.MinValue;
        top_limit = double.MinValue;
        right_limit = double.MaxValue;
        bottom_limit = double.MaxValue;

        PointerPressed += mouse_button_down;
        AddHandler(PointerPressedEvent, preview_mouse_button_down, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, preview_mouse_button_up, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, preview_mouse_move, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, preview_key_down, RoutingStrategies.Tunnel);
        LayoutUpdated += (_, _) => flush_invalidated_nodes();
    }

    public event EventHandler<menu_event_args>? menu_item_click;
    public event Action? context_menu_opening;
    public event EventHandler<selection_event_args>? selecting;
    public event EventHandler<selection_event_args>? deleting_selected;
    public event EventHandler<node_event_args>? node_creating;
    public event EventHandler<link_event_args>? link_creating;
    public event EventHandler<links_event_args>? links_enabling;
    public event EventHandler<links_event_args>? links_disabling;
    public event EventHandler<can_link_event_args>? can_link;
    public event EventHandler<node_move_event_args>? node_moved;

    private const double c_epsilon = 0.0001;
    private readonly Dictionary<string, node> m_nodes = new();
    private readonly HashSet<node> m_selected_nodes = new();
    private readonly Dictionary<string, link> m_links = new();
    private readonly HashSet<link> m_selected_links = new();
    private readonly List<string> m_selecting_nodes = new();
    private readonly List<link_id> m_selecting_links = new();
    private readonly HashSet<node> m_nodes_to_invalidate = new();

    private readonly ScaleTransform m_scale_transform;
    private readonly LineGeometry m_new_link_line;
    private readonly Path m_new_link_path;
    private readonly RectangleGeometry m_selection_rect;
    private readonly Path m_selection_path;
    private readonly Path m_link_cancel_icon;
    private readonly Path m_link_apply_icon;
    private readonly Canvas m_link_icon_container;

    private Point m_scale = new(1, 1);
    private int m_top_z_index;

    private readonly item_editor_selector m_item_editor_selector;
    private readonly value_editor_selector m_value_editor_selector;

    private bool m_pan_started;
    private bool m_zoom_started;
    private bool m_select_started;
    private bool m_nodes_move_started;

    private Point m_mouse_position;
    private Point m_mouse_start_position;

    private bool m_new_link_started;
    private link_point? m_link_start_point;
    private bool m_is_apply_link_icon_visible;

    public ICollection<node> selected_nodes => m_selected_nodes;
    public ICollection<link> selected_links => m_selected_links;

    public Point viewport_position
    {
        get => (Point)m_field_scroller.offset;
        set => m_field_scroller.offset = (Vector)value;
    }

    public Point viewport_scale
    {
        get => m_scale;
        set
        {
            double sx = Math.Max(c_epsilon, value.X);
            double sy = Math.Max(c_epsilon, value.Y);
            m_scale = new Point(sx, sy);
            m_scale_transform.ScaleX = sx;
            m_scale_transform.ScaleY = sy;
        }
    }

    public Vector viewport_origin
    {
        get => m_field_scroller.origin;
        set => m_field_scroller.origin = value;
    }

    public field field => m_field;
    public i_command_engine? command_engine { get; set; }
    public item_editor_selector item_editor_selector => m_item_editor_selector;
    public value_editor_selector value_editor_selector => m_value_editor_selector;

    public bool need_expand_to_field { get; set; }
    public pan_direction pan_direction { get; set; }

    public bool is_grid_enabled
    {
        get => m_field.is_grid_enabled;
        set => m_field.is_grid_enabled = value;
    }

    public bool is_nodes_moveable { get; set; }
    public bool is_panable { get; set; }
    public bool is_zoomable { get; set; }
    public bool is_linkable { get; set; }
    public bool is_selectable { get; set; }

    public bool is_fix_min_field_size
    {
        get => m_field.is_fix_min_size_by_parent;
        set
        {
            m_field.is_fix_min_size_by_parent = value;
            InvalidateVisual();
        }
    }

    public Size field_bounds_inflate
    {
        get => m_field.bounds_infalte_amount;
        set
        {
            m_field.bounds_infalte_amount = value;
            InvalidateVisual();
        }
    }

    public double left_limit { get; set; }
    public double top_limit { get; set; }
    public double right_limit { get; set; }
    public double bottom_limit { get; set; }

    public bool remove_menu_item_enabled
    {
        get => cm_remove.IsVisible;
        set => cm_remove.IsVisible = value;
    }

    public bool enable_disable_link_menu_item_enabled
    {
        get => cm_enable_disable_link.IsVisible;
        set => cm_enable_disable_link.IsVisible = value;
    }

    public Dictionary<string, node> nodes => m_nodes;
    public Dictionary<string, link> links => m_links;

    private void field_scroller_size_changed(object? sender, SizeChangedEventArgs e) => m_field.InvalidateMeasure();

    private void mouse_button_down(object? sender, PointerPressedEventArgs e)
    {
        if (is_selectable && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            m_select_started = true;
            m_mouse_position = e.GetPosition(m_field);
            m_mouse_start_position = m_mouse_position;
            e.Pointer.Capture(this);
            m_selection_rect.Rect = new Rect(m_mouse_start_position, new Size(1, 1));
            if (m_selection_path.Parent == null)
                m_field.Children.Add(m_selection_path);
            e.Handled = true;
        }
    }

    private void preview_mouse_button_down(object? sender, PointerPressedEventArgs e)
    {
        m_mouse_position = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (is_panable && props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_pan_started = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
        else if (is_zoomable && props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_zoom_started = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
        else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            m_mouse_position = e.GetPosition(m_field);
            var rect = new Rect(m_mouse_position, new Size(1, 1));
            m_selection_rect.Rect = rect;
            collect_hits(rect);

            bool need_raise = m_selecting_nodes.Select(nid => m_nodes[nid]).Any(n => !m_selected_nodes.Contains(n));
            if (need_raise)
                on_node_selecting(new selection_event_args(m_selecting_nodes.ToList(), m_selecting_links.ToList()), e.KeyModifiers);
        }
    }

    private void preview_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_pan_started)
        {
            var new_mouse_position = e.GetPosition(this);
            var delta = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;

            double dx = pan_direction == pan_direction.vertical ? 0 : delta.X;
            double dy = pan_direction == pan_direction.horizontal ? 0 : delta.Y;

            viewport_position = new Point(viewport_position.X - dx, viewport_position.Y - dy);
            e.Handled = true;
            return;
        }

        if (m_zoom_started)
        {
            var new_mouse_position = e.GetPosition(this);
            var delta = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;

            var mouse_start = (Vector)e.GetPosition(m_field);
            double factor = delta.X / 100.0;
            viewport_scale = new Point(viewport_scale.X + factor, viewport_scale.Y + factor);

            var mouse_end = new Vector(mouse_start.X * (1 + factor), mouse_start.Y * (1 + factor));
            viewport_position += mouse_end - mouse_start;
            e.Handled = true;
            return;
        }

        if (m_new_link_started && m_link_start_point != null)
        {
            var pt = e.GetPosition(m_field);
            m_new_link_line.EndPoint = pt;
            Canvas.SetLeft(m_link_icon_container, pt.X);
            Canvas.SetTop(m_link_icon_container, pt.Y);

            var place = find_link_point_at(pt);
            if (place != null && can_link_impl(m_link_start_point, place))
                show_link_apply();
            else
                show_link_cancel();

            e.Handled = true;
            return;
        }

        if (m_select_started)
        {
            var new_pos = e.GetPosition(m_field);
            m_selection_rect.Rect = new Rect(
                Math.Min(m_mouse_start_position.X, new_pos.X),
                Math.Min(m_mouse_start_position.Y, new_pos.Y),
                Math.Abs(new_pos.X - m_mouse_start_position.X),
                Math.Abs(new_pos.Y - m_mouse_start_position.Y));
            e.Handled = true;
            return;
        }

        if (m_nodes_move_started)
        {
            var new_mouse_position = e.GetPosition(this);
            var mouse_offset = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;
            move_selected_nodes(mouse_offset);
            e.Handled = true;
        }
    }

    private void preview_mouse_button_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_pan_started || m_zoom_started)
        {
            m_pan_started = false;
            m_zoom_started = false;
            e.Pointer.Capture(null);
            Cursor = null;
            e.Handled = true;
        }

        if (m_new_link_started && m_link_start_point != null)
        {
            m_new_link_started = false;
            e.Pointer.Capture(null);
            m_field.Children.Remove(m_new_link_path);
            m_field.Children.Remove(m_link_icon_container);

            var destination = find_link_point_at(e.GetPosition(m_field));
            if (destination != null)
            {
                destination.refresh_field_position();
                var source = m_link_start_point;
                if (can_link_impl(source, destination) && destination.node != null && source.node != null)
                {
                    on_link_creating(new link_event_args(
                        new link_id(source.node.id, source.id, destination.node.id, destination.id),
                        source,
                        destination));
                }
            }
            e.Handled = true;
        }

        if (m_select_started)
        {
            m_select_started = false;
            e.Pointer.Capture(null);
            m_field.Children.Remove(m_selection_path);

            var new_pos = e.GetPosition(m_field);
            var rect = new Rect(
                Math.Min(m_mouse_start_position.X, new_pos.X),
                Math.Min(m_mouse_start_position.Y, new_pos.Y),
                Math.Max(1, Math.Abs(new_pos.X - m_mouse_start_position.X)),
                Math.Max(1, Math.Abs(new_pos.Y - m_mouse_start_position.Y)));
            m_selection_rect.Rect = rect;

            collect_hits(rect);
            on_node_selecting(new selection_event_args(m_selecting_nodes.ToList(), m_selecting_links.ToList()), e.KeyModifiers);
            e.Handled = true;
        }

        if (m_nodes_move_started)
        {
            m_nodes_move_started = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void preview_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            e.Handled = true;
            m_field_scroller.offset = new Vector(0, 0);
            viewport_scale = new Point(1, 1);
            m_field_scroller.InvalidateArrange();
            m_field.InvalidateArrange();
            m_field.InvalidateVisual();
        }
    }

    private void show_link_apply()
    {
        if (!m_is_apply_link_icon_visible)
        {
            m_link_icon_container.Children.Remove(m_link_cancel_icon);
            m_link_icon_container.Children.Add(m_link_apply_icon);
            m_is_apply_link_icon_visible = true;
        }
    }

    private void show_link_cancel()
    {
        if (m_is_apply_link_icon_visible)
        {
            m_link_icon_container.Children.Remove(m_link_apply_icon);
            m_link_icon_container.Children.Add(m_link_cancel_icon);
            m_is_apply_link_icon_visible = false;
        }
    }

    private void context_menu_opened(object? sender, CancelEventArgs e)
    {
        context_menu_opening?.Invoke();
        cm_remove.IsEnabled = m_selected_links.Count != 0 || m_selected_nodes.Count != 0;
        cm_enable_disable_link.IsEnabled = m_selected_links.Count != 0;
        cm_enable_disable_link.Header = m_selected_links.Any(l => l.is_enabled) ? "Disable Link" : "Enable Link";
    }

    private void context_menu_remove_click(object? sender, RoutedEventArgs e) => cmd_delete_selected();

    private void enable_disable_link_click(object? sender, RoutedEventArgs e)
    {
        if (!m_selected_links.Any(l => l.is_enabled))
            on_links_enabling(new links_event_args(m_selected_links.Select(l => l.id.id).ToList()));
        else
            on_links_disabling(new links_event_args(m_selected_links.Select(l => l.id.id).ToList()));
    }

    private link_point? find_link_point_at(Point field_pos)
    {
        foreach (var vis in m_field.GetVisualsAt(field_pos))
        {
            var lp = (vis as Visual)?.FindAncestorOfType<link_point>(includeSelf: true);
            if (lp != null)
                return lp;
        }
        return null;
    }

    private void collect_hits(Rect rect)
    {
        m_selecting_links.Clear();
        m_selecting_nodes.Clear();

        foreach (var pair in m_links)
        {
            if (pair.Value.hits_rect(rect))
                m_selecting_links.Add(pair.Value.id);
        }

        foreach (var pair in m_nodes)
        {
            var n = pair.Value;
            var node_rect = new Rect(n.position, n.Bounds.Size);
            if (rect.Intersects(node_rect))
                m_selecting_nodes.Add(n.id);
        }
    }

    private void context_item_click(object? sender, RoutedEventArgs e)
    {
        if (menu_item_click == null || sender is not MenuItem item)
            return;
        menu_item_click(this, new menu_event_args(item, item.Tag?.ToString() ?? string.Empty, item.Header?.ToString() ?? string.Empty));
    }

    private void add_link(link link)
    {
        link.hypergraph = this;
        m_links[link.id.id] = link;
        m_field.Children.Add(link);
    }

    private void remove_link(link link)
    {
        m_links.Remove(link.id.id);
        m_field.Children.Remove(link);
    }

    private void add_node(node node)
    {
        node.hypergraph = this;
        node.ZIndex = m_top_z_index++;
        m_nodes[node.id] = node;
        m_field.Children.Add(node);
    }

    private void remove_node(node node)
    {
        m_selected_nodes.Remove(node);
        node.destruct();
        m_nodes.Remove(node.id);
        m_field.Children.Remove(node);
    }

    private bool can_link_impl(link_point first, link_point second)
    {
        if (can_link != null)
        {
            var args = new can_link_event_args(first, second);
            can_link(this, args);
            return args.can_link;
        }
        return true;
    }

    private link link(link_point source, link_point destination) =>
        link(new link_id(source.node!.id, source.id, destination.node!.id, destination.id), source, destination);

    private link link(link_id link_id, link_point source, link_point destination)
    {
        source.refresh_field_position();
        destination.refresh_field_position();
        var new_link = new link(source, destination) { id = link_id };
        source.add_link(new_link);
        destination.add_link(new_link);
        add_link(new_link);
        return new_link;
    }

    private void unlink(link link)
    {
        link.source?.remove_link(link);
        link.destination?.remove_link(link);
        remove_link(link);
    }

    private void on_node_selecting_impl(List<string> selecting_nodes, List<link_id> selecting_links, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            foreach (var nid in selecting_nodes)
            {
                var n = get_node(nid);
                if (n.is_selected) deselect_node(nid);
                else select_node(nid);
            }
            foreach (var lid in selecting_links)
            {
                var lnk = get_link(lid);
                if (lnk.is_selected) deselect_link(lid);
                else select_link(lid);
            }
        }
        else
        {
            if (selecting_nodes.Count == 1 && m_selected_nodes.Contains(get_node(selecting_nodes[0])))
                return;

            deselect_all_nodes();
            deselect_all_links();
            foreach (var nid in selecting_nodes) select_node(nid);
            foreach (var lid in selecting_links) select_link(lid);
        }
    }

    protected void on_node_selecting(selection_event_args e, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (selecting != null)
            selecting(this, e);
        else
            on_node_selecting_impl(e.selected_node_ids, e.selected_link_ids, modifiers);
    }

    protected void on_deleting_selected(selection_event_args e)
    {
        if (deleting_selected != null)
        {
            deleting_selected(this, e);
            m_selected_nodes.Clear();
            m_selected_links.Clear();
        }
    }

    protected void on_node_creating(node_event_args e) => node_creating?.Invoke(this, e);
    protected void on_link_creating(link_event_args e) => link_creating?.Invoke(this, e);
    protected void on_links_enabling(links_event_args e) => links_enabling?.Invoke(this, e);
    protected void on_links_disabling(links_event_args e) => links_disabling?.Invoke(this, e);

    internal void on_node_moved(Vector offset)
    {
        if (node_moved != null)
        {
            var lst = m_selected_nodes.Select(n => new node_move_args(n, offset)).ToList();
            node_moved(this, new node_move_event_args(lst));
        }
    }

    internal void move_selected_nodes(Vector offset)
    {
        foreach (var node in m_selected_nodes)
        {
            if (!node.is_movable)
                return;

            Point pos = node.position - offset;
            if (pos.X < left_limit) pos = new Point(left_limit, pos.Y);
            if (pos.Y < top_limit) pos = new Point(pos.X, top_limit);
            if (pos.X + node.Bounds.Width > right_limit) pos = new Point(right_limit - node.Bounds.Width, pos.Y);
            if (pos.Y + node.Bounds.Height > bottom_limit) pos = new Point(pos.X, bottom_limit - node.Bounds.Height);

            node.position = pos;
            node.is_node_moved = true;
        }
    }

    private void flush_invalidated_nodes()
    {
        if (m_nodes_to_invalidate.Count == 0)
            return;

        var snapshot = m_nodes_to_invalidate.ToArray();
        m_nodes_to_invalidate.Clear();
        foreach (var n in snapshot)
            n.update_link_points_positions();
    }

    internal void start_nodes_moveing(PointerPressedEventArgs e)
    {
        m_mouse_position = e.GetPosition(this);
        m_nodes_move_started = true;
        e.Pointer.Capture(this);
    }

    internal void start_link_creating(link_point point, PointerPressedEventArgs e)
    {
        if (!is_linkable)
            return;

        point.refresh_field_position();
        m_link_start_point = point;
        m_new_link_started = true;
        if (m_new_link_path.Parent == null) m_field.Children.Add(m_new_link_path);
        if (m_link_icon_container.Parent == null) m_field.Children.Add(m_link_icon_container);
        m_new_link_line.StartPoint = point.connection_point;
        e.Pointer.Capture(this);
    }

    internal void fire_node_selecting(selection_event_args e, KeyModifiers modifiers = KeyModifiers.None) =>
        on_node_selecting(e, modifiers);

    internal link_point get_link_point(string node_id, string link_id) => m_nodes[node_id].get_link_point(link_id);

    internal void recalculate_field(node n)
    {
        m_nodes_to_invalidate.Add(n);
        m_field.InvalidateMeasure();
    }

    internal void select_link_impl(link link) => m_selected_links.Add(link);
    internal void deselect_link_impl(link link) => m_selected_links.Remove(link);

    public void deselect_all_links()
    {
        foreach (var l in m_selected_links.ToArray())
            l.is_selected = false;
        m_selected_links.Clear();
    }

    public void deselect_all_nodes()
    {
        foreach (var n in m_selected_nodes.ToArray())
            n.is_selected = false;
        m_selected_nodes.Clear();
    }

    public void cmd_delete_selected() =>
        on_deleting_selected(new selection_event_args(
            m_selected_nodes.Select(n => n.id).ToList(),
            m_selected_links.Select(l => l.id).ToList()));

    public void cmd_enable_selected_links() =>
        on_links_enabling(new links_event_args(
            m_selected_links.Where(l => !l.is_enabled).Select(l => l.id.id).ToList()));

    public void cmd_disable_selected_links() =>
        on_links_disabling(new links_event_args(
            m_selected_links.Where(l => l.is_enabled).Select(l => l.id.id).ToList()));

    public MenuItem add_context_menu_item(string item_name)
    {
        var path_parts = item_name.Split('/');
        var items = m_context_menu.Items;
        MenuItem? item = null;

        foreach (var part in path_parts)
        {
            item = items.OfType<MenuItem>().FirstOrDefault(itm => itm.Header?.ToString() == part);
            if (item == null)
            {
                item = new MenuItem { Header = part };
                items.Add(item);
            }
            items = item.Items;
        }

        Debug.Assert(item != null);
        item.Tag = item_name;
        item.Click += context_item_click;
        return item;
    }

    public node create_node(string id)
    {
        var n = new node(this) { id = id };
        add_node(n);
        n.position = this.TranslatePoint(m_mouse_position, m_field) ?? default;
        return n;
    }

    public properties_node create_properties_node(string id)
    {
        var n = new properties_node(this) { id = id };
        add_node(n);
        n.position = this.TranslatePoint(m_mouse_position, m_field) ?? default;
        return n;
    }

    public properties_node create_node(string id, object source)
    {
        var n = new properties_node(this, source) { id = id };
        add_node(n);
        n.position = this.TranslatePoint(m_mouse_position, m_field) ?? default;
        return n;
    }

    public properties_node create_node(string id, object source, Point position)
    {
        var n = new properties_node(this, source) { id = id };
        add_node(n);
        n.position = position;
        return n;
    }

    public void remove_node(string node_id) => remove_node(m_nodes[node_id]);

    public void remove_nodes(IEnumerable<string> node_ids)
    {
        foreach (var node_id in node_ids)
            remove_node(m_nodes[node_id]);
    }

    public link create_link(string output_node_id, string input_node_id, string output_link_id, string input_link_id) =>
        link(get_link_point(output_node_id, output_link_id), get_link_point(input_node_id, input_link_id));

    public link create_link(link_id link_id) =>
        link(link_id, get_link_point(link_id.output_node_id, link_id.output_link_point_id), get_link_point(link_id.input_node_id, link_id.input_link_point_id));

    public link create_link(property_descriptor descr, properties_node properties_node)
    {
        var lp = ((property)descr.owner_property!).property_view!.output_link_point!;
        var lid = new link_id(lp.node!.id, lp.id, properties_node.id, "input");
        return link(lid, get_link_point(lid.output_node_id, lid.output_link_point_id), get_link_point(lid.input_node_id, lid.input_link_point_id));
    }

    public void remove_link(link_id link_id) => unlink(m_links[link_id.id]);

    public void remove_link(string output_node_id, string input_node_id, string output_link_id, string input_link_id) =>
        unlink(m_links[new link_id(output_node_id, output_link_id, input_node_id, input_link_id).id]);

    public void set_selected_nodes(List<string>? nodes_ids)
    {
        foreach (var n in m_selected_nodes.ToArray())
            deselect_node(n.id);

        if (nodes_ids == null)
            return;

        foreach (var nid in nodes_ids)
            select_node(nid);
    }

    public void set_selected_nodes(List<node>? selecting_nodes)
    {
        foreach (var n in m_selected_nodes.ToArray())
            deselect_node(n.id);

        if (selecting_nodes == null)
            return;

        foreach (var n in selecting_nodes)
            select_node(n.id);
    }

    public void select_node(string node_id)
    {
        if (!m_nodes[node_id].is_selectable)
            return;
        m_selected_nodes.Add(m_nodes[node_id]);
        m_nodes[node_id].ZIndex = ++m_top_z_index;
        m_nodes[node_id].is_selected = true;
    }

    public void deselect_node(string node_id)
    {
        m_selected_nodes.Remove(m_nodes[node_id]);
        m_nodes[node_id].is_selected = false;
    }

    public void select_link(link_id link_id) => m_links[link_id.id].is_selected = true;
    public void deselect_link(link_id link_id) => m_links[link_id.id].is_selected = false;

    public void select_all_links()
    {
        foreach (var pair in m_links)
            pair.Value.is_selected = true;
    }

    public void select_all_nodes()
    {
        foreach (var pair in m_nodes)
            pair.Value.is_selected = true;
    }

    public node get_node(string node_id) => m_nodes[node_id];
    public link get_link(link_id link_id) => m_links[link_id.id];
}
