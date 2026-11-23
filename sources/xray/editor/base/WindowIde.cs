#pragma warning disable WFO1000

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using WeifenLuo.WinFormsUI.Docking;
using xray.editor.controls;
using WpfControls = xray.editor.wpf_controls;

namespace xray.editor_base
{
    // C# port of editor/world/sources/window_ide.{h,cpp,resx}, window_ide_serialize.cpp,
    // and the IDE shell wiring from level_editor.cpp / level_editor_actions.cpp.
    public partial class WindowIde : Form, IContextManager
    {
        public const string IsolatedRegistrySubKey = @"Software\XRaySdkModernized\X-Ray SDK\level_editor";

        private readonly EditorWorld? m_editor_world;
        private readonly bool m_background_mode;
        private readonly VS2015LightTheme m_theme;
        private readonly DockPanel m_dock_panel;
        private readonly MenuStrip m_menu_strip;
        private readonly ToolStrip m_runtime_tool_strip;
        private readonly StatusStrip statusStrip1;
        private readonly ToolStripStatusLabel toolStripStatusLabel0;
        private readonly ToolStripStatusLabel toolStripStatusLabel3;
        private readonly ToolStripStatusLabel toolStripStatusLabel4;
        private readonly ToolStripSplitButton error_message_button;
        private readonly ToolStripMenuItem clearToolStripMenuItem;
        private readonly ToolStripButton message_button;

        private readonly ToolStripMenuItem FileMenuItem;
        private readonly ToolStripMenuItem ProjectMenuItem;
        private readonly ToolStripMenuItem EditMenuItem;
        private readonly ToolStripMenuItem ViewMenuItem;
        private readonly ToolStripMenuItem RunMenuItem;
        private readonly ToolStripMenuItem ToolsMenuItem;
        private readonly ToolStripMenuItem HelpMenuItem;
        private readonly ToolStripMenuItem RecentProjectsMenuItem;
        private readonly ToolStripMenuItem EditProjectMenuItem;
        private readonly List<Image> m_owned_toolbar_icons = new();

        private readonly InputEngine m_input_engine;
        private readonly GuiBinder m_gui_binder;
        private readonly ImageList m_images16;
        private readonly Project m_project;
        public CommandEngine Commands { get; } = new(100);
        private readonly ProjectEditing m_editing;
        public ProjectEditing Editing => m_editing;

        private readonly SceneViewPanel m_view_window;
        private readonly ProjectTab m_project_tab;
        private readonly ObjectInspectorTab m_object_inspector_tab;
        private readonly ToolsTab m_tools_tab;
        private readonly AnimationCurveEditorPanel m_animation_curve_editor_panel;
        private readonly SceneGraphTab m_scene_graph_tab;
        private readonly CameraViewPanel m_camera_view_panel;
        private readonly TriangulationTester m_triangulation_tester;
        private readonly PropertyConnectionEditor m_connection_editor;
        private readonly ToolStrip m_main_tool_strip;

        private readonly IdeLogForm m_log_form;
        private readonly OptionsDialog m_options_dialog;
        private readonly AboutDialog m_about_dialog;
        private readonly ProjectBrowserDialog m_project_browser_dialog;

        private readonly OptionsPageGeneral m_options_general;
        private readonly OptionsPageViewPanel m_options_view_panel;
        private readonly OptionsPageProject m_options_project;
        private readonly OptionsPageInput m_options_input;

        private readonly Dictionary<string, DockContent> m_known_panels = new(StringComparer.Ordinal);
        private string m_active_context = "Global";
        private string m_transform_mode = "Translate";
        private string m_coord_space = "World";
        private bool m_object_snap = true;
        private bool m_vertex_snap;
        private bool m_edge_snap;
        private bool m_face_snap;
        private bool m_grid_snap = true;
        private bool m_terrain_snap;
        private bool m_simulate_mode;
        private int m_error_count;
        private int m_warning_count;
        private byte[]? m_saved_layout_xml;
        private static string SavedLayoutFilePath => Path.Combine(SdkVfsManager.UserDataRoot, "workspace-layout.xml");

        public event System.Action? UndoRequested;
        public event System.Action? RedoRequested;
        public Func<bool>? CanUndoQuery { get; set; }
        public Func<bool>? CanRedoQuery { get; set; }

