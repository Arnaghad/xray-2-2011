using System;
using xray.editor.avalonia_controls.Editors;

namespace xray.editor_base;

public sealed class SdkDccWindow : IDisposable
{
    public SdkDccWindow(SdkDccService dcc, ISdkDccResources resources, string options)
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkDccEditorWindow(dcc, resources, options);
        View.Closed += (_, _) => IsDisposed = true;
    }

    public SdkDccEditorWindow View { get; }
    public bool IsDisposed { get; private set; }
    public string StatusText => View.StatusText;

    public event System.Action<ulong>? ModelPreview
    {
        add => View.ModelPreview += value;
        remove => View.ModelPreview -= value;
    }

    public void SelectAsset(string uri) => View.SelectAsset(uri);

    public void Show(object? owner = null)
    {
        if (!IsDisposed && !View.IsVisible)
            View.Show();
    }

    public void Activate()
    {
        if (!IsDisposed)
            View.Activate();
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        View.DisposeEditor();
        if (View.IsVisible)
            View.Close();
    }
}
