using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace xray.editor_base
{
    // C# port of editor/world/sources/project_browser_dialog.{h,cpp}
    public class ProjectBrowserDialog : Form
    {
        public sealed class FoldersSorter : IComparer
        {
            public int Compare(object? x, object? y)
            {
                if (x is not ListViewItem i1 || y is not ListViewItem i2)
                    return 0;

                if (i1.Text == "..") return -1;
                if (i2.Text == "..") return 1;

                string s1 = i1.SubItems.Count > 1 ? i1.SubItems[1].Text : string.Empty;
                string s2 = i2.SubItems.Count > 1 ? i2.SubItems[1].Text : string.Empty;
                if (!string.Equals(s1, s2, StringComparison.Ordinal))
                    return s1 == "folder" ? -1 : 1;

                return string.Compare(i1.Text, i2.Text, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        public readonly ListView projects_list_view = new();
        public readonly TextBox project_name_text_box = new();
        public readonly Button browse_up_button = new() { Text = "Up", Width = 52 };
        public readonly Button ok_button = new() { Text = "OK", Width = 80 };
        public readonly Button cancel_button = new() { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
        public readonly ContextMenuStrip contextMenuStrip1 = new();
        public readonly ToolStripMenuItem new_folder_menu_item = new("New folder");
        public readonly ToolStripMenuItem remove_item_menu_item = new("Remove");

        private string m_current_path = string.Empty;
        private string m_root_path = string.Empty;
        private bool m_b_save_mode;

        public string? selected_project;
        public string CurrentPath => m_current_path;
        public string RootPath => m_root_path;
        public bool IsSaveMode => m_b_save_mode;

        public static Func<bool, string, string, (bool Handled, DialogResult Result, string SelectedProject)>? NonInteractiveDialogOverride { get; set; }

        public ProjectBrowserDialog()
        {
            Text = "Project Browser";
            Size = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;

            projects_list_view.Dock = DockStyle.Fill;
            projects_list_view.View = View.Details;
            projects_list_view.FullRowSelect = true;
            projects_list_view.MultiSelect = false;
            projects_list_view.HideSelection = false;
            projects_list_view.Columns.Add("Name", 220);
            projects_list_view.Columns.Add("Type", 100);
            projects_list_view.Columns.Add("Modified", 120);
            projects_list_view.Columns.Add("Access", 80);
            projects_list_view.ListViewItemSorter = new FoldersSorter();

            new_folder_menu_item.Click += new_folder_menu_item_Click;
            remove_item_menu_item.Click += remove_item_menu_item_Click;
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { new_folder_menu_item, remove_item_menu_item });
            contextMenuStrip1.Opening += contextMenuStrip1_Opening;
            projects_list_view.ContextMenuStrip = contextMenuStrip1;

            projects_list_view.ItemSelectionChanged += projects_list_view_ItemSelectionChanged;
            projects_list_view.MouseDoubleClick += projects_list_view_MouseDoubleClick;
            project_name_text_box.TextChanged += project_name_text_box_TextChanged;
            browse_up_button.Click += browse_up_button_Click;
            ok_button.Click += ok_button_Click;

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(4)
            };
            topBar.Controls.Add(browse_up_button);

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                Padding = new Padding(6)
            };
            project_name_text_box.Dock = DockStyle.Top;
            var buttonsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 32
            };
            buttonsFlow.Controls.Add(cancel_button);
            buttonsFlow.Controls.Add(ok_button);
            bottomPanel.Controls.Add(project_name_text_box);
            bottomPanel.Controls.Add(buttonsFlow);

            Controls.Add(projects_list_view);
            Controls.Add(topBar);
            Controls.Add(bottomPanel);
            AcceptButton = ok_button;
            CancelButton = cancel_button;
        }

        private ListViewItem? focused_item =>
            projects_list_view.SelectedItems.Count > 0 ? projects_list_view.SelectedItems[0] : null;

        private bool is_focused_item_valid_project() =>
            focused_item != null && focused_item.SubItems.Count > 1 && focused_item.SubItems[1].Text == "xray project";

        private bool is_focused_item_folder() =>
            focused_item != null && focused_item.SubItems.Count > 1 && focused_item.SubItems[1].Text == "folder";

        public static bool check_valid_xray_project_folder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string normPath = Path.GetFullPath(path);
            if (!File.Exists(Path.Combine(normPath, "project.xprj")))
                return false;

            string folderName = Path.GetFileName(normPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return !folderName.StartsWith('#');
        }

        public void InitializeDialog(bool bSaveMode, string rootPath, string currentProjectName)
        {
            m_b_save_mode = bSaveMode;
            selected_project = currentProjectName;
            project_name_text_box.ReadOnly = !m_b_save_mode;

            DirectoryInfo rootDirInfo = new(Path.GetFullPath(rootPath));
            if (!rootDirInfo.Exists)
                rootDirInfo.Create();

            m_root_path = NormalizeTrailingSlash(rootDirInfo.FullName);

            DirectoryInfo currentDirInfo = rootDirInfo;
            if (!string.IsNullOrWhiteSpace(selected_project))
            {
                string candidate = Path.GetFullPath(Path.Combine(m_root_path, selected_project.Replace('/', Path.DirectorySeparatorChar)));
                if (!SdkVfsManager.IsInside(candidate, m_root_path)) throw new ArgumentException("Project must remain inside the project root.");
                var candidateDir = new DirectoryInfo(candidate);
                if ((candidateDir.Exists || (IsVirtualBrowser && Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical("resources/projects/" + selected_project)))) && candidateDir.Parent != null)
                {
                    currentDirInfo = candidateDir.Parent;
                }
            }

            m_current_path = NormalizeTrailingSlash(currentDirInfo.FullName);
            fill_current_path();
            _ = projects_list_view.Handle;

            if (!string.IsNullOrWhiteSpace(selected_project))
            {
                string leafName = Path.GetFileName(selected_project.TrimEnd('/', '\\'));
                foreach (ListViewItem itm in projects_list_view.Items)
                {
                    if (string.Equals(itm.Text, leafName, StringComparison.OrdinalIgnoreCase))
                    {
                        itm.Selected = true;
                        itm.EnsureVisible();
                        break;
                    }
                }
            }

            project_name_text_box_TextChanged(null, EventArgs.Empty);
        }

        public DialogResult SelectProjectToLoad(string rootPath, string currentProject, IWin32Window? owner = null)
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

        public DialogResult SelectProjectToSave(string rootPath, string currentProject, IWin32Window? owner = null)
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

        private DialogResult ShowProjectDialog(IWin32Window? owner)
        {
            var input = (owner as WindowIde)?.InputEngine;
            bool resumeInput = input != null && !input.IsPaused;
            input?.keys_holder.reset();
            if (resumeInput) input!.stop();
            try { return ShowDialog(owner); }
            finally
            {
                input?.keys_holder.reset();
                if (resumeInput) input!.resume();
            }
        }

        public bool TryAcceptProjectNameProgrammatically(string enteredProjectName, out string validationError, bool autoCreateDirectory = true)
        {
            project_name_text_box.Text = enteredProjectName;
            project_name_text_box_TextChanged(null, EventArgs.Empty);

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (string segment in enteredProjectName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.IndexOfAny(invalidChars) != -1)
                {
                    validationError = $"The name [{enteredProjectName}] contains invalid characters.";
                    return false;
                }
            }

            if (!ok_button.Enabled)
            {
                validationError = "OK button is disabled for the current selection.";
                return false;
            }

            string targetFullPath = Path.GetFullPath(Path.Combine(m_current_path, enteredProjectName.Replace('/', Path.DirectorySeparatorChar)));
            if (!SdkVfsManager.IsInside(targetFullPath, m_root_path) || enteredProjectName.Split('/', '\\').Any(p => p is "." or ".."))
            { validationError = "Project path must remain inside the project root."; return false; }
            if (m_b_save_mode)
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
                    ? SdkVfsManager.ResolveVirtualToPhysical("resources/projects/" + MakeRelativeProjectName(targetFullPath)) : targetFullPath;
                if (!check_valid_xray_project_folder(readPath))
                {
                    validationError = $"'{targetFullPath}' is not a valid X-Ray project folder.";
                    return false;
                }
            }

            selected_project = MakeRelativeProjectName(targetFullPath);
            validationError = string.Empty;
            return true;
        }

        public void fill_current_path()
        {
            projects_list_view.Items.Clear();

            var dirInfo = new DirectoryInfo(m_current_path);
            if (!dirInfo.Exists && !IsVirtualBrowser)
                dirInfo.Create();

            bool isRoot = string.Equals(
                NormalizeTrailingSlash(dirInfo.FullName),
                m_root_path,
                StringComparison.OrdinalIgnoreCase);

            browse_up_button.Enabled = !isRoot;
            if (!isRoot)
            {
                projects_list_view.Items.Add(new ListViewItem(new[] { "..", "", "", "" }, 0));
            }

            IEnumerable<DirectoryInfo> directories = IsVirtualBrowser
                ? SdkVfsManager.EnumerateDirectories("resources/projects" +
                    (string.Equals(m_current_path, m_root_path, StringComparison.OrdinalIgnoreCase) ? "" : "/" + MakeRelativeProjectName(m_current_path))).Select(p => new DirectoryInfo(p))
                : dirInfo.GetDirectories();
            foreach (var subDir in directories)
            {
                if ((subDir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    continue;

                bool isProject = check_valid_xray_project_folder(subDir.FullName);
                string itemType = isProject ? "xray project" : "folder";
                string itemModDate = subDir.LastWriteTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
                bool isReadOnly = (subDir.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
                string itemRights = isReadOnly ? "readonly" : "full";

                var item = new ListViewItem(new[] { subDir.Name, itemType, itemModDate, itemRights }, isProject ? 2 : 1);
                projects_list_view.Items.Add(item);
            }

            projects_list_view.Sort();
        }

        private void projects_list_view_ItemSelectionChanged(object? sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (focused_item == null)
            {
                if (!m_b_save_mode)
                    project_name_text_box.Text = string.Empty;
                return;
            }

            project_name_text_box.Text = is_focused_item_valid_project()
                ? focused_item.Text
                : string.Empty;

            remove_item_menu_item.Enabled = focused_item.Text != "..";
        }

        private void project_name_text_box_TextChanged(object? sender, EventArgs e)
        {
            if (m_b_save_mode)
                ok_button.Enabled = project_name_text_box.Text.Length > 0;
            else
                ok_button.Enabled = project_name_text_box.Text.Length > 0 && is_focused_item_valid_project();
        }

        public void browse_down()
        {
            if (!is_focused_item_folder() || focused_item == null)
                return;

            var di = new DirectoryInfo(Path.Combine(m_current_path, focused_item.Text));
            m_current_path = NormalizeTrailingSlash(di.FullName);
            fill_current_path();
        }

        public void browse_back()
        {
            if (string.Equals(m_current_path, m_root_path, StringComparison.OrdinalIgnoreCase))
                return;

            var di = new DirectoryInfo(m_current_path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (di.Parent != null)
            {
                m_current_path = NormalizeTrailingSlash(di.Parent.FullName);
                fill_current_path();
            }
        }

        private void projects_list_view_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (focused_item == null) return;

            if (focused_item.Text == "..")
                browse_back();
            else if (is_focused_item_folder())
                browse_down();
            else if (is_focused_item_valid_project())
                ok_button_Click(sender, EventArgs.Empty);
        }

        private void browse_up_button_Click(object? sender, EventArgs e)
        {
            browse_back();
        }

        private void ok_button_Click(object? sender, EventArgs e)
        {
            if (!TryAcceptProjectNameProgrammatically(project_name_text_box.Text, out _, autoCreateDirectory: true))
                return;

            DialogResult = DialogResult.OK;
        }

        private void new_folder_menu_item_Click(object? sender, EventArgs e)
        {
            string baseName = "new_folder";
            string candidateName = baseName;
            int index = 1;
            while (Directory.Exists(Path.Combine(m_current_path, candidateName)))
            {
                candidateName = $"{baseName}_{index++}";
            }

            string fullNewFolder = Path.Combine(m_current_path, candidateName);
            SdkVfsManager.EnsurePathWritable(fullNewFolder);
            Directory.CreateDirectory(fullNewFolder);
            fill_current_path();
        }

        private void remove_item_menu_item_Click(object? sender, EventArgs e)
        {
            if (focused_item == null || focused_item.Text == "..")
                return;

            string path = Path.Combine(m_current_path, focused_item.Text);
            SdkVfsManager.EnsurePathWritable(path);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                fill_current_path();
            }
        }

        private void contextMenuStrip1_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            remove_item_menu_item.Enabled = focused_item != null && focused_item.Text != "..";
        }

        private string MakeRelativeProjectName(string fullTargetDir)
        {
            string normTarget = NormalizeTrailingSlash(Path.GetFullPath(fullTargetDir));
            if (normTarget.StartsWith(m_root_path, StringComparison.OrdinalIgnoreCase))
            {
                string rel = normTarget.Substring(m_root_path.Length)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return rel;
            }
            return Path.GetFileName(normTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string NormalizeTrailingSlash(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private bool IsVirtualBrowser => SdkVfsManager.IsConfigured && string.Equals(
            NormalizeTrailingSlash(SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true)),
            m_root_path, StringComparison.OrdinalIgnoreCase);
    }
}

