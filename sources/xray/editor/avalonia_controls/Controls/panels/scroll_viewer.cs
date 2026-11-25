using Avalonia.Controls;
using Avalonia.Input;

namespace xray.editor.avalonia_controls.panels;

public class scroll_viewer : ScrollViewer
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            e.Handled = false;
    }
}
