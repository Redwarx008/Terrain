using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TerrainPreProcessor.Services;
using TerrainPreProcessor.Views;

namespace TerrainPreProcessor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var windowService = new WindowService();
            desktop.MainWindow = windowService.CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
