using System;

namespace xray.editor.avalonia_controls.property_editors.value;

public class value_editor_base : property_editor_base
{
    public override void update() { }
}

public class value_collection_editor_base : value_editor_base
{
    internal virtual void remove_sub_property(property sub_property) =>
        throw new NotImplementedException("methods must be implemented!");

    internal virtual void move_up_sub_property(property sub_property) { }

    internal virtual void move_down_sub_property(property sub_property) { }
}

public class value_editor_extension_base : value_editor_base
{
}
