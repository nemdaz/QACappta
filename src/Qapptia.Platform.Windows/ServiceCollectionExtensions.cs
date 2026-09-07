using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qapptia.Core.Abstractions;

namespace Qapptia.Platform.Windows;

/// <summary>
/// Registra los servicios OS-specific de Windows en el contenedor de DI.
/// Lanzar PlatformNotSupportedException en runtime si no es Windows.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AddWindowsPlatform requiere Windows.");

        services.TryAddSingleton<IScreenCapture, WindowsScreenCapture>();
        services.TryAddSingleton<ICursorCapture, WindowsCursorCapture>();
        services.TryAddSingleton<IHotkeyRegistrar, WindowsHotkeyRegistrar>();
        services.TryAddSingleton<IPowerEvents, WindowsPowerEvents>();
        services.TryAddSingleton<IDesktopService, WindowsDesktopService>();
        services.TryAddSingleton<IShutterSoundService, WindowsShutterSoundService>();
        services.TryAddSingleton<IClipboardService, WindowsClipboardService>();
        services.TryAddSingleton<ITrayIconService, WindowsTrayIconService>();
        services.TryAddSingleton<IShellService, WindowsShellService>();
        return services;
    }
}
