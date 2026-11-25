using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.curve_editor.effects;

public partial class noise_effect_settings_window : UserControl
{
    public noise_effect_settings_window()
    {
        InitializeComponent();
        m_ceed_number_editor.value_changed += ceed_changed;
        m_frequency_number_editor.value_changed += frequency_changed;
        m_strength_number_editor.value_changed += strength_changed;
        m_fade_in_number_editor.value_changed += fade_in_changed;
        m_fade_out_number_editor.value_changed += fade_out_changed;
    }

    private List<visual_noise_effect>? m_effects;

    internal List<visual_noise_effect>? effects
    {
        get => m_effects;
        set
        {
            m_effects = value;
            if (m_effects != null && m_effects.Count > 0)
            {
                m_ceed_number_editor.value = m_effects[0].seed;
                m_frequency_number_editor.value = m_effects[0].frequency;
                m_strength_number_editor.value = m_effects[0].strength;
                m_fade_in_number_editor.value = m_effects[0].fade_in;
                m_fade_out_number_editor.value = m_effects[0].fade_out;
            }
        }
    }

    private void done_click(object? sender, RoutedEventArgs e)
    {
        utils.find_visual_parent<Window>(this)?.Close();
    }

    private void ceed_changed()
    {
        if (m_effects == null) return;
        foreach (var effect in m_effects)
            effect.seed = (int)m_ceed_number_editor.value;
    }

    private void frequency_changed()
    {
        if (m_effects == null) return;
        foreach (var effect in m_effects)
            effect.frequency = m_frequency_number_editor.value;
    }

    private void strength_changed()
    {
        if (m_effects == null) return;
        foreach (var effect in m_effects)
            effect.strength = m_strength_number_editor.value;
    }

    private void fade_in_changed()
    {
        if (m_effects == null) return;
        foreach (var effect in m_effects)
            effect.fade_in = m_fade_in_number_editor.value;
    }

    private void fade_out_changed()
    {
        if (m_effects == null) return;
        foreach (var effect in m_effects)
            effect.fade_out = m_fade_out_number_editor.value;
    }
}
