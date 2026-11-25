using System;
using System.Windows.Input;

namespace xray.editor.avalonia_controls.Shell;

public sealed class SdkDelegateCommand : ICommand
{
    private readonly Action m_execute;
    private readonly Func<bool>? m_canExecute;

    public SdkDelegateCommand(Action execute, Func<bool>? canExecute = null)
    {
        m_execute = execute ?? throw new ArgumentNullException(nameof(execute));
        m_canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => m_canExecute == null || m_canExecute();

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            m_execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
