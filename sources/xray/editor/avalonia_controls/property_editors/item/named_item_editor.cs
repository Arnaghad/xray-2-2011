using Avalonia;
using xray.editor.avalonia_controls.property_editors.value;

namespace xray.editor.avalonia_controls.property_editors.item;

public class named_item_editor : item_editor_base
{
    public static readonly StyledProperty<value_editor_base?> value_editorProperty =
        AvaloniaProperty.Register<named_item_editor, value_editor_base?>(nameof(value_editor));

    public named_item_editor()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_property.owner_editor = this;

            if (m_property.inner_properties != null && value_editor_selector != null && parent_container != null)
            {
                foreach (var inner_prop in m_property.inner_properties)
                {
                    var innerEditor = value_editor_selector.select_editor(inner_prop);
                    innerEditor.item_editor = this;
                    parent_container.add_right_pocket_inner(innerEditor);
                    innerEditor.DataContext = inner_prop;
                }
            }

            if (value_editor_selector != null)
            {
                var editor = value_editor_selector.select_editor(m_property);
                editor.DataContext = null;
                editor.item_editor = this;
                value_editor = editor;
                editor.DataContext = m_property;
            }
        };
    }

    public value_editor_base? value_editor
    {
        get => GetValue(value_editorProperty);
        set => SetValue(value_editorProperty, value);
    }

    public override void update() => value_editor?.update();
}
