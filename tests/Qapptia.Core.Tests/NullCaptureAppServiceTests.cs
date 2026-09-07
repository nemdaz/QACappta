using System;
using System.Threading.Tasks;
using FluentAssertions;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Services;
using Xunit;

namespace Qapptia.Core.Tests;

public sealed class NullCaptureAppServiceTests
{
    [Fact]
    public void InstanceReturnsSingleton()
    {
        var instance1 = NullCaptureAppService.Instance;
        var instance2 = NullCaptureAppService.Instance;

        instance1.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void IsRunningIsAlwaysFalse()
    {
        var service = NullCaptureAppService.Instance;
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task CheckStatusAsyncReturnsFalse()
    {
        var service = NullCaptureAppService.Instance;
        bool status = await service.CheckStatusAsync();
        status.Should().BeFalse();
    }

    [Fact]
    public async Task LaunchOrWakeAsyncReturnsFalse()
    {
        var service = NullCaptureAppService.Instance;
        bool result = await service.LaunchOrWakeAsync();
        result.Should().BeFalse();
    }

    [Fact]
    public void MonitoringMethodsDoNotThrow()
    {
        var service = NullCaptureAppService.Instance;
        var actStart = () => service.StartMonitoring(TimeSpan.FromSeconds(1));
        var actStop = () => service.StopMonitoring();
        var actDispose = () => service.Dispose();

        actStart.Should().NotThrow();
        actStop.Should().NotThrow();
        actDispose.Should().NotThrow();
    }
}
