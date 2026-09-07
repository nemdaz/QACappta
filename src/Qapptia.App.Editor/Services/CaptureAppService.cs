using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Ipc;
using Serilog;

namespace Qapptia.App.Editor.Services;

/// <summary>
/// Implementación de <see cref="ICaptureAppService"/> que monitorea el estado del proceso
/// de captura mediante sondeo IPC Named Pipes y gestiona su ciclo de vida y arranque.
/// </summary>
public sealed class CaptureAppService : ICaptureAppService
{
    private readonly ILogger _logger;
    private readonly string _baseDirectory;
    private bool _isRunning;
    private CancellationTokenSource? _monitoringCts;

    public bool IsRunning => _isRunning;

    public event EventHandler<bool>? StatusChanged;

    public CaptureAppService(ILogger? logger = null, string? baseDirectory = null)
    {
        _logger = (logger ?? Log.Logger).ForContext<CaptureAppService>();
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public async Task<bool> CheckStatusAsync(CancellationToken ct = default)
    {
        bool wasRunning = _isRunning;
        bool isAlive = false;

        try
        {
            var response = await QapptiaIpcClient.SendAsync(
                IpcChannels.Capture,
                new Ping(),
                timeoutMs: 300,
                ct).ConfigureAwait(false);

            isAlive = response is Pong;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Excepción al sondear estado de Qapptia.App.Capture");
            isAlive = false;
        }

        _isRunning = isAlive;

        if (wasRunning != isAlive)
        {
            _logger.Information("Estado de Qapptia.App.Capture cambió a: {Status}", isAlive ? "Activo" : "Inactivo");
            StatusChanged?.Invoke(this, isAlive);
        }

        return isAlive;
    }

    public async Task<bool> LaunchOrWakeAsync(CancellationToken ct = default)
    {
        // 1. Verificación real inmediata
        bool isAlreadyActive = await CheckStatusAsync(ct).ConfigureAwait(false);
        if (isAlreadyActive)
        {
            _logger.Information("Qapptia.App.Capture ya está activo. Enviando señal WakeUp...");
            try
            {
                await QapptiaIpcClient.SendAsync(
                    IpcChannels.Capture,
                    new WakeUpRequest(),
                    timeoutMs: 500,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo entregar WakeUpRequest a Qapptia.App.Capture");
            }
            return true;
        }

        // 2. Localizar ejecutable cross-platform
        var exeName = Qapptia.Core.Constants.CaptureExecutableName;
        var exePath = Path.Combine(_baseDirectory, exeName);

        if (!File.Exists(exePath) && !OperatingSystem.IsWindows())
        {
            var unixName = Path.GetFileNameWithoutExtension(exeName);
            var unixPath = Path.Combine(_baseDirectory, unixName);
            if (File.Exists(unixPath))
            {
                exePath = unixPath;
            }
        }

        if (!File.Exists(exePath))
        {
            _logger.Error("No se encontró el ejecutable de captura en: {Path}", exePath);
            return false;
        }

        _logger.Information("Iniciando proceso de captura desde: {Path}", exePath);
        try
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true
            };
            Process.Start(startInfo);

            // Reintentos rápidos para reflejar el estado activo inmediatamente tras el arranque
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
                    if (await CheckStatusAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }, CancellationToken.None);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error al iniciar el proceso de captura: {Path}", exePath);
            return false;
        }
    }

    public void StartMonitoring(TimeSpan interval)
    {
        StopMonitoring();

        _monitoringCts = new CancellationTokenSource();
        var token = _monitoringCts.Token;

        _ = Task.Run(async () =>
        {
            // Sondeo inicial inmediato
            await CheckStatusAsync(token).ConfigureAwait(false);

            using var timer = new PeriodicTimer(interval);
            try
            {
                while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    await CheckStatusAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Monitoreo detenido normalmente
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error no controlado en el ciclo de monitoreo de captura");
            }
        }, token);
    }

    public void StopMonitoring()
    {
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = null;
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
