using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.curve_editor.effects;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class panel_curve_group : UserControl
{
    public panel_curve_group()
    {
        InitializeComponent();
    }

    public panel_curve_group(curve_editor_panel panel) : this()
    {
        m_parent_panel = panel;
    }

    public panel_curve_group(curve_editor_panel panel, float_curve_group group) : this(panel)
    {
        header = group.name;
        m_curve_group = group;

        foreach (var sub_group in group.groups)
            add_group(sub_group);

        foreach (var sub_curve in group.curves)
            add_curve(sub_curve.name, sub_curve.name, sub_curve);

        foreach (var eff in group.effects)
        {
            if (eff is float_curve_noise_effect noise)
            {
                var effect_control = new panel_curve_effect(this, noise) { name = "noise" };
                add_noise_to_hierarchy(this, noise, effect_control);
                m_effects.Items.Add(effect_control);
            }
        }
    }

    public static readonly StyledProperty<string> headerProperty =
        AvaloniaProperty.Register<panel_curve_group, string>(nameof(header), "unnamed");

    private bool m_is_selected;
    private float_curve_group? m_curve_group;
    private readonly curve_editor_panel? m_parent_panel;

    internal bool has_effects => m_effects.Items.Count > 0;

    public string header
    {
        get => GetValue(headerProperty);
        set
        {
            SetValue(headerProperty, value);
            m_header.Text = value;
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            m_is_selected = value;
            m_header.Background = m_is_selected ? SdkWorkspaceTheme.Selection : Brushes.Transparent;
            m_header.Foreground = m_is_selected ? SdkWorkspaceTheme.SelectionText : SdkWorkspaceTheme.Text;

            foreach (var channel in m_sub_channels.Items)
            {
                if (channel is panel_curve_item item)
                    item.is_selected = value;
                else if (channel is panel_curve_group sub_group)
                    sub_group.is_selected = value;
            }
        }
    }

    public curve_editor_panel parent_panel => m_parent_panel!;

    private void add_effect_click(object? sender, RoutedEventArgs e)
    {
        var noise = new float_curve_noise_effect();
        var effect_control = new panel_curve_effect(this, noise) { name = "noise" };

        m_curve_group?.effects.Add(noise);
        add_noise_to_hierarchy(this, noise, effect_control);
        m_effects.Items.Add(effect_control);
        effect_control.open_effect_settings();
        e.Handled = true;
    }

    private static void add_noise_to_hierarchy(panel_curve_group group, float_curve_noise_effect noise, panel_curve_effect effect_item)
    {
        foreach (var item in group.m_sub_channels.Items.OfType<panel_curve_item>())
        {
            if (item.visual_curve != null)
            {
                var effect = new visual_noise_effect(item.visual_curve, noise);
                effect_item.add_effect(effect);
            }
        }
        foreach (var sub_group in group.m_sub_channels.Items.OfType<panel_curve_group>())
            add_noise_to_hierarchy(sub_group, noise, effect_item);
    }

    private void mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_parent_panel == null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            is_selected = !is_selected;
        }
        else
        {
            m_parent_panel.deselect_all_groups();
            is_selected = true;
        }
        e.Handled = true;
    }

    private static void get_group_curves_r(panel_curve_group group, List<visual_curve> list)
    {
        foreach (var item in group.m_sub_channels.Items)
        {
            if (item is panel_curve_item ci && ci.visual_curve != null)
                list.Add(ci.visual_curve);
            else if (item is panel_curve_group cg)
                get_group_curves_r(cg, list);
        }
    }

    internal void remove_effect(panel_curve_effect effect)
    {
        m_effects.Items.Remove(effect);
        foreach (var item in m_sub_channels.Items.OfType<panel_curve_item>())
        {
            if (item.visual_curve != null && item.visual_curve.effects.Count == 0)
                item.visual_curve.path.StrokeDashArray = null;
        }
    }

    public visual_curve add_curve(string curve_key, string name, float_curve curve)
    {
        var item = new panel_curve_item(this)
        {
            header = name,
            curve_key = curve_key,
            curve = curve,
            is_selected = true
        };
        m_sub_channels.Items.Add(item);
        return item.visual_curve!;
    }

    public panel_curve_group add_group(string name)
    {
        var group = new panel_curve_group(m_parent_panel!) { header = name };
        m_sub_channels.Items.Add(group);
        return group;
    }

    public panel_curve_group add_group(float_curve_group group)
    {
        var panel_group = new panel_curve_group(m_parent_panel!, group);
        m_sub_channels.Items.Add(panel_group);
        return panel_group;
    }

    public curves_link link_group_curves()
    {
        var list = new List<visual_curve>();
        get_group_curves_r(this, list);
        return m_parent_panel!.link_curves(list);
    }

    public void get_selected_curves(List<visual_curve> selected_curves)
    {
        foreach (var channel in m_sub_channels.Items)
        {
            if (channel is panel_curve_group cg)
                cg.get_selected_curves(selected_curves);
            else if (channel is panel_curve_item ci && ci.is_selected && ci.visual_curve != null)
                selected_curves.Add(ci.visual_curve);
        }
    }
}
