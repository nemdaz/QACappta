using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Configuration;
using Qapptia.Core.Ipc;
using Qapptia.Core.Platform;
using Qapptia.Core.Services;
using Qapptia.Editor.Core;
using Qapptia.Editor.Services;
using Qapptia.UI.Components.Theme;
using Serilog;

#if WINDOWS
using Qapptia.Platform.Windows;
#elif LINUX
using Qapptia.Platform.Linux;
#elif MAC
using Qapptia.Platform.MacOS;
#endif

namespace Qapptia.App.Editor;

public sealed class Program
{
    public static IServiceProvider? Services { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var logDir = Qapptia.Core.Constants.DefaultLogDirectory;
#if DEBUG
        var logLevel = Serilog.Events.LogEventLevel.Debug;
#else
        var logLevel = Serilog.Events.LogEventLevel.Information;
#endif
        using var log = Qapptia.Core.Logging.LoggingBootstrap.ConfigureGlobal(logDir, logLevel, "editor");

        using var guard = new MutexSingleInstanceGuard(IpcChannels.Editor);
        if (!guard.Acquire())
        {
            Log.Warning("Instancia de Editor ya existente, intentando despertar...");
            try
            {
                QapptiaIpcClient.SendAsync(IpcChannels.Editor, new WakeUpRequest(), timeoutMs: 1000).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se pudo despertar la otra instancia de Editor");
            }
            return;
        }

        var dispatcher = new IpcMessageDispatcher(
            (msg, ct) =>
            {
                switch (msg)
                {
                    case WakeUpRequest:
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                                desktop.MainWindow != null)
                            {
                                desktop.MainWindow.Show();
                                desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                                desktop.MainWindow.Activate();
                                desktop.MainWindow.Topmost = true;
                                desktop.MainWindow.Topmost = false;
                            }
                        });
                        return Task.FromResult<IpcMessage>(new Ack { OriginalType = msg.Type });

                    case ThemeChangedNotification themeMsg:
                        Log.Information("Editor recibió cambio de tema vía IPC: {Theme}", themeMsg.Theme);
                        Dispatcher.UIThread.Post(() =>
                        {
                            ThemeManager.ApplyTheme(themeMsg.Theme);
                        });
                        return Task.FromResult<IpcMessage>(new Ack { OriginalType = msg.Type });

                    default:
                        return Task.FromResult<IpcMessage>(new Ack { OriginalType = msg.Type });
                }
            },
            Log.Logger.ForContext<IpcMessageDispatcher>());

        using var ipcServer = new QapptiaIpcServer(
            IpcChannels.Editor,
            IpcChannels.GetPipeName(IpcChannels.Editor),
            dispatcher,
            Log.Logger.ForContext<QapptiaIpcServer>());

        _ = ipcServer.StartAsync();

        try
        {
            Services = ConfigureServices();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try { ipcServer.StopAsync().GetAwaiter().GetResult(); } catch { }
        }
    }

    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Logging & Configuración
        services.AddSingleton<Serilog.ILogger>(Log.Logger);
        var configPath = Qapptia.Core.Constants.DefaultConfigPath;
        services.AddSingleton<IConfigService>(_ => new JsonConfigService(configPath));

        // Módulos transversales de Plataforma (mismo estándar que App.Capture)
#if WINDOWS
        if (OperatingSystem.IsWindows()) services.AddWindowsPlatform();
#elif LINUX
        if (OperatingSystem.IsLinux()) services.AddLinuxPlatform();
#elif MAC
        if (OperatingSystem.IsMacOS()) services.AddMacOSPlatform();
#endif

        // Fallback neutro si no hubiese shell en la plataforma actual
        services.TryAddSingleton<IShellService>(NullShellService.Instance);

        // Servicios de dominio del Editor
        services.AddSingleton<IFontProvider>(sp =>
            new AssetFontProvider(sp.GetRequiredService<Serilog.ILogger>().ForContext<AssetFontProvider>()));

        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(sp.GetRequiredService<Serilog.ILogger>().ForContext<NavigationService>()));

        services.AddSingleton<ICanvasStateService>(sp =>
            new CanvasStateService(sp.GetRequiredService<Serilog.ILogger>().ForContext<CanvasStateService>()));

        services.AddSingleton<IEditorStateService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var savePath = string.IsNullOrWhiteSpace(config.Current.SavePath)
                ? Qapptia.Core.Constants.DefaultSavePath
                : config.Current.SavePath;
            var logger = sp.GetRequiredService<Serilog.ILogger>().ForContext<EditorStateService>();
            return new EditorStateService(savePath, Qapptia.Core.Constants.EditorStateFileName, logger);
        });

        services.AddSingleton<ICaptureAppService>(sp =>
            new Qapptia.App.Editor.Services.CaptureAppService(sp.GetRequiredService<Serilog.ILogger>()));

        // ViewModel y Vistas
        services.AddTransient<EditorViewModel>(sp =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            var savePath = string.IsNullOrWhiteSpace(config.Current.SavePath)
                ? Qapptia.Core.Constants.DefaultSavePath
                : config.Current.SavePath;

            return new EditorViewModel(
                sp.GetRequiredService<IEditorStateService>(),
                savePath,
                sp.GetRequiredService<IFontProvider>(),
                sp.GetService<IClipboardService>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<ICanvasStateService>(),
                sp.GetRequiredService<IShellService>(),
                sp.GetRequiredService<ICaptureAppService>());
        });

        return services.BuildServiceProvider();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(b =>
            {
                if (b.Instance is App app)
                {
                    app.Services = Services ?? ConfigureServices();
                }
            });
}
