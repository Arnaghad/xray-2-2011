#pragma warning disable CS8981

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using xray.editor.avalonia_controls.Ide.Dialogs;

namespace xray.editor_base
{
    // C# port of editor/world/sources/objects_library.{h,cpp}, objects_library_serialize.cpp,
    // select_library_class.{h,cpp}, resource_loading_dialog.{h,cpp}, and editor/base/sources/resource_chooser.{h,cpp}.

    public class lib_item_base
    {
        public Guid m_guid = Guid.NewGuid();
        public string m_name = string.Empty;
        public lib_folder? m_parent_folder;
        public int m_image_index = (int)images16x16.node_mesh;

        // Matches C++ lib_item_base::full_path_name(): "<folder>/<sub>/<name>"
        public string full_path_name()
        {
            if (m_parent_folder == null)
                return m_name;
            return m_parent_folder.full_path_name() + "/" + m_name;
        }
    }

    public class lib_folder : lib_item_base
    {
        public readonly List<lib_folder> m_folders = new();
        public readonly List<lib_item> m_items = new();

        public lib_folder()
        {
            m_image_index = (int)images16x16.folder_closed;
        }

        public lib_folder? get_folder(string name)
        {
            foreach (var f in m_folders)
            {
                if (string.Equals(f.m_name, name, StringComparison.Ordinal))
                    return f;
            }
            return null;
        }

        public lib_item? get_item(string name)
        {
            foreach (var i in m_items)
            {
                if (string.Equals(i.m_name, name, StringComparison.Ordinal))
                    return i;
            }
            return null;
        }
    }

    public class lib_item : lib_item_base
    {
        // Full original item table (name, lib_name = base class, tool_name, defaults, unknown fields)
        public LuaConfigValue m_config;
        internal string SourceGuidKey = string.Empty;

        public lib_item(LuaConfigValue? cfg = null)
        {
            m_config = cfg?.Clone() ?? LuaConfigValue.CreateTableNamed();
        }

        // lib_name inside a library item is the base class (e.g. "base:light")
        public string m_lib_item_name => m_config.GetStringOrDefault("lib_name");
        public string tool_name => m_config.GetStringOrDefault("tool_name");
    }

    public delegate bool item_filter(lib_item item);

    // C# port of objects_library (objects_library.cpp + objects_library_serialize.cpp).
    // File format: library = { ["<guid>"] = { name = "folder/item", ... } }, empty_folders = { ["a/b"] = 0 }
    public class objects_library
    {
        private readonly lib_folder m_root = new();
        private readonly List<lib_item> m_tool_library = new();
        private readonly List<lib_folder> m_folders = new();
        private string m_current_source = string.Empty;
        private LuaConfigValue m_raw = LuaConfigValue.CreateTableNamed();

        public lib_folder Root => m_root;
        public string CurrentSource => m_current_source;
        public IReadOnlyList<lib_item> Items => m_tool_library;
        public IReadOnlyList<lib_folder> Folders => m_folders;
        public int FolderCount => m_folders.Count;
        public int ItemCount => m_tool_library.Count;

        public static string extract_folder_name(string full_name)
        {
            if (string.IsNullOrEmpty(full_name))
                return string.Empty;
            string norm = full_name.Replace('\\', '/');
            int idx = norm.LastIndexOf('/');
            return idx == -1 ? string.Empty : norm.Substring(0, idx);
        }

        public static string extract_item_name(string full_name)
        {
            if (string.IsNullOrEmpty(full_name))
                return string.Empty;
            string norm = full_name.Replace('\\', '/');
            int idx = norm.LastIndexOf('/');
            return idx == -1 ? norm : norm.Substring(idx + 1);
        }

        public void clear()
        {
            m_root.m_folders.Clear();
            m_root.m_items.Clear();
            m_tool_library.Clear();
            m_folders.Clear();
        }

