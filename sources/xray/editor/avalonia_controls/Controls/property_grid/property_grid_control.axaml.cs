using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using xray.editor.avalonia_controls.property_editors;
using xray.editor.avalonia_controls.property_editors.item;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_grid;

public partial class property_grid_control : ContentControl
{
    public static readonly AttachedProperty<double> NameColumnLimitProperty =
        AvaloniaProperty.RegisterAttached<property_grid_control, Control, double>(
            "NameColumnLimit",
            double.PositiveInfinity,
            inherits: true);

    public static double GetNameColumnLimit(AvaloniaObject target) =>
        target.GetValue(NameColumnLimitProperty);

    public static void SetNameColumnLimit(AvaloniaObject target, double value) =>
        target.SetValue(NameColumnLimitProperty, value);

    private object[]? m_selected_objects;
    private readonly List<editor_picker> m_value_editors;
    private readonly List<editor_picker> m_item_editors;
    internal readonly value_editor_selector m_value_editor_selector;
    internal readonly item_editor_selector m_item_editor_selector;
    private bool m_read_only_mode;

    private List<property>? m_properties;
    private readonly List<property_grid_item> m_grid_items = new();
    private readonly List<Attribute> m_browsable_attributes = new();
    private readonly ObservableCollection<property_category_group> m_categories = new();

    private bool m_auto_update;
    private bool m_is_focused;
    private DispatcherTimer? m_auto_update_timer;
    private readonly hierarchy_node m_root_hierarchy_node = new() { name = "root" };

    public property_grid_control()
    {
        m_properties = new List<property>();
        m_value_editors = default_editors_lists.value_editors;
        m_item_editors = default_editors_lists.item_editors;
        m_value_editor_selector = new value_editor_selector(m_value_editors);
        m_item_editor_selector = new item_editor_selector(m_item_editors, typeof(named_item_editor));

        selected_properties = new List<property>();
        remember_expanded_hierarchy = true;

        InitializeComponent();

        m_groups_panel.ItemsSource = m_categories;
        m_root_border.PointerPressed += mouse_down;
        GotFocus += (_, _) => m_is_focused = true;
        LostFocus += (_, _) => m_is_focused = false;
        SizeChanged += property_grid_size_changed;
        DetachedFromVisualTree += (_, _) => stop_auto_update_timer();
    }

    private void property_grid_size_changed(object? sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            SetValue(NameColumnLimitProperty, Math.Max(0, (e.NewSize.Width - 40) * 0.56));
    }

    internal bool setting_values { get; private set; }
    internal hierarchy_node root_hierarchy_node => m_root_hierarchy_node;

    public ObservableCollection<property_category_group> data_view => m_categories;
    public IReadOnlyList<property_grid_item> grid_items => m_grid_items;

    public object? selected_object
    {
        get => m_selected_objects == null || m_selected_objects.Length == 0 ? null : m_selected_objects[0];
        set => selected_objects = value != null ? new[] { value } : null;
    }

    public object[]? selected_objects
    {
        get => m_selected_objects;
        set
        {
            m_selected_objects = value;
            fill_properties_from_objects();
        }
    }

    public bool auto_update
    {
        get => m_auto_update;
        set
        {
            m_auto_update = value;
            if (m_auto_update)
                start_auto_update_timer();
            else
                stop_auto_update_timer();
        }
    }

    public bool read_only_mode
    {
        get => m_read_only_mode;
        set
        {
            bool changed = m_read_only_mode != value;
            m_read_only_mode = value;
            if (changed)
                reset();
        }
    }

    public List<Attribute> browsable_attributes => m_browsable_attributes;
    public browsable_state browsable_attribute_state { get; set; }
    public bool browsable_attribute_affects_children { get; set; }
    public List<property> selected_properties { get; set; }
    public bool remember_expanded_hierarchy { get; set; }

    public event value_changed_event_handler? property_value_changed;
    public event EventHandler? refresh_property_changed;

