using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;
using xray.editor.avalonia_controls.curve_editor.effects;

namespace xray.editor.avalonia_controls.curve_editor;

public partial class panel_curve_item : UserControl
{
    public panel_curve_item()
    {
        InitializeComponent();
    }

    public panel_curve_item(panel_curve_group group) : this()
    {
        m_parent_group = group;
    }

    public static readonly StyledProperty<string> headerProperty =
        AvaloniaProperty.Register<panel_curve_item, string>(nameof(header), "unnamed");

    private bool m_is_selected;
    private float_curve? m_curve;
    private readonly panel_curve_group? m_parent_group;

    internal visual_curve? visual_curve { get; private set; }

    internal curve_editor_panel curves_panel => m_parent_group!.parent_panel;

    internal string header
    {
        get => GetValue(headerProperty);
        set
        {
            SetValue(headerProperty, value);
            m_text_box.Text = value;
        }
    }

    internal string curve_key { get; set; } = string.Empty;

    internal float_curve? curve
    {
        get => m_curve;
        set
        {
            Debug.Assert(m_curve == null);
            m_curve = value;
            if (m_curve == null)
                return;

            visual_curve = curves_panel.add_curve(curve_key, m_curve);
            m_curve_color_rect.Background = new SolidColorBrush(m_curve.color);

            visual_curve.double_click += curve_double_click;
            visual_curve.is_selected_changed += curve_selection_changed;
        }
    }

    public bool is_selected
    {
        get => m_is_selected;
        set
        {
            m_is_selected = value;
            if (visual_curve != null)
                visual_curve.IsVisible = m_is_selected;
            Background = m_is_selected ? SdkWorkspaceTheme.Selection : Brushes.Transparent;
            Foreground = m_is_selected ? SdkWorkspaceTheme.SelectionText : SdkWorkspaceTheme.Text;
        }
    }

    private void mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || m_parent_group == null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            is_selected = !is_selected;
        else
        {
            curves_panel.deselect_all_groups();
            is_selected = true;
        }
        e.Handled = true;
    }

    private void mouse_move(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            is_selected = true;
    }

    private void add_effect_click(object? sender, RoutedEventArgs e)
    {
        if (m_parent_group == null || visual_curve == null || m_parent_group.has_effects)
            return;

        var noise = new float_curve_noise_effect();
        var effect = new visual_noise_effect(visual_curve, noise);
        var effect_item = new panel_curve_effect(this, noise) { name = "noise" };
        effect_item.add_effect(effect);
        m_effects.Items.Add(effect_item);
        effect.open_effect_settings(utils.find_visual_parent<Window>(this));
        e.Handled = true;
    }

    private void curve_double_click()
    {
        if (!curves_panel.double_click_key_adding_enabled)
        {
            curves_panel.deselect_all_groups();
            is_selected = true;
        }
    }

    private void curve_selection_changed()
    {
        if (visual_curve != null)
            m_text_box.FontWeight = visual_curve.is_selected ? FontWeight.SemiBold : FontWeight.Normal;
    }

    internal void remove_effect(panel_curve_effect effect)
    {
        m_effects.Items.Remove(effect);
        if (visual_curve != null && visual_curve.effects.Count == 0)
            visual_curve.path.StrokeDashArray = null;
    }
}
