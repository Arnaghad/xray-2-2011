using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Input;
using xray.editor.avalonia_controls.Controls;

namespace xray.editor_base;

public sealed class ProjectViewportOutputs : IDisposable
{
    public sealed class Lease
    {
        internal enum ViewportGesture
        {
            None,
            Left,
            Middle,
            Right
        }

        public readonly IViewportSurfaceHost Host;
        public readonly CameraViewPanel? Panel;
        public readonly AvaloniaPreviewWindow? PreviewWindow;
        public SdkOutputSettings Settings;
        public SdkOutputStatus Status;
        public bool Closing, Forced, Framed;
        public Vector3 Pivot;
        internal Avalonia.Point PreviousDip;
        internal ViewportGesture Gesture;
        private readonly Func<bool>? isPanelVisible;
        internal bool IsVisible => PreviewWindow != null
            ? PreviewWindow.IsVisible
            : Panel != null && (isPanelVisible?.Invoke() ?? Panel.View.IsEffectivelyVisible);

        internal Lease(IViewportSurfaceHost host, CameraViewPanel? panel, Func<bool>? isPanelVisible, AvaloniaPreviewWindow? previewWindow, SdkOutputSettings settings, bool forced)
        {
            Host = host;
            Panel = panel;
            this.isPanelVisible = isPanelVisible;
            PreviewWindow = previewWindow;
            Settings = settings;
            Forced = forced;
        }
    }

