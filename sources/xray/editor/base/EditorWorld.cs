using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

namespace xray.editor_base;

// Manifest main and static-model preview. Automated runs never show a HWND.
public sealed unsafe partial class EditorWorld : IDisposable
{
    private NativeCallbacks native;
    private readonly int owner = Environment.CurrentManagedThreadId;
    private readonly bool background;
    private readonly System.Diagnostics.Stopwatch selectionClock=System.Diagnostics.Stopwatch.StartNew();
    private Form window;
    private readonly Panel view;
    private ToolStrip? toolbar;
    private readonly Dictionary<uint, ToolStripButton> commands = new();
    private HostForm? preview;
    private bool previewClosing;
    private bool loaded, cleared, disposed;
    private bool interactiveCameraSeeded;
    private sealed class HostForm : Form
    {
        public bool Background;
        public System.Action? CloseRequested;
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x10 && CloseRequested != null) { CloseRequested(); return; }
            base.WndProc(ref message);
        }
        protected override bool ShowWithoutActivation => Background;
        protected override CreateParams CreateParams {
            get { var p = base.CreateParams; if (Background) p.ExStyle |= 0x08000000 | 0x20 | 0x80; return p; }
        }
    }
    private sealed class HostView : SdkViewportHost
    {
        public bool Background;
        protected override CreateParams CreateParams {
            get { var p = base.CreateParams; if (Background) p.ExStyle |= 0x08000000 | 0x20; return p; }
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);
    public EditorWorld(NativeCallbacks callbacks)
    {
        native = callbacks; background = (callbacks.Flags & 1) != 0;
        SetThreadDpiAwarenessContext((nint)(-4));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        window = new HostForm { Background = background, Text = "X-Ray Vulkan Editor", ClientSize = new Size(960, 668), StartPosition = FormStartPosition.Manual };
        try {
        view = new HostView { Background = background, Dock = DockStyle.Fill, BackColor = Color.Black };
        toolbar = new ToolStrip { Dock = DockStyle.Top, AutoSize = false, Height = 28 };
        toolbar.Items.Add(new ToolStripLabel("Vulkan · main scene"));
        string[] labels = { "Props", "Move props", "Add layer", "Remove layer", "Exchange layer", "Restore layers", "Overlay", "Isolate bar" };
        for (int i = 0; i < labels.Length; ++i) {
            uint command = (uint)i + 1;
            var button = new ToolStripButton(labels[i]);
            button.Click += (_, _) => {
                commandStatus = native.Command(native.Context, command);
                if (commandStatus < 0) window.Text = "X-Ray Vulkan Editor · Command unavailable in the current state";
                else if (command is 1 or 2 or 7 or 8) button.Checked = !button.Checked;
            };
            commands.Add(command, button); toolbar.Items.Add(button);
        }
        var previewButton = new ToolStripButton("Model preview");
        previewButton.Click += (_, _) => {
            commandStatus = 0;
            try { TogglePreview(); }
            catch (Exception error) { commandStatus = -1; Console.Error.WriteLine("Editor preview: " + error); window.Text = "X-Ray Vulkan Editor · Preview unavailable"; }
        };
        commands.Add(9, previewButton); toolbar.Items.Add(previewButton);
        toolbar.Enabled = false;
        window.Controls.Add(view); window.Controls.Add(toolbar);
        ((HostForm)window).CloseRequested = () => { if (!disposed) Require(native.RequestExit(native.Context, 0)); };
        window.FormClosing += (_, e) => { if (!disposed) { e.Cancel = true; Require(native.RequestExit(native.Context, 0)); } };
        // Engine requires both handles before load/render initialization.
        _ = window.Handle; _ = view.Handle;
        } catch { disposed = true; window.Dispose(); native = default; throw; }
    }
    public void CheckOwner()
    {
        if (disposed || Environment.CurrentManagedThreadId != owner) throw new InvalidOperationException("Closed/foreign editor owner");
    }
    private static void Require(int status) { if (status < 0) throw new InvalidOperationException("Native editor callback failed"); }
    public nint MainHandle { get { CheckOwner(); return window.Handle; } }
    public nint ViewHandle { get { CheckOwner(); var host=(SdkViewportHost)view;if(!host.SurfaceLeased)host.AcquireSurfaceLease();return host.SurfaceHandle; } }
    public nint PreviewHandle { get { CheckOwner(); return preview?.Handle ?? 0; } }
    private void TogglePreview()
    {
        if (previewClosing) throw new InvalidOperationException("Preview close is pending");
        if (preview != null) { RequestPreviewClose(); return; }
        var form = new HostForm { Background = background, Text = "X-Ray · woodbox_mil", ClientSize = new Size(640, 480), StartPosition = FormStartPosition.Manual };
        try {
            _ = form.Handle;
            // Publish only after the native side accepts the independent HWND.
            Require(native.Preview(native.Context, (nuint)form.Handle, 0));
            preview = form;
            form.CloseRequested = () => { try { RequestPreviewClose(); } catch (Exception error) { Console.Error.WriteLine("Editor preview close: " + error); } };
            form.FormClosing += (_, e) => { if (!disposed && preview != null) {
                e.Cancel = true;
                try { RequestPreviewClose(); }
                catch (Exception error) { Console.Error.WriteLine("Editor preview close: " + error); }
            } };
            if (commands.TryGetValue(9, out var button)) button.Checked = true;
            if (!background) form.Show();
        } catch { form.Dispose(); throw; }
    }
    private void RequestPreviewClose()
    {
        if (preview == null || previewClosing) return;
        Require(native.Preview(native.Context, (nuint)preview.Handle, 1));
        previewClosing = true;
        if (commands.TryGetValue(9, out var button)) button.Enabled = false;
    }
    public void PreviewClosed()
    {
        CheckOwner();
        if (preview == null || !previewClosing) throw new InvalidOperationException("Unexpected preview close acknowledgement");
        var form = preview; preview = null; previewClosing = false;
        form.Dispose();
        if (commands.TryGetValue(9, out var button)) { button.Checked = false; button.Enabled = true; }
    }
    public void Load() { CheckOwner(); if (loaded) throw new InvalidOperationException("Editor already loaded"); loaded = true; }
    public void Run()
    {
        CheckOwner(); if (!loaded || cleared) throw new InvalidOperationException("Editor is not loaded");
        if (!background) window.Show();
        while (native.Closing(native.Context) == 0)
        {
            Application.DoEvents(); Require(native.Tick(native.Context)); Thread.Sleep(1);
        }
        window.Hide(); preview?.Hide();
    }
    public void Clear() { CheckOwner(); if (cleared) return; ClearSdk(); Require(native.Clear(native.Context)); cleared = true; }
    public void Render(uint frame, float* matrix)
    {
        CheckOwner(); if (!loaded || cleared || matrix == null) throw new InvalidOperationException("Invalid editor render state");
        Span<float> copy = stackalloc float[16];
        for (int i = 0; i < 16; ++i) { if (!float.IsFinite(matrix[i])) throw new ArgumentException("Nonfinite matrix"); copy[i] = matrix[i]; }
        if (projectIde?.Project.IsProjectOpen == true && sceneBinding != null)
        {
            projectIde.Navigation?.Tick();projectIde.UpdateRuntimeFrame();
            if(sceneBinding.Scene is SdkNativeScene scene) {
                scene.SetProjection(projectIde.Project,projectIde.ViewWindow.get_projection_matrix());
                if(projectIde.Navigation!=null && !projectIde.RuntimeActive){scene.SetViewport(projectIde.Project,ViewportSettings.Capture(projectIde.ViewWindow));scene.SetEditorSelection(projectIde.Project,EditorSelectionSettings.Capture(projectIde.ViewWindow,(float)(selectionClock.Elapsed.TotalSeconds%1e6)));}
                projectIde.ViewportEditing?.SynchronizeGizmo();
            }
            System.Numerics.Matrix4x4.Invert(projectIde.ViewWindow.get_inverted_view_matrix(), out var viewMatrix);
            var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4, float>(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref viewMatrix, 1));
            floats.CopyTo(copy);
        }
        else if (!background && projectIde != null && !projectIde.RuntimeActive)
        {
            var nativeView = System.Runtime.InteropServices.MemoryMarshal.Read<System.Numerics.Matrix4x4>(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(copy));
            if (!interactiveCameraSeeded && nativeView != System.Numerics.Matrix4x4.Identity)
            {
                projectIde.ViewWindow.set_view_matrix(nativeView);
                float dist = System.Numerics.Vector3.Distance(
                    projectIde.ViewWindow.get_inverted_view_matrix().Translation,
                    new System.Numerics.Vector3(23f, 1f, -33f));
                projectIde.ViewWindow.view_point_distance = Math.Clamp(dist, 4f, 30f);
                interactiveCameraSeeded = true;
            }
            if (interactiveCameraSeeded)
            {
                projectIde.Navigation?.Tick();
                System.Numerics.Matrix4x4.Invert(projectIde.ViewWindow.get_inverted_view_matrix(), out var viewMatrix);
                var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<System.Numerics.Matrix4x4, float>(
                    System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref viewMatrix, 1));
                floats.CopyTo(copy);
            }
        }
        projectIde?.RestoreTextureSources();projectIde?.RestoreMaterialSources();projectIde?.RestoreDccSources();projectIde?.RestoreModelSources();
        projectIde?.ViewportOutputs?.Tick();
        projectIde?.UpdateResourceReloadStatus();
        fixed (float* values = copy) Require(native.Render(native.Context, frame, values));
    }
    public void SetEditorMode(bool enabled) { CheckOwner(); if (toolbar != null) toolbar.Enabled = enabled && projectIde?.RuntimeActive!=true; UpdateSdk(enabled && !cleared); }
    public void Message(string message) { CheckOwner(); window.Text = "X-Ray Vulkan Editor · " + message; }
    public void Command(uint command) {
        CheckOwner();
        if (!loaded || cleared || toolbar == null || !toolbar.Enabled || !commands.TryGetValue(command, out var button) || !button.Enabled)
            throw new InvalidOperationException("Unavailable editor command");
        commandStatus = 0; button.PerformClick(); Require(commandStatus);
    }
    private int commandStatus;
    public void Dispose()
    {
        CheckOwner(); Clear(); disposed = true;
        projectIde?.ReleaseOutputSurfacesAfterRendererDrain();
        preview?.Dispose(); preview = null; sdkLayout?.Dispose(); sdkLayout = null; window.Dispose();
        var host=(SdkViewportHost)view;if(host.SurfaceLeased)host.ReleaseSurfaceLease();native = default;
    }
}
