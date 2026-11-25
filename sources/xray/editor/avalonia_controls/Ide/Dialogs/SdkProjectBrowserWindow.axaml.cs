using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public sealed record SdkProjectBrowserItem(
    string Name,
    string ItemType,
    string Modified,
    string Access);

/// <summary>
/// Project browser modal window replacing WinForms ProjectBrowserDialog.
/// </summary>
public partial class SdkProjectBrowserWindow : Window
{
    private readonly ObservableCollection<SdkProjectBrowserItem> m_items = new();

    private string m_currentPath = string.Empty;
    private string m_rootPath = string.Empty;
    private bool m_saveMode;

    public SdkProjectBrowserWindow()
    {
        InitializeComponent();

        m_projects_list_box.ItemsSource = m_items;
        m_projects_list_box.SelectionChanged += (_, _) => OnSelectionChanged();
        m_projects_list_box.DoubleTapped += OnListDoubleTapped;
        m_project_name_text_box.TextChanged += (_, _) => UpdateOkEnabled();

        m_up_button.Click += (_, _) => BrowseBack();
        m_browse_folder_button.Click += async (_, _) => await BrowseCustomFolderAsync();
        m_new_folder_menu.Click += (_, _) => CreateNewFolder();
        m_remove_menu.Click += (_, _) => RemoveSelectedItem();
        m_context_menu.Opening += (_, _) =>
        {
            var focused = FocusedItem;
            m_remove_menu.IsEnabled = focused != null && focused.Name != "..";
        };

        m_ok_button.Click += (_, _) =>
        {
            if (TryAcceptProjectNameProgrammatically(m_project_name_text_box.Text ?? string.Empty, out _))
            {
                DialogAccepted = true;
                Close(true);
            }
        };
        m_cancel_button.Click += (_, _) =>
        {
            DialogAccepted = false;
            Close(false);
        };
    }

    public ObservableCollection<SdkProjectBrowserItem> Items => m_items;
    public SdkProjectBrowserItem? FocusedItem => m_projects_list_box.SelectedItem as SdkProjectBrowserItem;
    public string? SelectedProject { get; private set; }
    public string CurrentPath => m_currentPath;
    public string RootPath => m_rootPath;
    public bool IsSaveMode => m_saveMode;
    public bool DialogAccepted { get; private set; }
    public Button OkButton => m_ok_button;
    public TextBox ProjectNameTextBox => m_project_name_text_box;

