using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.logic_view;

public partial class event_control : Thumb, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public static readonly StyledProperty<string?> event_nameProperty =
        AvaloniaProperty.Register<event_control, string?>(nameof(event_name));

    protected override Type StyleKeyOverride => typeof(event_control);

    public event_control()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, on_event_control_preview_mouse_down, RoutingStrategies.Tunnel);
        PointerEntered += (_, _) => { if (m_name_box != null) m_name_box.ZIndex = 2; };
        PointerExited += (_, _) => { if (m_name_box != null) m_name_box.ZIndex = 0; };
    }

    private const double default_event_offset = 141;
    private bool m_is_event_name_changing;
    private double m_scale = 1.0;
    private double m_position;
    private double m_parent_control_scroller_offset;
    private logic_view? m_parent_logic_view;
    private bool m_is_selected;

    private Border? m_stem;
    private Path? m_event_border;
    private TextBox? m_name_box;

    public Action? on_delete_event;
    public Action? on_view_event_properties;
    public event Action<string?>? event_name_changed;

    public string? event_name
    {
        get => GetValue(event_nameProperty);
        set
        {
            if (value == "#StartScene")
                IsVisible = false;

            m_is_event_name_changing = true;
            SetValue(event_nameProperty, value);
            m_is_event_name_changing = false;
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            m_is_selected = value;
            update_selection_visual();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(is_selected)));
        }
    }

    public logic_view? parent_logic_view
    {
        get => m_parent_logic_view;
        set
        {
            m_parent_logic_view = value;
            if (m_parent_logic_view != null)
                m_scale = m_parent_logic_view.logic_panel_scale.X;
        }
    }

    public double position_scale
    {
        get
        {
            if (parent_logic_view == null)
                return m_scale;

            if (parent_logic_view.Bounds.Width <= 0 || parent_logic_view.field_scroller.Bounds.Width <= 0)
                return m_scale;

            return m_scale / (parent_logic_view.Bounds.Width / parent_logic_view.field_scroller.Bounds.Width);
        }
        set
        {
            m_scale = value;
            Canvas.SetLeft(this, visual_position);
        }
    }

    public double parent_control_scroller_offset
    {
        get => m_parent_control_scroller_offset;
        set
        {
            m_parent_control_scroller_offset = value;
            Canvas.SetLeft(this, visual_position);
        }
    }

    public double logical_position
    {
        get => m_position;
        set
        {
            m_position = value;
            Canvas.SetLeft(this, visual_position);
        }
    }

    public double visual_position
    {
        get => logical_position * position_scale - parent_control_scroller_offset;
        set
        {
            Canvas.SetLeft(this, value);
            m_position = (value + parent_control_scroller_offset) / Math.Max(0.001, position_scale);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        m_stem = e.NameScope.Find<Border>("PART_stem");
        m_event_border = e.NameScope.Find<Path>("PART_event_border");
        m_name_box = e.NameScope.Find<TextBox>("PART_name_box");
        update_selection_visual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == event_nameProperty && !m_is_event_name_changing)
            event_name_changed?.Invoke(change.NewValue as string);
    }

    private void update_selection_visual()
    {
        var brush = m_is_selected ? Brushes.Bisque : Brushes.DarkGray;
        if (m_stem != null) m_stem.BorderBrush = brush;
        if (m_event_border != null) m_event_border.Stroke = brush;
    }

    private void thumb_drag_delta(object? sender, VectorEventArgs e)
    {
        if (m_parent_logic_view == null)
            return;

        foreach (var child in m_parent_logic_view.events_strip.Children)
        {
            if (child is event_control event_ctrl && event_ctrl.is_selected)
                event_ctrl.set_position_offset(e.Vector.X);
        }
    }

    public void set_position_offset(double offset)
    {
        var new_position = Math.Max(default_event_offset * m_scale - parent_control_scroller_offset, visual_position + offset);
        visual_position = new_position;
    }

    public void set_default_position()
    {
        visual_position = default_event_offset * position_scale - parent_control_scroller_offset;
    }

    private void on_event_control_preview_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed || m_parent_logic_view == null)
            return;

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            foreach (var child in m_parent_logic_view.events_strip.Children)
            {
                if (child is event_control event_ctrl)
                    event_ctrl.is_selected = false;
            }
        }

        is_selected = true;
    }

    private void delete_selected_click(object? sender, RoutedEventArgs e)
    {
        is_selected = true;
        m_parent_logic_view?.delete_selected_events();
    }

    private void text_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            m_parent_logic_view?.scenes_view.Focus();
    }

    private void view_event_properties_click(object? sender, RoutedEventArgs e) =>
        on_view_event_properties?.Invoke();
}
