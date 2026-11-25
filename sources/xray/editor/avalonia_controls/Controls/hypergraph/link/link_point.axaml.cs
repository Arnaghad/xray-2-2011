using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Threading;

namespace xray.editor.avalonia_controls.hypergraph;

public partial class link_point : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public link_point()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!Design.IsDesignMode)
                update_field_position();
        };
    }

    private string? m_id;
    private Point m_absolute_point_position;
    private bool m_is_connected;
    private readonly List<link> m_links = new();

    internal Ellipse dot_ellipse => m_dot;

    public string id
    {
        get => m_id ?? string.Empty;
        set
        {
            if (m_id == value)
                return;

            if (!string.IsNullOrEmpty(m_id) && node != null)
                node.remove_link_point(m_id);

            m_id = value;

            if (!string.IsNullOrEmpty(m_id) && node != null)
                node.add_link_point(m_id, this);
        }
    }

    public node? node { get; set; }
    public List<link> links => m_links;
    public object? tag { get; set; }

    public bool is_connected
    {
        get => m_is_connected;
        set => m_is_connected = value;
    }

    internal Point connection_point => m_absolute_point_position;

    private void link_place_mouse_down(object? sender, PointerPressedEventArgs e)
    {
        if (node?.hypergraph != null)
        {
            node.hypergraph.start_link_creating(this, e);
            e.Handled = true;
        }
    }

    protected void on_property_changed(string property_name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == node.parent_nodeProperty)
        {
            if (node != null && !string.IsNullOrEmpty(m_id))
                node.remove_link_point(m_id);

            node = change.NewValue as node;
            if (!string.IsNullOrEmpty(m_id) && node != null)
                node.add_link_point(m_id, this);
        }
    }

    internal void refresh_field_position()
    {
        if (node?.hypergraph?.field is not { } fld)
            return;

        double w = Bounds.Width > 0 ? Bounds.Width : MinWidth;
        double h = Bounds.Height > 0 ? Bounds.Height : MinHeight;
        var pt = this.TranslatePoint(new Point(w / 2.0, h / 2.0), fld);
        if (pt.HasValue)
            m_absolute_point_position = pt.Value;
        on_property_changed("field_position");
    }

    internal void update_field_position()
    {
        if (!m_is_connected)
            return;

        Dispatcher.UIThread.Post(refresh_field_position, DispatcherPriority.Loaded);
        refresh_field_position();
    }

    public void add_link(link link)
    {
        m_links.Add(link);
        m_is_connected = true;
    }

    public void remove_link(link link)
    {
        m_links.Remove(link);
        m_is_connected = m_links.Count != 0;
    }
}