        public void load_from_file(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Library file path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Library file not found: {filePath}", filePath);

            m_current_source = Path.GetFullPath(filePath);
            var cfg = LuaConfig.FromFile(m_current_source);
            load(cfg.GetRoot());
        }

        public void load(LuaConfigValue cfg)
        {
            clear();
            m_raw = cfg.Clone();
            var empty = cfg.TryGet("empty_folders");
            if (empty != null)
            {
                foreach (string folderName in empty.GetFieldIds())
                    get_folder_item_by_full_path(folderName, true);
            }

            var library = cfg.TryGet("library");
            if (library == null || library.Type != LuaValueType.TableNamed)
                return;

            foreach (string guidStr in library.GetFieldIds())
            {
                var current = library[guidStr];
                string fullName = current.GetStringOrDefault("name");
                var itm = new lib_item(current);
                if (!Guid.TryParse(guidStr, out var g)) throw new InvalidDataException("Invalid library GUID: " + guidStr);
                itm.m_guid = g;
                itm.SourceGuidKey = guidStr;
                string folder = extract_folder_name(fullName);
                itm.m_parent_folder = string.IsNullOrEmpty(folder) ? null : get_folder_item_by_full_path(folder, true);
                itm.m_name = extract_item_name(fullName);
                (itm.m_parent_folder ?? m_root).m_items.Add(itm);
                m_tool_library.Add(itm);
            }
        }

        public void save(LuaConfigValue cfg)
        {
            cfg.AssignFrom(m_raw);
            cfg.Erase("library");
            cfg.Erase("empty_folders");
            var root = cfg["library"];
            var referenced = new HashSet<lib_folder>();
            foreach (var itm in m_tool_library)
            {
                var entry = itm.m_config.Clone();
                entry["name"].Assign(itm.full_path_name());
                string guidKey = Guid.TryParse(itm.SourceGuidKey, out var sourceGuid) && sourceGuid == itm.m_guid ? itm.SourceGuidKey : itm.m_guid.ToString();
                root[guidKey] = entry;
                for (var f = itm.m_parent_folder; f != null; f = f.m_parent_folder)
                    referenced.Add(f);
            }

            var emptyFolders = new List<lib_folder>();
            foreach (var f in m_folders)
            {
                if (!referenced.Contains(f))
                    emptyFolders.Add(f);
            }
            if (emptyFolders.Count > 0)
            {
                var empty = cfg["empty_folders"];
                foreach (var f in emptyFolders)
                    empty[f.full_path_name()].Assign(0);
            }
        }

        public void save_as(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Target library filename is required.", nameof(filename));
            SdkVfsManager.EnsurePathWritable(filename);

            m_current_source = Path.GetFullPath(filename);
            var cfg = LuaConfig.CreateEmpty();
            save(cfg.GetRoot());
            cfg.SaveAs(m_current_source);
        }

        public lib_folder add_new_folder(string full_path_name)
        {
            return get_folder_item_by_full_path(full_path_name, true)!;
        }

        public void delete_folder_item(lib_folder folder)
        {
            foreach (var sub in folder.m_folders.ToArray())
                delete_folder_item(sub);
            foreach (var itm in folder.m_items.ToArray())
                delete_library_item(itm);
            (folder.m_parent_folder ?? m_root).m_folders.Remove(folder);
            m_folders.Remove(folder);
        }

        public lib_item new_lib_item(string library_folder, string name, LuaConfigValue? source = null)
        {
            lib_folder? parent = string.IsNullOrEmpty(library_folder) ? null : get_folder_item_by_full_path(library_folder, true);
            var ln = new lib_item(source)
            {
                m_parent_folder = parent,
                m_name = name
            };
            ln.m_config["name"].Assign(ln.full_path_name());
            (parent ?? m_root).m_items.Add(ln);
            m_tool_library.Add(ln);
            return ln;
        }

