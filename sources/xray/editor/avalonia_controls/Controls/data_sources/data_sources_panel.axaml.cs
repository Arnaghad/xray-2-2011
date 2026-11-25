using Avalonia.Controls;

namespace xray.editor.avalonia_controls.data_sources;

public partial class data_sources_panel : UserControl
{
    public data_sources_panel()
    {
        InitializeComponent();
    }

    public ItemsControl data_sources => m_data_sources;
}
