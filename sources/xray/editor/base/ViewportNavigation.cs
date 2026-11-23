using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace xray.editor_base;

// Runtime camera navigation has no content command/history entry. Save writes
// the current camera metadata together with the next explicit project save.
public sealed class ViewportNavigation : IDisposable
{
    private readonly WindowIde ide;
    private readonly IProjectViewportScene scene;
    private readonly Control view;
    private readonly HashSet<Keys> held=new();
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private double lastTick;
    private Point previous;
    private Vector3 orbitCenter;
    private MouseButtons gesture;
    private bool orbit,ending,disposed;
    private readonly int owner=Environment.CurrentManagedThreadId;
    private void Check() {if(disposed || owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Closed or foreign viewport navigation.");}
    private SceneViewPanel Panel=>ide.ViewWindow;
    private bool CanNavigate=>!ide.RuntimeActive && (ide.Project.IsProjectOpen || !ide.BackgroundMode);
    public bool Active=>gesture!=MouseButtons.None;
    public bool Flying=>Panel.WsadMode || gesture==MouseButtons.Right || (!ide.Project.IsProjectOpen && !ide.BackgroundMode);
    public ViewportNavigation(WindowIde window,IProjectViewportScene backend)
    {
        ide=window;scene=backend;view=Panel.view_panel;
        Panel.RenderModeSupported=ViewportSettings.SupportsMode;
        if(!ViewportSettings.SupportsMode(Panel.RenderViewModeIndex))Panel.SetRenderViewMode("lit");
        view.MouseDown+=MouseDown;view.MouseMove+=MouseMove;view.MouseUp+=MouseUp;view.MouseWheel+=MouseWheel;
        view.MouseCaptureChanged+=CaptureChanged;view.Leave+=Leave;ide.Deactivate+=Leave;Panel.CameraMoved+=CameraChanged;ide.Project.SessionChanged+=Stop;
    }
    private void CameraChanged()
    {
        if(ide.RuntimeActive || !ide.Project.IsProjectOpen)return;
        var matrix=Panel.get_inverted_view_matrix();
        ide.Project.CameraPosition=matrix.Translation;
        ide.Project.CameraDirection=Vector3.Normalize(new Vector3(matrix.M31,matrix.M32,matrix.M33));
        ide.Project.CameraDistance=Panel.view_point_distance;
    }
    private void Apply(Vector3 eye,Vector3 forward)
    {
        if(ide.ViewportEditing?.Dragging==true)ide.ViewportEditing.PointerUp(false);
        forward=Vector3.Normalize(forward);var up=Math.Abs(Vector3.Dot(forward,Vector3.UnitY))>.999f?Vector3.UnitZ:Vector3.UnitY;
        var right=Vector3.Normalize(Vector3.Cross(up,forward));up=Vector3.Cross(forward,right);
        Panel.SetCameraWorld(new Matrix4x4(right.X,right.Y,right.Z,0,up.X,up.Y,up.Z,0,forward.X,forward.Y,forward.Z,0,eye.X,eye.Y,eye.Z,1));
    }
    public void MoveLocal(Vector3 delta)
    {
        Check();
        var m=Panel.get_inverted_view_matrix();var eye=m.Translation+Vector3.TransformNormal(delta,m);
        Apply(eye,new Vector3(m.M31,m.M32,m.M33));
    }
    public void Look(float dx,float dy,bool aroundSelection=false)
    {
        Check();
        if(!float.IsFinite(dx) || !float.IsFinite(dy))throw new ArgumentOutOfRangeException(nameof(dx));
        var m=Panel.get_inverted_view_matrix();var forward=new Vector3(m.M31,m.M32,m.M33);
        float speed=Panel.mouse_sensitivity*MathF.PI/180;
        float yaw=MathF.Atan2(forward.X,forward.Z)+dx*speed;
        float pitch=Math.Clamp(MathF.Asin(Math.Clamp(forward.Y,-1,1))-dy*speed,-MathF.PI*.495f,MathF.PI*.495f);
        forward=new Vector3(MathF.Sin(yaw)*MathF.Cos(pitch),MathF.Sin(pitch),MathF.Cos(yaw)*MathF.Cos(pitch));
        Apply(aroundSelection?orbitCenter-forward*Panel.view_point_distance:m.Translation,forward);
    }
    public void Dolly(float notches)
    {
        Check();
        if(!float.IsFinite(notches) || Math.Abs(notches)>100)throw new ArgumentOutOfRangeException(nameof(notches));
        float before=Panel.view_point_distance;
        float after=Math.Clamp(before*MathF.Exp(-notches*.15f),.1f,100000);
        MoveLocal(new Vector3(0,0,before-after));Panel.view_point_distance=after;
        if(ide.Project.IsProjectOpen)ide.Project.CameraDistance=after;
    }
    public bool FocusSelection()
    {
        Check();
        if(!ide.Project.IsProjectOpen)return false;
        var bounds=scene.SelectionBounds(ide.Project);if(bounds.Count==0)return false;
        float aspect=(float)Panel.get_view_size().Width/Panel.get_view_size().Height;
        float angle=MathF.Atan(Math.Min(1,aspect)*MathF.Tan(MathF.PI/6));
        float distance=Math.Max(bounds.Radius, .5f)/MathF.Sin(angle)+Panel.m_near_plane_val;
        var m=Panel.get_inverted_view_matrix();var forward=new Vector3(m.M31,m.M32,m.M33);
        Apply(bounds.Center-forward*distance,forward);Panel.view_point_distance=distance;ide.Project.CameraDistance=distance;return true;
    }
    public void Advance(float seconds)
    {
        Check();
        if(!float.IsFinite(seconds) || seconds<0 || seconds>.1f)throw new ArgumentOutOfRangeException(nameof(seconds));
        if(!CanNavigate || !Flying || held.Count==0)return;
        var delta=new Vector3(((held.Contains(Keys.D) || held.Contains(Keys.Right))?1:0)-((held.Contains(Keys.A) || held.Contains(Keys.Left))?1:0),
            (held.Contains(Keys.E)?1:0)-(held.Contains(Keys.Q)?1:0),((held.Contains(Keys.W) || held.Contains(Keys.Up))?1:0)-((held.Contains(Keys.S) || held.Contains(Keys.Down))?1:0));
        if(delta.LengthSquared()==0)return;
        bool fast=held.Contains(Keys.ShiftKey) || (Control.ModifierKeys&Keys.Shift)!=0;
        float speed=fast?20:5;
        MoveLocal(Vector3.Normalize(delta)*seconds*speed);
    }
    public void Tick()
    {
        Check();
        double now=clock.Elapsed.TotalSeconds;float dt=(float)Math.Clamp(now-lastTick,0,.05);lastTick=now;
        Guard(()=>Advance(dt));
    }
    public bool KeyDown(Keys key)
    {
        Check();
        if(key==Keys.Escape && Active){Stop();return true;}
        if(!CanNavigate || !Flying || key is not (Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E or Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.ShiftKey))return false;
        held.Add(key);return true;
    }
    public bool KeyUp(Keys key) {Check();return held.Remove(key);}
    public void Begin(MouseButtons button,Point p,bool alt)
    {
        Check();
        bool freeSceneLook=!ide.Project.IsProjectOpen && !ide.BackgroundMode && button==MouseButtons.Left;
        if(!CanNavigate || (button is not (MouseButtons.Right or MouseButtons.Middle) && !(alt && button==MouseButtons.Left) && !freeSceneLook))return;
        ide.ViewportEditing?.PointerUp(false);ide.InputEngine.reset_input();
        gesture=button;orbit=alt && button==MouseButtons.Left;previous=p;
        var m=Panel.get_inverted_view_matrix();orbitCenter=m.Translation+new Vector3(m.M31,m.M32,m.M33)*Panel.view_point_distance;
    }
    public void Move(Point point)
    {
        Check();
        if(!Active)return;
        int dx=point.X-previous.X,dy=point.Y-previous.Y;previous=point;
        if(gesture==MouseButtons.Middle)
        {
            float scale=2*Panel.view_point_distance*MathF.Tan(MathF.PI/6)/Panel.get_view_size().Height;
            MoveLocal(new Vector3(-dx*scale,dy*scale,0));
        }
        else Look(dx,dy,orbit);
    }
    public void Stop()
    {
        Check();
        held.Clear();gesture=MouseButtons.None;ending=true;
        try {view.Capture=false;} finally {ending=false;}
    }
    private void Guard(System.Action action) {try{action();}catch(Exception e){Stop();ide.set_status_label(0,e.Message);Console.Error.WriteLine("SDK navigation: "+e);}}
    private void MouseDown(object? sender,MouseEventArgs e) {Guard(()=>{if(!ide.BackgroundMode){view.Focus();ide.ActiveContext="View";}Begin(e.Button,e.Location,(Control.ModifierKeys&Keys.Alt)!=0);if(Active){view.Focus();view.Capture=true;}});}
    private void MouseMove(object? sender,MouseEventArgs e) {if(Active)Guard(()=>Move(e.Location));}
    private void MouseUp(object? sender,MouseEventArgs e) {if(e.Button==gesture)Stop();}
    private void MouseWheel(object? sender,MouseEventArgs e) {if(CanNavigate)Guard(()=>Dolly(e.Delta/120f));}
    private void CaptureChanged(object? sender,EventArgs e) {if(!ending && !view.Capture)Stop();}
    private void Leave(object? sender,EventArgs e)=>Stop();
    public void Dispose()
    {
        if(disposed)return;Stop();disposed=true;Panel.RenderModeSupported=null;
        view.MouseDown-=MouseDown;view.MouseMove-=MouseMove;view.MouseUp-=MouseUp;view.MouseWheel-=MouseWheel;
        view.MouseCaptureChanged-=CaptureChanged;view.Leave-=Leave;ide.Deactivate-=Leave;Panel.CameraMoved-=CameraChanged;ide.Project.SessionChanged-=Stop;
    }
}
