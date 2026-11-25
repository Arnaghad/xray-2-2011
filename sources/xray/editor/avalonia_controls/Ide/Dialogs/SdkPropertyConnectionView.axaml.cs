using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;

namespace xray.editor.avalonia_controls.Ide.Dialogs;

public sealed record SdkConnectableProperty(string Name, string DataType, bool ShowDetails = true);

public partial class SdkPropertyConnectionView : UserControl
{
    public const string PersistId = "xray.editor.property_connection_editor";

    private readonly ObservableCollection<SdkConnectableProperty> m_leftProps = new();
    private readonly ObservableCollection<SdkConnectableProperty> m_rightProps = new();

    private bool m_detailsMode = true;
    public SdkPropertyConnectionView()
    {
        InitializeComponent();

        m_left_props_list.ItemsSource = m_leftProps;
        m_right_props_list.ItemsSource = m_rightProps;
        m_left_props_list.SelectionChanged += (_, _) => UpdateLinkButtons();
        m_right_props_list.SelectionChanged += (_, _) => UpdateLinkButtons();

        m_details_mode_item.Click += (_, _) => SetViewMode(detailsMode: true);
        m_list_mode_item.Click += (_, _) => SetViewMode(detailsMode: false);
        m_left_grab_menu.Click += (_, _) => GrabEndpointRequested?.Invoke(true);
        m_right_grab_menu.Click += (_, _) => GrabEndpointRequested?.Invoke(false);
        m_left_clear_menu.Click += (_, _) => ClearEndpointRequested?.Invoke(true);
        m_right_clear_menu.Click += (_, _) => ClearEndpointRequested?.Invoke(false);

        m_grab_left_btn.Click += (_, _) => GrabEndpointRequested?.Invoke(true);
        m_grab_right_btn.Click += (_, _) => GrabEndpointRequested?.Invoke(false);
        m_swap_btn.Click += (_, _) => SwapRequested?.Invoke();

        m_connect_btn.Click += (_, _) =>
        {
            if (SelectedLeftProperty != null && SelectedRightProperty != null)
                ConnectRequested?.Invoke(SelectedLeftProperty.Name, SelectedRightProperty.Name);
        };
        m_disconnect_btn.Click += (_, _) =>
        {
            if (SelectedLeftProperty != null && SelectedRightProperty != null)
                DisconnectRequested?.Invoke(SelectedLeftProperty.Name, SelectedRightProperty.Name);
        };
        m_close_btn.Click += (_, _) => CloseRequested?.Invoke();
        UpdateLinkButtons();
    }

    public IReadOnlyList<SdkConnectableProperty> LeftProperties => m_leftProps;
    public IReadOnlyList<SdkConnectableProperty> RightProperties => m_rightProps;
    public SdkConnectableProperty? SelectedLeftProperty => m_left_props_list.SelectedItem as SdkConnectableProperty;
    public SdkConnectableProperty? SelectedRightProperty => m_right_props_list.SelectedItem as SdkConnectableProperty;
    public string LeftObjectLabel => m_left_label.Text ?? string.Empty;
    public string RightObjectLabel => m_right_label.Text ?? string.Empty;
    public bool IsDetailsMode => m_detailsMode;

    public event Action<bool>? GrabEndpointRequested;
    public event Action<bool>? ClearEndpointRequested;
    public event Action? SwapRequested;
    public event Action<string, string>? ConnectRequested;
    public event Action<string, string>? DisconnectRequested;
    public event Action? CloseRequested;

    public void SetViewMode(bool detailsMode)
    {
        string? left = SelectedLeftProperty?.Name;
        string? right = SelectedRightProperty?.Name;
        m_detailsMode = detailsMode;
        m_details_mode_item.IsChecked = detailsMode;
        m_list_mode_item.IsChecked = !detailsMode;
        RefreshDetailsFlag(m_leftProps, detailsMode);
        RefreshDetailsFlag(m_rightProps, detailsMode);
        SelectProperties(left, right);
    }

    public void SetEndpoints(
        string? leftName,
        IEnumerable<(string Property, string DataType)>? leftProperties,
        string? rightName,
        IEnumerable<(string Property, string DataType)>? rightProperties)
    {
        m_left_label.Text = string.IsNullOrEmpty(leftName) ? "None" : leftName;
        m_right_label.Text = string.IsNullOrEmpty(rightName) ? "None" : rightName;
        SetStatus(string.Empty);

        m_leftProps.Clear();
        if (leftProperties != null)
        {
            foreach (var (prop, dtype) in leftProperties)
                m_leftProps.Add(new SdkConnectableProperty(prop, dtype, m_detailsMode));
        }

        m_rightProps.Clear();
        if (rightProperties != null)
        {
            foreach (var (prop, dtype) in rightProperties)
                m_rightProps.Add(new SdkConnectableProperty(prop, dtype, m_detailsMode));
        }
    }

    public void SelectProperties(string? leftPropertyName, string? rightPropertyName)
    {
        if (!string.IsNullOrEmpty(leftPropertyName))
        {
            foreach (var item in m_leftProps)
            {
                if (string.Equals(item.Name, leftPropertyName, StringComparison.Ordinal))
                {
                    m_left_props_list.SelectedItem = item;
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(rightPropertyName))
        {
            foreach (var item in m_rightProps)
            {
                if (string.Equals(item.Name, rightPropertyName, StringComparison.Ordinal))
                {
                    m_right_props_list.SelectedItem = item;
                    break;
                }
            }
        }
    }

    public void SetStatus(string text)
    {
        m_status_label.Text = text;
        m_status_label.IsVisible = text.Length > 0;
    }

    private void UpdateLinkButtons()
    {
        bool hasPair = SelectedLeftProperty != null && SelectedRightProperty != null;
        m_connect_btn.IsEnabled = hasPair;
        m_disconnect_btn.IsEnabled = hasPair;
    }

    private static void RefreshDetailsFlag(ObservableCollection<SdkConnectableProperty> list, bool showDetails)
    {
        for (int i = 0; i < list.Count; i++)
            list[i] = list[i] with { ShowDetails = showDetails };
    }
}
