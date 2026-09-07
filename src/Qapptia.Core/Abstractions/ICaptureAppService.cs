using System;
using System.Threading;
using System.Threading.Tasks;

namespace Qapptia.Core.Abstractions;

/// <summary>
/// Servicio para monitorear y controlar el ciclo de vida de la aplicación
/// de captura en segundo plano (Qapptia.App.Capture).
/// </summary>
public interface ICaptureAppService : IDisposable
{
    /// <summary>
    /// Indica si la aplicación de captura se encuentra actualmente en ejecución y respondiendo.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Evento emitido cuando cambia el estado de disponibilidad del capturador.
    /// </summary>
    event EventHandler<bool>? StatusChanged;

    /// <summary>
    /// Realiza una verificación activa en tiempo real mediante sondeo IPC.
    /// </summary>
    Task<bool> CheckStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Inicia el capturador si está detenido, o envía una señal de activación si ya está en ejecución.
    /// </summary>
    Task<bool> LaunchOrWakeAsync(CancellationToken ct = default);

    /// <summary>
    /// Inicia el monitoreo periódico de disponibilidad en segundo plano.
    /// </summary>
    void StartMonitoring(TimeSpan interval);

    /// <summary>
    /// Detiene el monitoreo periódico en segundo plano.
    /// </summary>
    void StopMonitoring();
}
