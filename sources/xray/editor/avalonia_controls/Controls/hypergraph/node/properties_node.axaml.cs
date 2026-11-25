using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls.hypergraph;

public partial class properties_node : node, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public static readonly StyledProperty<bool> has_expandedProperty =
        AvaloniaProperty.Register<properties_node, bool>(nameof(has_expanded));

    public static readonly StyledProperty<bool> is_expandedProperty =
        AvaloniaProperty.Register<properties_node, bool>(nameof(is_expanded), true);

    public properties_node()
    {
        InitializeComponent();
        type_id = "node";

        m_expand_toggle.IsCheckedChanged += (_, _) =>
        {
            is_expanded = m_expand_toggle.IsChecked == true;
        };

        m_node_border.PointerEntered += (_, _) => { m_is_hovered = true; update_border_brush(); };
        m_node_border.PointerExited += (_, _) => { m_is_hovered = false; update_border_brush(); };

        update_border_brush();
    }

    internal properties_node(hypergraph_control hypergraph) : this()
    {
        this.hypergraph = hypergraph;
    }

    internal properties_node(hypergraph_control hypergraph, object source) : this(hypergraph)
    {
        source_object = source;
    }

    private bool m_is_node_captured;
    private Vector m_capture_mouse_offset;
    private Point m_last_node_position;
    private string m_name = string.Empty;
    private bool m_is_hovered;

    private IBrush m_normal_border = SdkWorkspaceTheme.Axis;
    private IBrush m_selected_border = SdkWorkspaceTheme.Accent;
    private IBrush m_highlighted_border = SdkWorkspaceTheme.MutedText;

    public object? source_object
    {
        get => m_property_container.source_object;
        set => m_property_container.source_object = value;
    }

    public bool is_expanded
    {
        get => GetValue(is_expandedProperty);
        set => SetValue(is_expandedProperty, value);
    }

    public bool has_expanded
    {
        get => GetValue(has_expandedProperty);
        set => SetValue(has_expandedProperty, value);
    }

    public string type_id { get; set; } = "node";

    public string name
    {
        get => m_name;
        set
        {
            m_name = value;
            m_name_text.Text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(name)));
        }
    }

    public link_point input_link_point => m_input_link_point;
    public link_point output_link_point => m_output_link_point;

    public bool input_link_place_enabled
    {
        get => m_input_link_point.IsVisible;
        set => m_input_link_point.IsVisible = value;
    }

    public bool output_link_place_enabled
    {
        get => m_output_link_point.IsVisible;
        set => m_output_link_point.IsVisible = value;
    }

    public IBrush? background
    {
        get => m_node_border.Background;
        set => m_node_border.Background = value;
    }

    public IBrush border_normal
    {
        get => m_normal_border;
        set { m_normal_border = value; update_border_brush(); }
    }

    public IBrush border_selected
    {
        get => m_selected_border;
        set { m_selected_border = value; update_border_brush(); }
    }

    public IBrush border_highlighted
    {
        get => m_highlighted_border;
        set { m_highlighted_border = value; update_border_brush(); }
    }

    public double header_panel_height
    {
        get => m_header_panel.Height;
        set => m_header_panel.Height = value;
    }

    public double border_min_width
    {
        get => m_node_border.MinWidth;
        set => m_node_border.MinWidth = value;
    }

    public object? above_header_content
    {
        get => m_above_header_content.Content;
        set => m_above_header_content.Content = value;
    }

    public DockPanel content_panel => m_content_panel;

    private void update_border_brush()
    {
        if (is_selected)
            m_node_border.BorderBrush = m_selected_border;
        else if (m_is_hovered)
            m_node_border.BorderBrush = m_highlighted_border;
        else
            m_node_border.BorderBrush = m_normal_border;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == is_selectedProperty)
        {
            update_border_brush();
        }
        else if (change.Property == has_expandedProperty)
        {
            m_expand_toggle.IsVisible = has_expanded;
        }
        else if (change.Property == is_expandedProperty)
        {
            m_expand_toggle.IsChecked = is_expanded;
            m_body_border.IsVisible = is_expanded;
        }
        else if (change.Property == TagProperty)
        {
            input_link_point.tag = change.NewValue;
            output_link_point.tag = change.NewValue;
        }
    }

    private void header_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (hypergraph != null && hypergraph.is_nodes_moveable && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (!is_selected)
            {
                hypergraph.deselect_all_nodes();
                hypergraph.deselect_all_links();
                hypergraph.select_node(id);
            }
            m_last_node_position = position;
            m_is_node_captured = true;
            m_capture_mouse_offset = e.GetPosition(this);
            e.Pointer.Capture(m_header);
            e.Handled = true;
            is_node_moved = false;
        }
    }

    private void header_mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_is_node_captured && hypergraph != null && Parent is Control parentCtrl)
        {
            var parent_mouse_position = e.GetPosition(parentCtrl);
            var target = parent_mouse_position - m_capture_mouse_offset;
            Vector offset = position - target;
            if (offset == default)
                return;

            hypergraph.move_selected_nodes(offset);
        }
    }

    private void header_mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_is_node_captured && hypergraph != null)
        {
            Vector offset = new Vector(m_last_node_position.X - position.X, m_last_node_position.Y - position.Y);
            hypergraph.on_node_moved(offset);
            e.Pointer.Capture(null);
            m_is_node_captured = false;
        }
    }

    internal property_view get_property_view(string view_key) => m_property_container.property_views[view_key];

    public void remove()
    {
        if (hypergraph == null)
            return;

        for (var i = input_link_point.links.Count - 1; i >= 0; --i)
            hypergraph.remove_link(input_link_point.links[i].id);

        for (var i = output_link_point.links.Count - 1; i >= 0; --i)
            hypergraph.remove_link(output_link_point.links[i].id);

        foreach (var pv in m_property_container.property_views.Values)
        {
            if (pv.input_link_point != null)
            {
                for (var i = pv.input_link_point.links.Count - 1; i >= 0; --i)
                    hypergraph.remove_link(pv.input_link_point.links[i].id);
            }
            if (pv.output_link_point != null)
            {
                for (var i = pv.output_link_point.links.Count - 1; i >= 0; --i)
                    hypergraph.remove_link(pv.output_link_point.links[i].id);
            }
        }
    }

    public void update_properties() => m_property_container.update_properties();
}
