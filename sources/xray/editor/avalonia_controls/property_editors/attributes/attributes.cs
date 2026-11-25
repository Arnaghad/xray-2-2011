using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using xray.editor_base;

namespace xray.editor.avalonia_controls.property_editors.attributes;

public class collection_attribute : Attribute
{
    public collection_attribute(Func<bool> add_func, Func<object, bool> remove_func)
    {
        this.add_func = add_func;
        this.remove_func = remove_func;
    }

    public collection_attribute(Func<bool> add_func, Func<object, bool> remove_func, Func<int, string, bool> move_func)
    {
        this.add_func = add_func;
        this.remove_func = remove_func;
        this.move_func = move_func;
    }

    public readonly Func<bool> add_func;
    public readonly Func<object, bool> remove_func;
    public readonly Func<int, string, bool>? move_func;
}

public class color_curve_editor_attribute : Attribute
{
    internal Action<double>? limit_setter;
    public Func<bool> recalculate_keys = static () => true;

    public void set_limit(double limit) => limit_setter?.Invoke(limit);
}

public class combo_box_items_attribute : Attribute
{
    public combo_box_items_attribute(params string[] items)
    {
        this.items = new ArrayList();
        foreach (string item in items)
            this.items.Add(item);
    }

    public combo_box_items_attribute(params object[] items)
    {
        is_associative = true;
        this.items = new ArrayList();
        this.values = new ArrayList();
        bool even = true;
        foreach (object item in items)
        {
            if (even)
                this.items.Add(item);
            else
                this.values.Add(item);
            even = !even;
        }
    }

    public combo_box_items_attribute(ArrayList items)
    {
        this.items = items;
    }

    public combo_box_items_attribute(string argument, Func<string, IEnumerable<string>> get_items)
    {
        this.get_items = get_items;
        this.argument = argument;
    }

    public combo_box_items_attribute(Func<int> items_count_func, Func<int, string> get_item_func)
    {
        this.items_count_func = items_count_func;
        this.get_item_func = get_item_func;
    }

    public readonly string? argument;
    public readonly ArrayList? items;
    public readonly ArrayList? values;
    public readonly Func<int>? items_count_func;
    public readonly Func<int, string>? get_item_func;
    public readonly Func<string, IEnumerable<string>>? get_items;
    public readonly bool is_associative;
}

public class control_attribute : Attribute
{
    public control_attribute(control_containers.control control) => m_control = control;
    public control_containers.control m_control;
}

public class control_container_attribute : Attribute
{
    public control_container_attribute(control_containers.control_container control_container) =>
        m_control_container = control_container;

    public control_containers.control_container m_control_container;
}

public class custom_property_attribute : Attribute
{
    public custom_property_attribute(Func<List<string>> box_values_getter, Func<string> box_value_getter, Action<string> box_value_setter)
    {
        this.box_values_getter = box_values_getter;
        this.box_value_getter = box_value_getter;
        this.box_value_setter = box_value_setter;
    }

    public Func<List<string>> box_values_getter;
    public Func<string> box_value_getter;
    public Action<string> box_value_setter;
}

public class data_source_attribute : Attribute
{
    public data_source_attribute() { }

    public data_source_attribute(
        string data_source_type,
        Func<string, IEnumerable<string>> get_items,
        List<string> input_values_list,
        Action<string> input_value_setter,
        Func<string> input_value_getter)
    {
        this.get_items = get_items;
        this.data_source_type = data_source_type;
        this.input_value_setter = input_value_setter;
        this.input_value_getter = input_value_getter;
        this.input_values_list = input_values_list;
    }

    public readonly string? data_source_type;
    public readonly Func<string, IEnumerable<string>>? get_items;
    public readonly Action<string>? input_value_setter;
    public readonly Func<string>? input_value_getter;
    public readonly List<string>? input_values_list;
}

public class expandable_item_attribute : Attribute
{
}

public abstract class extension_attribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public class external_editor_attribute : Attribute
{
    public external_editor_attribute(external_editor_event_handler external_editor_delegate)
        : this(external_editor_delegate, true)
    {
    }

    public external_editor_attribute(external_editor_event_handler external_editor_delegate, bool is_clear_visible)
    {
        m_is_clear_visible = is_clear_visible;
        this.external_editor_delegate = external_editor_delegate ?? throw new ArgumentNullException(nameof(external_editor_delegate));
        m_filter = null;
    }

    public external_editor_attribute(external_editor_event_handler external_editor_delegate, bool is_clear_visible, object filter)
    {
        m_is_clear_visible = is_clear_visible;
        this.external_editor_delegate = external_editor_delegate ?? throw new ArgumentNullException(nameof(external_editor_delegate));
        m_filter = filter;
    }

    public external_editor_attribute(Type editor_type)
    {
        if (editor_type == null || !typeof(i_external_property_editor).IsAssignableFrom(editor_type))
            throw new ArgumentException("Editor Type not valid. Editor Type must implement i_external_property_editor.");
        m_external_editor_type = editor_type;
    }

    public external_editor_attribute(Type editor_type, string additional_data)
    {
        if (editor_type == null || !typeof(i_external_property_editor).IsAssignableFrom(editor_type))
            throw new ArgumentException("Editor Type not valid. Editor Type must implement i_external_property_editor.");
        m_external_editor_type = editor_type;
        m_additional_data = additional_data;
    }

    public external_editor_attribute(Type editor_type, bool can_directly_set)
    {
        m_can_directly_set = can_directly_set;
        if (editor_type == null || !typeof(i_external_property_editor).IsAssignableFrom(editor_type))
            throw new ArgumentException("Editor Type not valid. Editor Type must implement i_external_property_editor.");
        m_external_editor_type = editor_type;
    }

