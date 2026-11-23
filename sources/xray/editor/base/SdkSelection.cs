using System;
using System.Runtime.InteropServices;
namespace xray.editor_base;
[StructLayout(LayoutKind.Sequential)]
public struct EditorSelectionSettings
{
    public uint Version,Size,Enabled,Color;
    public float Rate,Time,Width,Reserved;
    public readonly void Validate() {
        if(Version!=1 || Size!=32 || Enabled>1 || Reserved!=0 || !float.IsFinite(Rate) || Rate<0 || Rate>10 || !float.IsFinite(Time) || Time<0 || Time>1e6f || !float.IsFinite(Width) || Width<1 || Width>8)
            throw new ArgumentException("Invalid editor selection settings.");
    }
    public static EditorSelectionSettings Capture(SceneViewPanel panel,float time)=>new(){Version=1,Size=32,Enabled=panel.SelectionOutlines?1u:0,Color=unchecked((uint)panel.selection_color.ToArgb()),
        Rate=panel.m_selection_rate_val,Time=panel.SelectionTimeOverride??time,Width=2};
}
public sealed unsafe partial class SdkNativeScene
{
    public void SetEditorSelection(Project project,EditorSelectionSettings settings) {
        settings.Validate();var request=ViewRequest(project,13,&settings,null);
        if(Invoke(ref request)<0)throw new InvalidOperationException("Native selection configuration rejected.");
    }
}
