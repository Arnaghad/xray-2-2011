using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.Controls;

public partial class SdkAboutView : UserControl
{
    private uint m_buildId = 2011;
    private string m_buildDate = "2011-2026";

    public event Action? CloseRequested;

    public SdkAboutView()
    {
        InitializeComponent();
        OkButton.Click += OnOkClicked;
        UpdateLabels();
    }

    public SdkAboutView(uint buildId, string buildDate) : this()
    {
        m_buildId = buildId;
        m_buildDate = buildDate;
        UpdateLabels();
    }

    public uint BuildId
    {
        get => m_buildId;
        set
        {
            m_buildId = value;
            UpdateLabels();
        }
    }

    public string BuildDate
    {
        get => m_buildDate;
        set
        {
            m_buildDate = value ?? string.Empty;
            UpdateLabels();
        }
    }

    public string BuildSummary => $"X-Ray Engine 2.0 SDK · build #{m_buildId},   built on {m_buildDate}";

    private void UpdateLabels()
    {
        BuildTextBlock.Text = $" build #{m_buildId},   built on {m_buildDate}";
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}
