using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class visual_curve_key_range_control : UserControl
{
    public visual_curve_key_range_control()
    {
        InitializeComponent();
    }

    internal visual_curve_key_range_control(visual_curve_key parent_key) : this()
    {
        this.parent_key = parent_key;
    }

    private bool m_is_selected;

    public visual_curve_key? parent_key { get; internal set; }

    public float range_delta
    {
        get => parent_key?.range_delta ?? 0f;
        set
        {
            if (parent_key == null)
                return;
            parent_key.range_delta = value;
            update_visual();
            parent_key.parent_curve.range.update_visual();
            parent_key.parent_curve.parent_panel?.update_selected_items_position_viewer();
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            if (m_is_selected == value)
                return;

            m_is_selected = value;
            if (parent_key != null)
            {
                if (m_is_selected)
                {
                    if (!parent_key.parent_curve.selected_key_range_controls.Contains(this))
                        parent_key.parent_curve.selected_key_range_controls.Add(this);
                }
                else
                {
                    parent_key.parent_curve.selected_key_range_controls.Remove(this);
                }
            }

            m_shape.Fill = m_is_selected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x33))
                : Brushes.White;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (parent_key?.parent_curve.parent_panel is not { } panel)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        panel.last_mouse_down_position = e.GetPosition(panel.curves_panel);
        if (!is_selected)
            panel.select_single_range_control(this, e.KeyModifiers);
    }

    internal void process_selection()
    {
        is_selected = !is_selected || true;
    }

    public void move_to(double new_range_delta)
    {
        range_delta = (float)Math.Max(0, new_range_delta);
    }

    public void update_visual()
    {
        if (parent_key?.parent_curve.parent_panel is not { } panel)
            return;

        Point key_vis = parent_key.visual_position;
        double y_offset = parent_key.range_delta * panel.scale.Y;
        Canvas.SetLeft(this, key_vis.X - Width * 0.5);
        Canvas.SetTop(this, key_vis.Y - y_offset - Height * 0.5);
    }
}
