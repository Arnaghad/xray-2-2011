using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xray.editor.avalonia_controls.Controls;

public partial class SdkPropertyValuePreviewControl : UserControl
{
    public static readonly StyledProperty<string> PropertyNameProperty =
        AvaloniaProperty.Register<SdkPropertyValuePreviewControl, string>(nameof(PropertyName), "Property");

    public static readonly StyledProperty<string> TextValueProperty =
        AvaloniaProperty.Register<SdkPropertyValuePreviewControl, string>(nameof(TextValue), string.Empty);

    public static readonly StyledProperty<string> DefaultValueProperty =
        AvaloniaProperty.Register<SdkPropertyValuePreviewControl, string>(nameof(DefaultValue), string.Empty);

    public static readonly StyledProperty<string> ValidationErrorProperty =
        AvaloniaProperty.Register<SdkPropertyValuePreviewControl, string>(nameof(ValidationError), string.Empty);

    private bool m_syncing;

    public Func<string, string?>? Validator { get; set; }

    public event EventHandler<string>? ValueCommitted;

    public SdkPropertyValuePreviewControl()
    {
        InitializeComponent();
        ValueTextBox.TextChanged += OnTextBoxChanged;
        ValueTextBox.KeyDown += OnTextBoxKeyDown;
        ValueTextBox.LostFocus += OnTextBoxLostFocus;
        ResetButton.Click += OnResetClicked;
        SyncVisuals();
    }

    public string PropertyName
    {
        get => GetValue(PropertyNameProperty);
        set => SetValue(PropertyNameProperty, value);
    }

    public string TextValue
    {
        get => GetValue(TextValueProperty);
        set => SetValue(TextValueProperty, value);
    }

    public string DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public string ValidationError
    {
        get => GetValue(ValidationErrorProperty);
        set => SetValue(ValidationErrorProperty, value);
    }

    public bool IsModified => !string.Equals(TextValue, DefaultValue, StringComparison.Ordinal);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PropertyNameProperty ||
            change.Property == TextValueProperty ||
            change.Property == DefaultValueProperty ||
            change.Property == ValidationErrorProperty)
        {
            SyncVisuals();
        }
    }

    public bool CommitEdit()
    {
        string candidate = ValueTextBox.Text ?? string.Empty;
        string? error = Validator != null
            ? Validator(candidate)
            : (string.IsNullOrWhiteSpace(candidate) ? "Value cannot be empty." : null);

        if (!string.IsNullOrEmpty(error))
        {
            ValidationError = error;
            return false;
        }

        ValidationError = string.Empty;
        TextValue = candidate;
        ValueCommitted?.Invoke(this, candidate);
        return true;
    }

    public void ResetToDefault()
    {
        ValidationError = string.Empty;
        TextValue = DefaultValue;
        ValueCommitted?.Invoke(this, DefaultValue);
    }

    private void OnTextBoxChanged(object? sender, TextChangedEventArgs e)
    {
        if (m_syncing)
            return;
        m_syncing = true;
        try
        {
            TextValue = ValueTextBox.Text ?? string.Empty;
        }
        finally
        {
            m_syncing = false;
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (CommitEdit())
                e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ValidationError = string.Empty;
            SyncVisuals();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitEdit();
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        ResetToDefault();
    }

    private void SyncVisuals()
    {
        NameTextBlock.Text = PropertyName;
        if (!m_syncing && !string.Equals(ValueTextBox.Text ?? string.Empty, TextValue, StringComparison.Ordinal))
        {
            m_syncing = true;
            try
            {
                ValueTextBox.Text = TextValue;
            }
            finally
            {
                m_syncing = false;
            }
        }

        string error = ValidationError;
        bool hasError = !string.IsNullOrEmpty(error);
        ErrorTextBlock.Text = error;
        ErrorTextBlock.IsVisible = hasError;
        ResetButton.IsEnabled = IsModified;
    }
}
