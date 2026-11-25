using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public partial class SdkIdeLogView : UserControl
{
    private readonly ObservableCollection<string> m_messages = new();

    public SdkIdeLogView()
    {
        InitializeComponent();

        m_log_list_box.ItemsSource = m_messages;
        m_clear_button.Click += (_, _) => ClearMessages();
        m_close_button.Click += (_, _) => CloseRequested?.Invoke();
    }

    public IReadOnlyList<string> Messages => m_messages;
    public event Action? Cleared;
    public event Action? CloseRequested;

    public void AddMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        m_messages.Add(message);
    }

    public void AddWarning(string warning) => AddMessage("[WARN] " + warning);
    public void AddError(string error) => AddMessage("[ERROR] " + error);
    public void ClearMessages()
    {
        m_messages.Clear();
        Cleared?.Invoke();
    }
}