        public WindowIde(EditorWorld? world = null, Control? existingViewport = null, bool backgroundMode = false)
        {
            m_editor_world = world;
            m_background_mode = backgroundMode;
            Activated+=(_,_)=>RuntimeFocus(true);Deactivate+=(_,_)=>RuntimeFocus(false);
            WpfControls.library_initializer.initialize();

            m_theme = SdkWindowStyle.CreateDockTheme(backgroundMode);
            m_dock_panel = new DockPanel
            {
                Name = "Editor",
                Dock = DockStyle.Fill,
                Theme = m_theme,
                DockBackColor = SdkWindowStyle.Canvas,
                DocumentStyle = DocumentStyle.DockingWindow,
                DockLeftPortion = 0.22,
                DockRightPortion = 0.23,
                DockBottomPortion = 0.25,
                DockTopPortion = 0.25
            };

            m_menu_strip = new MenuStrip { Name = "m_menu_strip", Dock = DockStyle.Top };
            m_runtime_tool_strip = new ToolStrip
            {
                Name = "RuntimeToolStrip",
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(16, 16)
            };
            SdkWindowStyle.StyleStrip(m_menu_strip);
            SdkWindowStyle.StyleStrip(m_runtime_tool_strip);
            m_menu_strip.Padding = new Padding(6, 2, 6, 3);
            m_runtime_tool_strip.Padding = new Padding(6, 2, 6, 3);
            FileMenuItem = new ToolStripMenuItem("&File") { Name = "FileMenuItem" };
            ProjectMenuItem = new ToolStripMenuItem("&Project") { Name = "ProjectMenuItem" };
            EditMenuItem = new ToolStripMenuItem("&Edit") { Name = "EditMenuItem" };
            ViewMenuItem = new ToolStripMenuItem("&View") { Name = "ViewMenuItem" };
            RunMenuItem = new ToolStripMenuItem("&Run") { Name = "RunMenuItem" };
            ToolsMenuItem = new ToolStripMenuItem("&Tools") { Name = "ToolsMenuItem" };
            HelpMenuItem = new ToolStripMenuItem("&Help") { Name = "HelpMenuItem" };
            RecentProjectsMenuItem = new ToolStripMenuItem("Recent projects") { Name = "RecentProjectsMenuItem" };
            EditProjectMenuItem = new ToolStripMenuItem("Edit project") { Name = "EditProjectMenuItem" };

            m_menu_strip.Items.AddRange(new ToolStripItem[]
            {
                FileMenuItem,
                ProjectMenuItem,
                EditMenuItem,
                ViewMenuItem,
                RunMenuItem,
                ToolsMenuItem,
                HelpMenuItem
            });

            statusStrip1 = new StatusStrip { Name = "statusStrip1", SizingGrip = false };
            SdkWindowStyle.StyleStrip(statusStrip1);
            statusStrip1.Padding = new Padding(6, 1, 6, 0);
            toolStripStatusLabel0 = new ToolStripStatusLabel("Ready")
            {
                Name = "toolStripStatusLabel0",
                Spring = true,
                Margin = new Padding(4, 3, 4, 3),
                TextAlign = ContentAlignment.MiddleLeft
            };
            toolStripStatusLabel3 = new ToolStripStatusLabel("Cam: (0.0, 2.0, -10.0)")
            {
                Name = "toolStripStatusLabel3",
                ForeColor = SdkWindowStyle.MutedText,
                Padding = new Padding(8, 0, 8, 0)
            };
            toolStripStatusLabel4 = new ToolStripStatusLabel("Grid: 100 x 100")
            {
                Name = "toolStripStatusLabel4",
                ForeColor = SdkWindowStyle.MutedText,
                Padding = new Padding(8, 0, 8, 0)
            };
            clearToolStripMenuItem = new ToolStripMenuItem("Clear") { Name = "clearToolStripMenuItem" };
            clearToolStripMenuItem.Click += (_, _) => ClearLogMessages();

            error_message_button = new ToolStripSplitButton("Errors: 0")
            {
                Name = "error_message_button",
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            error_message_button.DropDownItems.Add(clearToolStripMenuItem);
            error_message_button.ButtonClick += (_, _) => ToggleLogForm();

            message_button = new ToolStripButton("Log")
            {
                Name = "message_button",
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            message_button.Click += (_, _) => ToggleLogForm();

            statusStrip1.Items.AddRange(new ToolStripItem[]
            {
                toolStripStatusLabel0,
                toolStripStatusLabel3,
                toolStripStatusLabel4,
                error_message_button,
                message_button
            });

            m_images16 = new ImageLoader().get_shared_images16();
            InputKeysHolder.Create();
            m_input_engine = new InputEngine(InputKeysHolder.Ref, this);
            m_gui_binder = new GuiBinder(m_input_engine);
            m_gui_binder.set_image_list("base", m_images16);

            // Construct project, panels & dialogs
            m_project = new Project();
            m_editing = new ProjectEditing(m_project, Commands);
            if (m_background_mode) m_editing.Clipboard = new LocalProjectClipboard();
            if (m_background_mode) m_project.SaveConfirmationPromptOverride = _ => DialogResult.Cancel;
            m_view_window = new SceneViewPanel(existingViewport);
            m_main_tool_strip = m_view_window.create_tool_strip("MainToolStrip", 1);
            m_object_inspector_tab = new ObjectInspectorTab();
            m_project_tab = new ProjectTab(m_project, m_images16);
            m_tools_tab = new ToolsTab();
            m_animation_curve_editor_panel = new AnimationCurveEditorPanel();
            m_scene_graph_tab = new SceneGraphTab(m_project);
            m_camera_view_panel = new CameraViewPanel();
            m_triangulation_tester = new TriangulationTester();
            m_connection_editor = new PropertyConnectionEditor();
            m_connection_editor.BindProject(m_project, Commands);

            m_log_form = new IdeLogForm();
            m_options_dialog = new OptionsDialog();
            m_about_dialog = new AboutDialog();
            m_project_browser_dialog = new ProjectBrowserDialog();

            m_options_general = new OptionsPageGeneral(m_view_window);
            m_options_view_panel = new OptionsPageViewPanel(m_view_window);
            m_options_project = new OptionsPageProject(m_project);
            m_options_input = new OptionsPageInput(m_input_engine);

            m_options_dialog.register_page("General", m_options_general);
            m_options_dialog.register_page("General\\View Panel", m_options_view_panel);
            m_options_dialog.register_page("General\\Project", m_options_project);
            m_options_dialog.register_page("Input", m_options_input);

            RegisterKnownPanel(SceneViewPanel.PersistIdentity, m_view_window);
            RegisterKnownPanel(SceneViewPanel.LegacyPersistIdentity, m_view_window);
            RegisterKnownPanel(ProjectTab.PersistIdentity, m_project_tab);
            RegisterKnownPanel(ObjectInspectorTab.PersistIdentity, m_object_inspector_tab);
            RegisterKnownPanel(ObjectInspectorTab.SharedControlsAlias, m_object_inspector_tab);
            RegisterKnownPanel(ToolsTab.PersistIdentity, m_tools_tab);
            RegisterKnownPanel(AnimationCurveEditorPanel.PersistIdentity, m_animation_curve_editor_panel);
            RegisterKnownPanel(SceneGraphTab.PersistIdentity, m_scene_graph_tab);
            RegisterKnownPanel(CameraViewPanel.PersistIdentity, m_camera_view_panel);
            RegisterKnownPanel(TriangulationTester.PersistIdentity, m_triangulation_tester);

            m_project.SelectionChanged += OnProjectSelectionSynchronizeInspector;
            m_project.ProjectStateChanged += OnProjectStateSynchronizeIde;

            SuspendLayout();
            SdkWindowStyle.StylePane(this);
            BackColor = SdkWindowStyle.Canvas;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(1280, 800);
            Text = "X-Ray Editor IDE";
            KeyPreview = true;
            MainMenuStrip = m_menu_strip;

            Controls.Add(m_dock_panel);
            Controls.Add(statusStrip1);
            Controls.Add(m_runtime_tool_strip);
            Controls.Add(m_menu_strip);
            ResumeLayout(false);
            PerformLayout();

            // Force handle creation so docking and ElementHost parent lookups work deterministically
            _ = Handle;
            _ = m_view_window.view_panel.Handle;

            m_view_window.register_actions(m_input_engine, m_gui_binder, true);
            ApplyDefaultReferenceWorkspaceLayout();
            if (!m_background_mode)
            {
                m_view_window.RenderGrid = true;
                m_view_window.TransformMode = EditorTransformMode.Translate;
                m_object_snap = false;
                m_grid_snap = false;
            }
            RegisterIdeActionsAndMenus();
            WirePanelFocusAndEvents();
            SyncWpfParentForms();
            InitializeEditingBindings();
            Commands.Changed += () => { if (!IsDisposed && !m_shutdown) m_gui_binder.update_items(); };
        }

        protected override bool ShowWithoutActivation => m_background_mode || base.ShowWithoutActivation;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SdkWindowStyle.StyleTitleBar(Handle);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (m_background_mode)
                {
                    cp.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
                }
                return cp;
            }
        }

        public DockPanel DockPanel => m_dock_panel;
        public bool BackgroundMode => m_background_mode;
        public DockPanel Editor => m_dock_panel;
        public MenuStrip MenuStripControl => m_menu_strip;
        public StatusStrip StatusStripControl => statusStrip1;
        public InputEngine InputEngine => m_input_engine;
        public GuiBinder GuiBinder => m_gui_binder;
        public Project Project => m_project;

        public SceneViewPanel ViewWindow => m_view_window;
        public ToolStrip MainToolStrip => m_main_tool_strip;
        public ToolStrip RuntimeToolStrip => m_runtime_tool_strip;
        public ProjectTab ProjectTab => m_project_tab;
        public ObjectInspectorTab ObjectInspectorTab => m_object_inspector_tab;
        public ToolsTab ToolsTab => m_tools_tab;
        public AnimationCurveEditorPanel AnimationCurveEditorPanel => m_animation_curve_editor_panel;
        public SceneGraphTab SceneGraphTab => m_scene_graph_tab;
        public CameraViewPanel CameraViewPanel => m_camera_view_panel;
        public TriangulationTester TriangulationTester => m_triangulation_tester;
        public PropertyConnectionEditor ConnectionEditor => m_connection_editor;

        public IdeLogForm LogForm => m_log_form;
        public OptionsDialog OptionsDialog => m_options_dialog;
        public AboutDialog AboutDialog => m_about_dialog;
        public ProjectBrowserDialog ProjectBrowserDialog => m_project_browser_dialog;
        public OptionsPageGeneral OptionsGeneral => m_options_general;
        public OptionsPageViewPanel OptionsViewPanel => m_options_view_panel;
        public OptionsPageProject OptionsProject => m_options_project;
        public OptionsPageInput OptionsInput => m_options_input;

        public ToolStripSplitButton ErrorMessageButton => error_message_button;
        public ToolStripButton MessageButton => message_button;

        public string ActiveContext
        {
            get => m_active_context;
            set => m_active_context = string.IsNullOrWhiteSpace(value) ? "Global" : value;
        }

        public string LastCommandStatus { get; private set; } = "Ready";
        public string TransformMode => m_transform_mode;
        public string CoordinateSpace => m_coord_space;
        public bool ObjectSnap => m_object_snap;
        public bool VertexSnap => m_vertex_snap;
        public bool EdgeSnap => m_edge_snap;
        public bool FaceSnap => m_face_snap;
        public bool GridSnap => m_grid_snap;
        public bool TerrainSnap => m_terrain_snap;
        public bool SimulateMode => m_simulate_mode;

        private void RegisterKnownPanel(string id, DockContent content)
        {
            SdkWindowStyle.StylePane(content);
            m_known_panels[id] = content;
        }

