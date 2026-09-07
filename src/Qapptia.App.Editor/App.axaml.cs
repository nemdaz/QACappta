using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Core.Configuration;
using Qapptia.UI.Components.Theme;

namespace Qapptia.App.Editor;

public partial class App : Application
{
    public IServiceProvider Services { get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var configService = Services?.GetService<IConfigService>()
            ?? new JsonConfigService(Qapptia.Core.Constants.DefaultConfigPath);
        RequestedThemeVariant = ThemeManager.GetThemeVariant(configService.Current.Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<EditorViewModel>();

            var mainWindow = new MainWindow();
            mainWindow.InitializeWithViewModel(vm);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
