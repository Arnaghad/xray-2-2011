using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.curve_editor.templates;
using xray.editor.avalonia_controls.curve_editor.tools;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class curve_editor_panel : UserControl, INotifyPropertyChanged
{
    public curve_editor_panel()
    {
        InitializeComponent();

        m_curves_panel.parent_panel = this;

        m_move_tool = new move_tool(this);
        m_scale_tool = new scale_tool(this);
        m_move_range_tool = new move_range_tool(this);
        m_add_tool = new add_tool(this);
        m_remove_tool = new remove_tool(this);
        m_active_tool = m_move_tool;

        m_move_tool_button.IsCheckedChanged += (_, _) => { if (m_move_tool_button.IsChecked == true) set_active_tool(m_move_tool_button, m_move_tool); };
        m_scale_tool_button.IsCheckedChanged += (_, _) => { if (m_scale_tool_button.IsChecked == true) set_active_tool(m_scale_tool_button, m_scale_tool); };
        m_move_range_tool_button.IsCheckedChanged += (_, _) => { if (m_move_range_tool_button.IsChecked == true) set_active_tool(m_move_range_tool_button, m_move_range_tool); };
        m_add_tool_button.IsCheckedChanged += (_, _) => { if (m_add_tool_button.IsChecked == true) set_active_tool(m_add_tool_button, m_add_tool); };
        m_remove_tool_button.IsCheckedChanged += (_, _) => { if (m_remove_tool_button.IsChecked == true) set_active_tool(m_remove_tool_button, m_remove_tool); };

        m_curves_panel.PointerPressed += mouse_down;
        m_curves_panel.PointerMoved += mouse_move;
        m_curves_panel.PointerReleased += mouse_up;
        m_curves_panel.SizeChanged += size_changed;
        m_curves_scroll_bar.ValueChanged += scroll_bar_value_changed;

        AddHandler(PointerPressedEvent, preview_mouse_down, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, preview_key_down, RoutingStrategies.Tunnel);

        m_keys_x_position_text_box.KeyDown += key_x_position_text_box_key_down;
        m_keys_y_position_text_box.KeyDown += key_y_position_text_box_key_down;
        m_keys_x_position_text_box.LostFocus += key_x_position_text_box_focus_lost;
        m_keys_y_position_text_box.LostFocus += key_y_position_text_box_focus_lost;

        m_selection_rect = new RectangleGeometry();
        m_selection_path = new Path
        {
            Data = m_selection_rect,
            Stroke = SdkWorkspaceTheme.Accent,
            StrokeThickness = 1,
            StrokeDashArray = new AvaloniaList<double> { 4, 2 },
            Fill = new SolidColorBrush(Color.FromArgb(30, 0x17, 0x6B, 0x80)),
            IsHitTestVisible = false
        };

        m_remove_menu_item = new MenuItem { Header = "Remove Selected Keys" };
        m_remove_menu_item.Click += remove_menu_item_click;

        m_set_template_menu_item = new MenuItem { Header = "Set Template" };
        var sinusoid_item = new MenuItem { Header = "Sinusoid" };
        sinusoid_item.Click += set_sinusoid_template;
        var sqrt_item = new MenuItem { Header = "Sqrt" };
        sqrt_item.Click += set_sqrt_template;
        m_set_template_menu_item.Items.Add(sinusoid_item);
        m_set_template_menu_item.Items.Add(sqrt_item);

        m_panel_context_menu = new ContextMenu();
        m_panel_context_menu.Items.Add(m_remove_menu_item);
        m_panel_context_menu.Items.Add(m_set_template_menu_item);
        m_curves_panel.ContextMenu = m_panel_context_menu;

        m_edited_curves.CollectionChanged += (_, _) => sync_edited_curves();
    }

    public static readonly StyledProperty<bool> is_toolbar_visibleProperty =
        AvaloniaProperty.Register<curve_editor_panel, bool>(nameof(is_toolbar_visible), true);

    public static readonly StyledProperty<bool> is_hierarchy_visibleProperty =
        AvaloniaProperty.Register<curve_editor_panel, bool>(nameof(is_hierarchy_visible), true);

    public static readonly StyledProperty<Control?> decoratorProperty =
        AvaloniaProperty.Register<curve_editor_panel, Control?>(nameof(decorator));

    private readonly move_tool m_move_tool;
    private readonly scale_tool m_scale_tool;
    private readonly move_range_tool m_move_range_tool;
    private readonly add_tool m_add_tool;
    private readonly remove_tool m_remove_tool;
    private tool_base m_active_tool;

    private readonly Dictionary<string, float_curve> m_panel_curves = new();
    private ObservableCollection<float_curve> m_edited_curves = new();
    private readonly List<visual_curve_key> m_selected_keys = new();
    private readonly List<visual_curve_key> m_just_selected_keys = new();
    private readonly List<visual_curve_key_tangent> m_just_selected_tangents = new();
    private readonly List<visual_curve_key_range_control> m_just_selected_key_range_controls = new();
    private readonly List<Point> m_points_positions = new();

    private readonly RectangleGeometry m_selection_rect;
    private readonly Path m_selection_path;
    private readonly ContextMenu m_panel_context_menu;
    private readonly MenuItem m_remove_menu_item;
    private readonly MenuItem m_set_template_menu_item;

    private Point m_pan = new(20, 120);
    private Point m_scale = new(100, 100);
    private double m_field_left_limit = double.NaN;
    private double m_field_right_limit = double.NaN;
    private double m_field_top_limit = double.NaN;
    private double m_field_bottom_limit = double.NaN;
    private bool? m_is_zoom_locked;

    private bool m_is_tool_captured;
    private bool m_is_selection_started;
    private bool m_is_dotted_selection;
    private bool m_is_field_move_captured;
    private bool m_is_field_scale_captured;
    private bool m_is_scroll_bar_updating;
    private Point m_selection_start_position;
    private Point m_mouse_position;
    private Point m_scaling_start_position;
    private Point m_start_scale;

    internal Point m_selected_points_top_left;
    internal Point m_selected_points_bottom_right;

    internal global::Avalonia.Controls.Controls items => m_curves_panel.curves_items;
    internal curves_panel curves_panel => m_curves_panel;
    internal List<visual_curve_key> selected_keys => m_selected_keys;
    internal List<visual_curve_key_tangent> selected_key_tangents =>
        items.OfType<visual_curve>().SelectMany(c => c.selected_tangents).ToList();
    internal List<Point> points_positions => m_points_positions;
    internal Point selected_points_top_left => m_selected_points_top_left;
    internal Point selected_points_bottom_right => m_selected_points_bottom_right;
    internal selected_entity_type selected_entity { get; set; } = selected_entity_type.none;
    internal Point last_mouse_down_position { get; set; }
    internal bool is_snap_to_grid => m_snap_to_grid_toggle.IsChecked == true;
    internal bool is_snap_to_point => m_snap_to_point_toggle.IsChecked == true;

    public bool has_selected_keys => m_selected_keys.Count > 0;
    public bool is_default_range_enabled =>
        !double.IsNaN(m_field_left_limit) || !double.IsNaN(m_field_right_limit) ||
        !double.IsNaN(m_field_top_limit) || !double.IsNaN(m_field_bottom_limit);

    public Point pan
    {
        get => m_pan;
        set
        {
            if (m_pan == value) return;
            m_pan = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(pan)));
        }
    }

    public Point scale
    {
        get => m_scale;
        set
        {
            if (m_scale == value) return;
            m_scale = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(scale)));
        }
    }

    public double field_left_limit
    {
        get => m_field_left_limit;
        set { m_field_left_limit = value; fix_keys_positions_to_limits(); m_curves_panel.InvalidateVisual(); }
    }

    public double field_right_limit
    {
        get => m_field_right_limit;
        set { m_field_right_limit = value; fix_keys_positions_to_limits(); m_curves_panel.InvalidateVisual(); }
    }

    public double field_top_limit
    {
        get => m_field_top_limit;
        set { m_field_top_limit = value; fix_keys_positions_to_limits(); m_curves_panel.InvalidateVisual(); }
    }

    public double field_bottom_limit
    {
        get => m_field_bottom_limit;
        set { m_field_bottom_limit = value; fix_keys_positions_to_limits(); m_curves_panel.InvalidateVisual(); }
    }

    public Dictionary<string, float_curve> panel_curves => m_panel_curves;

    public ObservableCollection<float_curve> edited_curves
    {
        get => m_edited_curves;
        set
        {
            m_edited_curves = value ?? new ObservableCollection<float_curve>();
            m_edited_curves.CollectionChanged += (_, _) => sync_edited_curves();
            sync_edited_curves();
        }
    }

    public bool is_zoom_locked
    {
        get => m_is_zoom_locked ?? settings.curve_editor_lock_zoom;
        set => m_is_zoom_locked = value;
    }

    public bool is_read_only { get; set; }
    public bool is_frequently_changed { get; set; } = true;
    public bool double_click_key_adding_enabled { get; set; } = true;
    public int focus_sample_count { get; set; } = 20;
    public List<float_curve> custom_templates { get; } = new();

    public bool focus_default_range_visibility
    {
        get => m_focus_range_button.IsVisible;
        set => m_focus_range_button.IsVisible = value;
    }

    public bool toggle_range_visibility
    {
        get => m_toggle_range_button.IsVisible;
        set => m_toggle_range_button.IsVisible = value;
    }

    public bool scroll_bar_visibility
    {
        get => m_curves_scroll_bar.IsVisible;
        set => m_curves_scroll_bar.IsVisible = value;
    }

    public bool is_toolbar_visible
    {
        get => GetValue(is_toolbar_visibleProperty);
        set => SetValue(is_toolbar_visibleProperty, value);
    }

    public bool is_hierarchy_visible
    {
        get => GetValue(is_hierarchy_visibleProperty);
        set => SetValue(is_hierarchy_visibleProperty, value);
    }

    public bool is_horizontal_numbers_visible
    {
        get => m_curves_panel.is_horizontal_numbers_visible;
        set => m_curves_panel.is_horizontal_numbers_visible = value;
    }

    public bool is_vertical_numbers_visible
    {
        get => m_curves_panel.is_vertical_numbers_visible;
        set => m_curves_panel.is_vertical_numbers_visible = value;
    }

    public double divide_factor_x_min
    {
        get => m_curves_panel.divide_factor_x_min;
        set => m_curves_panel.divide_factor_x_min = value;
    }

    public double divide_factor_y_min
    {
        get => m_curves_panel.divide_factor_y_min;
        set => m_curves_panel.divide_factor_y_min = value;
    }

    public double grid_x_scale_multiplier
    {
        get => m_curves_panel.grid_x_scale_multiplier;
        set => m_curves_panel.grid_x_scale_multiplier = value;
    }

    public double grid_y_scale_multiplier
    {
        get => m_curves_panel.grid_y_scale_multiplier;
        set => m_curves_panel.grid_y_scale_multiplier = value;
    }

    public Control? decorator
    {
        get => GetValue(decoratorProperty);
        set => SetValue(decoratorProperty, value);
    }

    public event Action<float_curve>? float_curve_changed;
    public event Action<float_curve>? float_curve_edit_complete;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public event Action? scale_changed;
    public event Action? pan_changed;
    public event Action? selection_changed;

    public delegate void ref_point_delegate(ref Point point_position);
    public ref_point_delegate? fix_point_position;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == is_toolbar_visibleProperty)
        {
            m_tool_bar_border.IsVisible = is_toolbar_visible;
        }
        else if (change.Property == is_hierarchy_visibleProperty)
        {
            bool visible = is_hierarchy_visible;
            m_hierarchy_border.IsVisible = visible;
            m_hierarchy_splitter.IsVisible = visible;
            m_main_grid.ColumnDefinitions[0].Width = visible ? new GridLength(150) : new GridLength(0);
        }
        else if (change.Property == decoratorProperty)
        {
            m_decorator_panel.Content = change.NewValue as Control;
        }
    }

    internal curves_panel detach_curves_panel()
    {
        if (m_curves_panel.Parent is Panel parent)
            parent.Children.Remove(m_curves_panel);
        return m_curves_panel;
    }

    internal void restore_curves_panel(curves_panel panel)
    {
        if (panel.Parent == null && !m_curves_host_grid.Children.Contains(panel))
            m_curves_host_grid.Children.Insert(0, panel);
    }

    private void sync_edited_curves()
    {
        items.Clear();
        m_curves_groups.Items.Clear();
        m_panel_curves.Clear();
        m_selected_keys.Clear();

        for (int i = 0; i < m_edited_curves.Count; ++i)
        {
            var curve = m_edited_curves[i];
            string key = string.IsNullOrEmpty(curve.name) ? $"curve_{i}" : curve.name;
            if (m_panel_curves.ContainsKey(key))
                key = $"{key}_{i}";
            add_curve(key, curve);
        }
    }

    private void set_active_tool(ToggleButton active_btn, tool_base tool)
    {
        m_active_tool = tool;
        m_move_tool_button.IsChecked = active_btn == m_move_tool_button;
        m_scale_tool_button.IsChecked = active_btn == m_scale_tool_button;
        m_move_range_tool_button.IsChecked = active_btn == m_move_range_tool_button;
        m_add_tool_button.IsChecked = active_btn == m_add_tool_button;
        m_remove_tool_button.IsChecked = active_btn == m_remove_tool_button;
    }

    internal Point clamp_to_limits(Point pt)
    {
        double x = pt.X;
        double y = pt.Y;
        if (!double.IsNaN(m_field_left_limit) && x < m_field_left_limit) x = m_field_left_limit;
        if (!double.IsNaN(m_field_right_limit) && x > m_field_right_limit) x = m_field_right_limit;
        if (!double.IsNaN(m_field_bottom_limit) && y < m_field_bottom_limit) y = m_field_bottom_limit;
        if (!double.IsNaN(m_field_top_limit) && y > m_field_top_limit) y = m_field_top_limit;
        return new Point(x, y);
    }

    private void scroll_bar_size_changed(object? sender, SizeChangedEventArgs e) => update_scroll_bar();

    private void open_remove_menu(List<visual_curve_key> visual_keys)
    {
        m_remove_menu_item.IsEnabled = visual_keys.Count > 0;
        m_panel_context_menu.Open(m_curves_panel);
    }

    private void remove_menu_item_click(object? sender, RoutedEventArgs e)
    {
        var changed_curves = items.OfType<visual_curve>().Where(vc => vc.selected_keys.Count != 0).ToArray();
        foreach (var key in m_selected_keys.ToArray())
            key.remove();

        foreach (var visual_curve in changed_curves)
        {
            if (visual_curve.float_curve != null)
                on_float_curve_changed(visual_curve.float_curve);
        }

        deselect_all_keys();
        hide_all_tangents();
    }

    private void key_x_position_text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            m_keys_x_position_text_box.Text = m_keys_x_position_text_box.Tag as string;
            Focus();
        }
        else if (e.Key == Key.Enter)
        {
            if (float.TryParse(m_keys_x_position_text_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            {
                foreach (var key in m_selected_keys)
                    key.position_x = value;
                m_keys_x_position_text_box.Tag = m_keys_x_position_text_box.Text;
            }
            Focus();
        }
    }

    private void key_y_position_text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            m_keys_y_position_text_box.Text = m_keys_y_position_text_box.Tag as string;
            Focus();
        }
        else if (e.Key == Key.Enter)
        {
            if (float.TryParse(m_keys_y_position_text_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            {
                if (selected_entity == selected_entity_type.keys)
                {
                    foreach (var key in m_selected_keys)
                        key.position_y = value;
                }
                else if (selected_entity == selected_entity_type.key_range_controls)
                {
                    foreach (var rc in items.OfType<visual_curve>().SelectMany(c => c.selected_key_range_controls))
                        rc.range_delta = value;
                }
                m_keys_y_position_text_box.Tag = m_keys_y_position_text_box.Text;
            }
            Focus();
        }
    }

    private void key_x_position_text_box_focus_lost(object? sender, RoutedEventArgs e)
    {
        if (!float.TryParse(m_keys_x_position_text_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
        {
            m_keys_x_position_text_box.Text = m_keys_x_position_text_box.Tag as string;
            return;
        }
        foreach (var key in m_selected_keys)
            key.position_x = value;
        m_keys_x_position_text_box.Tag = m_keys_x_position_text_box.Text;
    }

    private void key_y_position_text_box_focus_lost(object? sender, RoutedEventArgs e)
    {
        if (!float.TryParse(m_keys_y_position_text_box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
        {
            m_keys_y_position_text_box.Text = m_keys_y_position_text_box.Tag as string;
            return;
        }
        foreach (var key in m_selected_keys)
            key.position_y = value;
        m_keys_y_position_text_box.Tag = m_keys_y_position_text_box.Text;
    }

    private void toggle_range_button_click(object? sender, RoutedEventArgs e)
    {
        var curves = new HashSet<visual_curve>();
        var is_range_enabled = m_toggle_range_button.IsChecked == true;
        foreach (var key in m_selected_keys)
        {
            curves.Add(key.parent_curve);
            key.is_range_control_visible = is_range_enabled;
        }
        foreach (var curve in curves)
            curve.is_range = is_range_enabled;
    }

    private void linked_auto_click(object? sender, RoutedEventArgs e) { selected_entity_set_type(float_curve_key_type.locked_auto); toggle_key_type_buttons(m_linked_auto_button); }
    private void linear_click(object? sender, RoutedEventArgs e) { selected_entity_set_type(float_curve_key_type.linear); toggle_key_type_buttons(m_linear_button); }
    private void step_click(object? sender, RoutedEventArgs e) { selected_entity_set_type(float_curve_key_type.step); toggle_key_type_buttons(m_step_button); }
    private void linked_manual_click(object? sender, RoutedEventArgs e) { selected_entity_set_type(float_curve_key_type.locked_manual); toggle_key_type_buttons(m_linked_manual_button); }
    private void breaked_click(object? sender, RoutedEventArgs e) { selected_entity_set_type(float_curve_key_type.breaked); toggle_key_type_buttons(m_breaked_button); }

    private void focus_click(object? sender, RoutedEventArgs e)
    {
        if (m_selected_keys.Count > 0)
            focus_selection();
        else
            focus_curves();
    }

    private void focus_default_range_click(object? sender, RoutedEventArgs e) => focus_default_range();

    private void scroll_bar_value_changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (m_is_scroll_bar_updating)
            return;

        pan = new Point(-e.NewValue, pan.Y);
        foreach (var curve in items.OfType<visual_curve>())
            curve.update();

        pan_changed?.Invoke();
        m_curves_panel.InvalidateVisual();
    }

    private void fix_keys_positions_to_limits()
    {
        var affected_curves = new HashSet<visual_curve>();
        foreach (var visual_curve in items.OfType<visual_curve>())
        {
            foreach (var key in visual_curve.keys)
            {
                double x = key.position.X;
                double y = key.position.Y;
                bool changed = false;

                if (x > m_field_right_limit) { x = m_field_right_limit; changed = true; }
                if (x < m_field_left_limit) { x = m_field_left_limit; changed = true; }
                if (y > m_field_top_limit) { y = m_field_top_limit; changed = true; }
                if (y < m_field_bottom_limit) { y = m_field_bottom_limit; changed = true; }

                if (changed)
                {
                    key.move_to(new Point(x, y));
                    affected_curves.Add(visual_curve);
                }
            }
        }

        foreach (var curve in affected_curves)
            curve.float_curve?.fire_curve_changed();
    }

    private void toggle_key_type_buttons(ToggleButton checked_button)
    {
        m_linked_auto_button.IsChecked = m_linked_auto_button == checked_button;
        m_linked_manual_button.IsChecked = m_linked_manual_button == checked_button;
        m_linear_button.IsChecked = m_linear_button == checked_button;
        m_step_button.IsChecked = m_step_button == checked_button;
        m_breaked_button.IsChecked = m_breaked_button == checked_button;
    }

    private void toggle_key_type_buttons(List<visual_curve_key> keys)
    {
        m_linked_auto_button.IsChecked = keys.Any(k => k.type_of_key == float_curve_key_type.locked_auto);
        m_linked_manual_button.IsChecked = keys.Any(k => k.type_of_key == float_curve_key_type.locked_manual);
        m_linear_button.IsChecked = keys.Any(k => k.type_of_key == float_curve_key_type.linear);
        m_step_button.IsChecked = keys.Any(k => k.type_of_key == float_curve_key_type.step);
        m_breaked_button.IsChecked = keys.Any(k => k.type_of_key == float_curve_key_type.breaked);
    }

    internal void select_single_key(visual_curve_key key, KeyModifiers modifiers)
    {
        m_just_selected_tangents.Clear();
        m_just_selected_key_range_controls.Clear();
        m_just_selected_keys.Clear();
        m_just_selected_keys.Add(key);
        process_just_selected_entities(modifiers);
        selection_changed?.Invoke();
    }

    internal void select_single_tangent(visual_curve_key_tangent tangent, KeyModifiers modifiers)
    {
        m_just_selected_keys.Clear();
        m_just_selected_key_range_controls.Clear();
        m_just_selected_tangents.Clear();
        m_just_selected_tangents.Add(tangent);
        process_just_selected_entities(modifiers);
        selection_changed?.Invoke();
    }

    internal void select_single_range_control(visual_curve_key_range_control rc, KeyModifiers modifiers)
    {
        m_just_selected_keys.Clear();
        m_just_selected_tangents.Clear();
        m_just_selected_key_range_controls.Clear();
        m_just_selected_key_range_controls.Add(rc);
        process_just_selected_entities(modifiers);
        selection_changed?.Invoke();
    }

    private void process_just_selected_entities(KeyModifiers modifiers = KeyModifiers.None)
    {
        foreach (var button in m_key_type_buttons.Children.OfType<Control>())
            button.IsEnabled = false;

        m_toggle_range_button.IsChecked = false;
        m_keys_x_position_text_box.Text = "";
        m_keys_y_position_text_box.Text = "";

        bool new_selection = !modifiers.HasFlag(KeyModifiers.Shift) && !modifiers.HasFlag(KeyModifiers.Control);
        selected_entity = selected_entity_type.none;
        m_points_positions.Clear();
        toggle_key_type_buttons(new List<visual_curve_key>());

        if (new_selection)
        {
            deselect_all();
            if (m_just_selected_tangents.Count == 0 && m_just_selected_keys.Count == 0)
                hide_all_tangents();
            hide_all_key_range_controls();
        }

        if ((new_selection || !has_selected_keys) && m_just_selected_keys.Count == 0)
        {
            deselect_all_keys();
            if (m_just_selected_tangents.Count > 0)
            {
                foreach (var tangent in m_just_selected_tangents)
                    tangent.process_selection();
                selected_entity = selected_entity_type.tangents;
                m_linked_auto_button.IsEnabled = true;
                m_linear_button.IsEnabled = true;
                m_step_button.IsEnabled = true;
            }
            else if (m_just_selected_key_range_controls.Count > 0)
            {
                deselect_all_tangents();
                foreach (var control in m_just_selected_key_range_controls)
                    control.process_selection();
                selected_entity = selected_entity_type.key_range_controls;
                m_keys_position_panel.IsEnabled = true;
                initialize_selected_items_position_viewer();
            }
            return;
        }

        if (!has_selected_keys)
            hide_all_tangents();

        deselect_all_tangents();
        foreach (var key in m_just_selected_keys)
            key.process_selection();

        selected_entity = selected_entity_type.keys;
        toggle_key_type_buttons(m_selected_keys);

        foreach (var button in m_key_type_buttons.Children.OfType<Control>())
            button.IsEnabled = true;

        m_toggle_range_button.IsChecked = m_selected_keys.Any(k => k.parent_curve.is_range);
        initialize_selected_items_position_viewer();
    }

    private void selected_entity_set_type(float_curve_key_type key_type)
    {
        switch (selected_entity)
        {
            case selected_entity_type.keys:
                selected_keys_set_type(key_type);
                break;
            case selected_entity_type.tangents:
                selected_tangents_set_type(key_type);
                break;
        }
    }

    private void selected_keys_set_type(float_curve_key_type key_type)
    {
        foreach (var curv in items.OfType<visual_curve>())
        {
            bool changed = false;
            if (selected_entity == selected_entity_type.keys)
            {
                foreach (var visual_key in curv.selected_keys)
                {
                    visual_key.type_of_key = key_type;
                    changed = true;
                }
            }
            if (changed && curv.float_curve != null)
                on_float_curve_changed(curv.float_curve);
        }
    }

    private void selected_tangents_set_type(float_curve_key_type key_type)
    {
        foreach (var curv in items.OfType<visual_curve>())
        {
            bool changed = false;
            foreach (var visual_tangent in curv.selected_tangents)
            {
                visual_tangent.compute_tangent_as(key_type);
                changed = true;
            }
            if (changed && curv.float_curve != null)
                on_float_curve_changed(curv.float_curve);
        }
    }

    private void mouse_down(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(m_curves_panel).Properties;
        if (!is_read_only && m_active_tool.mouse_down(e))
        {
            m_is_tool_captured = true;
            m_points_positions.Clear();

            if (selected_entity == selected_entity_type.key_range_controls)
            {
                foreach (var rc in items.OfType<visual_curve>().SelectMany(c => c.selected_key_range_controls))
                    m_points_positions.Add(new Point(0, rc.range_delta));
            }
            else if (selected_entity == selected_entity_type.tangents)
            {
                foreach (var t in items.OfType<visual_curve>().SelectMany(c => c.selected_tangents))
                    m_points_positions.Add(t.position);
            }
            else if (m_selected_keys.Count > 0)
            {
                foreach (var k in m_selected_keys)
                    m_points_positions.Add(k.position);
            }

            if (m_points_positions.Count > 0)
            {
                double minX = m_points_positions[0].X;
                double maxX = m_points_positions[0].X;
                double minY = m_points_positions[0].Y;
                double maxY = m_points_positions[0].Y;
                for (var i = 1; i < m_points_positions.Count; ++i)
                {
                    var p = m_points_positions[i];
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
                m_selected_points_top_left = new Point(minX, maxY);
                m_selected_points_bottom_right = new Point(maxX, minY);
            }
            return;
        }

        if (props.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
        {
            if (is_read_only)
                return;

            m_is_selection_started = true;
            m_is_dotted_selection = true;
            m_selection_start_position = e.GetPosition(m_curves_panel);
            e.Pointer.Capture(m_curves_panel);
            m_selection_rect.Rect = new Rect(m_selection_start_position, new Size(1, 1));
            if (m_selection_path.Parent == null)
                items.Add(m_selection_path);
            e.Handled = true;
        }
        else if (props.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_mouse_position = e.GetPosition(m_curves_panel);
            m_is_field_move_captured = true;
            e.Pointer.Capture(m_curves_panel);
            m_curves_panel.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            m_is_field_scale_captured = true;
            m_mouse_position = e.GetPosition(m_curves_panel);
            m_scaling_start_position = m_mouse_position;
            m_start_scale = scale;
            e.Pointer.Capture(m_curves_panel);
            m_curves_panel.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        else if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (!is_read_only && items.Count > 0)
                open_remove_menu(m_selected_keys);
        }
    }

    private void mouse_move(object? sender, PointerEventArgs e)
    {
        if (m_is_tool_captured && m_active_tool.mouse_move(e))
            return;

        if (m_is_field_move_captured || m_is_field_scale_captured)
        {
            var new_mouse_position = e.GetPosition(m_curves_panel);
            var delta = new_mouse_position - m_mouse_position;
            m_mouse_position = new_mouse_position;

            if (m_is_field_move_captured)
                pan = new Point(pan.X + delta.X, pan.Y + delta.Y);

            if (m_is_field_scale_captured)
            {
                var logic_mouse = visual_to_logical_point(m_scaling_start_position);
                double dy = is_zoom_locked ? delta.X : -delta.Y;

                scale = new Point(
                    Math.Max(scale.X + m_start_scale.X * delta.X / 200.0, m_start_scale.X / 20.0),
                    Math.Max(scale.Y + m_start_scale.Y * dy / 200.0, m_start_scale.Y / 20.0));

                var logic_offset = logical_to_visual_point(logic_mouse);
                pan = new Point(pan.X + m_scaling_start_position.X - logic_offset.X, pan.Y + m_scaling_start_position.Y - logic_offset.Y);
            }

            foreach (var curve in items.OfType<visual_curve>())
                curve.update();

            m_curves_panel.InvalidateVisual();

            if (m_is_field_move_captured)
                pan_changed?.Invoke();
            if (m_is_field_scale_captured)
                scale_changed?.Invoke();

            update_scroll_bar();
        }
        else if (m_is_selection_started)
        {
            var new_pos = e.GetPosition(m_curves_panel);
            m_selection_rect.Rect = new Rect(
                Math.Min(m_selection_start_position.X, new_pos.X),
                Math.Min(m_selection_start_position.Y, new_pos.Y),
                Math.Abs(new_pos.X - m_selection_start_position.X),
                Math.Abs(new_pos.Y - m_selection_start_position.Y));
            if (m_selection_start_position != new_pos)
                m_is_dotted_selection = false;
        }
    }

    private void mouse_up(object? sender, PointerReleasedEventArgs e)
    {
        if (m_is_tool_captured && m_active_tool.mouse_up(e))
            return;

        if ((m_is_field_scale_captured || m_is_field_move_captured) &&
            e.GetCurrentPoint(m_curves_panel).Properties.PointerUpdateKind is PointerUpdateKind.MiddleButtonReleased or PointerUpdateKind.RightButtonReleased)
        {
            m_is_tool_captured = false;
            m_is_field_move_captured = false;
            m_is_field_scale_captured = false;
            e.Pointer.Capture(null);
            m_curves_panel.Cursor = null;
            return;
        }

        if (e.GetCurrentPoint(m_curves_panel).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (!m_is_selection_started)
                return;

            m_is_selection_started = false;
            m_just_selected_keys.Clear();
            m_just_selected_tangents.Clear();
            m_just_selected_key_range_controls.Clear();

            var sel_rect = m_selection_rect.Rect;
            foreach (var curve in items.OfType<visual_curve>().Where(c => c.IsVisible))
            {
                foreach (var k in curve.keys)
                {
                    if (k.is_range_control_visible)
                    {
                        var rc_center = new Point(k.visual_position.X, k.visual_position.Y - k.range_delta * scale.Y);
                        if (sel_rect.Contains(rc_center))
                        {
                            m_just_selected_key_range_controls.Add(k.range_control);
                            if (m_is_dotted_selection) break;
                        }
                    }
                    if (k.left_tangent.IsVisible && sel_rect.Contains(k.left_tangent.visual_position))
                    {
                        m_just_selected_tangents.Add(k.left_tangent);
                        if (m_is_dotted_selection) break;
                    }
                    if (k.right_tangent.IsVisible && sel_rect.Contains(k.right_tangent.visual_position))
                    {
                        m_just_selected_tangents.Add(k.right_tangent);
                        if (m_is_dotted_selection) break;
                    }
                    if (sel_rect.Contains(k.visual_position))
                    {
                        m_just_selected_keys.Add(k);
                        if (m_is_dotted_selection) break;
                    }
                }
                if (m_is_dotted_selection &&
                    (m_just_selected_keys.Count > 0 || m_just_selected_tangents.Count > 0 || m_just_selected_key_range_controls.Count > 0))
                    break;
            }

            e.Pointer.Capture(null);
            items.Remove(m_selection_path);

            process_just_selected_entities(e.KeyModifiers);
            selection_changed?.Invoke();
        }
    }

    private void groups_panel_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            deselect_all_groups();
    }

    private void preview_mouse_down(object? sender, PointerPressedEventArgs e) => Focus();

    private void preview_key_down(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.W: m_move_tool_button.IsChecked = true; break;
            case Key.R: m_scale_tool_button.IsChecked = true; break;
            case Key.A: m_add_tool_button.IsChecked = true; break;
            case Key.F:
                if (m_selected_keys.Count > 0) focus_selection();
                else focus_curves();
                break;
            case Key.D:
                if (is_default_range_enabled) focus_default_range();
                break;
            case Key.Delete:
                remove_menu_item_click(this, new RoutedEventArgs());
                break;
        }
    }

    private void size_changed(object? sender, SizeChangedEventArgs e)
    {
        foreach (var curve in items.OfType<visual_curve>())
        {
            curve.update_first_segment_visual();
            curve.update_last_segment_visual();
        }
    }

    private void deselect_all()
    {
        deselect_all_keys();
        deselect_all_tangents();
        deselect_all_keys_range_controls();
    }

    private void deselect_all_keys_range_controls()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.deselect_all_range_controls();
    }

    private void deselect_all_tangents()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.deselect_all_tangents();
    }

    private void hide_all_tangents()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.hide_all_tangents();
    }

    private void hide_all_key_range_controls()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.hide_all_key_range_controls();
    }

    private void focus_rect(Rect rect)
    {
        var border_size = new Point(40, 40);
        var actual_width = m_curves_panel.Bounds.Width;
        var actual_height = m_curves_panel.Bounds.Height;
        if (actual_height <= 1) actual_height = 120;
        if (actual_width <= 1) actual_width = 200;

        if (rect.Width < math.epsilon_4)
            rect = new Rect(rect.X - 0.5, rect.Y, 1.0, rect.Height);
        if (rect.Height < math.epsilon_4)
            rect = new Rect(rect.X, rect.Y - 0.5, rect.Width, 1.0);

        double horizontal_scale = Math.Max(0.01, (actual_width - border_size.X) / rect.Width);
        double vertical_scale = Math.Max(0.01, (actual_height - border_size.Y) / rect.Height);

        m_scale = new Point(horizontal_scale, vertical_scale);
        m_pan = new Point(-rect.X * m_scale.X + border_size.X * 0.5, rect.Bottom * m_scale.Y + border_size.Y * 0.5);

        foreach (var curve in items.OfType<visual_curve>())
            curve.update();

        m_curves_panel.InvalidateVisual();
        pan_changed?.Invoke();
        scale_changed?.Invoke();
        update_scroll_bar();
    }

    private Rect get_visible_logical_curves_rect()
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        bool any = false;

        foreach (var curve in items.OfType<visual_curve>().Where(c => c.IsVisible && c.keys.Count > 0))
        {
            foreach (var k in curve.keys)
            {
                any = true;
                if (k.position_x < minX) minX = k.position_x;
                if (k.position_x > maxX) maxX = k.position_x;
                if (k.position_y < minY) minY = k.position_y;
                if (k.position_y > maxY) maxY = k.position_y;
                if (curve.is_range)
                {
                    if (k.position_y - k.range_delta < minY) minY = k.position_y - k.range_delta;
                    if (k.position_y + k.range_delta > maxY) maxY = k.position_y + k.range_delta;
                }
            }
        }

        if (!any)
            return new Rect(0, 0, 1, 1);

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private string? find_curve_key(float_curve curve)
    {
        foreach (var pair in m_panel_curves)
        {
            if (pair.Value == curve)
                return pair.Key;
        }
        return null;
    }

    private visual_curve? get_curve()
    {
        if (selected_keys.Count > 0)
            return selected_keys[0].parent_curve;

        var selected_curves = new List<visual_curve>();
        foreach (var group in m_curves_groups.Items.OfType<panel_curve_group>())
            group.get_selected_curves(selected_curves);

        return selected_curves.FirstOrDefault() ?? items.OfType<visual_curve>().FirstOrDefault();
    }

    private void set_template(curve_template_base template)
    {
        var curve = get_curve();
        if (curve == null)
            return;

        template.curve = curve;
        template.commit();
        template.show_settings();
    }

    private void set_sinusoid_template(object? sender, RoutedEventArgs e) => set_template(new sinusoid_curve_template());
    private void set_sqrt_template(object? sender, RoutedEventArgs e) => set_template(new sqrt_curve_template());

    internal void initialize_selected_items_position_viewer()
    {
        switch (selected_entity)
        {
            case selected_entity_type.keys when m_selected_keys.Count > 0:
                if (m_selected_keys.All(k => k.position_x == m_selected_keys[0].position_x))
                    m_keys_x_position_text_box.Text = "_";
                if (m_selected_keys.All(k => k.position_y == m_selected_keys[0].position_y))
                    m_keys_y_position_text_box.Text = "_";
                break;
            case selected_entity_type.key_range_controls:
            {
                m_keys_x_position_text_box.Text = "";
                m_keys_x_position_text_box.Tag = "";
                var rcs = items.OfType<visual_curve>().SelectMany(c => c.selected_key_range_controls).ToList();
                if (rcs.Count > 0 && rcs.All(r => r.range_delta == rcs[0].range_delta))
                    m_keys_y_position_text_box.Text = "_";
                break;
            }
        }
        update_selected_items_position_viewer();
    }

    internal void update_selected_items_position_viewer()
    {
        switch (selected_entity)
        {
            case selected_entity_type.keys when m_selected_keys.Count > 0:
                if (!string.IsNullOrEmpty(m_keys_x_position_text_box.Text))
                {
                    m_keys_x_position_text_box.Text = string.Format(CultureInfo.CurrentCulture, "{0:F3}", m_selected_keys[0].position_x);
                    m_keys_x_position_text_box.Tag = m_keys_x_position_text_box.Text;
                }
                if (!string.IsNullOrEmpty(m_keys_y_position_text_box.Text))
                {
                    m_keys_y_position_text_box.Text = string.Format(CultureInfo.CurrentCulture, "{0:F3}", m_selected_keys[0].position_y);
                    m_keys_y_position_text_box.Tag = m_keys_y_position_text_box.Text;
                }
                break;
            case selected_entity_type.key_range_controls:
            {
                m_keys_x_position_text_box.Text = "";
                m_keys_x_position_text_box.Tag = "";
                var rcs = items.OfType<visual_curve>().SelectMany(c => c.selected_key_range_controls).ToList();
                if (!string.IsNullOrEmpty(m_keys_y_position_text_box.Text) && rcs.Count > 0)
                {
                    m_keys_y_position_text_box.Text = string.Format(CultureInfo.CurrentCulture, "{0:F3}", rcs[0].range_delta);
                    m_keys_y_position_text_box.Tag = m_keys_y_position_text_box.Text;
                }
                break;
            }
        }
    }

    internal void update_scroll_bar()
    {
        if (!m_curves_scroll_bar.IsVisible)
            return;

        m_is_scroll_bar_updating = true;
        double minimum = double.MaxValue;
        double maximum = double.MinValue;

        foreach (var curve in items.OfType<visual_curve>().Where(c => c.keys.Count > 0))
        {
            minimum = Math.Min(minimum, curve.keys[0].position_x * scale.X);
            maximum = Math.Max(maximum, curve.keys[^1].position_x * scale.X);
        }

        if (minimum == double.MaxValue)
        {
            m_curves_scroll_bar.IsEnabled = false;
            m_is_scroll_bar_updating = false;
            return;
        }

        minimum -= 100;
        maximum += 100;
        double width = m_curves_panel.Bounds.Width;
        m_curves_scroll_bar.IsEnabled = maximum - minimum > width;
        maximum -= width;

        if (m_curves_scroll_bar.IsEnabled && maximum > minimum)
        {
            m_curves_scroll_bar.Minimum = minimum;
            m_curves_scroll_bar.Maximum = maximum;
            m_curves_scroll_bar.ViewportSize = width;
            m_curves_scroll_bar.Value = Math.Clamp(-pan.X, minimum, maximum);
            m_curves_scroll_bar.SmallChange = (maximum - minimum) / 100;
            m_curves_scroll_bar.LargeChange = (maximum - minimum) / 10;
        }
        m_is_scroll_bar_updating = false;
    }

    internal visual_curve add_curve(string key, float_curve curve)
    {
        var v_curve = new visual_curve
        {
            parent_panel = this,
            brush = new SolidColorBrush(curve.color)
        };
        m_panel_curves[key] = curve;
        items.Add(v_curve);
        v_curve.float_curve = curve;
        v_curve.update();
        update_scroll_bar();
        return v_curve;
    }

    internal void remove_curve(float_curve curve)
    {
        var key = find_curve_key(curve);
        if (key != null)
            m_panel_curves.Remove(key);

        for (var i = items.Count - 1; i >= 0; --i)
        {
            if (items[i] is visual_curve vc && vc.float_curve == curve)
            {
                items.RemoveAt(i);
                return;
            }
        }
    }

    internal void on_float_curve_changed(float_curve curve)
    {
        curve.fire_curve_changed();
        float_curve_changed?.Invoke(curve);
    }

    internal void on_float_curve_edit_complete(float_curve curve)
    {
        curve.fire_edit_completed();
        float_curve_edit_complete?.Invoke(curve);
    }

    internal void deselect_all_keys()
    {
        foreach (var item in items.OfType<visual_curve>())
        {
            for (var i = item.selected_keys.Count - 1; i >= 0; --i)
                item.selected_keys[i].is_selected = false;
            item.selected_keys.Clear();
        }
        m_selected_keys.Clear();
    }

    internal Point visual_to_logical_point(Point point) =>
        new((point.X - pan.X) / scale.X, -(point.Y - pan.Y) / scale.Y);

    internal Point logical_to_visual_point(Point point) =>
        new(point.X * scale.X + pan.X, -(point.Y * scale.Y) + pan.Y);

    internal void deselect_all_groups()
    {
        foreach (var group in m_curves_groups.Items.OfType<panel_curve_group>())
            group.is_selected = false;
    }

    public void select_keys_by_index(int index)
    {
        m_just_selected_tangents.Clear();
        m_just_selected_keys.Clear();
        m_just_selected_key_range_controls.Clear();

        foreach (var curve in items.OfType<visual_curve>().Where(c => c.keys.Count > index && index >= 0))
            m_just_selected_keys.Add(curve.keys[index]);

        process_just_selected_entities();
    }

    public panel_curve_group add_group(string name)
    {
        var group = new panel_curve_group(this) { header = name };
        m_curves_groups.Items.Add(group);
        return group;
    }

    public panel_curve_group add_group(float_curve_group group)
    {
        var panel_group = new panel_curve_group(this, group) { header = group.name };
        m_curves_groups.Items.Add(panel_group);
        return panel_group;
    }

    public void focus_curves()
    {
        if (m_panel_curves.Count == 0)
            return;
        focus_rect(get_visible_logical_curves_rect());
    }

    public void focus_selection()
    {
        if (m_selected_keys.Count == 0)
            return;

        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var k in m_selected_keys)
        {
            if (k.position_x < minX) minX = k.position_x;
            if (k.position_x > maxX) maxX = k.position_x;
            if (k.position_y < minY) minY = k.position_y;
            if (k.position_y > maxY) maxY = k.position_y;
        }
        focus_rect(new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)));
    }

    public void focus_default_range()
    {
        var r = get_visible_logical_curves_rect();
        double minX = r.X, maxX = r.Right, minY = r.Y, maxY = r.Bottom;
        if (!double.IsNaN(m_field_left_limit)) minX = Math.Min(minX, m_field_left_limit);
        if (!double.IsNaN(m_field_right_limit)) maxX = Math.Max(maxX, m_field_right_limit);
        if (!double.IsNaN(m_field_bottom_limit)) minY = Math.Min(minY, m_field_bottom_limit);
        if (!double.IsNaN(m_field_top_limit)) maxY = Math.Max(maxY, m_field_top_limit);
        focus_rect(new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)));
    }

    public void clear()
    {
        items.Clear();
        m_curves_groups.Items.Clear();
        m_panel_curves.Clear();
        m_selected_keys.Clear();
    }

    public curves_link link_curves(IList<visual_curve> curves) => new(curves);

    public void update_curves()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.update();
    }

    public void update_visual()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.update_visual();
        InvalidateVisual();
    }

    public void raise_curves_change()
    {
        foreach (var curve in items.OfType<visual_curve>())
            curve.raise_curve_changed();
    }

    public void update_chacked_key_type_buttons() => toggle_key_type_buttons(selected_keys);

    public void set_scale(Vector new_scale)
    {
        m_scale = new Point(new_scale.X, new_scale.Y);
        update_visual();
    }

    public void add_custom_template(float_curve template)
    {
        custom_templates.Add(template);
        var custom_template = new custom_curve_template(template);
        var menu_item = new MenuItem { Header = template.name, Tag = custom_template };
        menu_item.Click += (o, _) =>
        {
            if (o is MenuItem mi && mi.Tag is custom_curve_template templ)
            {
                var curve = get_curve();
                if (curve != null)
                {
                    templ.curve = curve;
                    templ.commit();
                }
            }
        };
        m_set_template_menu_item.Items.Add(menu_item);
    }

    internal enum selected_entity_type
    {
        none,
        keys,
        tangents,
        key_range_controls
    }
}
