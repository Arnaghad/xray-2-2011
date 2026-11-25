using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor_base;

// Runtime camera navigation has no content command/history entry. Save writes
// the current camera metadata together with the next explicit project save.
public sealed class ViewportNavigation : IDisposable
{
    public enum NavMouseButton
    {
        None,
        Left,
        Middle,
        Right
    }

    private readonly WindowIde ide;
    private readonly IProjectViewportScene scene;
    private readonly AvaloniaViewportHost view;
    private readonly HashSet<Key> held = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick;
    private Point previous;
    private Vector3 orbitCenter;
    private NavMouseButton gesture;
    private IPointer? activePointer;
    private bool orbit, shiftHeld, ending, disposed;
    private readonly int owner = Environment.CurrentManagedThreadId;

    private void Check()
    {
        if (disposed || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Closed or foreign viewport navigation.");
    }

    private SceneViewPanel Panel => ide.ViewWindow;
    private bool CanNavigate => !ide.RuntimeActive && (ide.Project.IsProjectOpen || !ide.BackgroundMode);
    public bool Active => gesture != NavMouseButton.None;
    public bool Flying => Panel.WsadMode || gesture == NavMouseButton.Right || (!ide.Project.IsProjectOpen && !ide.BackgroundMode);

    public ViewportNavigation(WindowIde window, IProjectViewportScene backend)
    {
        ide = window;
        scene = backend;
        view = Panel.view_panel;
        Panel.RenderModeSupported = ViewportSettings.SupportsMode;
        if (!ViewportSettings.SupportsMode(Panel.RenderViewModeIndex))
            Panel.SetRenderViewMode("lit");
        view.PointerPressed += OnPointerPressed;
        view.PointerMoved += OnPointerMoved;
        view.PointerReleased += OnPointerReleased;
        view.PointerWheelChanged += OnPointerWheelChanged;
        view.PointerCaptureLost += OnCaptureLost;
        view.LostFocus += OnLostFocus;
        ide.Deactivate += OnDeactivate;
        Panel.CameraMoved += CameraChanged;
        ide.Project.SessionChanged += Stop;
    }

    private void CameraChanged()
    {
        if (ide.RuntimeActive || !ide.Project.IsProjectOpen)
            return;
        var matrix = Panel.get_inverted_view_matrix();
        ide.Project.CameraPosition = matrix.Translation;
        ide.Project.CameraDirection = Vector3.Normalize(new Vector3(matrix.M31, matrix.M32, matrix.M33));
        ide.Project.CameraDistance = Panel.view_point_distance;
    }

    private void Apply(Vector3 eye, Vector3 forward)
    {
        if (ide.ViewportEditing?.Dragging == true)
            ide.ViewportEditing.PointerUp(false);
        forward = Vector3.Normalize(forward);
        var up = Math.Abs(Vector3.Dot(forward, Vector3.UnitY)) > .999f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, right);
        Panel.SetCameraWorld(new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            eye.X, eye.Y, eye.Z, 1));
    }

    public void MoveLocal(Vector3 delta)
    {
        Check();
        var m = Panel.get_inverted_view_matrix();
        var eye = m.Translation + Vector3.TransformNormal(delta, m);
        Apply(eye, new Vector3(m.M31, m.M32, m.M33));
    }

    public void Look(float dx, float dy, bool aroundSelection = false)
    {
        Check();
        if (!float.IsFinite(dx) || !float.IsFinite(dy))
            throw new ArgumentOutOfRangeException(nameof(dx));
        var m = Panel.get_inverted_view_matrix();
        var forward = new Vector3(m.M31, m.M32, m.M33);
        float speed = Panel.mouse_sensitivity * MathF.PI / 180f;
        float yaw = MathF.Atan2(forward.X, forward.Z) + dx * speed;
        float pitch = Math.Clamp(MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)) - dy * speed, -MathF.PI * .495f, MathF.PI * .495f);
        forward = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        Apply(aroundSelection ? orbitCenter - forward * Panel.view_point_distance : m.Translation, forward);
    }

    public void Dolly(float notches)
    {
        Check();
        if (!float.IsFinite(notches) || Math.Abs(notches) > 100)
            throw new ArgumentOutOfRangeException(nameof(notches));
        float before = Panel.view_point_distance;
        float after = Math.Clamp(before * MathF.Exp(-notches * .15f), .1f, 100000);
        MoveLocal(new Vector3(0, 0, before - after));
        Panel.view_point_distance = after;
        if (ide.Project.IsProjectOpen)
            ide.Project.CameraDistance = after;
    }

    public bool FocusSelection()
    {
        Check();
        if (!ide.Project.IsProjectOpen)
            return false;
        var bounds = scene.SelectionBounds(ide.Project);
        if (bounds.Count == 0)
            return false;
        var sz = Panel.get_view_size();
        float aspect = (float)sz.Width / Math.Max(1, sz.Height);
        float angle = MathF.Atan(Math.Min(1, aspect) * MathF.Tan(MathF.PI / 6));
        float distance = Math.Max(bounds.Radius, .5f) / MathF.Sin(angle) + Panel.m_near_plane_val;
        var m = Panel.get_inverted_view_matrix();
        var forward = new Vector3(m.M31, m.M32, m.M33);
        Apply(bounds.Center - forward * distance, forward);
        Panel.view_point_distance = distance;
        ide.Project.CameraDistance = distance;
        return true;
    }

    public void Advance(float seconds)
    {
        Check();
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > .1f)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!CanNavigate || !Flying || held.Count == 0)
            return;
        var delta = new Vector3(
            ((held.Contains(Key.D) || held.Contains(Key.Right)) ? 1 : 0) - ((held.Contains(Key.A) || held.Contains(Key.Left)) ? 1 : 0),
            (held.Contains(Key.E) ? 1 : 0) - (held.Contains(Key.Q) ? 1 : 0),
            ((held.Contains(Key.W) || held.Contains(Key.Up)) ? 1 : 0) - ((held.Contains(Key.S) || held.Contains(Key.Down)) ? 1 : 0));
        if (delta.LengthSquared() == 0)
            return;
        bool fast = shiftHeld || held.Contains(Key.LeftShift) || held.Contains(Key.RightShift);
        float speed = fast ? 20f : 5f;
        MoveLocal(Vector3.Normalize(delta) * seconds * speed);
    }

    public void Tick()
    {
        Check();
        double now = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - lastTick, 0, .05);
        lastTick = now;
        Guard(() => Advance(dt));
    }

    public bool KeyDown(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Check();
        shiftHeld = (modifiers & KeyModifiers.Shift) != 0 || held.Contains(Key.LeftShift) || held.Contains(Key.RightShift);
        if (key == Key.Escape && Active)
        {
            Stop();
            return true;
        }
        if (!CanNavigate || !Flying || key is not (Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.Up or Key.Down or Key.Left or Key.Right or Key.LeftShift or Key.RightShift))
            return false;
        held.Add(key);
        return true;
    }

    public bool KeyUp(Key key)
    {
        Check();
        if (key is Key.LeftShift or Key.RightShift)
            shiftHeld = false;
        return held.Remove(key);
    }

    public void Begin(NavMouseButton button, Point p, bool alt)
    {
        Check();
        bool freeSceneLook = !ide.Project.IsProjectOpen && !ide.BackgroundMode && button == NavMouseButton.Left;
        if (!CanNavigate || (button is not (NavMouseButton.Right or NavMouseButton.Middle) && !(alt && button == NavMouseButton.Left) && !freeSceneLook))
            return;
        ide.ViewportEditing?.PointerUp(false);
        ide.InputEngine.reset_input();
        gesture = button;
        orbit = alt && button == NavMouseButton.Left;
        previous = p;
        var m = Panel.get_inverted_view_matrix();
        orbitCenter = m.Translation + new Vector3(m.M31, m.M32, m.M33) * Panel.view_point_distance;
    }

    public void Move(Point point)
    {
        Check();
        if (!Active)
            return;
        double scaling = Math.Max(0.1, view.RenderScaling);
        float dx = (float)((point.X - previous.X) * scaling);
        float dy = (float)((point.Y - previous.Y) * scaling);
        previous = point;
        if (gesture == NavMouseButton.Middle)
        {
            float scale = 2 * Panel.view_point_distance * MathF.Tan(MathF.PI / 6) / Math.Max(1, Panel.get_view_size().Height);
            MoveLocal(new Vector3(-dx * scale, dy * scale, 0));
        }
        else
        {
            Look(dx, dy, orbit);
        }
    }

    public void Stop()
    {
        Check();
        held.Clear();
        shiftHeld = false;
        gesture = NavMouseButton.None;
        ending = true;
        try
        {
            activePointer?.Capture(null);
            activePointer = null;
        }
        finally
        {
            ending = false;
        }
    }

    private void Guard(System.Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            Stop();
            ide.set_status_label(0, e.Message);
            Console.Error.WriteLine("SDK navigation: " + e);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Guard(() =>
        {
            if (!ide.BackgroundMode)
            {
                view.Focus();
                ide.ActiveContext = "View";
            }
            var pt = e.GetCurrentPoint(view);
            var button = pt.Properties.IsRightButtonPressed ? NavMouseButton.Right
                : pt.Properties.IsMiddleButtonPressed ? NavMouseButton.Middle
                : pt.Properties.IsLeftButtonPressed ? NavMouseButton.Left
                : NavMouseButton.None;
            Begin(button, pt.Position, (e.KeyModifiers & KeyModifiers.Alt) != 0);
            if (Active)
            {
                view.Focus();
                activePointer = e.Pointer;
                e.Pointer.Capture(view);
                e.Handled = true;
            }
        });
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Active)
            Guard(() => Move(e.GetPosition(view)));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var released = e.InitialPressMouseButton switch
        {
            MouseButton.Left => NavMouseButton.Left,
            MouseButton.Middle => NavMouseButton.Middle,
            MouseButton.Right => NavMouseButton.Right,
            _ => NavMouseButton.None
        };
        if (released == gesture)
            Stop();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (CanNavigate)
            Guard(() => Dolly((float)e.Delta.Y));
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ending)
            Stop();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e) => Stop();
    private void OnDeactivate(object? sender, EventArgs e) => Stop();

    public void Dispose()
    {
        if (disposed)
            return;
        Stop();
        disposed = true;
        Panel.RenderModeSupported = null;
        view.PointerPressed -= OnPointerPressed;
        view.PointerMoved -= OnPointerMoved;
        view.PointerReleased -= OnPointerReleased;
        view.PointerWheelChanged -= OnPointerWheelChanged;
        view.PointerCaptureLost -= OnCaptureLost;
        view.LostFocus -= OnLostFocus;
        ide.Deactivate -= OnDeactivate;
        Panel.CameraMoved -= CameraChanged;
        ide.Project.SessionChanged -= Stop;
    }
}