    public readonly external_editor_event_handler? external_editor_delegate;
    public readonly Type? m_external_editor_type;
    public readonly bool m_is_clear_visible;
    public readonly object? m_filter;
    public readonly bool m_can_directly_set;
    public readonly string? m_additional_data;
}

public delegate void external_editor_event_handler(property prop, object? filter);

public class float_curve_editor_attribute : Attribute
{
    public Action<double>? left_limit_setter { get; set; }
    public Action<double>? top_limit_setter { get; set; }
    public Action<double>? right_limit_setter { get; set; }
    public Action<double>? bottom_limit_setter { get; set; }

    public double initial_left_limit = double.NaN;
    public double initial_top_limit = double.NaN;
    public double initial_right_limit = double.NaN;
    public double initial_bottom_limit = double.NaN;

    public List<float_curve> templates = new();

    public void set_left_limit(double limit) => left_limit_setter?.Invoke(limit);
    public void set_top_limit(double limit) => top_limit_setter?.Invoke(limit);
    public void set_right_limit(double limit) => right_limit_setter?.Invoke(limit);
    public void set_bottm_limit(double limit) => bottom_limit_setter?.Invoke(limit);
}

public class material_stage_effect_holder_attribute : Attribute
{
    public material_stage_effect_holder_attribute() => is_can_edit_effect = true;
    public material_stage_effect_holder_attribute(bool is_can_edit_effect) => this.is_can_edit_effect = is_can_edit_effect;
    public bool is_can_edit_effect { get; set; }
}

public class special_attribute : Attribute
{
    public special_attribute(string value) => this.value = value;
    public string value;
}

[AttributeUsage(AttributeTargets.Property)]
public class string_select_file_editor_attribute : Attribute
{
    public string_select_file_editor_attribute() { }

    public string_select_file_editor_attribute(string default_extension, string file_mask, string default_folder, string caption)
    {
        this.caption = caption;
        this.default_extension = default_extension;
        this.file_mask = file_mask;
        this.default_folder = default_folder;
    }

    public string? default_extension;
    public string? file_mask;
    public string? default_folder;
    public string? caption;
}

public class text_box_extension_attribute : extension_attribute
{
    public text_box_extension_attribute(Func<float> getter, Action<float> setter)
    {
        m_getter = getter;
        m_setter = setter;
    }

    public Func<float> m_getter;
    public Action<float> m_setter;
}

public abstract class type_converter_base
{
    public abstract Type base_type { get; }
    public abstract Type converted_type { get; }
    public abstract object? convert(object? obj);
    public abstract object? convert_back(object? obj);
}

public class type_converter_attribute : Attribute
{
    public type_converter_attribute(Type destination_type, Type converter_type)
    {
        m_destination_type = destination_type;
        m_converter_type = converter_type;
    }

    public readonly Type m_destination_type;
    public readonly Type m_converter_type;
}

public class value_range_and_format_attribute : Attribute
{
    public value_range_and_format_attribute(double min_value, double max_value)
    {
        this.min_value = min_value;
        this.max_value = max_value;
        this.precision = 2;
    }

    public value_range_and_format_attribute(double min_value, double max_value, double step_size)
        : this(min_value, max_value)
    {
        this.step_size = step_size;
    }

    public value_range_and_format_attribute(double min_value, double max_value, double step_size, int precision)
        : this(min_value, max_value, step_size)
    {
        this.precision = precision;
    }

    public value_range_and_format_attribute(Func<double> min_value_func, Func<double> max_value_func, double step_size, int precision)
    {
        this.min_value_func = min_value_func;
        this.max_value_func = max_value_func;
        this.step_size = step_size;
        this.precision = precision;
    }

    public Func<double>? min_value_func { get; set; }
    public Func<double>? max_value_func { get; set; }

    public double min_value { get; set; }
    public double max_value { get; set; }
    public double step_size { get; set; }
    public int precision { get; set; }
    public bool update_on_edit_complete { get; set; }

    internal static value_range_and_format_attribute? FromDescriptor(PropertyDescriptor descriptor)
    {
        if (descriptor.Attributes[typeof(value_range_and_format_attribute)] is value_range_and_format_attribute local)
            return local;
        if (descriptor.Attributes[typeof(SdkNumericRangeAttribute)] is SdkNumericRangeAttribute shared)
        {
            return new value_range_and_format_attribute(shared.Minimum, shared.Maximum, shared.Step, shared.Precision)
            {
                update_on_edit_complete = shared.UpdateOnEditComplete
            };
        }
        return null;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class vector3_attribute : Attribute
{
    public vector3_attribute()
    {
        x_name = "x";
        y_name = "y";
        z_name = "z";
        x_min = double.NaN;
        y_min = double.NaN;
        z_min = double.NaN;
        x_max = double.NaN;
        y_max = double.NaN;
        z_max = double.NaN;
        x_step_size = 0.1;
        y_step_size = 0.1;
        z_step_size = 0.1;
        x_precision = 3;
        y_precision = 3;
        z_precision = 3;
    }

    public string x_name { get; set; }
    public string y_name { get; set; }
    public string z_name { get; set; }
    public double x_min { get; set; }
    public double y_min { get; set; }
    public double z_min { get; set; }
    public double x_max { get; set; }
    public double y_max { get; set; }
    public double z_max { get; set; }
    public double x_step_size { get; set; }
    public double y_step_size { get; set; }
    public double z_step_size { get; set; }
    public int x_precision { get; set; }
    public int y_precision { get; set; }
    public int z_precision { get; set; }
}
