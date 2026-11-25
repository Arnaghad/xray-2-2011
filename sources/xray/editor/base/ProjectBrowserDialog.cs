using System;
using xray.editor.avalonia_controls.Ide.Dialogs;

namespace xray.editor_base;

// C# port of editor/world/sources/project_browser_dialog.{h,cpp}
public class ProjectBrowserDialog : IDisposable
{
    private bool m_disposed;

    public ProjectBrowserDialog()
    {
        SdkAvaloniaBootstrap.EnsureInitialized();
        View = new SdkProjectBrowserWindow();
    }

    public SdkProjectBrowserWindow View { get; }
    public string? selected_project;
    public string CurrentPath => View.CurrentPath;
    public string RootPath => View.RootPath;
    public bool IsSaveMode => View.IsSaveMode;

    public static Func<bool, string, string, (bool Handled, SdkDialogResult Result, string SelectedProject)>? NonInteractiveDialogOverride { get; set; }

    public static bool check_valid_xray_project_folder(string path) =>
        SdkProjectBrowserWindow.CheckValidXrayProjectFolder(path);

    public void InitializeDialog(bool bSaveMode, string rootPath, string currentProjectName)
    {
        View.InitializeDialog(bSaveMode, rootPath, currentProjectName);
        selected_project = View.SelectedProject;
    }

    public SdkDialogResult SelectProjectToLoad(string rootPath, string currentProject, object? owner = null)
    {
        InitializeDialog(false, rootPath, currentProject);
        if (NonInteractiveDialogOverride != null)
        {
            var ov = NonInteractiveDialogOverride(false, rootPath, currentProject);
            if (ov.Handled)
            {
                selected_project = ov.SelectedProject;
                return ov.Result;
            }
        }
        return ShowProjectDialog(owner);
    }

    public SdkDialogResult SelectProjectToSave(string rootPath, string currentProject, object? owner = null)
    {
        InitializeDialog(true, rootPath, currentProject);
        if (NonInteractiveDialogOverride != null)
        {
            var ov = NonInteractiveDialogOverride(true, rootPath, currentProject);
            if (ov.Handled)
            {
                selected_project = ov.SelectedProject;
                return ov.Result;
            }
        }
        return ShowProjectDialog(owner);
    }

    private SdkDialogResult ShowProjectDialog(object? owner)
    {
        var input = (owner as WindowIde)?.InputEngine;
        bool resumeInput = input != null && !input.IsPaused;
        input?.keys_holder.reset();
        if (resumeInput)
            input!.stop();
        try
        {
            SdkAvaloniaBootstrap.ShowModalWindow(View, owner, hideOnClose: true);
            selected_project = View.SelectedProject;
            return View.DialogAccepted ? SdkDialogResult.OK : SdkDialogResult.Cancel;
        }
        finally
        {
            input?.keys_holder.reset();
            if (resumeInput)
                input!.resume();
        }
    }

    public bool TryAcceptProjectNameProgrammatically(
        string enteredProjectName,
        out string validationError,
        bool autoCreateDirectory = true)
    {
        bool accepted = View.TryAcceptProjectNameProgrammatically(enteredProjectName, out validationError, autoCreateDirectory);
        if (accepted)
            selected_project = View.SelectedProject;
        return accepted;
    }

    public void fill_current_path() => View.FillCurrentPath();
    public void browse_down() => View.BrowseDown();
    public void browse_back() => View.BrowseBack();

    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        View.Close();
    }
}