        private void OnProjectSelectionSynchronizeInspector()
        {
            var adapter = m_editing.SelectionProperties();
            bool readOnly = m_project.SelectedItems.Count == 1
                && ProjectEditing.IsProtected(m_project.SelectedItems[0])
                && adapter is not ProjectTerrainProperties;
            m_object_inspector_tab.show_properties(adapter, readOnly);
        }

        private void OnProjectStateSynchronizeIde()
        {
            RefreshRecentProjectsMenu();
            string projLabel = m_project.empty()
                ? "(none)"
                : (string.IsNullOrWhiteSpace(m_project.Name) ? "unsaved" : m_project.Name);
            string dirtyMark = m_project.save_needed() ? "*" : string.Empty;
            Text = $"X-Ray Editor IDE - {projLabel}{dirtyMark}";
            m_gui_binder.update_items();
        }

        public void RefreshRecentProjectsMenu()
        {
            RecentProjectsMenuItem.DropDownItems.Clear();
            var recents = m_project.get_recent_list();
            if (recents.Count == 0)
            {
                RecentProjectsMenuItem.DropDownItems.Add(new ToolStripMenuItem("(No recent projects)") { Enabled = false });
                return;
            }

            foreach (string projName in recents)
            {
                string captured = projName;
                var item = new ToolStripMenuItem(captured)
                {
                    Name = $"recent_{captured.Replace('/', '_')}"
                };
                item.Click += (_, _) =>
                {
                    if (!ConfirmSaveBeforeTransition())
                        return;
                    RunProjectAction(() => OpenProject(captured));
                };
                RecentProjectsMenuItem.DropDownItems.Add(item);
            }
        }

        private bool ConfirmSaveBeforeTransition()
        {
            if (!ConfirmOwnedDocuments()) return false;
            var res = m_project.save_confirmation();
            if (res == DialogResult.Cancel)
            {
                SetStatus("Operation cancelled by save confirmation");
                return false;
            }
            if (res == DialogResult.Yes)
            {
                return SaveCurrentProject();
            }
            if (res == DialogResult.No && m_project.save_needed()) RunProjectAction(m_project.DiscardRecovery);
            return true;
        }

        public void AddWindow(DockContent form, DockState state)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            form.Show(m_dock_panel, state);
        }

        public void SetStatus(string text)
        {
            LastCommandStatus = text ?? string.Empty;
            toolStripStatusLabel0.Text = LastCommandStatus;
        }

        public void set_status_label(int idx, string text)
        {
            switch (idx)
            {
                case 0:
                    SetStatus(text);
                    break;
                case 1:
                case 3:
                    toolStripStatusLabel3.Text = text ?? string.Empty;
                    break;
                case 2:
                case 4:
                    toolStripStatusLabel4.Text = text ?? string.Empty;
                    break;
            }
        }

        public string get_status_label(int idx)
        {
            return idx switch
            {
                0 => toolStripStatusLabel0.Text ?? string.Empty,
                1 or 3 => toolStripStatusLabel3.Text ?? string.Empty,
                2 or 4 => toolStripStatusLabel4.Text ?? string.Empty,
                _ => string.Empty
            };
        }

        public MenuStrip get_menu_strip() => m_menu_strip;

        public void LogMessage(string message)
        {
            m_log_form.AddMessage(message);
            SetStatus(message);
        }

        public void LogWarning(string warning)
        {
            m_warning_count++;
            m_log_form.AddWarning(warning);
            if (m_error_count == 0)
            {
                error_message_button.BackColor = Color.FromArgb(255, 220, 140);
                error_message_button.Text = $"Warnings: {m_warning_count}";
            }
            SetStatus(warning);
        }

        public void LogError(string error)
        {
            m_error_count++;
            m_log_form.AddError(error);
            error_message_button.BackColor = Color.FromArgb(255, 128, 128);
            error_message_button.Text = $"Errors: {m_error_count}";
            SetStatus(error);
        }

        public void ClearLogMessages()
        {
            m_error_count = 0;
            m_warning_count = 0;
            m_log_form.ClearMessages();
            error_message_button.BackColor = Color.Empty;
            error_message_button.Text = "Errors: 0";
            SetStatus("Log cleared");
        }

        public void ToggleLogForm()
        {
            if (m_log_form.Visible)
            {
                m_log_form.Hide();
                SetStatus("Log view hidden");
            }
            else
            {
                if (!m_background_mode && Visible)
                {
                    m_log_form.Show(this);
                }
                else
                {
                    m_log_form.ShowInTaskbar = false;
                    m_log_form.Opacity = 0;
                    _ = m_log_form.Handle;
                    m_log_form.Visible = true;
                }
                error_message_button.BackColor = Color.Empty;
                SetStatus("Log view shown");
            }
        }

        public void ToggleConnectionEditor()
        {
            if (m_connection_editor.Visible)
            {
                m_connection_editor.Hide();
                SetStatus("Connection Editor hidden");
            }
            else
            {
                if (!m_background_mode && Visible)
                {
                    m_connection_editor.Show(this);
                }
                else
                {
                    m_connection_editor.ShowInTaskbar = false;
                    m_connection_editor.Opacity = 0;
                    _ = m_connection_editor.Handle;
                    m_connection_editor.Visible = true;
                }
                SetStatus("Connection Editor shown");
            }
        }

