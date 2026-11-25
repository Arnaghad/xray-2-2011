using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

/// <summary>
/// General viewport/grid options page replacing WinForms OptionsPageGeneral.
/// </summary>
public partial class SdkOptionsPageGeneral : UserControl, ISdkOptionsPage
{
    private readonly ISdkSceneViewOptionsTarget m_target;
    private readonly Dictionary<string, object> m_backup = new(StringComparer.Ordinal);
    private bool m_suppressEvents;

    public SdkOptionsPageGeneral()
        : this(new NullSceneViewOptionsTarget())
    {
    }

    public SdkOptionsPageGeneral(ISdkSceneViewOptionsTarget target)
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
        m_focus_distance_box.ValueChanged += (_, _) =>
        {
            if (m_suppressEvents || !m_focus_distance_box.Value.HasValue) return;
            m_target.ViewPointDistance = (float)m_focus_distance_box.Value.Value;
            PageChanged?.Invoke();
        };
        m_mouse_sens_slider.PropertyChanged += (_, e) =>
        {
            if (m_suppressEvents || !string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal)) return;
            m_target.MouseSensitivity = (float)(m_mouse_sens_slider.Value / 100.0);
            PageChanged?.Invoke();
        };
        m_transparency_slider.PropertyChanged += (_, e) =>
        {
            if (m_suppressEvents || !string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal)) return;
            m_target.SelectionRate = (float)(m_transparency_slider.Value / 100.0);
            PageChanged?.Invoke();
        };
        m_gizmo_size_slider.PropertyChanged += (_, e) =>
        {
            if (m_suppressEvents || !string.Equals(e.Property.Name, nameof(Slider.Value), StringComparison.Ordinal)) return;
            m_target.GizmoSize = (float)(m_gizmo_size_slider.Value / 100.0);
            PageChanged?.Invoke();
        };
        m_enable_postprocess_check.IsCheckedChanged += (_, _) =>
        {
            if (m_suppressEvents) return;
            m_target.UsePostProcess = m_enable_postprocess_check.IsChecked == true;
            PageChanged?.Invoke();
        };
        m_draw_grid_check.IsCheckedChanged += (_, _) =>
        {
            if (m_suppressEvents) return;
            m_target.RenderGrid = m_draw_grid_check.IsChecked == true;
            PageChanged?.Invoke();
        };
        m_grid_step_box.ValueChanged += (_, _) =>
        {
            if (m_suppressEvents || !m_grid_step_box.Value.HasValue) return;
            m_target.GridStep = (float)m_grid_step_box.Value.Value;
            PageChanged?.Invoke();
        };
        m_grid_size_box.ValueChanged += (_, _) =>
        {
            if (m_suppressEvents || !m_grid_size_box.Value.HasValue) return;
            m_target.GridDim = (uint)Math.Max(1m, m_grid_size_box.Value.Value);
            PageChanged?.Invoke();
        };
    }

    public event Action? PageChanged;

    public NumericUpDown NearPlaneBox => m_near_plane_box;
    public NumericUpDown FarPlaneBox => m_far_plane_box;
    public CheckBox EnablePostprocessCheck => m_enable_postprocess_check;
    public CheckBox DrawGridCheck => m_draw_grid_check;

    public void ActivatePage()
    {
        if (m_backup.Count == 0)
        {
            m_backup["near_plane"] = m_target.NearPlane;
            m_backup["far_plane"] = m_target.FarPlane;
            m_backup["focus_distance"] = m_target.ViewPointDistance;
            m_backup["mouse_sens"] = m_target.MouseSensitivity;
            m_backup["selection_rate"] = m_target.SelectionRate;
            m_backup["gizmo_size"] = m_target.GizmoSize;
            m_backup["postprocess"] = m_target.UsePostProcess;
            m_backup["draw_grid"] = m_target.RenderGrid;
            m_backup["grid_step"] = m_target.GridStep;
            m_backup["grid_dim"] = m_target.GridDim;
        }

        m_suppressEvents = true;
        try
        {
            m_near_plane_box.Value = Math.Clamp((decimal)m_target.NearPlane, m_near_plane_box.Minimum, m_near_plane_box.Maximum);
            m_far_plane_box.Value = Math.Clamp((decimal)m_target.FarPlane, m_far_plane_box.Minimum, m_far_plane_box.Maximum);
            m_focus_distance_box.Value = Math.Clamp((decimal)m_target.ViewPointDistance, m_focus_distance_box.Minimum, m_focus_distance_box.Maximum);
            m_mouse_sens_slider.Value = Math.Clamp(m_target.MouseSensitivity * 100.0, m_mouse_sens_slider.Minimum, m_mouse_sens_slider.Maximum);
            m_transparency_slider.Value = Math.Clamp(m_target.SelectionRate * 100.0, m_transparency_slider.Minimum, m_transparency_slider.Maximum);
            m_gizmo_size_slider.Value = Math.Clamp(m_target.GizmoSize * 100.0, m_gizmo_size_slider.Minimum, m_gizmo_size_slider.Maximum);
            m_enable_postprocess_check.IsChecked = m_target.UsePostProcess;
            m_draw_grid_check.IsChecked = m_target.RenderGrid;
            m_grid_step_box.Value = Math.Clamp((decimal)m_target.GridStep, m_grid_step_box.Minimum, m_grid_step_box.Maximum);
            m_grid_size_box.Value = Math.Clamp((decimal)m_target.GridDim, m_grid_size_box.Minimum, m_grid_size_box.Maximum);
        }
        finally
        {
            m_suppressEvents = false;
        }
        IsVisible = true;
    }

    public void DeactivatePage() => IsVisible = false;

    public bool Changed()
    {
        if (m_backup.Count == 0)
            return false;

        return Math.Abs((float)m_backup["near_plane"] - m_target.NearPlane) > 1e-4f ||
               Math.Abs((float)m_backup["far_plane"] - m_target.FarPlane) > 1e-4f ||
               Math.Abs((float)m_backup["focus_distance"] - m_target.ViewPointDistance) > 1e-4f ||
               Math.Abs((float)m_backup["mouse_sens"] - m_target.MouseSensitivity) > 1e-4f ||
               Math.Abs((float)m_backup["selection_rate"] - m_target.SelectionRate) > 1e-4f ||
               Math.Abs((float)m_backup["gizmo_size"] - m_target.GizmoSize) > 1e-4f ||
               (bool)m_backup["postprocess"] != m_target.UsePostProcess ||
               (bool)m_backup["draw_grid"] != m_target.RenderGrid ||
               Math.Abs((float)m_backup["grid_step"] - m_target.GridStep) > 1e-4f ||
               (uint)m_backup["grid_dim"] != m_target.GridDim;
    }

    public bool AcceptChanges()
    {
        m_backup.Clear();
        return true;
    }

    public void CancelChanges()
    {
        if (m_backup.Count == 0)
            return;

        m_target.NearPlane = (float)m_backup["near_plane"];
        m_target.FarPlane = (float)m_backup["far_plane"];
        m_target.ViewPointDistance = (float)m_backup["focus_distance"];
        m_target.MouseSensitivity = (float)m_backup["mouse_sens"];
        m_target.SelectionRate = (float)m_backup["selection_rate"];
        m_target.GizmoSize = (float)m_backup["gizmo_size"];
        m_target.UsePostProcess = (bool)m_backup["postprocess"];
        m_target.RenderGrid = (bool)m_backup["draw_grid"];
        m_target.GridStep = (float)m_backup["grid_step"];
        m_target.GridDim = (uint)m_backup["grid_dim"];
        m_backup.Clear();
    }

    public Control GetControl() => this;
}
