using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public interface ISdkOptionsPage
{
    bool AcceptChanges();
    void CancelChanges();
    void ActivatePage();
    void DeactivatePage();
    bool Changed();
    Control GetControl();
    event Action? PageChanged;
}

public interface ISdkOptionsManager
{
    void RegisterPage(string fullName, ISdkOptionsPage page);
    void UnregisterPage(string fullName);
}

public interface ISdkSceneViewOptionsTarget
{
    float NearPlane { get; set; }
    float FarPlane { get; set; }
    float ViewPointDistance { get; set; }
    float MouseSensitivity { get; set; }
    float SelectionRate { get; set; }
    float GizmoSize { get; set; }
    bool UsePostProcess { get; set; }
    bool RenderGrid { get; set; }
    float GridStep { get; set; }
    uint GridDim { get; set; }
}

public interface ISdkInputOptionsAdapter
{
    List<string> GetActionNames();
    List<string> GetActionShortcutsWithContexts(string actionName);
    bool AddShortcut(string actionName, string combination);
    bool RemoveShortcut(string actionName, string combination);
    void ResetToDefaults();
    Dictionary<string, List<string>> CaptureSnapshot();
    void RestoreSnapshot(Dictionary<string, List<string>> snapshot);
}

internal sealed class NullSceneViewOptionsTarget : ISdkSceneViewOptionsTarget
{
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 300.0f;
    public float ViewPointDistance { get; set; } = 70.7f;
    public float MouseSensitivity { get; set; } = 0.3f;
    public float SelectionRate { get; set; } = 1.0f;
    public float GizmoSize { get; set; } = 0.7f;
    public bool UsePostProcess { get; set; } = true;
    public bool RenderGrid { get; set; }
    public float GridStep { get; set; } = 1.0f;
    public uint GridDim { get; set; } = 100;
}

internal sealed class NullInputOptionsAdapter : ISdkInputOptionsAdapter
{
    public List<string> GetActionNames() => new();
    public List<string> GetActionShortcutsWithContexts(string actionName) => new();
    public bool AddShortcut(string actionName, string combination) => false;
    public bool RemoveShortcut(string actionName, string combination) => false;
    public void ResetToDefaults() { }
    public Dictionary<string, List<string>> CaptureSnapshot() => new();
    public void RestoreSnapshot(Dictionary<string, List<string>> snapshot) { }
}