        public void delete_library_item(lib_item item)
        {
            (item.m_parent_folder ?? m_root).m_items.Remove(item);
            m_tool_library.Remove(item);
        }

        public lib_item add_simple(string full_name)
        {
            var existing = get_library_item_by_full_path(full_name, false);
            if (existing != null)
                return existing;
            return new_lib_item(extract_folder_name(full_name), extract_item_name(full_name));
        }

        public lib_folder? get_folder_item_by_full_path(string folder_full_path, bool b_create)
        {
            if (string.IsNullOrEmpty(folder_full_path))
                return null;

            string[] parts = folder_full_path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            lib_folder? parent = null;
            var curr = m_root;
            foreach (string part in parts)
            {
                var folder = curr.get_folder(part);
                if (folder == null)
                {
                    if (!b_create)
                        return null;
                    folder = new lib_folder { m_name = part, m_parent_folder = parent };
                    curr.m_folders.Add(folder);
                    m_folders.Add(folder);
                }
                parent = folder;
                curr = folder;
            }
            return parent;
        }

        public lib_item? get_library_item_by_full_path(string item_full_path, bool assert_on_fail)
        {
            if (!string.IsNullOrWhiteSpace(item_full_path))
            {
                string norm = item_full_path.Replace('\\', '/');
                foreach (var itm in m_tool_library)
                {
                    if (string.Equals(itm.full_path_name(), norm, StringComparison.Ordinal))
                        return itm;
                }
            }

            if (assert_on_fail)
                throw new KeyNotFoundException($"Library item '{item_full_path}' was not found in '{m_current_source}'.");
            return null;
        }

        public lib_item? get_library_item_by_guid(Guid guid)
        {
            foreach (var itm in m_tool_library)
            {
                if (itm.m_guid == guid)
                    return itm;
            }
            return null;
        }

        // Returns a detached copy of the item's defaults without library bookkeeping fields.
        public LuaConfig extract_library_item(LuaConfigValue cfg)
        {
            var result = LuaConfig.CreateEmpty();
            result.GetRoot().AssignFrom(cfg);
            result.GetRoot().Erase("guid");
            return result;
        }
    }

    public interface chooser
    {
        string id();
        bool choose(string? initial, string? filter, out string result, bool read_only);
    }

    public static class resource_chooser
    {
        private static readonly Dictionary<string, chooser> s_choosers = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, object?> s_shared_context = new(StringComparer.Ordinal);

        public static Func<string, string?, string?, bool, (bool Handled, bool Accepted, string Result)>? ChooseOverride { get; set; }
        [ThreadStatic] public static object? DialogOwner;

        public static void register_chooser(chooser c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            s_choosers[c.id()] = c;
        }

        public static void unregister_chooser(chooser c)
        {
            if (c == null) return;
            if (s_choosers.TryGetValue(c.id(), out var registered) && ReferenceEquals(registered, c)) s_choosers.Remove(c.id());
        }

        public static bool is_registered(string id) => s_choosers.ContainsKey(id);

        public static void set_shared_context(string key, object? value)
        {
            s_shared_context[key] = value;
        }

        public static object? get_shared_context(string key)
        {
            return s_shared_context.TryGetValue(key, out var val) ? val : null;
        }

        public static bool choose(string id, string? initial, string? filter, out string result, bool read_only = false)
        {
            if (ChooseOverride != null)
            {
                var overrideOutcome = ChooseOverride(id, initial, filter, read_only);
                if (overrideOutcome.Handled)
                {
                    result = overrideOutcome.Result;
                    return overrideOutcome.Accepted;
                }
            }

            if (!s_choosers.TryGetValue(id, out var c))
            {
                result = string.Empty;
                return false;
            }
            return c.choose(initial, filter, out result, read_only);
        }
    }

    // C# port of editor/world/sources/select_library_class.{h,cpp}
    public class SelectLibraryClassForm : IDisposable
    {
        private bool m_disposed;

