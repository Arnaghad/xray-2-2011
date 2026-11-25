using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// Missing resource substitution dialog replacing WinForms ResourceLoadingDialog.
/// </summary>
public partial class SdkResourceLoadingWindow : Window
{
    private string m_classId = string.Empty;

    public SdkResourceLoadingWindow()
    {
        InitializeComponent();

        m_browse_button.Click += async (_, _) =>
        {
            if (BrowseCallback != null)
            {
                string? chosen = BrowseCallback(m_classId, m_substitute_text_box.Text ?? string.Empty);
                if (!string.IsNullOrEmpty(chosen))
                    m_substitute_text_box.Text = chosen;
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Select substitute for [{m_classId}]",
                AllowMultiple = false
            });
            string? picked = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(picked))
                m_substitute_text_box.Text = picked;
        };

        m_replace_button.Click += (_, _) =>
        {
            if (AcceptReplace())
                Close(true);
        };

        m_ignore_button.Click += (_, _) =>
        {
            AcceptIgnore();
            Close(false);
        };
    }

    public Func<string, string, string?>? BrowseCallback { get; set; }
    public subst_resource_result DialogActionResult { get; private set; } = subst_resource_result.ignore_one;
    public string SelectedResourceName => m_substitute_text_box.Text ?? string.Empty;
    public bool DialogAccepted { get; private set; }

    public TextBox SubstituteTextBox => m_substitute_text_box;
    public RadioButton RadioThisObject => m_radio_this_object;
    public RadioButton RadioAllWithName => m_radio_all_with_name;
    public RadioButton RadioAllAnyName => m_radio_all_any_name;

    public void InitializeDialog(string classId, string missingResourceName)
    {
        m_classId = classId;
        m_missing_label.Text = $"Missing [{classId}]: {missingResourceName}";
        m_substitute_text_box.Text = string.Empty;
        m_radio_this_object.IsChecked = true;
        DialogActionResult = subst_resource_result.ignore_one;
        DialogAccepted = false;
    }

    public bool AcceptReplace()
    {
        if (string.IsNullOrWhiteSpace(m_substitute_text_box.Text))
            return false;

        DialogActionResult = m_radio_all_any_name.IsChecked == true
            ? subst_resource_result.subst_all_any_name
            : m_radio_all_with_name.IsChecked == true
                ? subst_resource_result.subst_all_by_name
                : subst_resource_result.subst_one;
        DialogAccepted = true;
        return true;
    }

    public void AcceptIgnore()
    {
        DialogActionResult = m_radio_all_any_name.IsChecked == true
            ? subst_resource_result.ignore_all_any_name
            : m_radio_all_with_name.IsChecked == true
                ? subst_resource_result.ignore_all_by_name
                : subst_resource_result.ignore_one;
        DialogAccepted = false;
    }
}
