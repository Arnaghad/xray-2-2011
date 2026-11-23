using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace xray.editor_base;

// VFS view shared by project dependencies and native resource loading.
public sealed class SdkResourceBrowser : Form
{
    public readonly TreeView ResourceTree = new() { Dock = DockStyle.Left, Width = 350, HideSelection = false };
    public readonly TextBox Details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
    public readonly Button LoadButton = new() { Text = "Load resource", AutoSize = true };
    public readonly Button CancelQueryButton = new() { Text = "Cancel loading", AutoSize = true, Enabled = false };
    public readonly Button PreviewButton = new() {Text="Preview",AutoSize=true};
    public readonly Button EditSourceButton = new() {Text="Edit source",AutoSize=true};
    public event System.Action<string>? EditSourceRequested;
    public event System.Action<string>? DccRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<string,string>? DccSourceDetails {get;set;}
    public event System.Action<string,SdkOutputKind>? PreviewRequested;
    private readonly SdkResourcePipeline pipeline;
    private readonly Project project;
    private SdkResourceQueryHandle? query;
    private bool loading;
    public SdkCookedCacheEntry? LastResult { get; private set; }

    public SdkResourceBrowser(SdkResourcePipeline resources, Project owner)
    {
        pipeline = resources;
        project = owner;
        Text = "Resources";
        Size = new Size(850, 560);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38 };
        var dependencies = new Button { Text = "Project dependencies", AutoSize = true };
        dependencies.Click += (_, _) => ShowDependencies();
        var dcc=new Button{Text="DCC source / Reimport",AutoSize=true};dcc.Click+=(_,_)=>{if(ResourceTree.SelectedNode?.Tag is string path)DccRequested?.Invoke(path);};
        bar.Controls.AddRange(new Control[] { LoadButton, CancelQueryButton, PreviewButton, EditSourceButton, dcc, dependencies });
        EditSourceButton.Click+=(_,_)=>{
            if(ResourceTree.SelectedNode?.Tag is not string path)return;
            try{if(!path.EndsWith(".avs",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".anim_clip",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".anim_collection",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tga",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".material",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".model",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".skinned_model",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".dlg",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".template",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".particle",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".wav",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".raw_options",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".single_sound_options",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".sound_collection",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".composite_sound",StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".editor_sound_scene",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("Select a supported asset source (model, texture, material, animation, dialog, particle, or sound).");
                if(EditSourceRequested==null)throw new NotSupportedException("Texture authoring services are unavailable.");EditSourceRequested(path);
            }catch(Exception error){Details.Text="Source editor failed: "+error.Message;}
        };
        Controls.Add(Details); Controls.Add(ResourceTree); Controls.Add(bar);
        ResourceTree.BeforeExpand += (_, e) => { if (e.Node != null) Populate(e.Node); };
        ResourceTree.AfterSelect += (_, e) => {string path=e.Node?.Tag as string??"";Details.Text=path;try{if(DccSourceDetails!=null)Details.AppendText(Environment.NewLine+DccSourceDetails(path));}catch(Exception error){Details.AppendText(Environment.NewLine+error.Message);}};
        LoadButton.Click += (_, _) => { if (ResourceTree.SelectedNode?.Tag is string path) LoadPath(path); };
        CancelQueryButton.Click += (_, _) => CancelLoading();
        PreviewButton.Click+=(_,_)=>{
            if(ResourceTree.SelectedNode?.Tag is not string path)return;
            try {
            SdkOutputKind kind=path.EndsWith(".model",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".model/render",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".skinned_model",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".skinned_model/render",StringComparison.OrdinalIgnoreCase)?SdkOutputKind.Model
                :path.EndsWith(".material",StringComparison.OrdinalIgnoreCase)?SdkOutputKind.Material
                :path.EndsWith(".dds",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tga",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)?SdkOutputKind.Texture
                :throw new NotSupportedException("This resource does not have a viewport preview.");
            if(PreviewRequested==null)throw new NotSupportedException("Preview services are unavailable.");
            PreviewRequested(path,kind);
            }catch(Exception error){Details.Text="Preview failed: "+error.Message;}
        };
        foreach (string path in SdkVfsManager.GetMounts().Where(m => !m.IsDynamicRuntimeMount).Select(m => m.VirtualPrefix).Distinct())
        {
            var root = new TreeNode(path) { Tag = path };
            root.Nodes.Add("...");
            ResourceTree.Nodes.Add(root);
        }
        ShowDependencies();
    }

    private static void Populate(TreeNode node)
    {
        if (node.Tag is not string path || node.Nodes.Count != 1 || node.Nodes[0].Tag != null) return;
        node.Nodes.Clear();
        foreach (string directory in SdkVfsManager.EnumerateDirectories(path))
        {
            var child = new TreeNode(Path.GetFileName(directory)) { Tag = path + "/" + Path.GetFileName(directory) };
            child.Nodes.Add("..."); node.Nodes.Add(child);
        }
        foreach (string file in SdkVfsManager.EnumerateFiles(path)) node.Nodes.Add(new TreeNode(Path.GetFileName(file)) { Tag = path + "/" + Path.GetFileName(file) });
    }

    public void LoadPath(string path)
    {
        if (loading) return;
        if (path.EndsWith(".model", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".skinned_model",StringComparison.OrdinalIgnoreCase)) path += "/render";
        query = pipeline.BeginQuery(path);
        loading = true; LoadButton.Enabled = false; CancelQueryButton.Enabled = true; LastResult = null;
        try
        {
            var result = pipeline.QueryOrCookResource(path, query);
            if (!IsDisposed)
            {
                LastResult = result;
                Details.Text = path + Environment.NewLine + "Loaded: " + result.NativeBytes + " bytes; " + result.NativeUnits + " units" + Environment.NewLine
                    + (result.CacheHit ? "Cache hit" : "Loaded from source") + Environment.NewLine + "Receipt: " + result.CookedPhysicalPath;
            }
        }
        catch (Exception error) { if (!IsDisposed) Details.Text = "Resource load failed: " + error.Message; }
        finally { query = null; loading = false; if (!IsDisposed) { LoadButton.Enabled = true; CancelQueryButton.Enabled = false; } }
    }

    public void ShowDependencies()
    {
        var report = SdkResourcePipeline.InspectProjectDependencies(project, project.ToolLibraries);
        Details.Text = "Dependencies: " + report.TotalCount + "; missing: " + report.MissingCount + Environment.NewLine
            + string.Join(Environment.NewLine, report.Entries.Select(e => (e.Exists ? "OK  " : "Missing  ") + e.Category + ": " + e.AuthoredReference));
    }
    private void CancelLoading() { if (query != null) pipeline.CancelQuery(query.QueryId); }
    protected override void Dispose(bool disposing) { if (disposing) CancelLoading(); base.Dispose(disposing); }
}