        public SelectLibraryClassForm()
        {
            SdkAvaloniaBootstrap.EnsureInitialized();
            View = new SdkSelectLibraryClassWindow();
        }

        public SdkSelectLibraryClassWindow View { get; }
        public string? SelectedPath => View.SelectedPath;
        public bool IsReadOnlyMode => View.IsReadOnlyMode;

        public void Initialize(objects_library library, string? initial, string? filter, bool read_only)
        {
            ArgumentNullException.ThrowIfNull(library);
            var entries = library.Items.Select(item =>
                new SdkLibraryClassEntry(item.full_path_name(), item.m_lib_item_name, item.m_image_index, item));
            View.Initialize(entries, initial, filter, read_only);
        }

        public bool SelectLibraryPathProgrammatically(string fullPath) =>
            View.SelectLibraryPathProgrammatically(fullPath);

        public SdkDialogResult ShowDialog(object? owner = null)
        {
            SdkAvaloniaBootstrap.ShowModalWindow(View, owner, hideOnClose: false);
            return View.DialogAccepted ? SdkDialogResult.OK : SdkDialogResult.Cancel;
        }

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            View.Close();
        }
    }

    public class library_class_chooser : chooser
    {
        private readonly string m_id;
        private readonly objects_library m_library;

        public library_class_chooser(objects_library lib, string chooserId = "library_class")
        {
            m_library = lib ?? throw new ArgumentNullException(nameof(lib));
            m_id = chooserId;
        }

        public string id() => m_id;

        public bool choose(string? initial, string? filter, out string result, bool read_only)
        {
            using var form = new SelectLibraryClassForm();
            form.Initialize(m_library, initial, filter, read_only);
            if (form.ShowDialog(resource_chooser.DialogOwner) == SdkDialogResult.OK && !string.IsNullOrEmpty(form.SelectedPath))
            {
                result = form.SelectedPath!;
                return true;
            }

            result = string.Empty;
            return false;
        }
    }

    public class object_in_project_chooser : chooser
    {
        private readonly Project m_project;

        public object_in_project_chooser(Project project)
        {
            m_project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string id() => "project_item";

        public bool choose(string? initial, string? filter, out string result, bool read_only)
        {
            if (!string.IsNullOrWhiteSpace(initial))
            {
                var found = m_project.FindByFullPath(initial!);
                if (found != null)
                {
                    result = found.FullPath;
                    return !read_only;
                }
            }

            var all = new List<ProjectItemBase>();
            m_project.Root.get_all_items(all, true);
            foreach (var item in all)
            {
                if (!item.is_object()) continue;
                if (!string.IsNullOrWhiteSpace(filter) &&
                    !item.FullPath.Contains(filter!, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                result = item.FullPath;
                return !read_only;
            }

            result = string.Empty;
            return false;
        }
    }

    // subst_resource_result and subst_record live in xray.editor.core (SdkUiAbstractions.cs)

    public class ResourceLoadingDialog : IDisposable
    {
        private bool m_disposed;

        public ResourceLoadingDialog()
        {
            SdkAvaloniaBootstrap.EnsureInitialized();
            View = new SdkResourceLoadingWindow
            {
                BrowseCallback = (classId, current) =>
                    resource_chooser.choose(classId, current, null, out string chosen, false) ? chosen : null
            };
        }

        public SdkResourceLoadingWindow View { get; }
        public subst_resource_result DialogActionResult => View.DialogActionResult;
        public string SelectedResourceName => View.SelectedResourceName;

        public void InitializeDialog(string classId, string missingResourceName) =>
            View.InitializeDialog(classId, missingResourceName);

        public SdkDialogResult ShowDialog(object? owner = null)
        {
            SdkAvaloniaBootstrap.ShowModalWindow(View, owner, hideOnClose: false);
            return View.DialogAccepted ? SdkDialogResult.OK : SdkDialogResult.Cancel;
        }

        public void Dispose()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            View.Close();
        }
    }

    public static class missing_resource_registry
    {
        private static readonly List<subst_record> s_subst_View = new();

        public static Func<string, string, (subst_resource_result Result, string SubstituteName)>? NonInteractiveResolver { get; set; }

        public static IReadOnlyList<subst_record> Records => s_subst_View;

        public static void clear()
        {
            s_subst_View.Clear();
        }

        public static void register_substitute(string class_id, string source_name, string dest_name, subst_resource_result res)
        {
            s_subst_View.Add(new subst_record
            {
                class_id = class_id,
                source_name = source_name,
                dest_name = dest_name,
                result = res
            });
        }

        public static subst_resource_result find(string class_id, string name, out string result_value)
        {
            foreach (var r in s_subst_View)
            {
                if (!string.Equals(r.class_id, class_id, StringComparison.Ordinal))
                    continue;

                if (r.result == subst_resource_result.ignore_all_any_name)
                {
                    result_value = string.Empty;
                    return subst_resource_result.ignore_one;
                }

                if (r.result == subst_resource_result.ignore_all_by_name &&
                    string.Equals(r.source_name, name, StringComparison.Ordinal))
                {
                    result_value = string.Empty;
                    return subst_resource_result.ignore_one;
                }

                if (r.result == subst_resource_result.subst_all_any_name)
                {
                    result_value = r.dest_name;
                    return subst_resource_result.subst_one;
                }

                if (r.result == subst_resource_result.subst_all_by_name &&
                    string.Equals(r.source_name, name, StringComparison.Ordinal))
                {
                    result_value = r.dest_name;
                    return subst_resource_result.subst_one;
                }
            }

            result_value = string.Empty;
            return subst_resource_result.ignore_one;
        }

        public static bool choose_missing_resource(string class_id, string missing_name, out string substituted_name)
        {
            // Check existing batch rules first
            foreach (var r in s_subst_View)
            {
                if (!string.Equals(r.class_id, class_id, StringComparison.Ordinal))
                    continue;

                if (r.result == subst_resource_result.ignore_all_any_name ||
                    (r.result == subst_resource_result.ignore_all_by_name && string.Equals(r.source_name, missing_name, StringComparison.Ordinal)))
                {
                    substituted_name = string.Empty;
                    return false;
                }

                if (r.result == subst_resource_result.subst_all_any_name ||
                    (r.result == subst_resource_result.subst_all_by_name && string.Equals(r.source_name, missing_name, StringComparison.Ordinal)))
                {
                    substituted_name = r.dest_name;
                    return true;
                }
            }

            if (NonInteractiveResolver != null)
            {
                var resolved = NonInteractiveResolver(class_id, missing_name);
                if (resolved.Result != subst_resource_result.subst_one &&
                    resolved.Result != subst_resource_result.ignore_one)
                {
                    register_substitute(class_id, missing_name, resolved.SubstituteName, resolved.Result);
                }

                if (resolved.Result == subst_resource_result.subst_one ||
                    resolved.Result == subst_resource_result.subst_all_by_name ||
                    resolved.Result == subst_resource_result.subst_all_any_name)
                {
                    substituted_name = resolved.SubstituteName;
                    return !string.IsNullOrWhiteSpace(substituted_name);
                }

                substituted_name = string.Empty;
                return false;
            }

            using var dialog = new ResourceLoadingDialog();
            dialog.InitializeDialog(class_id, missing_name);
            var answer = dialog.ShowDialog(resource_chooser.DialogOwner);
            register_substitute(class_id, missing_name, dialog.SelectedResourceName, dialog.DialogActionResult);
            substituted_name = answer == SdkDialogResult.OK ? dialog.SelectedResourceName : string.Empty;
            return answer == SdkDialogResult.OK && !string.IsNullOrWhiteSpace(substituted_name);
        }
    }
}
