using System;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// Project options page replacing WinForms OptionsPageProject.
/// </summary>
public partial class SdkOptionsPageProject : UserControl, ISdkOptionsPage
{
    private readonly Func<bool> m_getter;
    private readonly Action<bool> m_setter;
    private bool? m_backupTrack;
    private bool m_suppressEvents;

    public SdkOptionsPageProject()
        : this(static () => false, static _ => { })
    {
    }

    public SdkOptionsPageProject(Func<bool> getTrackActiveItem, Action<bool> setTrackActiveItem)
    {
        m_getter = getTrackActiveItem ?? throw new ArgumentNullException(nameof(getTrackActiveItem));
        m_setter = setTrackActiveItem ?? throw new ArgumentNullException(nameof(setTrackActiveItem));
        InitializeComponent();

        m_track_active_item_check.IsCheckedChanged += (_, _) =>
        {
            if (m_suppressEvents)
                return;
            m_setter(m_track_active_item_check.IsChecked == true);
            PageChanged?.Invoke();
        };
    }

    public event Action? PageChanged;
    public CheckBox TrackActiveItemCheck => m_track_active_item_check;

    public void ActivatePage()
    {
        m_backupTrack ??= m_getter();
        m_suppressEvents = true;
        try
        {
            m_track_active_item_check.IsChecked = m_getter();
        }
        finally
        {
            m_suppressEvents = false;
        }
        IsVisible = true;
    }

    public void DeactivatePage() => IsVisible = false;
    public bool Changed() => m_backupTrack.HasValue && m_backupTrack.Value != m_getter();

    public bool AcceptChanges()
    {
        m_backupTrack = null;
        return true;
    }

    public void CancelChanges()
    {
        if (m_backupTrack.HasValue)
            m_setter(m_backupTrack.Value);
        m_backupTrack = null;
    }

    public Control GetControl() => this;
}
