using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using xray.editor.avalonia_controls.Shell;
using xray.editor.avalonia_controls.Themes;
using xray.editor_base;

namespace xray.editor.avalonia_controls.Ide.Controls;

/// <summary>
/// Configurable toolbar tool panel replacing WinForms toolbar_panel_base.
/// </summary>
public class SdkToolbarPanel : UserControl
{
    public const string PersistId = "xray.editor.controls.toolbar_panel_base";

    private readonly WrapPanel m_itemsPanel = new()
    {
        Orientation = Orientation.Horizontal
    };

    public SdkToolbarPanel(SdkDocumentEditorView? editor = null)
    {
        Editor = editor;
        Background = SdkWorkspaceTheme.Chrome;
        Padding = new Thickness(4);
        Content = m_itemsPanel;
    }

    public SdkDocumentEditorView? Editor { get; internal set; }
    public WrapPanel ItemsPanel => m_itemsPanel;

    public Button AddButton(string text, Action onClick, images16x16? icon = null)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };
        if (icon.HasValue)
        {
            content.Children.Add(new Image
            {
                Source = AvaloniaImageAtlas.Shared.GetIcon(icon.Value),
                Width = 16,
                Height = 16
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            Margin = new Thickness(2),
            Padding = new Thickness(6, 3)
        };
        button.Click += (_, _) => onClick();
        m_itemsPanel.Children.Add(button);
        return button;
    }
}
