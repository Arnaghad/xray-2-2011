using System;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class Enum_editor : value_editor_base
{
    private bool m_updating;

    public Enum_editor()
    {
        InitializeComponent();
        combo_box.SelectionChanged += combo_box_selection_changed;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_updating = true;
            m_property = prop;
            combo_box.ItemsSource = Enum.GetNames(m_property.type);
            combo_box.IsEnabled = !m_property.is_read_only;
            combo_box.SelectedItem = m_property.is_multiple_values ? null : m_property.value?.ToString();
            m_updating = false;
        };
    }

    private void combo_box_selection_changed(object? sender, SelectionChangedEventArgs e)
    {
        if (m_updating || m_property == null || combo_box.SelectedItem is not string name)
            return;

        if (Enum.TryParse(m_property.type, name, out object? parsed) &&
            (m_property.is_multiple_values || !Equals(parsed, m_property.value)))
        {
            m_property.value = parsed;
        }
    }

    public override void update()
    {
        if (m_property == null)
            return;

        m_updating = true;
        combo_box.SelectedItem = m_property.is_multiple_values ? null : m_property.value?.ToString();
        m_updating = false;
        m_property.invalidate_value();
    }
}
