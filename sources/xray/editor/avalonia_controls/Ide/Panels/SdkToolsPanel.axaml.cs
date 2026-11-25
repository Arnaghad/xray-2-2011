using System;
using System.Collections.Generic;
using Avalonia.Controls;
using xray.editor.avalonia_controls.Shell.Docking;

namespace xray.editor.avalonia_controls.Ide.Panels;

/// <summary>
/// Avalonia level-editor tools dock panel replacing WinForms ToolsTab.
/// </summary>
public partial class SdkToolsPanel : UserControl
{
    public const string PersistId = SdkDockPanelIds.Tools;

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

    public static readonly string[] DormantLevelEditorTools =
    {
        "object painter"
    };

    private readonly Dictionary<string, SdkToolLibraryTabView> m_toolTabs = new(StringComparer.Ordinal);

    public SdkToolsPanel()
        : this(populateDefaultLevelTools: true)
    {
    }

    public SdkToolsPanel(bool populateDefaultLevelTools)
    {
        InitializeComponent();

        m_tab_control.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, m_tab_control) && m_tab_control.SelectedItem is TabItem { Header: string header })
                ActiveToolChanged?.Invoke(header);
        };

        if (populateDefaultLevelTools)
            PopulateDefaultLevelTools();
    }

    public event Action<string>? ActiveToolChanged;
    public event Action? LibrarySelectionChanged;
    public event Action<SdkToolLibraryEntry>? LibraryEntryActivated;

    public TabControl TabControl => m_tab_control;
    public IReadOnlyDictionary<string, SdkToolLibraryTabView> ToolTabs => m_toolTabs;
    public int ActiveToolCount => m_toolTabs.Count;
    public string ActiveToolName => (m_tab_control.SelectedItem as TabItem)?.Header?.ToString() ?? string.Empty;
    public SdkToolLibraryEntry? SelectedEntry => ActiveLibrary?.SelectedEntry;

    private SdkToolLibraryTabView? ActiveLibrary => (m_tab_control.SelectedItem as TabItem)?.Content as SdkToolLibraryTabView;

    public void AddTab(string name, Control content)
    {
        m_tab_control.Items.Add(new TabItem
        {
            Header = name,
            Content = content
        });
    }

    public void ActivateTab(int idx)
    {
        if (idx >= 0 && idx < m_tab_control.Items.Count)
            m_tab_control.SelectedIndex = idx;
    }

    private void PopulateDefaultLevelTools()
    {
        var seeds = new Dictionary<string, string[]>(StringComparer.Ordinal)
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
            var view = new SdkToolLibraryTabView(toolName, seeds.GetValueOrDefault(toolName));
            view.LibraryTree.SelectedItemsChanged += (_, _) =>
            {
                if (ReferenceEquals(view, ActiveLibrary))
                    LibrarySelectionChanged?.Invoke();
            };
            view.EntryActivated += entry =>
            {
                if (ReferenceEquals(view, ActiveLibrary))
                    LibraryEntryActivated?.Invoke(entry);
            };
            m_toolTabs[toolName] = view;
            AddTab(toolName, view);
        }

        if (m_tab_control.Items.Count > 0)
            m_tab_control.SelectedIndex = 0;
    }
}
