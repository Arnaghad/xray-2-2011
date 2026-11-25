using System;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls;

public class InputCommandEventArgs : RoutedEventArgs
{
    public InputCommandEventArgs(string command_string, RoutedEvent routed_event)
        : base(routed_event)
    {
        this.command_string = command_string;
    }

    public string command_string { get; }
}

public delegate void InputCommandEventHandler(object? sender, InputCommandEventArgs e);

public static class input
{
    public static readonly RoutedEvent<InputCommandEventArgs> PreviewInputCommandEvent =
        RoutedEvent.Register<InputCommandEventArgs>("PreviewInputCommand", RoutingStrategies.Tunnel, typeof(input));

    public static readonly RoutedEvent<InputCommandEventArgs> InputCommandEvent =
        RoutedEvent.Register<InputCommandEventArgs>("InputCommand", RoutingStrategies.Bubble, typeof(input));

    public static void AddPreviewInputCommandHandler(Interactive? element, EventHandler<InputCommandEventArgs> handler) =>
        element?.AddHandler(PreviewInputCommandEvent, handler);

    public static void RemovePreviewInputCommandHandler(Interactive? element, EventHandler<InputCommandEventArgs> handler) =>
        element?.RemoveHandler(PreviewInputCommandEvent, handler);

    public static void AddInputCommandHandler(Interactive? element, EventHandler<InputCommandEventArgs> handler) =>
        element?.AddHandler(InputCommandEvent, handler);

    public static void RemoveInputCommandHandler(Interactive? element, EventHandler<InputCommandEventArgs> handler) =>
        element?.RemoveHandler(InputCommandEvent, handler);

    public static bool RaiseInputCommandEvent(string command, Interactive? element)
    {
        if (element == null)
            return false;

        var args = new InputCommandEventArgs(command, PreviewInputCommandEvent);
        element.RaiseEvent(args);
        if (!args.Handled)
        {
            args = new InputCommandEventArgs(command, InputCommandEvent);
            element.RaiseEvent(args);
        }

        return args.Handled;
    }
}
