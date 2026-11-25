using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// View-panel clipping options page replacing WinForms OptionsPageViewPanel.
/// </summary>
public partial class SdkOptionsPageViewPanel : UserControl, ISdkOptionsPage
{
    private readonly ISdkSceneViewOptionsTarget m_target;
    private readonly Dictionary<string, float> m_backup = new(StringComparer.Ordinal);
    private bool m_suppressEvents;

    public SdkOptionsPageViewPanel()
        : this(new NullSceneViewOptionsTarget())
    {
    }

    public SdkOptionsPageViewPanel(ISdkSceneViewOptionsTarget target)
    {
        m_target = target ?? throw new ArgumentNullException(nameof(target));
        InitializeComponent();

        m_near_plane_box.ValueChanged += (_, _) =>
        {
            if (m_suppressEvents || !m_near_plane_box.Value.HasValue) return;
            m_target.NearPlane = (float)m_near_plane_box.Value.Value;
            PageChanged?.Invoke();
        };
        m_far_plane_box.ValueChanged += (_, _) =>
        {
            if (m_suppressEvents || !m_far_plane_box.Value.HasValue) return;
            m_target.FarPlane = (float)m_far_plane_box.Value.Value;
            PageChanged?.Invoke();
        };
    }

    public event Action? PageChanged;
    public NumericUpDown NearPlaneBox => m_near_plane_box;
    public NumericUpDown FarPlaneBox => m_far_plane_box;

    public void ActivatePage()
    {
        if (m_backup.Count == 0)
        {
            m_backup["near_plane"] = m_target.NearPlane;
            m_backup["far_plane"] = m_target.FarPlane;
        }

        m_suppressEvents = true;
        try
        {
            m_near_plane_box.Value = Math.Clamp((decimal)m_target.NearPlane, m_near_plane_box.Minimum, m_near_plane_box.Maximum);
            m_far_plane_box.Value = Math.Clamp((decimal)m_target.FarPlane, m_far_plane_box.Minimum, m_far_plane_box.Maximum);
        }
        finally
        {
            m_suppressEvents = false;
        }
        IsVisible = true;
    }

    public void DeactivatePage() => IsVisible = false;

    public bool Changed() =>
        m_backup.Count > 0 &&
        (Math.Abs(m_backup["near_plane"] - m_target.NearPlane) > 1e-4f ||
         Math.Abs(m_backup["far_plane"] - m_target.FarPlane) > 1e-4f);

    public bool AcceptChanges()
    {
        m_backup.Clear();
        return true;
    }

    public void CancelChanges()
    {
        if (m_backup.Count == 0)
            return;

        m_target.NearPlane = m_backup["near_plane"];
        m_target.FarPlane = m_backup["far_plane"];
        m_backup.Clear();
    }

    public Control GetControl() => this;
}
