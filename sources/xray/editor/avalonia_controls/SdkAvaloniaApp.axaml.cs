using Avalonia;
using Avalonia.Markup.Xaml;

namespace xray.editor.avalonia_controls;

public sealed class SdkAvaloniaApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
