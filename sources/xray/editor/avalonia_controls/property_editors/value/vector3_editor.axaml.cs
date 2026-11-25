using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.converters;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class vector3_editor : value_editor_base
{
    private property_container[]? m_vector_helpers;
    private readonly Vector3D_to_string_converter m_converter;

    public vector3_editor()
    {
        InitializeComponent();
        m_converter = new Vector3D_to_string_converter(_ => fill_sub_items());

        m_text_box.KeyDown += text_box_preview_key_down;
        m_text_box.LostFocus += text_box_lost_focus;
        input.AddPreviewInputCommandHandler(m_text_box, handle_input);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;

            object?[] values = m_property.values;
            m_vector_helpers = new property_container[values.Length];

            for (int i = 0; i < values.Length; ++i)
            {
                var obj = new vector3D_helper(m_property, i);
                var container = new property_container();
                var x_desc = container.properties.add_from_obj(obj, "x");
                var y_desc = container.properties.add_from_obj(obj, "y");
                var z_desc = container.properties.add_from_obj(obj, "z");

                if (m_property.descriptor.Attributes[typeof(vector3_attribute)] is vector3_attribute vec_attr)
                {
                    x_desc.display_name = vec_attr.x_name;
                    y_desc.display_name = vec_attr.y_name;
                    z_desc.display_name = vec_attr.z_name;

                    x_desc.dynamic_attributes.add(new value_range_and_format_attribute(vec_attr.x_min, vec_attr.x_max, vec_attr.x_step_size, vec_attr.x_precision));
                    y_desc.dynamic_attributes.add(new value_range_and_format_attribute(vec_attr.y_min, vec_attr.y_max, vec_attr.y_step_size, vec_attr.y_precision));
                    z_desc.dynamic_attributes.add(new value_range_and_format_attribute(vec_attr.z_min, vec_attr.z_max, vec_attr.z_step_size, vec_attr.z_precision));
                }

                m_vector_helpers[i] = container;
            }

            m_text_box.IsEnabled = !m_property.is_read_only;

            if (m_property.descriptors[0].Attributes[typeof(DescriptionAttribute)] is DescriptionAttribute desc)
                ToolTip.SetTip(m_text_box, desc.Description);

            if (item_editor?.parent_container != null)
            {
                item_editor.parent_container.is_expand_visible = true;
                item_editor.parent_container.fill_sub_properties = fill_sub_items;
            }

            update();
        };
    }

    private void fill_sub_items()
    {
        if (m_property == null || m_vector_helpers == null)
            return;
        m_property.sub_properties = property_extractor.extract(m_vector_helpers, m_property, m_property.extract_settings ?? new property_extractor.settings());
    }

    private static void handle_input(object? sender, InputCommandEventArgs e) => e.Handled = true;

    private void commit_text()
    {
        if (m_property == null || m_text_box.Text == "<many>")
            return;
        m_property.value = m_converter.ConvertBack(m_text_box.Text, typeof(Vector3), null, CultureInfo.InvariantCulture);
        item_editor?.parent_container?.update_hierarchy();
    }

    private void text_box_preview_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            update();
            Focus();
            return;
        }

        if (e.Key != Key.Return && e.Key != Key.Enter)
            return;

        commit_text();
        Focus();
    }

    private void text_box_lost_focus(object? sender, RoutedEventArgs e) => commit_text();

    public override void update()
    {
        if (m_property == null)
            return;
        m_text_box.Text = m_property.is_multiple_values
            ? "<many>"
            : m_converter.Convert(m_property.value, typeof(string), null, CultureInfo.InvariantCulture)?.ToString() ?? string.Empty;
    }

    private class vector3D_helper
    {
        public vector3D_helper(property prop, int index)
        {
            this.prop = prop;
            this.index = index;
        }

        [Browsable(false)]
        public Vector3 vector => (Vector3)prop.get_descriptor_value(prop.property_owners[index], prop.descriptors[index])!;

        public readonly property prop;
        public readonly int index;

        public double x
        {
            get => vector.X;
            set { var vec = vector; vec.X = (float)value; set_parent_vector(vec); }
        }

        public double y
        {
            get => vector.Y;
            set { var vec = vector; vec.Y = (float)value; set_parent_vector(vec); }
        }

        public double z
        {
            get => vector.Z;
            set { var vec = vector; vec.Z = (float)value; set_parent_vector(vec); }
        }

        private void set_parent_vector(Vector3 vec)
        {
            prop.set_descriptor_value(prop.property_owners[index], prop.descriptors[index], vec);
            prop.invalidate_value();
        }
    }
}
