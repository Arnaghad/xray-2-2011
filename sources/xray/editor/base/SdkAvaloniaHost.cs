using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using xray.editor.avalonia_controls;
using xray.editor.avalonia_controls.Ide.Controls;
using xray.editor.avalonia_controls.Themes;

namespace xray.editor_base;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
    public void Post(System.Action action) => Dispatcher.UIThread.Post(action);
    public void Invoke(System.Action action) => Dispatcher.UIThread.Invoke(action);
}

internal static class SdkAvaloniaBootstrap
{
    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;

    private static bool s_initialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out NativeMsg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMsg lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref NativeMsg lpMsg);

    internal static void EnsureInitialized()
    {
        if (s_initialized && Avalonia.Application.Current != null)
            return;

        if (Avalonia.Application.Current == null)
        {
            AppBuilder.Configure<SdkAvaloniaApp>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
        }

        SdkUiHost.RegisterServices(dispatcher: new AvaloniaUiDispatcher());
        SdkUiHost.RegisterEventPump(PumpAvaloniaOnly);
        s_initialized = true;
    }

    internal static void SetPureAvaloniaPump(bool enabled)
    {
        EnsureInitialized();
        SdkUiHost.RegisterEventPump(PumpAvaloniaOnly);
    }

    internal static Window CreateSavePromptWindow(
        string prompt,
        string title,
        bool allowCancel,
        System.Action<SdkDocumentSaveChoice> onChoice)
    {
        EnsureInitialized();
        Window? window = null;

        Avalonia.Controls.Button MakeButton(string text, SdkDocumentSaveChoice choice, bool isDefault = false, bool isCancel = false)
        {
            var btn = new Avalonia.Controls.Button
            {
                Content = text,
                MinWidth = 76,
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            btn.Click += (_, _) =>
            {
                onChoice(choice);
                window?.Close();
            };
            return btn;
        }

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(MakeButton("Yes", SdkDocumentSaveChoice.Yes, isDefault: true));
        buttons.Children.Add(MakeButton("No", SdkDocumentSaveChoice.No, isCancel: !allowCancel));
        if (allowCancel)
            buttons.Children.Add(MakeButton("Cancel", SdkDocumentSaveChoice.Cancel, isCancel: true));

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 16
        };
        layout.Children.Add(new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SdkWorkspaceTheme.Text
        });
        layout.Children.Add(buttons);

        window = new Window
        {
            Title = string.IsNullOrWhiteSpace(title) ? "X-Ray SDK" : title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = SdkWorkspaceTheme.Surface,
            Content = layout
        };
        return window;
    }

    internal static SdkDocumentSaveChoice ShowSavePrompt(
        string prompt,
        string title,
        bool allowCancel = true,
        object? owner = null)
    {
        SdkDocumentSaveChoice result = allowCancel ? SdkDocumentSaveChoice.Cancel : SdkDocumentSaveChoice.No;
        var dialog = CreateSavePromptWindow(prompt, title, allowCancel, choice => result = choice);
        ShowModalWindow(dialog, owner, hideOnClose: false);
        return result;
    }

    internal static SdkDialogResult ShowSavePromptAsSdkDialogResult(
        string prompt,
        string title,
        bool allowCancel = true,
        object? owner = null)
    {
        return ShowSavePrompt(prompt, title, allowCancel, owner) switch
        {
            SdkDocumentSaveChoice.Yes => SdkDialogResult.Yes,
            SdkDocumentSaveChoice.No => SdkDialogResult.No,
            _ => SdkDialogResult.Cancel
        };
    }

    internal static SdkDocumentSaveChoice PromptSaveChoice(
        Window view,
        string prompt,
        Func<SdkDialogResult>? overridePrompt)
    {
        if (overridePrompt != null)
        {
            return overridePrompt() switch
            {
                SdkDialogResult.Yes => SdkDocumentSaveChoice.Yes,
                SdkDialogResult.No => SdkDocumentSaveChoice.No,
                _ => SdkDocumentSaveChoice.Cancel
            };
        }

        return view.IsVisible
            ? ShowSavePrompt(prompt, view.Title ?? "X-Ray SDK", allowCancel: true)
            : SdkDocumentSaveChoice.No;
    }

    internal static T RunAsyncOnUiThread<T>(Func<System.Threading.Tasks.Task<T>> operation)
    {
        EnsureInitialized();
        var task = operation();
        if (task.IsCompleted)
            return task.GetAwaiter().GetResult();

        var frame = new DispatcherFrame();
        _ = task.ContinueWith(
            _ => Dispatcher.UIThread.Post(() => frame.Continue = false),
            System.Threading.Tasks.TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    internal static IReadOnlyList<FilePickerFileType> ParseFileFilter(string? filter)
    {
        var result = new List<FilePickerFileType>();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            string[] parts = filter.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string label = parts[i].Trim();
                string[] patterns = parts[i + 1]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToArray();
                if (!string.IsNullOrEmpty(label) && patterns.Length > 0)
                    result.Add(new FilePickerFileType(label) { Patterns = patterns });
            }
        }
        if (result.Count == 0)
            result.Add(FilePickerFileTypes.All);
        return result;
    }

    internal static string? PickOpenFile(
        string title,
        string? filter,
        string? initialDirectory = null,
        Avalonia.Controls.Control? anchor = null)
    {
        return RunWithStorageProvider(anchor, async storage =>
        {
            IStorageFolder? startFolder = null;
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                startFolder = await storage.TryGetFolderFromPathAsync(Path.GetFullPath(initialDirectory));

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = ParseFileFilter(filter),
                SuggestedStartLocation = startFolder
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    internal static string? PickSaveFile(
        string title,
        string? filter,
        string? initialDirectory = null,
        string? defaultFileName = null,
        string? defaultExtension = null,
        Avalonia.Controls.Control? anchor = null)
    {
        return RunWithStorageProvider(anchor, async storage =>
        {
            IStorageFolder? startFolder = null;
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                startFolder = await storage.TryGetFolderFromPathAsync(Path.GetFullPath(initialDirectory));

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                FileTypeChoices = ParseFileFilter(filter),
                SuggestedStartLocation = startFolder,
                SuggestedFileName = defaultFileName,
                DefaultExtension = defaultExtension
            });
            return file?.TryGetLocalPath();
        });
    }

    internal static IClipboard? ResolveClipboard(Avalonia.Controls.Control? anchor = null)
    {
        EnsureInitialized();
        return anchor != null ? TopLevel.GetTopLevel(anchor)?.Clipboard : null;
    }

    private static string? RunWithStorageProvider(
        Avalonia.Controls.Control? anchor,
        Func<IStorageProvider, System.Threading.Tasks.Task<string?>> action)
    {
        EnsureInitialized();
        Window? tempWindow = null;
        IStorageProvider? storage = anchor != null ? TopLevel.GetTopLevel(anchor)?.StorageProvider : null;
        if (storage == null)
        {
            tempWindow = new Window
            {
                Width = 1,
                Height = 1,
                Opacity = 0,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowDecorations = WindowDecorations.None
            };
            tempWindow.Show();
            storage = tempWindow.StorageProvider;
        }
        try
        {
            return RunAsyncOnUiThread(() => action(storage));
        }
        finally
        {
            tempWindow?.Close();
        }
    }

    internal static void ShowModalWindow(
        Window dialog,
        object? owner = null,
        bool hideOnClose = true)
    {
        EnsureInitialized();

        Window? ownerWindow = owner as Window ?? (owner as WindowIde)?.Window;
        bool reenableOwner = ownerWindow != null && ownerWindow.IsEnabled;
        if (reenableOwner)
            ownerWindow!.IsEnabled = false;

        var frame = new DispatcherFrame();
        EventHandler<WindowClosingEventArgs> closingHandler = (_, e) =>
        {
            if (hideOnClose)
            {
                e.Cancel = true;
                dialog.Hide();
            }
            frame.Continue = false;
        };
        EventHandler closedHandler = (_, _) => frame.Continue = false;

        dialog.Closing += closingHandler;
        dialog.Closed += closedHandler;
        try
        {
            dialog.Show();
            dialog.Activate();
            Dispatcher.UIThread.PushFrame(frame);
        }
        finally
        {
            dialog.Closing -= closingHandler;
            dialog.Closed -= closedHandler;
            if (reenableOwner && ownerWindow != null)
            {
                ownerWindow.IsEnabled = true;
                ownerWindow.Activate();
            }
        }
    }

    private static void PumpAvaloniaOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            for (int i = 0; i < 64 && PeekMessageW(out var msg, 0, 0, 0, PmRemove); i++)
            {
                if (msg.message == WmQuit)
                    break;
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        Dispatcher.UIThread.RunJobs();
    }
}
