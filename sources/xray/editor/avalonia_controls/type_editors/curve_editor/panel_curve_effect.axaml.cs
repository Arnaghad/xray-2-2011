using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.curve_editor.effects;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class panel_curve_effect : UserControl
{
    public panel_curve_effect()
    {
        InitializeComponent();
        m_visual_effects = new List<visual_effect_base>();
    }

    public panel_curve_effect(panel_curve_item panel_item, float_curve_effect effect) : this()
    {
        m_panel_item = panel_item;
        m_effect = effect;
    }

    public panel_curve_effect(panel_curve_group curve_item, float_curve_effect effect) : this()
    {
        m_panel_group = curve_item;
        m_effect = effect;
    }

    private readonly panel_curve_item? m_panel_item;
    private readonly panel_curve_group? m_panel_group;
    private readonly List<visual_effect_base> m_visual_effects;
    private float_curve_effect? m_effect;

    internal float_curve_effect? effect
    {
        get => m_effect;
        set => m_effect = value;
    }

    internal string name
    {
        get => m_text_block.Text ?? string.Empty;
        set => m_text_block.Text = value;
    }

    private void mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount > 1)
        {
            open_effect_settings();
            e.Handled = true;
        }
    }

    private void remove_click(object? sender, RoutedEventArgs e)
    {
        foreach (var current_effect in m_visual_effects)
        {
            current_effect.dispose();
            if (m_panel_group != null)
                m_panel_group.parent_panel.items.Remove(current_effect.effect_curve);
            else if (m_panel_item != null)
                m_panel_item.curves_panel.items.Remove(current_effect.effect_curve);
        }

        if (m_panel_group != null)
            m_panel_group.remove_effect(this);
        else
            m_panel_item?.remove_effect(this);
    }

    internal void add_effect(visual_effect_base new_visual_effect)
    {
        m_visual_effects.Add(new_visual_effect);
        if (m_panel_group != null)
            m_panel_group.parent_panel.items.Add(new_visual_effect.effect_curve);
        else if (m_panel_item != null)
            m_panel_item.curves_panel.items.Add(new_visual_effect.effect_curve);
    }

    public void open_effect_settings()
    {
        if (m_visual_effects.Count == 0)
            return;
        m_visual_effects[0].open_effect_settings(utils.find_visual_parent<Window>(this), m_visual_effects);
    }
}
