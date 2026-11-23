using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using xray.editor.controls;

namespace xray.editor_base;

public partial class WindowIde
{
    private readonly List<(Guid Session, document_base Document)> m_project_documents = new();
    private Timer? m_recovery_timer;
    private bool m_shutdown;
    private SdkResourceBrowser? m_resource_browser;
    private string? m_startup_recovery;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Action? HostCloseRequested { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, bool>? RecoveryPromptOverride { get; set; }
    public SdkResourcePipeline? ResourcePipeline { get; private set; }

    public void InitializeProjectSession(string assets, string working, string userData, bool deferRecovery = false)
    {
        CloseDcc();
        SdkVfsManager.ConfigureDefaultMounts(assets, working, userData);
        m_project.LoadStandardToolLibraries();
        RefreshToolLibraries();
        resource_chooser.DialogOwner = this;
        m_project.LoadRecentProjectsFile(Path.Combine(userData, "recent_projects.txt"));
        ResourcePipeline = new SdkResourcePipeline();
        RefreshRecentProjectsMenu();
        m_recovery_timer?.Dispose();
        m_recovery_timer = new Timer { Interval = 30000 };
        m_recovery_timer.Tick += (_, _) => RunProjectAction(SaveRecoverySnapshot);
        if (!m_background_mode) m_recovery_timer.Start();
        string unsaved = Path.Combine(SdkVfsManager.WorkingSourcesRoot, "projects", "_unsaved");
        m_startup_recovery = Directory.Exists(unsaved) ? Directory.GetFiles(unsaved, "project.xprj.recovery", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if(!deferRecovery) CompleteProjectSessionInitialization();
    }
    public void CompleteProjectSessionInitialization()
    {
        string? recovery=m_startup_recovery;m_startup_recovery=null;
        if(recovery==null)return;
        bool accept=RecoveryPromptOverride?.Invoke(recovery) ?? (!m_background_mode && MessageBox.Show(this,
            "Recover the unsaved project from your last session?", "Project recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Question)==DialogResult.Yes);
        if(accept)RunProjectAction(()=>m_project.RecoverUnsavedProject(recovery));
    }

    public void AttachProjectDocument(document_base document)
    {
        if (!m_project.IsProjectOpen) throw new InvalidOperationException("A project must own the document.");
        if (m_project_documents.Any(d => ReferenceEquals(d.Document, document))) return;
        m_project_documents.Add((m_project.SessionId, document));
        document.Disposed += (_, _) => m_project_documents.RemoveAll(d => ReferenceEquals(d.Document, document));
    }

    private bool ConfirmOwnedDocuments()
    {
        foreach(var editor in animationCollections.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in animationViewers.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in soundEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in particleEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in dialogEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in renderOptionEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in modelEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in materialEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach(var editor in textureEditors.ToArray())if(!editor.IsDisposed && !editor.ConfirmClose())return false;
        foreach (var entry in m_project_documents.ToArray())
        {
            if (entry.Document.IsDisposed || entry.Document.is_saved) continue;
            var answer = entry.Document.check_saved();
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes)
            {
                try { entry.Document.save(); }
                catch (Exception error) { LogError(error.Message); return false; }
                if (!entry.Document.is_saved) return false;
            }
        }
        return true;
    }

    private void CloseOwnedDocuments()
    {
        CloseDcc();
        foreach(var editor in animationCollections.ToArray())editor.Dispose();
        foreach(var editor in animationViewers.ToArray())editor.Dispose();
        foreach(var editor in soundEditors.ToArray())editor.Dispose();
        foreach(var editor in particleEditors.ToArray())editor.Dispose();
        foreach(var editor in dialogEditors.ToArray())editor.Dispose();
        foreach(var editor in logicEditors.ToArray())editor.Dispose();
        foreach(var editor in navigationPanels.ToArray())editor.Dispose();
        foreach(var editor in renderOptionEditors.ToArray())editor.Dispose();
        foreach(var editor in modelEditors.ToArray())editor.Dispose();
        foreach(var editor in materialEditors.ToArray())editor.Dispose();
        foreach(var editor in textureEditors.ToArray())editor.Dispose();
        m_resource_browser?.Dispose();
        m_resource_browser = null;
        foreach (var entry in m_project_documents.ToArray()) entry.Document.Dispose();
        m_project_documents.Clear();
        ResourcePipeline?.CancelAllQueries();
        ResourcePipeline?.InvalidateAllCache();
    }

    public void ShowResourceBrowser()
    {
        if (ResourcePipeline == null) { SetStatus("Configure a project resource workspace first"); return; }
        if (m_resource_browser == null || m_resource_browser.IsDisposed) {
            m_resource_browser = new SdkResourceBrowser(ResourcePipeline, m_project);
            m_resource_browser.DccRequested+=path=>RunProjectAction(()=>ShowDccSource(path));
            m_resource_browser.DccSourceDetails=path=>{try{var link=dcc?.Find(path);return link==null?"DCC source: unassigned":"DCC source: "+dcc!.Source(link);}catch(ArgumentException){return "";}};
            m_resource_browser.EditSourceRequested+=path=>RunProjectAction(()=>{if(path.EndsWith(".dlg",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".template",StringComparison.OrdinalIgnoreCase))OpenDialogEditor(path);else if(path.EndsWith(".particle",StringComparison.OrdinalIgnoreCase))OpenParticleEditor(path);else if(path.EndsWith(".sound_collection",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".composite_sound",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".editor_sound_scene",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wav",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".raw_options",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".single_sound_options",StringComparison.OrdinalIgnoreCase))OpenSoundEditor(path);else if(path.EndsWith(".avs",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".anim_clip",StringComparison.OrdinalIgnoreCase))OpenAnimationViewer(path);else if(path.EndsWith(".anim_collection",StringComparison.OrdinalIgnoreCase))OpenAnimationCollection(path);else if(path.EndsWith(".material",StringComparison.OrdinalIgnoreCase))OpenMaterialSource(path);else if(path.StartsWith("resources/render/",StringComparison.Ordinal))OpenRenderOptionsSource(path);else if(path.StartsWith("resources/models/",StringComparison.Ordinal))OpenModelSource(path);else OpenTextureSource(path);});
            m_resource_browser.PreviewRequested+=(path,kind)=>RunProjectAction(()=>{
                if(ViewportOutputs==null)throw new InvalidOperationException("Native preview services are unavailable.");
                ViewportOutputs.PreviewAsset(path,kind);
            });
        }
        m_resource_browser.Show(this);
        SetStatus("Resource browser opened");
    }

    private bool CanShowProjectDialog()
    {
        if (!SdkVfsManager.IsConfigured) { SetStatus("Configure a project resource workspace first"); return false; }
        if (m_background_mode && ProjectBrowserDialog.NonInteractiveDialogOverride == null)
        { SetStatus("Project dialog requires an interactive session"); return false; }
        return true;
    }

    private void RunProjectAction(System.Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException or TimeoutException or System.Runtime.InteropServices.ExternalException)
        {
            SetStatus("Project operation failed: " + error.Message);
            LogError(error.Message);
        }
    }

    public bool SaveCurrentProject(bool saveAs = false)
    {
        bool saved = false;
        RunProjectAction(() => {
            if (saveAs || string.IsNullOrEmpty(m_project.ProjectFilePath))
            {
                if (!CanShowProjectDialog()) return;
                string root = SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true);
                if (m_project_browser_dialog.SelectProjectToSave(root, m_project.Name, this) != DialogResult.OK)
                { SetStatus("Save project cancelled"); return; }
                m_project.SaveProjectAs(m_project_browser_dialog.selected_project!);
            }
            else m_project.SaveProject();
            saved = true;
            SetStatus("Project saved: " + m_project.Name);
            PersistRecentProjects();
        });
        return saved;
    }

    public void OpenProject(string name)
    {
        m_project.LoadProject(name);
        CloseOwnedDocuments();
        ResourcePipeline?.CancelAllQueries();
        var candidates = Project.FindRecoveryCandidates(m_project.ProjectFilePath);
        if (candidates.Count > 0)
        {
            string recovery = candidates[0];
            bool recover = RecoveryPromptOverride?.Invoke(recovery)
                ?? (!m_background_mode && MessageBox.Show(this, "Recover unsaved changes to '" + name + "'?", "Project recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
            if (recover) m_project.RecoverFromSaveCandidate(m_project.ProjectFilePath, recovery);
        }
        PersistRecentProjects();
        SetStatus("Project loaded: " + m_project.Name);
    }

    public void SaveRecoverySnapshot()
    {
        if (m_project.IsProjectOpen && m_project.save_needed()) m_project.save_intermediate();
    }

    private sealed class StarterSceneBatchCommand(System.Action body) : Command
    {
        public override bool AffectsDocument => false;
        public override bool commit() { body(); return true; }
        public override void rollback() { }
    }

    public void OpenInteractiveStarterScene()
    {
        m_project.CreateNewProject("default_scene");
        ProjectItemObject? woodbox = null;
        Commands.run(new StarterSceneBatchCommand(() =>
        {
            woodbox = m_editing.CreateObject("solid_visual", "props/box/woodbox_mil", new System.Numerics.Vector3(-1.8f, 0f, 0.5f));
            _ = m_editing.CreateObject("solid_visual", "props/barrel/barrel_metal03_radiation", new System.Numerics.Vector3(1.6f, 0f, 0.2f));
            var npc = m_editing.CreateObject("solid_visual", "character/human/neutral/neutral_01/neutral_01.skinned_model", new System.Numerics.Vector3(0.0f, 0f, 2.2f));
            var npcAnim = SdkAnimationPlacement.ReadFromObject(npc) with
            {
                AnimationSource = "resources/animations/single/human/camp/sit_high_guitar_play_1",
                Loop = true,
                Speed = 1f
            };
            SdkAnimationPlacement.WriteToConfig(npc.RawConfig, npcAnim);
            _ = m_editing.CreateObject("speedtree", "eastern_white_pine", new System.Numerics.Vector3(4.8f, 0.94f, 5.5f));

            var light = m_editing.CreateObject("light", "point_light", new System.Numerics.Vector3(-2.5f, 3.5f, -2.0f));
            light.RawConfig["intensity"].Assign(1.2f);
            light.RawConfig["range"].Assign(16.0f);

            var sky = m_editing.CreateObject("misc", "sky", System.Numerics.Vector3.Zero);
            sky.RawConfig["sky_material"].Assign("resources/materials/sky_base.material");

            if (woodbox != null) m_editing.Select(new[] { woodbox });
        }));
        Commands.clear_stack();
        Commands.set_saved_flag();
        m_project.set_unchanged();

        var eye = new System.Numerics.Vector3(0f, 4.2f, -8.5f);
        var target = new System.Numerics.Vector3(0.6f, 1.3f, 1.5f);
        var forward = System.Numerics.Vector3.Normalize(target - eye);
        var right = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitY, forward));
        var up = System.Numerics.Vector3.Cross(forward, right);
        var cam = new System.Numerics.Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            eye.X, eye.Y, eye.Z, 1f);
        m_view_window.SetCameraWorld(cam);
        m_view_window.view_point_distance = 10.5f;
        m_project.CameraPosition = eye;
        m_project.CameraDirection = forward;
        m_project.CameraDistance = 10.5f;

        m_view_window.RenderGrid = true;
        m_view_window.GizmosVisible = true;
        m_view_window.SelectionBounds = true;
        m_view_window.SelectionOutlines = true;
        m_view_window.SunActive = true;
        m_transform_mode = "Translate";
        m_view_window.TransformMode = EditorTransformMode.Translate;

        m_project_tab.RefreshTree();
        ViewportEditing?.SynchronizeGizmo();
        m_gui_binder.update_items();
        SetStatus("Default scene ready — click objects to select, W/E/R for Move/Rotate/Scale, Play (F5) to run animations, RMB+WASD to move camera");
    }

    private void PersistRecentProjects()
    {
        if (SdkVfsManager.IsConfigured)
            m_project.SaveRecentProjectsFile(Path.Combine(SdkVfsManager.UserDataRoot, "recent_projects.txt"));
    }
}
