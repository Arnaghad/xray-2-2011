using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.property_editors.attributes;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors.item;

public class material_stage_effect_editor : item_editor_base
{
    public static readonly StyledProperty<bool> is_can_edit_effectProperty =
        AvaloniaProperty.Register<material_stage_effect_editor, bool>(nameof(is_can_edit_effect), true);

    public static readonly StyledProperty<external_editor?> effects_editorProperty =
        AvaloniaProperty.Register<material_stage_effect_editor, external_editor?>(nameof(effects_editor));

    private Button? m_delete_button;

    public material_stage_effect_editor()
    {
        effects_editor = new external_editor { DataContext = null };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;
            if (m_property.descriptors[0].Attributes[typeof(material_stage_effect_holder_attribute)] is material_stage_effect_holder_attribute attr)
                is_can_edit_effect = attr.is_can_edit_effect;

            if (parent_container != null)
            {
                parent_container.fill_sub_properties = fill_sub_items_and_return_first;
                parent_container.is_expand_visible = true;
                parent_container.is_expanded = true;
            }
        };
    }

    public bool is_can_edit_effect
    {
        get => GetValue(is_can_edit_effectProperty);
        set => SetValue(is_can_edit_effectProperty, value);
    }

    public external_editor? effects_editor
    {
        get => GetValue(effects_editorProperty);
        set => SetValue(effects_editorProperty, value);
    }

    private void fill_sub_items_and_return_first()
    {
        if (m_property == null)
            return;

        m_property.sub_properties = property_extractor.extract(m_property.values!, m_property, m_property.extract_settings ?? new property_extractor.settings());
        if (m_property.sub_properties.Count > 0)
        {
            var first_property = m_property.sub_properties[0];
            m_property.sub_properties.RemoveAt(0);
            if (effects_editor != null)
                effects_editor.DataContext = first_property;
        }
    }

    private void delete_item_click(object? sender, RoutedEventArgs e)
    {
        if (m_property?.property_parent?.owner_editor is value_collection_editor_base collectionEditor)
            collectionEditor.remove_sub_property(m_property);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        m_delete_button = e.NameScope.Find<Button>("delete_item");
        if (m_delete_button != null)
            m_delete_button.Click += delete_item_click;
    }

    public override void update() => effects_editor?.update();
}
