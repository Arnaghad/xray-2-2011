using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls;

public class expandable_items_control : HeaderedItemsControl
{
    private readonly StackPanel m_left_pocket;
    private readonly StackPanel m_right_pocket;

    public expandable_items_control()
    {
        m_left_pocket = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        m_right_pocket = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        left_pocket = m_left_pocket;
        right_pocket = m_right_pocket;
    }

    public static readonly StyledProperty<bool> is_expandedProperty =
        AvaloniaProperty.Register<expandable_items_control, bool>(nameof(is_expanded));

    public static readonly StyledProperty<bool> is_expand_visibleProperty =
        AvaloniaProperty.Register<expandable_items_control, bool>(nameof(is_expand_visible), true);

    public static readonly StyledProperty<Panel?> right_pocketProperty =
        AvaloniaProperty.Register<expandable_items_control, Panel?>(nameof(right_pocket));

    public static readonly StyledProperty<Panel?> left_pocketProperty =
        AvaloniaProperty.Register<expandable_items_control, Panel?>(nameof(left_pocket));

    public static readonly StyledProperty<string?> nameProperty =
        AvaloniaProperty.Register<expandable_items_control, string?>(nameof(name));

    public static readonly RoutedEvent<RoutedEventArgs> preview_expanded_changedEvent =
        RoutedEvent.Register<expandable_items_control, RoutedEventArgs>("preview_expanded_changed", RoutingStrategies.Tunnel);

    public static readonly RoutedEvent<RoutedEventArgs> expanded_changedEvent =
        RoutedEvent.Register<expandable_items_control, RoutedEventArgs>("expanded_changed", RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? preview_expanded_changed
    {
        add => AddHandler(preview_expanded_changedEvent, value);
        remove => RemoveHandler(preview_expanded_changedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? expanded_changed
    {
        add => AddHandler(expanded_changedEvent, value);
        remove => RemoveHandler(expanded_changedEvent, value);
    }

    public bool is_expanded
    {
        get => GetValue(is_expandedProperty);
        set => SetValue(is_expandedProperty, value);
    }

    public bool is_expand_visible
    {
        get => GetValue(is_expand_visibleProperty);
        set => SetValue(is_expand_visibleProperty, value);
    }

    public double inner_item_indnt { get; set; }

    public string? name
    {
        get => GetValue(nameProperty);
        set => SetValue(nameProperty, value);
    }

    public Panel? left_pocket
    {
        get => GetValue(left_pocketProperty);
        set => SetValue(left_pocketProperty, value);
    }

    public Panel? right_pocket
    {
        get => GetValue(right_pocketProperty);
        set => SetValue(right_pocketProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == is_expandedProperty)
            on_expanded_changed();
    }

    protected virtual void on_expanded_changed()
    {
        var args = new RoutedEventArgs(preview_expanded_changedEvent, this);
        RaiseEvent(args);
        if (!args.Handled)
            RaiseEvent(new RoutedEventArgs(expanded_changedEvent, this));
    }

    public void add_right_pocket_inner(Control element) => m_right_pocket.Children.Insert(0, element);
    public void add_right_pocket_outer(Control element) => m_right_pocket.Children.Add(element);
    public void add_left_pocket_inner(Control element) => m_left_pocket.Children.Add(element);
    public void add_left_pocket_outer(Control element) => m_left_pocket.Children.Insert(0, element);
}
