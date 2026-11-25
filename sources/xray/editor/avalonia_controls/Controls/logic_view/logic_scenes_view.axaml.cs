using Avalonia.Controls;
using Avalonia.Input;

namespace xray.editor.avalonia_controls.logic_view;

public partial class logic_scenes_view : Border
{
    public logic_scenes_view()
    {
        InitializeComponent();
        logic_view_height = Height;
    }

    private const double min_view_size = 100;

    public double logic_view_height
    {
        get => Bounds.Height > 0 ? Bounds.Height : Height;
        set
        {
            var grip_h = bottom_grip.Bounds.Height > 0 ? bottom_grip.Bounds.Height : 10;
            var static_offset = grip_h + BorderThickness.Top + BorderThickness.Bottom;
            Height = value;
            logic_hypergraph.bottom_limit = value - static_offset;
        }
    }

    private void on_bottom_drag(object? sender, VectorEventArgs e)
    {
        var grip_h = bottom_grip.Bounds.Height > 0 ? bottom_grip.Bounds.Height : 10;
        var static_offset = grip_h + BorderThickness.Top + BorderThickness.Bottom;
        var new_height = Height + e.Vector.Y;

        if (new_height < logic_hypergraph.field.Bounds.Height + static_offset)
            new_height = logic_hypergraph.field.Bounds.Height + static_offset;
        if (new_height < min_view_size)
            new_height = min_view_size;

        logic_view_height = new_height;
    }
}
