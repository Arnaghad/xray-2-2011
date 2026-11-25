using System;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// Pure Avalonia modal window replacing WinForms AboutDialog.
/// </summary>
public sealed class SdkAboutWindow : Window
{
    private readonly SdkAboutView m_view;

    public SdkAboutWindow(uint buildId = 2011, string buildDate = "2011-2026")
    {
        Title = "About X-Ray Engine 2.0 SDK";
        Width = 340;
        Height = 216;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        m_view = new SdkAboutView(buildId, buildDate);
        m_view.CloseRequested += () =>
        {
            DialogAccepted = true;
            Close(true);
        };
        Content = m_view;
    }

    public SdkAboutView View => m_view;
    public string BuildSummary => m_view.BuildSummary;
    public bool DialogAccepted { get; private set; }
    public void ResetDialogResult() => DialogAccepted = false;
}
