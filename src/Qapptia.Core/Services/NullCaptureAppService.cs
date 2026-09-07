using System;
using System.Threading;
using System.Threading.Tasks;
using Qapptia.Core.Abstractions;

namespace Qapptia.Core.Services;

/// <summary>
/// Implementación neutra (Null Object Pattern) de <see cref="ICaptureAppService"/>
/// para entornos de prueba o cuando el servicio no esté disponible.
/// </summary>
public sealed class NullCaptureAppService : ICaptureAppService
{
    public static readonly NullCaptureAppService Instance = new();

    public bool IsRunning => false;

    public event EventHandler<bool>? StatusChanged
    {
        add { }
        remove { }
    }

    public Task<bool> CheckStatusAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> LaunchOrWakeAsync(CancellationToken ct = default) => Task.FromResult(false);

    public void StartMonitoring(TimeSpan interval) { }

    public void StopMonitoring() { }

    public void Dispose() { }
}
