using System;
using Avalonia;

namespace xray.editor.avalonia_controls.property_editors.item;

public class item_editor_base : property_editor_base
{
    public static readonly DirectProperty<item_editor_base, double> item_indentProperty =
        AvaloniaProperty.RegisterDirect<item_editor_base, double>(
            nameof(item_indent),
            o => o.item_indent);

    protected item_editor_base()
    {
        item_editor = this;
    }

    internal property_items_control? parent_container;
    internal item_editor_selector? item_editor_selector;
    internal value_editor_selector? value_editor_selector;

    protected override Type StyleKeyOverride => GetType();

    public double item_indent =>
        parent_container != null ? parent_container.level * settings.hierarchical_item_indent : 0;

    public virtual void reset_value_editor() { }
    public override void update() { }
}
