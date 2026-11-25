using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.item;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors;

internal static class default_editors_lists
{
    private static readonly List<editor_picker> m_item_editors =
    [
        new attribute_editor_picker(typeof(control_container_attribute), typeof(control_container_editor)),
        new attribute_editor_picker(typeof(data_source_attribute), typeof(data_source_editor)),
        new attribute_editor_picker(typeof(custom_property_attribute), typeof(custom_item_editor)),
        new attribute_editor_picker(typeof(special_attribute), o => ((special_attribute)o).value == "material_stage_holder", typeof(material_stage_editor)),
        new attribute_editor_picker(typeof(material_stage_effect_holder_attribute), typeof(material_stage_effect_editor))
    ];

    private static readonly List<editor_picker> m_value_editors =
    [
        new attribute_editor_picker(typeof(text_box_extension_attribute), typeof(text_box_editor_extension)),
        new attribute_editor_picker(typeof(control_attribute), typeof(control_editor)),
        new attribute_editor_picker(typeof(external_editor_attribute), typeof(external_editor)),
        new attribute_editor_picker(typeof(combo_box_items_attribute), typeof(combo_box_editor)),

        new type_editor_picker(typeof(float_curve), typeof(float_curve_editor)),
        new type_editor_picker(typeof(List<float_curve>), typeof(float_curve_editor)),

        new attribute_editor_picker(typeof(collection_attribute), typeof(properties_collection_editor)),
        new assignable_type_editor_picker(typeof(ICollection), typeof(IList_collection_editor), true),
        new assignable_type_editor_picker(typeof(property_container_base), typeof(property_container_editor)),
        new attribute_editor_picker(typeof(string_select_file_editor_attribute), typeof(string_select_file_editor)),
        new type_editor_picker(typeof(Vector3), typeof(vector3_editor)),
        new type_editor_picker(typeof(bool), typeof(Boolean_editor)),
        new type_editor_picker(typeof(Color), typeof(color_editor)),
        new type_editor_picker(typeof(color_rgb), typeof(color_editor)),
        new type_editor_picker(typeof(color_matrix), typeof(value.color_matrix_editor)),
        new type_editor_picker(typeof(color_curve), typeof(color_curve_editor)),
        new type_editor_picker(typeof(int), typeof(int32_editor)),
        new type_editor_picker(typeof(uint), typeof(int32_editor)),
        new type_editor_picker(typeof(string), typeof(string_editor)),
        new type_editor_picker(typeof(float), typeof(single_editor)),
        new type_editor_picker(typeof(double), typeof(single_editor)),
        new assignable_type_editor_picker(typeof(Enum), typeof(Enum_editor))
    ];

    internal static void initialize() { }

    public static List<editor_picker> item_editors => m_item_editors;
    public static List<editor_picker> value_editors => m_value_editors;
}

public class value_editor_selector
{
    public value_editor_selector(List<editor_picker> editors) => m_editors = editors;

    private readonly List<editor_picker> m_editors;

    public value_editor_base select_editor(property prop)
    {
        Debug.Assert(prop != null);
        foreach (var prop_editor in m_editors.Where(prop_editor => prop_editor.can_edit(prop)))
            return (value_editor_base)Activator.CreateInstance(prop_editor.editor_type)!;
        return new default_editor();
    }

    public value_editor_base select_editor_without_extendion(property prop)
    {
        Debug.Assert(prop != null);
        foreach (var prop_editor in m_editors.Where(prop_editor => prop_editor.can_edit(prop) && !typeof(value_editor_extension_base).IsAssignableFrom(prop_editor.editor_type)))
            return (value_editor_base)Activator.CreateInstance(prop_editor.editor_type)!;
        return new default_editor();
    }

    public void select_sub_editors(ItemsControl itemsCtrl, List<property>? sub_properties)
    {
        if (sub_properties == null)
            return;

        foreach (var property in sub_properties)
        {
            var editor = select_editor(property);
            itemsCtrl.DataContext = property;
            itemsCtrl.Items.Add(editor);
            editor.DataContext = property;
        }
    }
}

