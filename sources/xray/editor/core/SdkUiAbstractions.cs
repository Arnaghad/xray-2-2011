using System;

namespace xray.editor_base;

public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(System.Action action);
    void Invoke(System.Action action);
}

public interface IFileDialogService
{
    string? OpenFile(string title, string? initialDirectory, string? filter);
    string? SaveFile(string title, string? initialDirectory, string? defaultFileName, string? filter);
    string? PickFolder(string title, string? initialDirectory);
}

public interface IClipboardService
{
    string? GetText();
    void SetText(string text);
}

public readonly record struct SdkColor(byte A, byte R, byte G, byte B)
{
    public uint ToArgb() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
}

public enum EditorTransformMode
{
    None,
    Translate,
    Rotate,
    Scale
}

public enum SdkDialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public enum subst_resource_result
{
    subst_one,
    subst_all_by_name,
    subst_all_any_name,
    ignore_one,
    ignore_all_by_name,
    ignore_all_any_name
}

public struct subst_record
{
    public string class_id;
    public string source_name;
    public string dest_name;
    public subst_resource_result result;
}

[Flags]
public enum SdkKeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}

public readonly record struct SdkKeyGesture(string Key, SdkKeyModifiers Modifiers);

public static class SdkUiHost
{
    private static readonly System.Action s_noopPump = static () => { };
    private static volatile System.Action s_eventPump = s_noopPump;

    public static IUiDispatcher? Dispatcher { get; private set; }
    public static IFileDialogService? FileDialogs { get; private set; }
    public static IClipboardService? Clipboard { get; private set; }

    public static void RegisterEventPump(System.Action? pump) => s_eventPump = pump ?? s_noopPump;

    public static void PumpEvents() => s_eventPump();

    public static void RegisterServices(
        IUiDispatcher? dispatcher = null,
        IFileDialogService? fileDialogs = null,
        IClipboardService? clipboard = null)
    {
        Dispatcher = dispatcher;
        FileDialogs = fileDialogs;
        Clipboard = clipboard;
    }
}
