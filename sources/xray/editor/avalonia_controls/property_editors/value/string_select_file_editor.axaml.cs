using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using xray.editor.avalonia_controls.property_editors.attributes;

namespace xray.editor.avalonia_controls.property_editors.value;

public partial class string_select_file_editor : value_editor_base
{
    public string_select_file_editor()
    {
        InitializeComponent();
        Browse.Click += browse_click;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not property prop)
                return;
            m_property = prop;
            Browse.IsEnabled = !m_property.is_read_only;
            file_path.Text = m_property.value as string ?? string.Empty;
        };
    }

    private async void browse_click(object? sender, RoutedEventArgs e)
    {
        if (m_property == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
            return;

        foreach (var attribute in m_property.descriptors[0].Attributes.OfType<string_select_file_editor_attribute>())
        {
            var options = new FilePickerOpenOptions
            {
                Title = attribute.caption,
                AllowMultiple = false
            };

            if (!string.IsNullOrWhiteSpace(attribute.default_folder))
                options.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(attribute.default_folder);

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count > 0)
                m_property.value = files[0].Path.LocalPath;

            break;
        }

        file_path.Text = m_property.value as string ?? string.Empty;
    }

    public override void update()
    {
        if (m_property != null)
            file_path.Text = m_property.value as string ?? string.Empty;
    }
}