        public bool context_fit(string contextStr)
        {
            if (string.IsNullOrEmpty(contextStr) || string.Equals(contextStr, "Global", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(contextStr, m_active_context, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(contextStr, "View", StringComparison.OrdinalIgnoreCase))
                return ReferenceEquals(m_dock_panel.ActiveContent, m_view_window)
                    || m_view_window.ContainsFocus
                    || string.Equals(m_active_context, "View", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(contextStr, "Project", StringComparison.OrdinalIgnoreCase))
                return ReferenceEquals(m_dock_panel.ActiveContent, m_project_tab)
                    || m_project_tab.ContainsFocus
                    || string.Equals(m_active_context, "Project", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(contextStr, "Properties", StringComparison.OrdinalIgnoreCase))
                return ReferenceEquals(m_dock_panel.ActiveContent, m_object_inspector_tab)
                    || m_object_inspector_tab.ContainsFocus
                    || string.Equals(m_active_context, "Properties", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public List<string> get_contexts_list()
        {
            return new List<string> { "Global", "View", "Project", "Properties", "Tools", "CurveEditor", "SceneGraph" };
        }

        private void SyncWpfParentForms()
        {
            WpfControls.utils.set_parent_form(m_object_inspector_tab.PropertyGridHost.property_grid, this);
            WpfControls.utils.set_parent_form(m_animation_curve_editor_panel.CurveEditorControl, this);
        }

        private void WirePanelFocusAndEvents()
        {
            m_view_window.Enter += (_, _) => m_active_context = "View";
            m_view_window.view_panel.Enter += (_, _) => m_active_context = "View";
            m_project_tab.Enter += (_, _) => m_active_context = "Project";
            m_project_tab.treeView.Enter += (_, _) => m_active_context = "Project";
            m_object_inspector_tab.Enter += (_, _) => m_active_context = "Properties";
            m_tools_tab.Enter += (_, _) => m_active_context = "Tools";
            m_animation_curve_editor_panel.Enter += (_, _) => m_active_context = "CurveEditor";
            m_scene_graph_tab.Enter += (_, _) => m_active_context = "SceneGraph";

            m_view_window.StatusChanged += status => SetStatus(status);
            m_view_window.CameraMoved += () =>
            {
                var pos = m_view_window.get_inverted_view_matrix().Translation;
                set_status_label(3, string.Format(CultureInfo.InvariantCulture, "Cam: ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z));
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (ShouldKeepTextInput(e)) { m_input_engine.reset_input(); return; }
            if((e.Modifiers&(Keys.Control|Keys.Alt))==0 && (context_fit("View") || Navigation?.Active==true) && Navigation?.KeyDown(e.KeyCode)==true) {e.Handled=true;e.SuppressKeyPress=true;return;}
            if (e.Modifiers == Keys.None && (context_fit("View") || ViewportEditing?.Dragging == true) && ViewportEditing != null)
            {
                bool handled = false;
                RunProjectAction(() => handled = ViewportEditing.ProcessKey(e.KeyCode));
                if (handled) { e.Handled = true; e.SuppressKeyPress = true; return; }
            }
            InputKeysHolder.Ref.InjectKeyDown(e.KeyCode);
            m_input_engine.execute();
            m_gui_binder.update_items();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if(Navigation?.KeyUp(e.KeyCode)==true) {e.Handled=true;e.SuppressKeyPress=true;return;}
            if (ShouldKeepTextInput(e)) { m_input_engine.reset_input(); return; }
            InputKeysHolder.Ref.InjectKeyUp(e.KeyCode);
            m_input_engine.execute();
        }

        public bool ProcessKeyCombination(string combination)
        {
            bool handled = m_input_engine.ProcessCombination(combination);
            m_gui_binder.update_items();
            return handled;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern nint SetFocus(nint hWnd);

        public void ApplyDefaultReferenceWorkspaceLayout()
        {
            var viewportHandle = m_view_window.view_panel.Handle;

            // Detach all known panels without closing or disposing them
            SetFocus(Handle);
            ActiveControl = null;
            m_dock_panel.ActiveAutoHideContent = null;
            foreach (var panel in m_known_panels.Values.Distinct())
            {
                panel.ActiveControl = null;
                panel.DockHandler.DockPanel = null;
            }
            foreach (var floatWindow in m_dock_panel.FloatWindows.ToArray())
                floatWindow.Dispose();
            foreach (var pane in m_dock_panel.Panes.ToArray())
                pane.Dispose();

            // Reference workspace layout from level_editor.cpp:
            // 1. m_view_window -> DockState.Document
            // 2. m_project_tab -> DockState.DockLeft
            // 3. m_object_inspector_tab -> nested inside m_project_tab.Pane, DockAlignment.Bottom, 0.25
            // 4. m_tools_tab -> DockState.DockRight
            m_view_window.Show(m_dock_panel, DockState.Document);
            m_project_tab.Show(m_dock_panel, DockState.DockLeft);
            if (m_project_tab.Pane != null)
            {
                m_object_inspector_tab.Show(m_project_tab.Pane, DockAlignment.Bottom, 0.25);
            }
            else
            {
                m_object_inspector_tab.Show(m_dock_panel, DockState.DockLeft);
            }
            m_tools_tab.Show(m_dock_panel, DockState.DockRight);

            foreach (DockWindow dockWindow in m_dock_panel.DockWindows)
            {
                dockWindow.PerformLayout();
                dockWindow.Visible = dockWindow.VisibleNestedPanes.Count != 0;
            }
            m_dock_panel.PerformLayout();
            PerformLayout();
            SyncWpfParentForms();

            if (m_view_window.view_panel.Handle != viewportHandle)
                throw new InvalidOperationException("Default layout reset recreated the live viewport HWND.");

            set_status_label(4, $"Grid: {m_view_window.m_grid_dim_val} x {m_view_window.m_grid_dim_val}");
            SetStatus("Reference workspace layout active");
            if (!m_background_mode)
            {
                m_active_context = "View";
                m_view_window.Activate();
                m_view_window.view_panel.Focus();
            }
        }

        public void ResetToDefaultLayout()
        {
            if (m_log_form.Visible) m_log_form.Hide();
            if (m_connection_editor.Visible) m_connection_editor.Hide();
            ApplyDefaultReferenceWorkspaceLayout();
            m_gui_binder.update_items();
        }

        public byte[] SaveLayoutXml()
        {
            return serializer.SaveDockPanelXml(m_dock_panel);
        }

        public void SaveLayoutFile(string filePath)
        {
            var bytes = SaveLayoutXml();
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, filePath, true);
                SetStatus($"Layout saved: {Path.GetFileName(filePath)}");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public void LoadLayoutFile(string filePath, bool strictUnknownPanels = true)
        {
            if (new FileInfo(filePath).Length > 1024 * 1024)
                throw new InvalidDataException("SDK layout file exceeds 1 MiB.");
            LoadLayoutXml(File.ReadAllBytes(filePath), strictUnknownPanels);
            SetStatus($"Layout loaded: {Path.GetFileName(filePath)}");
        }

        public void LoadLayoutXml(byte[] layoutBytes, bool strictUnknownPanels = true, IReadOnlyList<Rectangle>? workingAreas = null)
        {
            if (layoutBytes == null) throw new ArgumentNullException(nameof(layoutBytes));
            var doc = serializer.ParseAndValidateLayoutXml(
                layoutBytes,
                rejectFloating: false,
                isKnownContentId: id => m_known_panels.ContainsKey(id),
                strictUnknownContent: strictUnknownPanels,
                out var skippedUnknownIds);

            serializer.ClampFloatWindowsInXml(doc, workingAreas);

            // If tolerant mode skipped unknown panels, strip their <Content> and <Ref> nodes so DockPanel Suite's
            // fixed Content/Ref index loader cleanly loads the remaining known panels without index shifts.
            if (!strictUnknownPanels && skippedUnknownIds.Count > 0)
            {
                RemoveUnknownContentNodesFromXml(doc);
            }

            using var normalizedStream = new MemoryStream();
            using (var writer = new XmlTextWriter(normalizedStream, Encoding.UTF8))
            {
                doc.Save(writer);
            }
            byte[] normalizedBytes = normalizedStream.ToArray();

            var originalLayout = SaveLayoutXml();
            var viewportHandle = m_view_window.view_panel.Handle;
            var previousSelection = m_object_inspector_tab.PropertyGridHost.selected_object;

            try
            {
                ApplyLayoutBytesCore(normalizedBytes, strictUnknownPanels);
            }
            catch
            {
                ApplyLayoutBytesCore(originalLayout, false);
                throw;
            }

            if (m_view_window.view_panel.Handle != viewportHandle)
                throw new InvalidOperationException("DockPanel layout restore recreated the viewport HWND.");

            if (!ReferenceEquals(m_object_inspector_tab.PropertyGridHost.selected_object, previousSelection))
            {
                m_object_inspector_tab.show_properties(previousSelection);
            }
            SyncWpfParentForms();
            m_gui_binder.update_items();
            SetStatus(skippedUnknownIds.Count > 0
                ? $"Workspace layout restored (skipped {skippedUnknownIds.Count} unknown panel(s))"
                : "Workspace layout restored");
        }

        private void RemoveUnknownContentNodesFromXml(XDocument doc)
        {
            var contentsElement = doc.Root?.Element("Contents");
            if (contentsElement == null) return;
            var contentList = contentsElement.Elements("Content").ToList();
            var oldToNewIndex = new Dictionary<int, int>();
            int newIdx = 0;

            for (int oldIdx = 0; oldIdx < contentList.Count; oldIdx++)
            {
                string id = (string?)contentList[oldIdx].Attribute("PersistString") ?? string.Empty;
                if (m_known_panels.ContainsKey(id))
                {
                    oldToNewIndex[oldIdx] = newIdx++;
                }
                else
                {
                    contentList[oldIdx].Remove();
                }
            }
            contentsElement.SetAttributeValue("Count", newIdx);

            // Update or remove <Content ID="..." /> references inside <Pane><Contents> and <AutoHidePane>
            foreach (var paneContents in doc.Descendants("Contents").Where(e => e != contentsElement).ToList())
            {
                var refs = paneContents.Elements("Content").ToList();
                int keptCount = 0;
                foreach (var refEl in refs)
                {
                    if (int.TryParse((string?)refEl.Attribute("RefID"), out int refId) &&
                        oldToNewIndex.TryGetValue(refId, out int mappedId))
                    {
                        refEl.SetAttributeValue("RefID", mappedId);
                        keptCount++;
                    }
                    else
                    {
                        refEl.Remove();
                    }
                }
                paneContents.SetAttributeValue("Count", keptCount);
            }
        }

        private void ApplyLayoutBytesCore(byte[] layoutBytes, bool strictUnknownPanels)
        {
            SetFocus(Handle);
            ActiveControl = null;
            m_dock_panel.ActiveAutoHideContent = null;
            foreach (var panel in m_known_panels.Values.Distinct())
            {
                panel.ActiveControl = null;
                panel.DockHandler.DockPanel = null;
            }
            foreach (var floatWindow in m_dock_panel.FloatWindows.ToArray())
                floatWindow.Dispose();
            foreach (var pane in m_dock_panel.Panes.ToArray())
                pane.Dispose();

            using var stream = new MemoryStream(layoutBytes);
            m_dock_panel.LoadFromXml(stream, persistId =>
            {
                if (m_known_panels.TryGetValue(persistId, out var content))
                    return content;
                if (strictUnknownPanels)
                    throw new InvalidDataException($"Unknown SDK panel identity '{persistId}'.");
                return null;
            });

            foreach (var floatWindow in m_dock_panel.FloatWindows.Where(fw => fw.NestedPanes.Count == 0).ToArray())
                floatWindow.Dispose();

            foreach (DockWindow dockWindow in m_dock_panel.DockWindows)
            {
                dockWindow.PerformLayout();
                dockWindow.Visible = dockWindow.VisibleNestedPanes.Count != 0;
            }
            m_dock_panel.PerformLayout();
            PerformLayout();
        }

        public void SaveWorkspaceSettingsIsolated(string isolatedFilePath)
        {
            if (string.IsNullOrWhiteSpace(isolatedFilePath))
                throw new ArgumentException("Isolated settings file path is required.", nameof(isolatedFilePath));

            var custom = new Dictionary<string, string>();
            m_input_engine.save(custom);
            m_view_window.save_settings(custom);
            serializer.SaveWindowSettingsIsolated(isolatedFilePath, this, m_dock_panel, "level_editor", custom);
            SetStatus($"Isolated settings saved: {Path.GetFileName(isolatedFilePath)}");
        }

        public void LoadWorkspaceSettingsIsolated(
            string isolatedFilePath,
            IReadOnlyList<Rectangle>? monitorWorkingAreas = null,
            bool strictUnknownPanels = false)
        {
            var snapshot = serializer.LoadWindowSettingsIsolated(isolatedFilePath);
            var clampedBounds = serializer.ClampBoundsToWorkingAreas(
                new Rectangle(snapshot.Left, snapshot.Top, snapshot.Width, snapshot.Height),
                monitorWorkingAreas);

            WindowState = FormWindowState.Normal;
            Bounds = clampedBounds;
            if (snapshot.WindowState == (int)FormWindowState.Maximized && !m_background_mode)
            {
                WindowState = FormWindowState.Maximized;
            }

            if (snapshot.CustomValues != null && snapshot.CustomValues.Count > 0)
            {
                var shortcutsDict = snapshot.CustomValues
                    .Where(kv => kv.Key == "shortcuts_ver" || kv.Key.All(char.IsDigit))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (shortcutsDict.Count > 0)
                    m_input_engine.load(shortcutsDict);
                m_view_window.load_settings(snapshot.CustomValues);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.DockPanelXmlBase64))
            {
                byte[] xmlBytes = Convert.FromBase64String(snapshot.DockPanelXmlBase64);
                LoadLayoutXml(xmlBytes, strictUnknownPanels, monitorWorkingAreas);
            }

            set_status_label(4, $"Grid: {m_view_window.m_grid_dim_val} x {m_view_window.m_grid_dim_val}");
            m_gui_binder.update_items();
            SetStatus($"Isolated settings loaded: {Path.GetFileName(isolatedFilePath)}");
        }

        private void ToggleDockPanel(DockContent panel, DockState defaultDockState, string displayName)
        {
            if (!panel.IsHidden && panel.DockState != DockState.Unknown && panel.DockState != DockState.Hidden)
            {
                panel.Hide();
                SetStatus($"{displayName} hidden");
            }
            else
            {
                if (panel.DockState == DockState.Hidden && panel.DockHandler.DockPanel == m_dock_panel)
                {
                    panel.Show();
                }
                else if (ReferenceEquals(panel, m_object_inspector_tab) && m_project_tab.Pane != null && !m_project_tab.IsHidden)
                {
                    panel.Show(m_project_tab.Pane, DockAlignment.Bottom, 0.25);
                }
                else
                {
                    panel.Show(m_dock_panel, defaultDockState);
                }
                SetStatus($"{displayName} shown");
            }
            SyncWpfParentForms();
            m_gui_binder.update_items();
        }

        private static bool IsPanelVisible(DockContent panel)
        {
            return !panel.IsHidden && panel.DockState != DockState.Unknown && panel.DockState != DockState.Hidden;
        }

        private void RegisterAction(
            string name,
            ExecuteDelegate execute,
            EnabledDelegate? enabled = null,
            CheckedDelegate? checkedState = null,
            string? shortcut = null)
        {
            var action = new ActionDelegate(name, execute);
            if (enabled != null) action.set_enabled(enabled);
            if (checkedState != null) action.set_checked(checkedState);
            if (!m_input_engine.action_exists(name))
                m_input_engine.register_action(action, shortcut ?? string.Empty);
        }

        internal static string ResolveModelSourceUri(string libraryName)
        {
            string lib = libraryName.Trim().Replace('\\', '/');
            if (lib.StartsWith("resources/models/", StringComparison.OrdinalIgnoreCase)) lib = lib["resources/models/".Length..];
            if (lib.EndsWith("/render", StringComparison.OrdinalIgnoreCase)) lib = lib[..^"/render".Length];
            if (lib.EndsWith(".skinned_model", StringComparison.OrdinalIgnoreCase) || lib.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                return "resources/models/" + lib;
            if (SdkVfsManager.IsConfigured && Directory.Exists(SdkVfsManager.ResolveVirtualToPhysical("resources/models/" + lib + ".skinned_model")))
                return "resources/models/" + lib + ".skinned_model";
            return "resources/models/" + lib + ".model";
        }

        internal static string ResolveModelRenderUri(string libraryName) => ResolveModelSourceUri(libraryName) + "/render";

        private void RegisterIdeActionsAndMenus()
        {
            // Submenus in Project
            ProjectMenuItem.DropDownItems.Add(RecentProjectsMenuItem);
            ProjectMenuItem.DropDownItems.Add(EditProjectMenuItem);
            RecentProjectsMenuItem.DropDownItems.Add(new ToolStripMenuItem("(No recent projects)") { Enabled = false });
            var addRootFolderItem = new ToolStripMenuItem("Add New Filter");
            addRootFolderItem.Click += (_, _) => RunProjectAction(() =>
            {
                var group = m_project_tab.AddNewFilterFolder("new_filter");
                SetStatus($"Added folder: {group.Name}");
            });
            EditProjectMenuItem.DropDownItems.Add(addRootFolderItem);

            // 1. File menu actions
            RegisterAction("Save layout", () =>
            {
                m_saved_layout_xml = SaveLayoutXml();
                if (SdkVfsManager.IsConfigured)
                    RunProjectAction(() => SaveLayoutFile(SavedLayoutFilePath));
                SetStatus("Workspace layout saved");
            });
            RegisterAction("Load layout", () =>
            {
                if (SdkVfsManager.IsConfigured && File.Exists(SavedLayoutFilePath))
                    RunProjectAction(() => LoadLayoutFile(SavedLayoutFilePath));
                else if (m_saved_layout_xml != null)
                    RunProjectAction(() => LoadLayoutXml(m_saved_layout_xml));
                else
                    SetStatus("No saved workspace layout found. Use File -> Save layout first.");
            });
            RegisterAction("Reset layout", () =>
            {
                ResetToDefaultLayout();
            });
            RegisterAction("Exit", () =>
            {
                SetStatus("Exit requested");
                Close();
            }, shortcut: "Alt+X(Global)");

            m_gui_binder.add_action_menu_item(m_menu_strip, "Save layout", "FileMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Load layout", "FileMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Reset layout", "FileMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Exit", "FileMenuItem", 10);

            // 2. Project menu actions
            RegisterAction("New project", () => RunProjectAction(() =>
            {
                if (!ConfirmSaveBeforeTransition())
                    return;
                CloseOwnedDocuments();
                ResourcePipeline?.CancelAllQueries();
                m_project.CreateNewProject("new_project");
                m_project_tab.RefreshTree();
                SetStatus("New project created");
            }), shortcut: "Ctrl+N(Global)");

            RegisterAction("Open project", () =>
            {
                if (!ConfirmSaveBeforeTransition())
                    return;
                RunProjectAction(() => {
                    if (!CanShowProjectDialog()) return;
                    string rootPath = SdkVfsManager.ResolveVirtualToPhysical("resources/projects", forWrite: true);
                    if (m_project_browser_dialog.SelectProjectToLoad(rootPath, m_project.Name, this) == DialogResult.OK)
                        OpenProject(m_project_browser_dialog.selected_project!);
                    else SetStatus("Open project cancelled");
                });
            }, shortcut: "Ctrl+O(Global)");

            RegisterAction("Save project", () =>
            {
                SaveCurrentProject();
            }, enabled: () => m_project.IsChanged() && !m_project.empty(), shortcut: "Ctrl+S(Global)");

            RegisterAction("Save project as", () =>
            {
                SaveCurrentProject(saveAs: true);
            }, enabled: () => !m_project.empty());

            RegisterAction("Close project", () =>
            {
                if (!ConfirmSaveBeforeTransition())
                    return;
                CloseOwnedDocuments();
                ResourcePipeline?.CancelAllQueries();
                m_project.CloseProject();
                m_project_tab.RefreshTree();
                SetStatus("Project closed");
            }, enabled: () => !m_project.empty());

            m_gui_binder.add_action_menu_item(m_menu_strip, "New project", "ProjectMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Open project", "ProjectMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Save project", "ProjectMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Save project as", "ProjectMenuItem", 4);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Close project", "ProjectMenuItem", 5);

            // 3. Edit menu actions
            RegisterAction("Undo", () =>
            {
                if (UndoRequested != null) UndoRequested.Invoke(); else Commands.rollback(-1);
                SetStatus("Undo executed");
            }, enabled: () => CanUndoQuery?.Invoke() ?? Commands.CanUndo, shortcut: "Ctrl+Z(Global)");

            RegisterAction("Redo", () =>
            {
                if (RedoRequested != null) RedoRequested.Invoke(); else Commands.rollback(1);
                SetStatus("Redo executed");
            }, enabled: () => CanRedoQuery?.Invoke() ?? Commands.CanRedo, shortcut: "Ctrl+Y(Global)");

            RegisterAction("Cut", () =>
            {
                RunProjectAction(() => { m_editing.CutSelection(); SetStatus("Selection cut"); });
            }, enabled: () => CanEditSelection, shortcut: "Ctrl+X(Global)");

            RegisterAction("Copy", () =>
            {
                RunProjectAction(() => { m_editing.CopySelection(); SetStatus("Selection copied"); });
            }, enabled: () => m_editing.CanCopy, shortcut: "Ctrl+C(Global)");

            RegisterAction("Paste", () =>
            {
                RunProjectAction(() => { var items = m_editing.PasteSelection(); SetStatus($"Pasted {items.Length} item(s)"); });
            }, enabled: () => m_editing.CanPaste, shortcut: "Ctrl+V(Global)");

            RegisterAction("Delete", () =>
            {
                RunProjectAction(() => { int count = m_editing.EditableSelection.Length; m_editing.Delete(m_editing.EditableSelection); SetStatus($"Deleted {count} item(s)"); });
            }, enabled: () => CanEditSelection, shortcut: "Delete(Global)");

            RegisterAction("Select All", () =>
            {
                var all = new List<ProjectItemBase>();
                m_project.Root.get_all_items(all, true);
                m_editing.Select(all.Where(i => !ProjectEditing.IsProtected(i)));
                SetStatus($"Selected all ({m_project.SelectedItems.Count} items)");
            }, enabled: () => m_project.IsProjectOpen && !Commands.InPreview, shortcut: "Ctrl+A(Global)");

            RegisterAction("Deselect", () =>
            {
                m_editing.Select(Array.Empty<ProjectItemBase>());
                SetStatus("Selection cleared");
            }, enabled: () => m_project.IsProjectOpen && !Commands.InPreview, shortcut: "Escape(Global)");

            m_gui_binder.add_action_menu_item(m_menu_strip, "Undo", "EditMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Redo", "EditMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Cut", "EditMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Copy", "EditMenuItem", 4);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Paste", "EditMenuItem", 5);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Delete", "EditMenuItem", 6);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Select All", "EditMenuItem", 7);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Deselect", "EditMenuItem", 8);

            // 4. View menu panel toggles
            RegisterAction("Project explorer",
                () => ToggleDockPanel(m_project_tab, DockState.DockLeft, "Project explorer"),
                checkedState: () => IsPanelVisible(m_project_tab));

            RegisterAction("Properties Panel",
                () => ToggleDockPanel(m_object_inspector_tab, DockState.DockLeft, "Properties Panel"),
                checkedState: () => IsPanelVisible(m_object_inspector_tab));

            RegisterAction("Tools frame",
                () => ToggleDockPanel(m_tools_tab, DockState.DockRight, "Tools frame"),
                checkedState: () => IsPanelVisible(m_tools_tab));

            RegisterAction("Animation Curve Editor",
                () => ToggleDockPanel(m_animation_curve_editor_panel, DockState.DockBottom, "Animation Curve Editor"),
                checkedState: () => IsPanelVisible(m_animation_curve_editor_panel));

            RegisterAction("Scene graph",
                () => ToggleDockPanel(m_scene_graph_tab, DockState.Float, "Scene graph"),
                checkedState: () => IsPanelVisible(m_scene_graph_tab));

            RegisterAction("Camera View",
                () => ToggleDockPanel(m_camera_view_panel, DockState.Float, "Camera View"),
                checkedState: () => IsPanelVisible(m_camera_view_panel));
            RegisterAction("Preview selection",()=>RunProjectAction(()=>{
                var item=m_editing.Objects(m_project.SelectedItems).FirstOrDefault(i=>i.ToolName=="solid_visual") ?? throw new InvalidOperationException("Select a model to preview.");
                ViewportOutputs!.PreviewAsset(ResolveModelRenderUri(item.LibraryName),SdkOutputKind.Model);
            }),enabled:()=>ViewportOutputs!=null && m_project.IsProjectOpen);

            RegisterAction("Triangulation tester",
                () => ToggleDockPanel(m_triangulation_tester, DockState.Float, "Triangulation tester"),
                checkedState: () => IsPanelVisible(m_triangulation_tester));

            RegisterAction("Log view",
                () => ToggleLogForm(),
                checkedState: () => m_log_form.Visible);

            m_gui_binder.add_action_menu_item(m_menu_strip, "Project explorer", "ViewMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Properties Panel", "ViewMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Tools frame", "ViewMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Animation Curve Editor", "ViewMenuItem", 4);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Scene graph", "ViewMenuItem", 5);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Camera View", "ViewMenuItem", 6);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Triangulation tester", "ViewMenuItem", 7);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Log view", "ViewMenuItem", 10);
            RegisterAction("Resource browser", ShowResourceBrowser);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Resource browser", "ViewMenuItem", 11);

            // 5. Tools menu actions (including connected Connection Editor, Options, and explicit dormant routes for SDK-5/SDK-6 asset editors)
            RegisterAction("Connection Editor",
                () => ToggleConnectionEditor(),
                checkedState: () => m_connection_editor.Visible);

            RegisterAction("Sound editor", () => RunProjectAction(ShowSoundEditor));
            RegisterAction("Particle editor", () => RunProjectAction(ShowParticleEditor));
            RegisterAction("Texture editor", ShowTextureEditor);
            RegisterAction("Model editor", ShowModelEditor);
            RegisterAction("Export selection OBJ",()=>ShowObjExport(true),enabled:()=>Project.IsProjectOpen && !RuntimeActive);
            RegisterAction("Export project OBJ",()=>ShowObjExport(false),enabled:()=>Project.IsProjectOpen && !RuntimeActive);
            m_gui_binder.add_action_menu_item(m_menu_strip,"Export selection OBJ","ToolsMenuItem",12);
            m_gui_binder.add_action_menu_item(m_menu_strip,"Export project OBJ","ToolsMenuItem",13);
            RegisterAction("Material editor", ShowMaterialEditor);
            RegisterAction("Render options editor",ShowRenderOptionsEditor);
            m_gui_binder.add_action_menu_item(m_menu_strip,"Render options editor","ToolsMenuItem",14);
            RegisterAction("Animation editor",()=>RunProjectAction(ShowAnimationEditor));
            RegisterAction("New animation collection",()=>RunProjectAction(NewAnimationCollection));
            RegisterAction("New animation viewer",()=>RunProjectAction(()=>NewAnimationViewer()));
            RegisterAction("New interval clip",()=>RunProjectAction(()=>NewAnimationViewer(true)));
            m_gui_binder.add_action_menu_item(m_menu_strip,"New animation viewer","ToolsMenuItem",19);
            m_gui_binder.add_action_menu_item(m_menu_strip,"New interval clip","ToolsMenuItem",20);
            m_gui_binder.add_action_menu_item(m_menu_strip,"New animation collection","ToolsMenuItem",18);
            RegisterAction("DCC sources / playback",()=>RunProjectAction(()=>ShowDccSource()));
            m_gui_binder.add_action_menu_item(m_menu_strip,"DCC sources / playback","ToolsMenuItem",15);
            RegisterAction("Dialog editor", () => RunProjectAction(() => ShowDialogEditor()));
            RegisterAction("Logic / Scripting editor", () => RunProjectAction(() => ShowLogicEditor()));
            RegisterAction("Navigation mesh...", () => RunProjectAction(() => ShowNavigationPanel()));
            RegisterAction("Options...", () =>
            {
                m_options_dialog.EnsureInitialized();
                if (!m_background_mode && Visible)
                {
                    m_options_dialog.ShowDialog(this);
                }
                SetStatus("Options dialog initialized");
            });

            m_gui_binder.add_action_menu_item(m_menu_strip, "Connection Editor", "ToolsMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Sound editor", "ToolsMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Particle editor", "ToolsMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Texture editor", "ToolsMenuItem", 4);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Model editor", "ToolsMenuItem", 5);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Material editor", "ToolsMenuItem", 6);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Animation editor", "ToolsMenuItem", 7);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Dialog editor", "ToolsMenuItem", 8);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Logic / Scripting editor", "ToolsMenuItem", 9);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Navigation mesh...", "ToolsMenuItem", 10);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Options...", "ToolsMenuItem", 20);

            // 6. Help menu actions
            RegisterAction("About...", () =>
            {
                if (!m_background_mode && Visible)
                {
                    m_about_dialog.ShowDialog(this);
                }
                SetStatus($"About: {m_about_dialog.BuildSummary}");
            });
            m_gui_binder.add_action_menu_item(m_menu_strip, "About...", "HelpMenuItem", 1);

            // 7. Main ToolStrip (transform, snap, coordinate space, undo/redo, simulate) in SceneViewPanel
            RegisterAction("Select mode",
                () => { m_transform_mode = "Select"; m_view_window.TransformMode = EditorTransformMode.None; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Select"); },
                enabled: () => !Commands.InPreview,
                checkedState: () => m_transform_mode == "Select",
                shortcut: "Q(View)");

            RegisterAction("Translate mode",
                () => { m_transform_mode = "Translate"; m_view_window.TransformMode = EditorTransformMode.Translate; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Translate"); },
                enabled: () => !Commands.InPreview,
                checkedState: () => m_transform_mode == "Translate",
                shortcut: "W(View)");

            RegisterAction("Rotate mode",
                () => { m_transform_mode = "Rotate"; m_view_window.TransformMode = EditorTransformMode.Rotate; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Rotate"); },
                enabled: () => !Commands.InPreview,
                checkedState: () => m_transform_mode == "Rotate",
                shortcut: "E(View)");

            RegisterAction("Scale mode",
                () => { m_transform_mode = "Scale"; m_view_window.TransformMode = EditorTransformMode.Scale; ViewportEditing?.SynchronizeGizmo(); SetStatus("Transform mode: Scale"); },
                enabled: () => !Commands.InPreview,
                checkedState: () => m_transform_mode == "Scale",
                shortcut: "R(View)");

            RegisterAction("Coordinate space",
                () =>
                {
                    m_coord_space = m_coord_space == "World" ? "Local" : "World";
                    ViewportEditing?.SynchronizeGizmo();
                    SetStatus($"Coordinate space: {m_coord_space}");
                },
                enabled: () => !Commands.InPreview,
                checkedState: () => m_coord_space == "Local");

            RegisterAction("Object snap",
                () => { m_object_snap = !m_object_snap; SetStatus($"Object snap: {(m_object_snap ? "On" : "Off")}"); },
                checkedState: () => m_object_snap);

            RegisterAction("Vertex snap",
                () => { m_vertex_snap = !m_vertex_snap; SetStatus($"Vertex snap: {(m_vertex_snap ? "On" : "Off")}"); },
                checkedState: () => m_vertex_snap);

            RegisterAction("Edge snap",
                () => { m_edge_snap = !m_edge_snap; SetStatus($"Edge snap: {(m_edge_snap ? "On" : "Off")}"); },
                checkedState: () => m_edge_snap);

            RegisterAction("Face snap",
                () => { m_face_snap = !m_face_snap; SetStatus($"Face snap: {(m_face_snap ? "On" : "Off")}"); },
                checkedState: () => m_face_snap);

            RegisterAction("Grid snap",
                () => { m_grid_snap = !m_grid_snap; SetStatus($"Grid snap: {(m_grid_snap ? "On" : "Off")}"); },
                checkedState: () => m_grid_snap);

            RegisterAction("Terrain snap",
                () => { m_terrain_snap = !m_terrain_snap; SetStatus($"Terrain snap: {(m_terrain_snap ? "On" : "Off")}"); },
                checkedState: () => m_terrain_snap, enabled: () => !Commands.InPreview);

            RegisterAction("Play mode",()=>RunProjectAction(()=>{if(RuntimeActive)StopRuntime();else StartRuntime(false);}),enabled:()=>runtimeScene!=null && m_project.IsProjectOpen && !Commands.InPreview,checkedState:()=>RuntimeActive && !m_simulate_mode,shortcut:"F5(Global)|Alt+Enter(Global)");
            RegisterAction("Simulate mode",()=>RunProjectAction(()=>{if(RuntimeActive)StopRuntime();else StartRuntime(true);}),enabled:()=>runtimeScene!=null && m_project.IsProjectOpen && !Commands.InPreview,checkedState:()=>RuntimeActive && m_simulate_mode,shortcut:"Shift+F5(Global)");
            RegisterAction("Pause runtime",()=>RunProjectAction(PauseRuntime),enabled:()=>RuntimeActive,checkedState:()=>RuntimePaused,shortcut:"F6(Global)");
            RegisterAction("Step runtime",()=>RunProjectAction(StepRuntime),enabled:()=>RuntimeActive && RuntimePaused,shortcut:"F7(Global)");
            RegisterAction("Stop runtime",()=>RunProjectAction(StopRuntime),enabled:()=>RuntimeActive,shortcut:"Shift+F6(Global)");
            RegisterAction("Reload resources",()=>RunProjectAction(ReloadNativeResources),enabled:()=>!RuntimeActive && reloadScene!=null && m_project.IsProjectOpen && !Commands.InPreview,shortcut:"Ctrl+R(View)");

            RegisterAction("Focus selection",
                () => RunProjectAction(()=>SetStatus(Navigation==null || Navigation.FocusSelection()?"Focused camera on active selection":"Select an object to focus")),
                shortcut: "F(View)");

            m_gui_binder.add_action_menu_item(m_menu_strip, "Play mode", "RunMenuItem", 1);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Simulate mode", "RunMenuItem", 2);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Pause runtime", "RunMenuItem", 3);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Step runtime", "RunMenuItem", 4);
            m_gui_binder.add_action_menu_item(m_menu_strip, "Stop runtime", "RunMenuItem", 5);

            var playIcon = CreateTransportIcon("play");
            var simIcon = CreateTransportIcon("simulate");
            var pauseIcon = CreateTransportIcon("pause");
            var stepIcon = CreateTransportIcon("step");
            var stopIcon = CreateTransportIcon("stop");

            AddBoundToolbarButton(m_runtime_tool_strip, "Play (F5)", "Play mode", (int)images16x16.mode_editor, playIcon, "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Simulate", "Simulate mode", (int)images16x16.mode_editor, simIcon, "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Pause", "Pause runtime", (int)images16x16.mode_pause, pauseIcon, "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Step", "Step runtime", (int)images16x16.mode_pause, stepIcon, "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Stop", "Stop runtime", (int)images16x16.mode_editor, stopIcon, "Runtime_");
            m_runtime_tool_strip.Items.Add(new ToolStripSeparator());
            AddBoundToolbarButton(m_runtime_tool_strip, "Focus (F)", "Focus selection", (int)images16x16.focus_to_origin, namePrefix: "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Animation Editor", "Animation editor", (int)images16x16.node_mesh, namePrefix: "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Curves", "Animation Curve Editor", (int)images16x16.camera_track, namePrefix: "Runtime_");
            AddBoundToolbarButton(m_runtime_tool_strip, "Reload", "Reload resources", (int)images16x16.reload_modified_textures, namePrefix: "Runtime_");

            AddBoundToolbarButton(m_main_tool_strip, "Select", "Select mode", (int)images16x16.tool_select);
            AddBoundToolbarButton(m_main_tool_strip, "Move", "Translate mode", (int)images16x16.tool_translate);
            AddBoundToolbarButton(m_main_tool_strip, "Rotate", "Rotate mode", (int)images16x16.tool_rotate);
            AddBoundToolbarButton(m_main_tool_strip, "Scale", "Scale mode", (int)images16x16.tool_scale);
            m_main_tool_strip.Items.Add(new ToolStripSeparator());
            AddBoundToolbarButton(m_main_tool_strip, "World/Local", "Coordinate space", (int)images16x16.tool_manipulator);
            AddBoundToolbarButton(m_main_tool_strip, "Obj Snap", "Object snap", (int)images16x16.object_collision);
            AddBoundToolbarButton(m_main_tool_strip, "Vtx Snap", "Vertex snap", (int)images16x16.pin);
            AddBoundToolbarButton(m_main_tool_strip, "Edge Snap", "Edge snap", (int)images16x16.bsp_tree_edge_mode);
            AddBoundToolbarButton(m_main_tool_strip, "Face Snap", "Face snap", (int)images16x16.make_portals_coplanar);
            AddBoundToolbarButton(m_main_tool_strip, "Grid Snap", "Grid snap", (int)images16x16.toggle_draw_grid);
            AddBoundToolbarButton(m_main_tool_strip, "Terrain Snap", "Terrain snap", (int)images16x16.terrain_selector);
            m_main_tool_strip.Items.Add(new ToolStripSeparator());
            AddBoundToolbarButton(m_main_tool_strip, "Undo", "Undo", (int)images16x16.reload_shaders);
            AddBoundToolbarButton(m_main_tool_strip, "Redo", "Redo", (int)images16x16.reload_modified_textures);
            AddBoundToolbarButton(m_main_tool_strip, "Play", "Play mode", (int)images16x16.mode_editor, playIcon);
            AddBoundToolbarButton(m_main_tool_strip, "Simulate", "Simulate mode", (int)images16x16.mode_editor, simIcon);
            AddBoundToolbarButton(m_main_tool_strip, "Pause", "Pause runtime", (int)images16x16.mode_pause, pauseIcon);
            AddBoundToolbarButton(m_main_tool_strip, "Step", "Step runtime", (int)images16x16.mode_pause, stepIcon);
            AddBoundToolbarButton(m_main_tool_strip, "Stop", "Stop runtime", (int)images16x16.mode_editor, stopIcon);
            AddBoundToolbarButton(m_main_tool_strip, "Focus", "Focus selection", (int)images16x16.focus_to_origin);
            AddBoundToolbarButton(m_main_tool_strip, "Preview", "Preview selection", (int)images16x16.node_mesh);
            AddBoundToolbarButton(m_main_tool_strip, "Reload", "Reload resources", (int)images16x16.reload_modified_textures);

            m_gui_binder.update_items();
        }

        private Image CreateTransportIcon(string kind)
        {
            var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                switch (kind)
                {
                    case "play":
                    {
                        using var brush = new SolidBrush(Color.FromArgb(46, 178, 86));
                        g.FillPolygon(brush, new[] { new Point(4, 3), new Point(13, 8), new Point(4, 13) });
                        break;
                    }
                    case "simulate":
                    {
                        using var brush = new SolidBrush(Color.FromArgb(38, 138, 224));
                        g.FillPolygon(brush, new[] { new Point(3, 3), new Point(10, 8), new Point(3, 13) });
                        g.FillEllipse(brush, 11, 6, 3, 4);
                        break;
                    }
                    case "pause":
                    {
                        using var brush = new SolidBrush(Color.FromArgb(224, 152, 24));
                        g.FillRectangle(brush, 3, 3, 4, 10);
                        g.FillRectangle(brush, 9, 3, 4, 10);
                        break;
                    }
                    case "step":
                    {
                        using var brush = new SolidBrush(Color.FromArgb(36, 132, 214));
                        g.FillPolygon(brush, new[] { new Point(3, 3), new Point(10, 8), new Point(3, 13) });
                        g.FillRectangle(brush, 11, 3, 2, 10);
                        break;
                    }
                    default:
                    {
                        using var brush = new SolidBrush(Color.FromArgb(214, 58, 58));
                        g.FillRectangle(brush, 3, 3, 10, 10);
                        break;
                    }
                }
            }
            m_owned_toolbar_icons.Add(bmp);
            return bmp;
        }

        private void AddBoundToolbarButton(ToolStrip strip, string text, string actionName, int imageIndex, Image? customIcon = null, string? namePrefix = null)
        {
            var button = new ToolStripButton(text)
            {
                Name = (namePrefix ?? string.Empty) + actionName.Replace(' ', '_') + "_btn",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
            };
            strip.Items.Add(button);
            m_gui_binder.register_image("base", actionName, imageIndex);
            m_gui_binder.bind(button, actionName);
            var img = customIcon ?? m_gui_binder.get_image(actionName);
            if (img != null)
            {
                button.Image = img;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!m_shutdown && !ConfirmSaveBeforeTransition()) { e.Cancel = true; return; }
            if (!m_shutdown && HostCloseRequested != null)
            {
                e.Cancel = true;
                ResourcePipeline?.CancelAllQueries();
                m_recovery_timer?.Stop();
                HostCloseRequested();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_shutdown = true;
                DetachProjectScene();
                m_recovery_timer?.Dispose();
                ResourcePipeline?.CancelAllQueries();
                CloseOwnedDocuments();
                if (ReferenceEquals(resource_chooser.DialogOwner, this)) resource_chooser.DialogOwner = null;
                try { if (SdkVfsManager.IsConfigured) m_project.SaveRecentProjectsFile(Path.Combine(SdkVfsManager.UserDataRoot, "recent_projects.txt")); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("SDK recent projects: " + error.Message); }
                m_editing.Dispose();
                m_project.Dispose();
                WpfControls.utils.set_parent_form(m_object_inspector_tab.PropertyGridHost.property_grid, null);
                WpfControls.utils.set_parent_form(m_animation_curve_editor_panel.CurveEditorControl, null);
                m_dock_panel.ActiveAutoHideContent = null;
                foreach (var floatWindow in m_dock_panel.FloatWindows.ToArray())
                    floatWindow.Dispose();
                m_log_form.Dispose();
                m_options_dialog.Dispose();
                m_about_dialog.Dispose();
                m_project_browser_dialog.Dispose();
                m_connection_editor.Dispose();
                m_triangulation_tester.Dispose();
                m_camera_view_panel.Dispose();
                m_scene_graph_tab.Dispose();
                m_animation_curve_editor_panel.Dispose();
                m_tools_tab.Dispose();
                m_object_inspector_tab.Dispose();
                m_project_tab.Dispose();
                m_view_window.Dispose();
                m_dock_panel.Dispose();
                m_theme.Dispose();
                m_input_engine.Dispose();
                Commands.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (var icon in m_owned_toolbar_icons)
                    icon.Dispose();
                m_owned_toolbar_icons.Clear();
            }
        }
    }
}
