using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using xray.editor.avalonia_controls.color_picker;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class color_editor : value_editor_base
{
    private color_helper[]? m_color_helpers;

    public color_editor()
    {
        InitializeComponent();
        m_pick_color_button.Click += pick_color_button_click;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            object?[] values = m_property.values;
            m_color_helpers = new color_helper[values.Length];

            for (int i = 0; i < values.Length; ++i)
            {
                color_rgb rgb = values[i] is Color c ? (color_rgb)c : values[i] is color_rgb r ? r : default;
                m_color_helpers[i] = new color_helper(rgb, m_property) { editor = this };
            }

            if (m_property.is_multiple_values)
                m_multivalue_text_block.Text = "<many>";

            m_pick_color_button.IsEnabled = !m_property.is_read_only;

            if (m_property.descriptors[0].Attributes[typeof(DescriptionAttribute)] is DescriptionAttribute desc)
                ToolTip.SetTip(m_multivalue_text_block, desc.Description);

            if (item_editor?.parent_container != null)
            {
                item_editor.parent_container.is_expand_visible = true;
                item_editor.parent_container.fill_sub_properties = fill_sub_items;
            }
        };
    }

    private void fill_sub_items()
    {
        if (m_property == null || m_color_helpers == null)
            return;

        object[] objects = new object[m_color_helpers.Length];
        m_color_helpers.CopyTo(objects, 0);
        m_property.sub_properties = property_extractor.extract(objects, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }

    private void make_text_unvisible() => m_multivalue_text_block.Text = string.Empty;

    private async void pick_color_button_click(object? sender, RoutedEventArgs e)
    {
        if (m_property == null || m_color_helpers == null)
            return;

        var initial = m_property.value switch
        {
            Color c => (color_rgb)c,
            color_rgb rgb => rgb,
            _ => new color_rgb(0, 0, 0, 0)
        };

        var dlg = new dialog { selected_color = initial };
        bool accepted = false;

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            bool? res = await dlg.ShowDialog<bool?>(owner);
            accepted = res == true;
        }
        else
        {
            dlg.Show();
        }

        if (accepted)
        {
            m_property.is_multiple_values = false;
            m_multivalue_text_block.Text = string.Empty;
            bool isColorType = m_property.type == typeof(Color) || m_property.value is Color;
            m_property.value = isColorType ? (Color)dlg.selected_color : dlg.selected_color;

            color_rgb updatedRgb = m_property.value is Color col ? (color_rgb)col : (color_rgb)m_property.value!;
            foreach (var h in m_color_helpers)
                h.color = updatedRgb;

            if (m_property.sub_properties != null)
            {
                foreach (var sp in m_property.sub_properties)
                    sp.update_owner_editor();
            }
        }
    }

    public override void update() => m_property?.invalidate_value();

    private class color_helper
    {
        public color_helper(color_rgb color, property prop)
        {
            this.color = color;
            this.prop = prop;
        }

        public color_rgb color;
        public readonly property prop;
        public color_editor? editor;

        [value_range_and_format_attribute(0, 1, 0.01, 2)]
        public double a
        {
            get => color.a;
            set { color.a = value; set_color(color); }
        }

        [value_range_and_format_attribute(0, 1, 0.01, 2)]
        public double r
        {
            get => color.r;
            set { color.r = value; set_color(color); }
        }

        [value_range_and_format_attribute(0, 1, 0.01, 2)]
        public double g
        {
            get => color.g;
            set { color.g = value; set_color(color); }
        }

        [value_range_and_format_attribute(0, 1, 0.01, 2)]
        public double b
        {
            get => color.b;
            set { color.b = value; set_color(color); }
        }

        private void set_color(color_rgb new_color)
        {
            prop.is_multiple_values = false;
            prop.value = prop.value is Color ? (Color)new_color : new_color;
            editor?.make_text_unvisible();
        }
    }
}
