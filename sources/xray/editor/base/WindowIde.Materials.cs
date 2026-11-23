using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace xray.editor_base;
public partial class WindowIde {
    private readonly List<SdkMaterialEditor> materialEditors=new();
    private readonly SdkMaterialGate materialGate=new();
    private bool materialSourcesRestored,materialSourcesRestoring;
    private ISdkMaterialAuthoring MaterialAuthoring=>ResourcePipeline?.Backend is {} backend && assetScene is ISdkMaterialScene materialScene
        ?new SdkMaterialAuthoring(backend,materialScene,()=>RuntimeActive,materialGate):throw new InvalidOperationException("Native material authoring services are unavailable.");
    internal void RestoreMaterialSources() {
        if(materialSourcesRestored || materialSourcesRestoring || assetScene is not ISdkMaterialScene materialScene || ResourcePipeline?.Backend==null)return;
        try{materialScene.MaterialKeys();}catch(InvalidOperationException){return;}
        materialSourcesRestoring=true;
        try {
            foreach(string kind in new[]{"materials","material_instances"}) {
                string folder=Path.Combine(SdkVfsManager.WorkingSourcesRoot,kind);if(!Directory.Exists(folder))continue;
                foreach(string file in Directory.EnumerateFiles(folder,"*.material",SearchOption.AllDirectories)) {
                    try{string uri="resources/"+kind+"/"+Path.GetRelativePath(folder,file).Replace('\\','/');using var prepared=MaterialAuthoring.Prepare(uri,File.ReadAllText(file),true,default);prepared.Commit();}
                    catch(Exception error) when(error is IOException or FormatException or InvalidOperationException or NotSupportedException){SetStatus("Material source reload failed: "+error.Message);LogError(file+": "+error.Message);}
                }
            }
            materialSourcesRestored=true;
        }finally{materialSourcesRestoring=false;}
    }
    public SdkMaterialEditor OpenMaterialSource(string uri) {
        RestoreMaterialSources();uri=SdkMaterialGraph.Normalize(uri);var existing=materialEditors.FirstOrDefault(e=>!e.IsDisposed && e.Document.VirtualPath==uri);
        if(existing!=null){if(!BackgroundMode)existing.Activate();return existing;}
        var outputs=ViewportOutputs;ProjectViewportOutputs.Lease? preview=null;
        var editor=new SdkMaterialEditor(new SdkMaterialDocument(uri,MaterialAuthoring),path=>{
            if(outputs==null)throw new InvalidOperationException("Native material preview is unavailable.");
            var next=outputs.PreviewAsset(path,SdkOutputKind.Material);if(preview is {Closing:false})outputs.Close(preview);preview=next;
        });
        materialEditors.Add(editor);editor.Disposed+=(_,_)=>{materialEditors.Remove(editor);if(preview is {Closing:false})outputs!.Close(preview);};
        if(!BackgroundMode)editor.Show(this);SetStatus("Material source opened: "+uri);return editor;
    }
    public void ShowMaterialEditor() {
        if(ResourcePipeline?.Backend==null || assetScene is not ISdkMaterialScene){SetStatus("Configure native material services first");return;}
        if(BackgroundMode){SetStatus("Material selection requires an interactive session");return;}
        using var dialog=new OpenFileDialog {Title="Open material or instance source",Filter="Material source|*.material",InitialDirectory=SdkVfsManager.ResolveVirtualToPhysical("resources/material_instances")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var mount=SdkVfsManager.GetMounts().Where(m=>m.VirtualPrefix.StartsWith("resources/material",StringComparison.Ordinal) && SdkVfsManager.IsInside(dialog.FileName,m.PhysicalDirectory)).OrderByDescending(m=>m.PhysicalDirectory.Length).FirstOrDefault();
        if(mount==null){SetStatus("Select a material inside configured resource mounts");return;}
        RunProjectAction(()=>OpenMaterialSource(mount.VirtualPrefix+"/"+Path.GetRelativePath(mount.PhysicalDirectory,dialog.FileName).Replace('\\','/')));
    }
}