    private readonly WindowIde ide;
    private readonly ISdkOutputScene scene;
    private readonly List<Lease> leases = new();
    private readonly int owner = Environment.CurrentManagedThreadId;
    private bool disposed;
    public IReadOnlyList<Lease> Leases => leases;
    public string? LastError { get; private set; }
    private void Check() { if (disposed || owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Closed or foreign viewport output owner."); }
    public ProjectViewportOutputs(WindowIde window, ISdkOutputScene backend) { ide = window; scene = backend; }

    public Lease OpenCamera(bool force = false)
    {
        Check();
        var existing = leases.FirstOrDefault(l => l.Settings.Kind == SdkOutputKind.Camera && !l.Closing);
        if (existing != null) { existing.Forced |= force; return existing; }
        var panel = ide.CameraViewPanel;
        var settings = SdkOutputSettings.Create(SdkOutputKind.Camera, panel.ViewportPanel.SurfaceHandle);
        settings.Camera = ide.ViewWindow.get_inverted_view_matrix();
        return Open(panel.ViewportPanel, panel, () => ide.IsDockPanelVisible(CameraViewPanel.PersistIdentity), null, settings, null, force);
    }

    public Lease PreviewPreparedModel(ulong resource, bool force = false, bool simulate = false) =>
        PreviewAsset("resources/models/__sdk6_preview/prepared/render", SdkOutputKind.Model, force, resource, simulate);

    public Lease PreviewAsset(string path, SdkOutputKind kind, bool force = false, ulong prepared = 0, bool simulate = false)
    {
        Check();
        if (kind == SdkOutputKind.Camera) throw new ArgumentException("Camera previews use OpenCamera.");
        if (kind == SdkOutputKind.Model && path.EndsWith(".model", StringComparison.OrdinalIgnoreCase)) path += "/render";
        if (kind == SdkOutputKind.Model && path.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase)) path += "/render";
        path = SdkVfsManager.NormalizeVirtualPath(path);
        var previewWindow = new AvaloniaPreviewWindow(System.IO.Path.GetFileName(path) + " · " + kind, 640, 480, ide.BackgroundMode);
        try
        {
            var settings = SdkOutputSettings.Create(kind, previewWindow.ViewportHost.SurfaceHandle);
            if (prepared != 0) settings.Flags |= 4;
            if (simulate) settings.Flags |= 2;
            var lease = Open(previewWindow.ViewportHost, null, null, previewWindow, settings, path, force, prepared);
            previewWindow.CloseRequested += () => { if (!lease.Closing) Close(lease); };
            if (!ide.BackgroundMode) previewWindow.Show();
            return lease;
        }
        catch { previewWindow.Dispose(); throw; }
    }

    private Lease Open(IViewportSurfaceHost host, CameraViewPanel? panel, Func<bool>? isPanelVisible, AvaloniaPreviewWindow? previewWindow, SdkOutputSettings settings, string? asset, bool force, ulong prepared = 0)
    {
        host.AcquireSurfaceLease();
        var lease = new Lease(host, panel, isPanelVisible, previewWindow, settings, force);
        lease.Pivot = settings.Camera.Translation + new Vector3(settings.Camera.M31, settings.Camera.M32, settings.Camera.M33) * Math.Max(1, ide.ViewWindow.view_point_distance);
        try
        {
            lease.Status = prepared == 0
                ? scene.OpenOutput(settings, asset)
                : scene is ISdkModelScene models
                    ? models.OpenPreparedModelOutput(settings, prepared)
                    : throw new NotSupportedException("Prepared model previews are unavailable.");
            lease.Settings.Id = lease.Status.Id;
            leases.Add(lease);
            if (host is AvaloniaViewportHost avHost)
                WireAvaloniaCamera(lease, avHost);
            return lease;
        }
        catch { host.ReleaseSurfaceLease(); throw; }
    }

    private static void ApplyCameraDelta(Lease lease, float dx, float dy)
    {
        var camera = lease.Settings.Camera;
        if (lease.Gesture == Lease.ViewportGesture.Middle)
        {
            var delta = new Vector3(-camera.M11 * dx + camera.M21 * dy, -camera.M12 * dx + camera.M22 * dy, -camera.M13 * dx + camera.M23 * dy) * .01f;
            camera.Translation += delta;
            lease.Pivot += delta;
        }
        else
        {
            var forward = Vector3.Normalize(new Vector3(camera.M31, camera.M32, camera.M33));
            float yaw = MathF.Atan2(forward.X, forward.Z) + dx * .005f;
            float pitch = Math.Clamp(MathF.Asin(forward.Y) - dy * .005f, -1.55f, 1.55f);
            forward = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            camera = Camera(lease.Gesture == Lease.ViewportGesture.Left ? lease.Pivot - forward * Vector3.Distance(camera.Translation, lease.Pivot) : camera.Translation, forward);
        }
        lease.Settings.Camera = camera;
    }

    private static void WireAvaloniaCamera(Lease lease, AvaloniaViewportHost host)
    {
        host.PointerPressed += (_, e) =>
        {
            if (lease.Closing || lease.Settings.Kind == SdkOutputKind.Texture) return;
            var pt = e.GetCurrentPoint(host);
            lease.Gesture = pt.Properties.IsLeftButtonPressed ? Lease.ViewportGesture.Left
                : pt.Properties.IsMiddleButtonPressed ? Lease.ViewportGesture.Middle
                : pt.Properties.IsRightButtonPressed ? Lease.ViewportGesture.Right
                : Lease.ViewportGesture.None;
            if (lease.Gesture == Lease.ViewportGesture.None) return;
            lease.PreviousDip = pt.Position;
            host.Focus();
            e.Pointer.Capture(host);
            e.Handled = true;
        };
        host.PointerReleased += (_, e) =>
        {
            lease.Gesture = Lease.ViewportGesture.None;
            e.Pointer.Capture(null);
        };
        host.PointerCaptureLost += (_, _) => lease.Gesture = Lease.ViewportGesture.None;
        host.PointerMoved += (_, e) =>
        {
            if (lease.Closing || lease.Gesture == Lease.ViewportGesture.None) return;
            var pos = e.GetPosition(host);
            double scale = Math.Max(0.1, host.RenderScaling);
            float dx = (float)((pos.X - lease.PreviousDip.X) * scale);
            float dy = (float)((pos.Y - lease.PreviousDip.Y) * scale);
            lease.PreviousDip = pos;
            ApplyCameraDelta(lease, dx, dy);
        };
        host.PointerWheelChanged += (_, e) =>
        {
            if (lease.Closing) return;
            var camera = lease.Settings.Camera;
            camera.Translation += new Vector3(camera.M31, camera.M32, camera.M33) * (float)e.Delta.Y * .3f;
            lease.Settings.Camera = camera;
        };
    }

    private static Matrix4x4 Camera(Vector3 eye, Vector3 forward)
    {
        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(Math.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY, forward));
        var up = Vector3.Cross(forward, right);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, eye.X, eye.Y, eye.Z, 1);
    }

    public void Tick()
    {
        Check();
        if (ide.Project.IsProjectOpen && ide.IsDockPanelVisible(CameraViewPanel.PersistIdentity) && !leases.Any(l => l.Settings.Kind == SdkOutputKind.Camera && !l.Closing)) OpenCamera();
        foreach (var lease in leases.ToArray())
        {
            if (lease.Host.IsDisposed && !lease.Closing) Close(lease);
            if (!leases.Contains(lease)) continue;
            var status = lease.Status = scene.PollOutput(lease.Settings.Id);
            if (status.State == 3) { Release(lease); continue; }
            if (status.State == 2 && !lease.Closing) { LastError = "Native preview failed to load."; ide.SetStatus(LastError); Close(lease); continue; }
            if (lease.Closing) continue;
            if (!lease.Framed && status.State == 1 && lease.Settings.Kind is SdkOutputKind.Model or SdkOutputKind.Material)
            {
                var center = (status.Minimum + status.Maximum) * .5f;
                float radius = Math.Max(.1f, (status.Maximum - status.Minimum).Length() * .5f);
                var forward = Vector3.Normalize(new Vector3(-.4f, -.25f, 1));
                lease.Settings.Camera = Camera(center - forward * (radius / MathF.Sin(MathF.PI / 6) * 1.25f), forward);
                lease.Pivot = center;
                lease.Framed = true;
            }
            lease.Settings.Flags = (lease.Settings.Flags & 6u) | ((lease.Forced || lease.IsVisible) && !lease.Host.IsDisposed ? 1u : 0);
            lease.Status = scene.ConfigureOutput(lease.Settings);
        }
    }

    public void Close(Lease lease)
    {
        Check();
        if (!leases.Contains(lease) || lease.Closing) return;
        lease.Closing = true;
        lease.Gesture = Lease.ViewportGesture.None;
        lease.Status = scene.CloseOutput(lease.Settings.Id);
        if (lease.Status.State == 3) Release(lease);
    }

    private void Release(Lease lease)
    {
        leases.Remove(lease);
        if (lease.Host.SurfaceLeased)
            lease.Host.ReleaseSurfaceLease();
        if (lease.PreviewWindow != null)
            lease.PreviewWindow.Dispose();
        else if (lease.Settings.Kind != SdkOutputKind.Camera)
            lease.Panel?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        Check();
        foreach (var lease in leases.ToArray()) Close(lease);
        disposed = true;
    }

    // Called only by the editor world's post-render-session disposal path.
    internal void ReleaseAfterRendererDrain()
    {
        if (owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Foreign viewport output drain.");
        foreach (var lease in leases.ToArray()) Release(lease);
    }
}
