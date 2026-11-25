using System.Collections.Generic;
using Avalonia.Controls;
using xray.editor.avalonia_controls.property_editors;

namespace xray.editor.avalonia_controls.data_sources;

public partial class data_source_item : UserControl
{
    public data_source_item()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext == null)
                return;

            var prop = property_extractor.extract<List<property>>(DataContext);
            if (prop.Count > 2)
                m_data_source_editor.Content = prop[2];
            else if (prop.Count > 0)
                m_data_source_editor.Content = prop[0];
        };
    }
}
