using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Capture;
using Serilog;

namespace Qapptia.Platform.MacOS;

public sealed class MacScreenCapture : IScreenCapture
{
    public Task<ScreenCaptureResult> CaptureScreenAsync(bool captureAllScreens = false, CancellationToken ct = default)
        => throw new PlatformNotSupportedException("IScreenCapture en macOS se implementa en Fase 3 (CGDisplayCreateImage / ScreenCaptureKit).");
}

public sealed class MacCursorCapture : ICursorCapture
{
    public Task<CursorImage?> CaptureCursorAsync(CancellationToken ct = default)
        => throw new PlatformNotSupportedException("ICursorCapture en macOS se implementa en Fase 3 (NSCursor + CGEvent).");
}

public sealed class MacHotkeyRegistrar : IHotkeyRegistrar
{
    public IHotkeyHandle Register(HotkeyModifiers modifiers, uint virtualKey, Action callback)
        => throw new PlatformNotSupportedException("IHotkeyRegistrar en macOS se implementa en Fase 3 (CGEventTap / GlobalEventMonitor).");
}

public sealed class MacPowerEvents : IPowerEvents
{
    public event EventHandler<PowerMode>? PowerModeChanged { add { } remove { } }
    public bool RequiresHotkeyReRegistrationAfterResume => true;
    public void Dispose() { }
}

public sealed class MacDesktopService : IDesktopService
{
    public void ShowInfo(string title, string message) => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public void ShowError(string title, string message) => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public MonitorInfo GetMonitorAtCursor() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public (int X, int Y) GetCursorPosition() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public (int X, int Y) GetVirtualScreenOrigin() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public int GetVirtualScreenWidth() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public int GetVirtualScreenHeight() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
    public double GetDpiScalingAtCursor() => throw new PlatformNotSupportedException("MacDesktopService: Fase 3.");
}

public sealed class MacShutterSoundService : IShutterSoundService
{
    public Task PlayAsync(CancellationToken ct = default)
        => throw new PlatformNotSupportedException("IShutterSoundService en macOS se implementa en Fase 3 (AVAudioPlayer / NSSound).");
}


public sealed class MacClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken ct = default)
        => throw new PlatformNotSupportedException("IClipboardService en macOS se implementa en Fase 3 (NSPasteboard).");
    public Task SetImageAsync(byte[] pngBytes, CancellationToken ct = default) => throw new PlatformNotSupportedException("MacClipboardService: Fase 3.");
    public Task SetFileDropListAsync(string[] filePaths, CancellationToken ct = default) => throw new PlatformNotSupportedException("MacClipboardService: Fase 3.");
}

public sealed class MacShellService : IShellService
{
    private readonly ILogger? _logger;

    public MacShellService(ILogger? logger = null)
    {
        _logger = logger?.ForContext<MacShellService>();
    }

    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath)) return false;

        try
        {
            Process.Start(new ProcessStartInfo("open", $"\"{fullPath}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error al abrir archivo en macOS: {FilePath}", fullPath);
            return false;
        }
    }

    public bool ShowInFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var fullPath = Path.GetFullPath(filePath);

        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo("open", $"-R \"{fullPath}\"") { UseShellExecute = false });
                return true;
            }

            var parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                Process.Start(new ProcessStartInfo("open", $"\"{parentDir}\"") { UseShellExecute = false });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error al mostrar en carpeta en macOS: {FilePath}", fullPath);
            return false;
        }
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMacOSPlatform(this IServiceCollection services)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("AddMacOSPlatform requiere macOS.");
        services.TryAddSingleton<IScreenCapture, MacScreenCapture>();
        services.TryAddSingleton<ICursorCapture, MacCursorCapture>();
        services.TryAddSingleton<IHotkeyRegistrar, MacHotkeyRegistrar>();
        services.TryAddSingleton<IPowerEvents, MacPowerEvents>();
        services.TryAddSingleton<IDesktopService, MacDesktopService>();
        services.TryAddSingleton<IShutterSoundService, MacShutterSoundService>();
        services.TryAddSingleton<IClipboardService, MacClipboardService>();
        services.TryAddSingleton<ITrayIconService, MacOSTrayIconService>();
        services.TryAddSingleton<IShellService, MacShellService>();
        return services;
    }
}
