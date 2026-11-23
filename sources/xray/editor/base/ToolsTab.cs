using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using xray.editor.controls;

namespace xray.editor_base
{
    // C# port of world/sources/tools_tab.{h,cpp} and active tool tabs in level_editor.cpp
    public class ToolLibraryTabControl : UserControl
    {
        public string ToolName { get; }
        public readonly tree_view LibraryTree = new tree_view { Dock = DockStyle.Fill, filter_visible = true };
        public readonly StatusStrip StatusStrip = new StatusStrip();
        public readonly ToolStripStatusLabel StatusLabel = new ToolStripStatusLabel();

        public ToolLibraryTabControl(string toolName, IEnumerable<string>? seedItems = null, ImageList? sharedImages = null)
        {
            ToolName = toolName;
            LibraryTree.is_multiselect = false;
            Name = toolName + "_tab";
            SdkWindowStyle.StylePane(this);
            SdkWindowStyle.StyleTree(LibraryTree);
            SdkWindowStyle.StyleStrip(StatusStrip);
            StatusStrip.SizingGrip = false;
            if (sharedImages != null)
                LibraryTree.ImageList = sharedImages;
            StatusLabel.Text = $"{toolName}: ready";
            StatusStrip.Items.Add(StatusLabel);
            Controls.Add(LibraryTree);
            Controls.Add(StatusStrip);

            if (seedItems != null)
            {
                foreach (string path in seedItems)
                    LibraryTree.add_item(path, (int)images16x16.node_resource);
                LibraryTree.ExpandAll();
            }

            LibraryTree.selected_items_changed += (_, e) =>
            {
                StatusLabel.Text = e.new_selection.Count > 0
                    ? $"{toolName}: {e.new_selection[0].FullPath}"
                    : $"{toolName}: ready";
            };
        }
    }

    public class ToolsTab : DockContent
    {
        public const string PersistIdentity = "xray.editor.tools_tab";

        // Active tool tabs including resolved scripting route (SDK-11)
        public static readonly string[] ActiveLevelEditorTools =
        {
            "solid visual",
            "lights",
            "composite",
            "terrain",
            "particle",
            "speedtree",
            "misc",
            "sound",
            "scripting"
        };

        // Remaining dormant tool tab commented out in level_editor.cpp:432
        public static readonly string[] DormantLevelEditorTools =
        {
            "object painter"
        };

        private readonly TabControl _tabControl = new TabControl();
        private readonly Dictionary<string, ToolLibraryTabControl> _toolTabs = new Dictionary<string, ToolLibraryTabControl>();
        private int _prevActiveTab;

        public event Action<string>? ActiveToolChanged;

        public ToolsTab(ImageList? sharedImages = null, bool populateDefaultLevelTools = true)
        {
            InitializeComponent();
            if (populateDefaultLevelTools)
            {
                PopulateDefaultLevelTools(sharedImages);
            }
        }

        protected override string GetPersistString() => PersistIdentity;

        public TabControl TabControl => _tabControl;
        public IReadOnlyDictionary<string, ToolLibraryTabControl> ToolTabs => _toolTabs;
        public int ActiveToolCount => _toolTabs.Count;
        public string ActiveToolName => _tabControl.SelectedTab?.Text ?? string.Empty;

        private void InitializeComponent()
        {
            SuspendLayout();

            _tabControl.Dock = DockStyle.Fill;
            _tabControl.Location = new Point(0, 0);
            _tabControl.Multiline = true;
            _tabControl.Name = "tab_control";
            _tabControl.Padding = new Point(10, 5);
            if (!SystemInformation.HighContrast)
            {
                _tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
                _tabControl.DrawItem += DrawToolTab;
            }
            _tabControl.SelectedIndex = 0;
            _tabControl.Size = new Size(284, 467);
            _tabControl.TabIndex = 0;
            _tabControl.SelectedIndexChanged += tab_control_SelectedIndexChanged;

            Controls.Add(_tabControl);
            DockAreas = DockAreas.Float | DockAreas.DockLeft | DockAreas.DockRight | DockAreas.DockTop | DockAreas.DockBottom;
            HideOnClose = true;
            Name = "tools_tab";
            Text = "Tools";
            TabText = Text;
            ResumeLayout(false);
        }

        private void PopulateDefaultLevelTools(ImageList? sharedImages)
        {
            var seeds = new Dictionary<string, string[]>
            {
                ["solid visual"] = new[] { "props/woodbox_mil", "props/barrel_fuel" },
                ["lights"] = new[] { "lights/sun_light", "lights/point_light", "lights/spot_light" },
                ["composite"] = new[] { "composites/camp_fire_set" },
                ["terrain"] = new[] { "terrain/terrain_core" },
                ["particle"] = new[] { "particles/fire_torch" },
                ["speedtree"] = new[] { "trees/pine_01" },
                ["misc"] = new[] { "misc/camera_actor", "misc/decal_graffiti" },
                ["sound"] = new[] { "point_sound", "volumetric_sound" },
                ["scripting"] = new[] { "scene", "npc", "job" }
            };

            foreach (string toolName in ActiveLevelEditorTools)
            {
                var ctrl = new ToolLibraryTabControl(toolName, seeds.GetValueOrDefault(toolName), sharedImages);
                _toolTabs[toolName] = ctrl;
                add_tab(toolName, ctrl);
            }
        }

        public void add_tab(string name, UserControl tab)
        {
            TabPage page = new TabPage(name) { Name = name };
            SdkWindowStyle.StylePane(page);
            tab.Dock = DockStyle.Fill;
            page.Controls.Add(tab);
            _tabControl.TabPages.Add(page);
        }

        public void activate_tab(int idx)
        {
            if (idx >= 0 && idx < _tabControl.TabCount)
                _tabControl.SelectedIndex = idx;
        }

        private void tab_control_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _prevActiveTab = _tabControl.SelectedIndex;
            if (_tabControl.SelectedTab != null)
                ActiveToolChanged?.Invoke(_tabControl.SelectedTab.Text);
        }

        private void DrawToolTab(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _tabControl.TabCount)
                return;

            bool selected = e.Index == _tabControl.SelectedIndex;
            var page = _tabControl.TabPages[e.Index];
            var fore = selected ? SdkWindowStyle.SelectionText : SdkWindowStyle.MutedText;
            using var background = new SolidBrush(selected ? SdkWindowStyle.Selection : SdkWindowStyle.Canvas);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (selected)
            {
                using var accent = new SolidBrush(SdkWindowStyle.Accent);
                int height = LogicalToDeviceUnits(2);
                e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Bottom - height, e.Bounds.Width, height);
            }
            TextRenderer.DrawText(e.Graphics, page.Text, _tabControl.Font, e.Bounds, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
    }
}