    public static bool CheckValidXrayProjectFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normPath = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(normPath, "project.xprj")))
            return false;

        string folderName = Path.GetFileName(normPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !folderName.StartsWith('#');
    }

    public void InitializeDialog(bool saveMode, string rootPath, string currentProjectName)
    {
        m_saveMode = saveMode;
        SelectedProject = currentProjectName;
        DialogAccepted = false;
        m_project_name_text_box.IsReadOnly = !m_saveMode;

        var rootDirInfo = new DirectoryInfo(Path.GetFullPath(rootPath));
        if (!rootDirInfo.Exists)
            rootDirInfo.Create();

        m_rootPath = NormalizeTrailingSlash(rootDirInfo.FullName);

        DirectoryInfo currentDirInfo = rootDirInfo;
        if (!string.IsNullOrWhiteSpace(SelectedProject))
        {
            string candidate = Path.GetFullPath(Path.Combine(m_rootPath, SelectedProject.Replace('/', Path.DirectorySeparatorChar)));
            if (!SdkVfsManager.IsInside(candidate, m_rootPath))
                throw new ArgumentException("Project must remain inside the project root.");

            var candidateDir = new DirectoryInfo(candidate);
            if ((candidateDir.Exists ||
                 (IsVirtualBrowser && Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical("resources/projects/" + SelectedProject)))) &&
                candidateDir.Parent != null)
            {
                currentDirInfo = candidateDir.Parent;
            }
        }

        m_currentPath = NormalizeTrailingSlash(currentDirInfo.FullName);
        FillCurrentPath();

        if (!string.IsNullOrWhiteSpace(SelectedProject))
        {
            string leafName = Path.GetFileName(SelectedProject.TrimEnd('/', '\\'));
            var match = m_items.FirstOrDefault(i => string.Equals(i.Name, leafName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                m_projects_list_box.SelectedItem = match;
        }

        UpdateOkEnabled();
    }

    public void FillCurrentPath()
    {
        m_items.Clear();

        var dirInfo = new DirectoryInfo(m_currentPath);
        if (!dirInfo.Exists && !IsVirtualBrowser)
            dirInfo.Create();

        m_current_path_label.Text = m_currentPath;

        bool isRoot = string.Equals(
            NormalizeTrailingSlash(dirInfo.FullName),
            m_rootPath,
            StringComparison.OrdinalIgnoreCase);

        m_up_button.IsEnabled = !isRoot;
        if (!isRoot)
            m_items.Add(new SdkProjectBrowserItem("..", string.Empty, string.Empty, string.Empty));

        IEnumerable<DirectoryInfo> directories = IsVirtualBrowser
            ? SdkVfsManager.EnumerateDirectories("resources/projects" +
                (string.Equals(m_currentPath, m_rootPath, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : "/" + MakeRelativeProjectName(m_currentPath))).Select(p => new DirectoryInfo(p))
            : dirInfo.GetDirectories();

        var collected = new List<SdkProjectBrowserItem>();
        foreach (var subDir in directories)
        {
            if ((subDir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                continue;

            bool isProject = CheckValidXrayProjectFolder(subDir.FullName);
            string itemType = isProject ? "xray project" : "folder";
            string itemModDate = subDir.LastWriteTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            bool isReadOnly = (subDir.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
            string itemRights = isReadOnly ? "readonly" : "full";
            collected.Add(new SdkProjectBrowserItem(subDir.Name, itemType, itemModDate, itemRights));
        }

        collected.Sort((a, b) =>
        {
            if (!string.Equals(a.ItemType, b.ItemType, StringComparison.Ordinal))
                return a.ItemType == "folder" ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var entry in collected)
            m_items.Add(entry);
    }

    public bool TryAcceptProjectNameProgrammatically(
        string enteredProjectName,
        out string validationError,
        bool autoCreateDirectory = true)
    {
        m_project_name_text_box.Text = enteredProjectName;
        UpdateOkEnabled();

        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (string segment in enteredProjectName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.IndexOfAny(invalidChars) != -1)
            {
                validationError = $"The name [{enteredProjectName}] contains invalid characters.";
                return false;
            }
        }

        if (!m_ok_button.IsEnabled)
        {
            validationError = "OK button is disabled for the current selection.";
            return false;
        }

        string targetFullPath = Path.GetFullPath(Path.Combine(m_currentPath, enteredProjectName.Replace('/', Path.DirectorySeparatorChar)));
        if (!SdkVfsManager.IsInside(targetFullPath, m_rootPath) ||
            enteredProjectName.Split('/', '\\').Any(p => p is "." or ".."))
        {
            validationError = "Project path must remain inside the project root.";
            return false;
        }

        if (m_saveMode)
        {
            if (!Directory.Exists(targetFullPath))
            {
                if (!autoCreateDirectory)
                {
                    validationError = $"Directory '{targetFullPath}' does not exist.";
                    return false;
                }
                SdkVfsManager.EnsurePathWritable(targetFullPath);
                Directory.CreateDirectory(targetFullPath);
            }
        }
        else
        {
            string readPath = IsVirtualBrowser
                ? SdkVfsManager.ResolveVirtualToPhysical("resources/projects/" + MakeRelativeProjectName(targetFullPath))
                : targetFullPath;
            if (!CheckValidXrayProjectFolder(readPath))
            {
                validationError = $"'{targetFullPath}' is not a valid X-Ray project folder.";
                return false;
            }
        }

        SelectedProject = MakeRelativeProjectName(targetFullPath);
        validationError = string.Empty;
        return true;
    }

    public void BrowseDown()
    {
        if (FocusedItem is not { ItemType: "folder" } folder)
            return;

        var di = new DirectoryInfo(Path.Combine(m_currentPath, folder.Name));
        m_currentPath = NormalizeTrailingSlash(di.FullName);
        FillCurrentPath();
    }

    public void BrowseBack()
    {
        if (string.Equals(m_currentPath, m_rootPath, StringComparison.OrdinalIgnoreCase))
            return;

        var di = new DirectoryInfo(m_currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (di.Parent != null)
        {
            m_currentPath = NormalizeTrailingSlash(di.Parent.FullName);
            FillCurrentPath();
        }
    }

    public string CreateNewFolder()
    {
        const string baseName = "new_folder";
        string candidateName = baseName;
        int index = 1;
        while (Directory.Exists(Path.Combine(m_currentPath, candidateName)))
            candidateName = $"{baseName}_{index++}";

        string fullNewFolder = Path.Combine(m_currentPath, candidateName);
        SdkVfsManager.EnsurePathWritable(fullNewFolder);
        Directory.CreateDirectory(fullNewFolder);
        FillCurrentPath();
        return candidateName;
    }

    public bool RemoveSelectedItem()
    {
        var focused = FocusedItem;
        if (focused == null || focused.Name == "..")
            return false;

        string path = Path.Combine(m_currentPath, focused.Name);
        SdkVfsManager.EnsurePathWritable(path);
        if (!Directory.Exists(path))
            return false;

        Directory.Delete(path, recursive: true);
        FillCurrentPath();
        return true;
    }

    private void OnSelectionChanged()
    {
        var focused = FocusedItem;
        if (focused == null)
        {
            if (!m_saveMode)
                m_project_name_text_box.Text = string.Empty;
            UpdateOkEnabled();
            return;
        }

        m_project_name_text_box.Text = focused.ItemType == "xray project"
            ? focused.Name
            : string.Empty;
        UpdateOkEnabled();
    }

    private void UpdateOkEnabled()
    {
        string text = m_project_name_text_box.Text ?? string.Empty;
        if (m_saveMode)
            m_ok_button.IsEnabled = text.Length > 0;
        else
            m_ok_button.IsEnabled = text.Length > 0 && FocusedItem?.ItemType == "xray project";
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var focused = FocusedItem;
        if (focused == null)
            return;

        if (focused.Name == "..")
        {
            BrowseBack();
        }
        else if (focused.ItemType == "folder")
        {
            BrowseDown();
        }
        else if (focused.ItemType == "xray project" &&
                 TryAcceptProjectNameProgrammatically(m_project_name_text_box.Text ?? string.Empty, out _))
        {
            DialogAccepted = true;
            Close(true);
        }
    }

    private async System.Threading.Tasks.Task BrowseCustomFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select projects folder",
            AllowMultiple = false
        });
        string? picked = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(picked) && SdkVfsManager.IsInside(picked, m_rootPath))
        {
            m_currentPath = NormalizeTrailingSlash(picked);
            FillCurrentPath();
        }
    }

    private string MakeRelativeProjectName(string fullTargetDir)
    {
        string normTarget = NormalizeTrailingSlash(Path.GetFullPath(fullTargetDir));
        if (normTarget.StartsWith(m_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return normTarget.Substring(m_rootPath.Length)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
        }
        return Path.GetFileName(normTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string NormalizeTrailingSlash(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private bool IsVirtualBrowser => SdkVfsManager.IsConfigured && string.Equals(
        NormalizeTrailingSlash(SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true)),
        m_rootPath,
        StringComparison.OrdinalIgnoreCase);
}
