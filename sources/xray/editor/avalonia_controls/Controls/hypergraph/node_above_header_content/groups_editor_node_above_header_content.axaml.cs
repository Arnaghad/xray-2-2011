using Avalonia.Controls;

namespace xray.editor.avalonia_controls.hypergraph;

public partial class groups_editor_node_above_header_content : UserControl
{
    public groups_editor_node_above_header_content()
    {
        InitializeComponent();
    }

    public NumericUpDown up_down => m_up_down;
}