    private void start_auto_update_timer()
    {
        m_auto_update_timer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.05) };
        m_auto_update_timer.Tick -= on_auto_update_tick;
        m_auto_update_timer.Tick += on_auto_update_tick;
        m_auto_update_timer.Start();
    }

    private void stop_auto_update_timer() => m_auto_update_timer?.Stop();

    private void on_auto_update_tick(object? sender, EventArgs e)
    {
        if (!m_auto_update || m_is_focused || m_properties == null)
            return;
        update();
    }

    private static void invalidate_properties(IEnumerable? properties)
    {
        if (properties == null)
            return;

        foreach (property prop in properties)
        {
            prop.is_valid = false;
            invalidate_properties(prop.sub_properties);
        }
    }

    private void fill_properties_from_objects()
    {
        if (remember_expanded_hierarchy && m_grid_items.Count > 0)
        {
            m_root_hierarchy_node.sub_nodes.Clear();
            save_expanded_hierarchy(m_grid_items, m_root_hierarchy_node);
        }

        invalidate_properties(m_properties);
        m_categories.Clear();
        m_grid_items.Clear();
        m_properties?.Clear();

        m_properties = selected_objects != null && selected_objects.Length != 0 ? new List<property>() : null;
        selected_properties.Clear();

        if (m_properties == null)
        {
            update_empty_state();
            return;
        }

        var settings = new property_extractor.settings
        {
            browsable_attribute_affects_children = browsable_attribute_affects_children,
            browsable_attribute_state = browsable_attribute_state,
            browsable_attributes = m_browsable_attributes,
            read_only_mode = read_only_mode,
            create_prop_delegate = () => new property(this)
        };

        m_properties = property_extractor.extract<List<property>>(selected_objects!, null, settings);

        var groupsByName = new Dictionary<string, property_category_group>(StringComparer.Ordinal);
        foreach (var prop in m_properties)
        {
            var item = new property_grid_item(
                prop,
                m_item_editor_selector,
                m_value_editor_selector,
                0,
                m_root_hierarchy_node.sub_nodes.Find(node => node.name == prop.name));

            m_grid_items.Add(item);

            string catName = "Misc";
            if (prop.descriptors.Count > 0 &&
                prop.descriptors[0].Attributes[typeof(CategoryAttribute)] is CategoryAttribute cat &&
                !string.IsNullOrWhiteSpace(cat.Category))
            {
                catName = cat.Category.Trim();
            }

            if (!groupsByName.TryGetValue(catName, out var group))
            {
                group = new property_category_group(catName);
                groupsByName.Add(catName, group);
                m_categories.Add(group);
            }
            group.Items.Add(item);
        }

        update_empty_state();
    }

    private void update_empty_state()
    {
        bool hasItems = m_categories.Count > 0;
        m_header_border.IsVisible = hasItems;
        m_empty_text.IsVisible = !hasItems;
    }

    private void mouse_down(object? sender, PointerPressedEventArgs e) => deselect_all_properties();

    private static void save_expanded_hierarchy(IEnumerable items, hierarchy_node node)
    {
        foreach (var obj in items)
        {
            if (obj is property_grid_item item && item.is_expanded)
            {
                var hierarchy_node = new hierarchy_node { name = item.name };
                node.sub_nodes.Add(hierarchy_node);
                save_expanded_hierarchy(item.Items, hierarchy_node);
            }
        }
    }

    public void deselect_all_properties()
    {
        for (int i = selected_properties.Count - 1; i >= 0; --i)
            selected_properties[i].is_selected = false;
    }

    internal void on_property_changed(property prop, object? old_value) =>
        property_value_changed?.Invoke(this, new value_changed_event_args(prop, old_value));

    internal void on_refresh_property_changed() =>
        refresh_property_changed?.Invoke(this, EventArgs.Empty);

    public void set_properties_values(object? value)
    {
        setting_values = true;

        foreach (var property in selected_properties)
        {
            try
            {
                if (property.owner_editor is property_container_editor pce)
                {
                    if (pce.inner_property != null)
                        pce.inner_property.value = value;
                }
                else if (property.owner_editor is named_item_editor nie &&
                         nie.value_editor is property_container_editor innerPce)
                {
                    if (innerPce.inner_property != null)
                        innerPce.inner_property.value = value;
                }
                else
                {
                    property.value = value;
                }
            }
            catch (InvalidCastException)
            {
            }
        }

        setting_values = false;
    }

    public void reset() => selected_objects = selected_objects;

    public void update()
    {
        foreach (var control in m_grid_items)
            control.update_hierarchy();
    }

    public void remove_object_from_selection(object @object)
    {
        if (m_selected_objects != null && ((IList)m_selected_objects).Contains(@object))
        {
            var remaining = m_selected_objects.Where(o => o != @object).ToArray();
            selected_objects = remaining;
        }
    }
}
