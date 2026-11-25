using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.color_matrix_editor;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class color_matrix_editor : value_editor_base
{
    public color_matrix_editor()
    {
        InitializeComponent();
        big_editor.Click += big_editor_click;

        DataContextChanged += (_, _) =>
        {
            m_property = DataContext as property;
            if (m_property?.value is color_matrix cm)
                m_color_matrix_editor.edited_color_matrix = cm;
        };
    }

    private void big_editor_click(object? sender, RoutedEventArgs e)
    {
        if (m_property?.value is not color_matrix cm)
            return;

        var matrixEditor = new color_matrix_control { margin = 10 };
        var border = new Border { Padding = new Thickness(4), Child = matrixEditor };
        m_color_matrix_editor.IsEnabled = false;

        var window = utils.new_window_host("Color Matrix Editor", border, 800, 600);
        matrixEditor.edited_color_matrix = cm;
        window.Opened += (_, _) => matrixEditor.fix_control_points_positions();
        window.Closed += (_, _) =>
        {
            window.Content = null;
            m_color_matrix_editor.edited_color_matrix = matrixEditor.edited_color_matrix;
            m_color_matrix_editor.IsEnabled = true;
        };
        window.Show();
    }

    public override void update()
    {
        if (m_property?.value is color_matrix cm)
            m_color_matrix_editor.edited_color_matrix = cm;
    }
}
