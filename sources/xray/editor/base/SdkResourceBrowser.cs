using System;
using xray.editor.avalonia_controls.Ide.Dialogs;

namespace xray.editor_base;

// VFS view shared by project dependencies and native resource loading.
public sealed class SdkResourceBrowser : IDisposable
{
    public SdkResourceBrowser(SdkResourcePipeline resources, Project owner)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(owner);

        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkResourceBrowserWindow
        {
            LoadResourceHandler = path =>
            {
                var q = resources.BeginQuery(path);
                var result = resources.QueryOrCookResource(path, q);
                return (q.QueryId, result);
            },
            CancelLoadingHandler = queryId => resources.CancelQuery(queryId),
            InspectDependenciesHandler = () => SdkResourcePipeline.InspectProjectDependencies(owner, owner.ToolLibraries)
        };
        View.Closed += (_, _) => IsDisposed = true;
        View.ShowDependencies();
    }

    public SdkResourceBrowserWindow View { get; }
    public bool IsDisposed { get; private set; }
    public SdkCookedCacheEntry? LastResult => View.LastResult;
    public string DetailsText => View.DetailsText;

    public Func<string, string>? DccSourceDetails
    {
        get => View.DccSourceDetails;
        set => View.DccSourceDetails = value;
    }

    public event System.Action<string>? EditSourceRequested
    {
        add => View.EditSourceRequested += value;
        remove => View.EditSourceRequested -= value;
    }

    public event System.Action<string>? DccRequested
    {
        add => View.DccRequested += value;
        remove => View.DccRequested -= value;
    }

    public event System.Action<string, SdkOutputKind>? PreviewRequested
    {
        add => View.PreviewRequested += value;
        remove => View.PreviewRequested -= value;
    }

    public void LoadPath(string path) => View.LoadPath(path);
    public void ShowDependencies() => View.ShowDependencies();

    public void Show(object? owner = null)
    {
        if (IsDisposed)
            return;
        if (!View.IsVisible)
            View.Show();
        else
            View.Activate();
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
        View.Close();
    }
}
