using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Qapptia.Core;
using Qapptia.Core.Configuration;
using Qapptia.UI.Components.Theme;

namespace Qapptia.App.Editor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var configPath = Constants.DefaultConfigPath;
        var configService = new JsonConfigService(configPath);
        RequestedThemeVariant = ThemeManager.GetThemeVariant(configService.Current.Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var configService = new Qapptia.Core.Configuration.JsonConfigService(Qapptia.Core.Constants.DefaultConfigPath);
            var savePath = string.IsNullOrWhiteSpace(configService.Current.SavePath) ? Qapptia.Core.Constants.DefaultSavePath : configService.Current.SavePath;
            var stateServiceLogger = Serilog.Log.Logger.ForContext<Qapptia.Editor.Services.EditorStateService>();
            var stateService = new Qapptia.Editor.Services.EditorStateService(
                savePath,
                Qapptia.Core.Constants.EditorStateFileName,
                stateServiceLogger);

#if WINDOWS
            var clipboardService = new Qapptia.Platform.Windows.WindowsClipboardService(Serilog.Log.Logger);
#else
            Qapptia.Core.Abstractions.IClipboardService? clipboardService = null;
#endif

            var fontProviderLogger = Serilog.Log.Logger.ForContext<Qapptia.Editor.Core.AssetFontProvider>();
            var fontProvider = new Qapptia.Editor.Core.AssetFontProvider(fontProviderLogger);

            var navigationServiceLogger = Serilog.Log.Logger.ForContext<Qapptia.Editor.Services.NavigationService>();
            var navigationService = new Qapptia.Editor.Services.NavigationService(navigationServiceLogger);

            var canvasStateServiceLogger = Serilog.Log.Logger.ForContext<Qapptia.Editor.Services.CanvasStateService>();
            var canvasStateService = new Qapptia.Editor.Services.CanvasStateService(canvasStateServiceLogger);

#if WINDOWS
            var shellService = new Qapptia.Platform.Windows.WindowsShellService(Serilog.Log.Logger);
#elif MAC
            var shellService = new Qapptia.Platform.MacOS.MacShellService(Serilog.Log.Logger);
#elif LINUX
            var shellService = new Qapptia.Platform.Linux.LinuxShellService(Serilog.Log.Logger);
#else
            var shellService = Qapptia.Core.Services.NullShellService.Instance;
#endif

            var vm = new Qapptia.App.Editor.ViewModels.EditorViewModel(stateService, savePath, fontProvider, clipboardService, navigationService, canvasStateService, shellService);

            var mainWindow = new MainWindow();
            mainWindow.InitializeWithViewModel(vm);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
