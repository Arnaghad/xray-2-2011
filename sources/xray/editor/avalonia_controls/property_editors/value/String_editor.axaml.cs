using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class string_editor : value_editor_base
{
    public string_editor()
    {
        InitializeComponent();
        m_text_box.AddHandler(KeyDownEvent, text_box_key_down, RoutingStrategies.Tunnel);
        input.AddPreviewInputCommandHandler(m_text_box, handle_input);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;

            m_property = prop;
            m_text_box.IsEnabled = !m_property.is_read_only;
        };
    }

    private void text_box_key_down(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            Focus();
    }

    private static void handle_input(object? sender, InputCommandEventArgs e) => e.Handled = true;

    public override void update() => m_property?.invalidate_value();
}