public class item_editor_selector
{
    public item_editor_selector(List<editor_picker> item_editors) => m_item_editors = item_editors;

    public item_editor_selector(List<editor_picker> item_editors, Type default_editor_type)
    {
        m_item_editors = item_editors;
        m_default_editor_type = default_editor_type;
    }

    private readonly List<editor_picker> m_item_editors;
    private readonly Type? m_default_editor_type;

    public item_editor_base select_editor(object item)
    {
        var prop = (property)item;
        foreach (var item_editor in m_item_editors.Where(ie => ie.can_edit(prop)))
        {
            var ret = (item_editor_base)Activator.CreateInstance(item_editor.editor_type)!;
            ret.item_editor_selector = this;
            return ret;
        }

        var def = (item_editor_base)Activator.CreateInstance(m_default_editor_type ?? typeof(named_item_editor))!;
        def.item_editor_selector = this;
        return def;
    }

    public void select_sub_editors(ItemsControl itemsCtrl, List<property> sub_properties)
    {
        foreach (var property in sub_properties)
        {
            var editor = select_editor(property);
            editor.DataContext = null;
            itemsCtrl.Items.Add(editor);
            editor.DataContext = property;
        }
    }

    public List<property_editor_base> select_sub_editors(List<property> sub_properties)
    {
        var list = new List<property_editor_base>();
        foreach (var property in sub_properties)
            list.Add(select_editor(property));
        return list;
    }
}

public class property_items_control : expandable_items_control
{
    public property_items_control(property prop, item_editor_selector item_editor_selector, value_editor_selector value_editor_selector, int level)
    {
        m_level = level;
        m_item_editor_selector = item_editor_selector;
        m_value_editor_selector = value_editor_selector;
        m_property = prop;
        DataContext = prop;
    }

    private readonly int m_level;

    public item_editor_selector m_item_editor_selector;
    public value_editor_selector m_value_editor_selector;
    public property? m_property;
    public bool is_create_properties;
    public Action? fill_sub_properties;
    public Action<property_items_control>? decorate_container;

    public int level => m_level;

    public item_editor_base? item_editor
    {
        get => Header as item_editor_base;
        set => Header = value;
    }

    public new string name => m_property?.name ?? string.Empty;

    public virtual void clear_sub_items()
    {
        if (m_property?.sub_properties != null)
        {
            foreach (var p in m_property.sub_properties)
                p.destroy();
            m_property.sub_properties.Clear();
        }

        foreach (var obj in Items)
        {
            if (obj is property_items_control container)
            {
                container.m_property = null;
                container.DataContext = null;
                if (container.item_editor != null)
                    container.item_editor.DataContext = null;
            }
        }

        Items.Clear();
    }

    public virtual void fill_sub_items()
    {
        if (fill_sub_properties == null)
            return;

        fill_sub_properties();
        if (m_property?.sub_properties == null)
            return;

        foreach (var prop in m_property.sub_properties)
        {
            var container = create_container_for_property(prop);
            Items.Add(container);
            decorate_container?.Invoke(container);
        }
    }

    public void reset_sub_properties()
    {
        clear_sub_items();
        fill_sub_items();
    }

    protected override void on_expanded_changed()
    {
        if (is_create_properties && Items.Count == 0)
        {
            clear_sub_items();
            fill_sub_items();
        }
        base.on_expanded_changed();
    }

    internal virtual property_items_control create_container_for_property(property prop) =>
        new(prop, m_item_editor_selector, m_value_editor_selector, m_level + 1);

    internal virtual void remove_sub_container(int index) => Items.RemoveAt(index);

    internal void insert_container_for_property(int index, property prop)
    {
        var container = create_container_for_property(prop);
        Items.Insert(index, container);
        container.ApplyTemplate();
        decorate_container?.Invoke(container);
    }

    internal void update_hierarchy()
    {
        item_editor?.update();
        foreach (var obj in Items)
        {
            if (obj is property_items_control control)
                control.update_hierarchy();
        }
    }

    public override string ToString() => name;
}
