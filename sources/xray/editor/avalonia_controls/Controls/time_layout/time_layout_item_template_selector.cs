using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor.avalonia_controls;

public class time_layout_item_template_selector : IDataTemplate
{
    private int m_row_index;

    public bool Match(object? data) => data is time_layout_item;

    public Control? Build(object? param)
    {
        var control = new time_layout_item_control
        {
            Background = (m_row_index++ % 2 == 0) ? SdkWorkspaceTheme.Surface : SdkWorkspaceTheme.Hover
        };
        return control;
    }
}
