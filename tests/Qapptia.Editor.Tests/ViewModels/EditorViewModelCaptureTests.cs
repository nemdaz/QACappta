using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAssertions;
using Moq;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Core.Abstractions;
using Qapptia.Editor.Core;
using Qapptia.Editor.Services;
using Qapptia.UI.Components.Controls;
using Xunit;
using EditorConstants = Qapptia.App.Editor.Common.Constants;

namespace Qapptia.Editor.Tests.ViewModels;

public sealed class EditorViewModelCaptureTests : IDisposable
{
    private readonly string _testDir;
    private readonly EditorStateService _stateService;
    private readonly CanvasStateService _canvasStateService;
    private readonly Mock<IFontProvider> _fontProviderMock;

    public EditorViewModelCaptureTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_CaptureTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _stateService = new EditorStateService(_testDir, "state.json");
        _canvasStateService = new CanvasStateService();
        _fontProviderMock = new Mock<IFontProvider>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private EditorViewModel CreateViewModel(ICaptureAppService captureService)
    {
        return new EditorViewModel(
            _stateService,
            _testDir,
            _fontProviderMock.Object,
            canvasStateService: _canvasStateService,
            captureAppService: captureService,
            uiDispatcher: action => action());
    }

    [Fact]
    public void EditorViewModelWhenCaptureIsActiveReflectsGreenBorderAndActiveTooltip()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(true);

        using var vm = CreateViewModel(captureMock.Object);

        vm.IsCaptureActive.Should().BeTrue();
        vm.CaptureStatusToolTip.Should().Be(EditorConstants.ToolTipCaptureActive);
        vm.CaptureStatusToolTip.Should().Be("Capturador activo en segundo plano");

        var borderBrush = vm.CaptureStatusBorderBrush as ISolidColorBrush;
        borderBrush.Should().NotBeNull();
        borderBrush!.Color.Should().Be(Color.Parse("#4CAF50"));

        var bgBrush = vm.CaptureStatusBackgroundBrush as ISolidColorBrush;
        bgBrush.Should().NotBeNull();
        bgBrush!.Color.Should().Be(Color.Parse("#4CAF50"));

        captureMock.Verify(c => c.StartMonitoring(It.Is<TimeSpan>(t => t.TotalSeconds >= 2 && t.TotalSeconds <= 3)), Times.Once);
    }

    [Fact]
    public void EditorViewModelWhenCaptureIsInactiveReflectsRedBorderAndInactiveTooltip()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(false);

        using var vm = CreateViewModel(captureMock.Object);

        vm.IsCaptureActive.Should().BeFalse();
        vm.CaptureStatusToolTip.Should().Be(EditorConstants.ToolTipCaptureInactive);
        vm.CaptureStatusToolTip.Should().Be("Capturador inactivo (clic para iniciar)");

        var borderBrush = vm.CaptureStatusBorderBrush as ISolidColorBrush;
        borderBrush.Should().NotBeNull();
        borderBrush!.Color.Should().Be(Color.Parse("#E53935"));

        var bgBrush = vm.CaptureStatusBackgroundBrush as ISolidColorBrush;
        bgBrush.Should().NotBeNull();
        bgBrush!.Color.Should().Be(Color.Parse("#E53935"));
    }

    [Fact]
    public void EditorViewModelReactsToCaptureStatusChangedEvent()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(false);

        using var vm = CreateViewModel(captureMock.Object);
        vm.IsCaptureActive.Should().BeFalse();

        // Simular que el capturador se inició (evento IPC)
        captureMock.Raise(c => c.StatusChanged += null, captureMock.Object, true);
        vm.IsCaptureActive.Should().BeTrue();
        vm.CaptureStatusToolTip.Should().Be(EditorConstants.ToolTipCaptureActive);

        // Simular que el capturador se cerró o cayó (muerte de proceso detectada)
        captureMock.Raise(c => c.StatusChanged += null, captureMock.Object, false);
        vm.IsCaptureActive.Should().BeFalse();
        vm.CaptureStatusToolTip.Should().Be(EditorConstants.ToolTipCaptureInactive);
    }

    [Fact]
    public async Task LaunchOrWakeCaptureAsyncWhenActiveAndProbeSucceedsShowsActiveToastOnRight()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(true);
        captureMock.Setup(c => c.CheckStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var vm = CreateViewModel(captureMock.Object);

        await vm.LaunchOrWakeCaptureCommand.ExecuteAsync(null);

        captureMock.Verify(c => c.CheckStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.ToastMessage.Should().Be(EditorConstants.ToastCaptureActive);
        vm.ToastAlignment.Should().Be(HorizontalAlignment.Right);
        vm.ToastType.Should().Be(ToastNotificationType.Info);
        vm.IsToastVisible.Should().BeTrue();
    }

    [Fact]
    public async Task LaunchOrWakeCaptureAsyncWhenActiveAndProbeFailsShowsErrorToastOnRight()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(true);
        captureMock.Setup(c => c.CheckStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        using var vm = CreateViewModel(captureMock.Object);

        await vm.LaunchOrWakeCaptureCommand.ExecuteAsync(null);

        captureMock.Verify(c => c.CheckStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.ToastMessage.Should().Be(EditorConstants.ToastCaptureError);
        vm.ToastAlignment.Should().Be(HorizontalAlignment.Right);
        vm.ToastType.Should().Be(ToastNotificationType.Warning);
    }

    [Fact]
    public async Task LaunchOrWakeCaptureAsyncWhenInactiveAndLaunchSucceedsLaunchesAndShowsToastOnRight()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(false);
        captureMock.Setup(c => c.LaunchOrWakeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        using var vm = CreateViewModel(captureMock.Object);

        await vm.LaunchOrWakeCaptureCommand.ExecuteAsync(null);

        captureMock.Verify(c => c.LaunchOrWakeAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.ToastMessage.Should().Be(EditorConstants.ToastCaptureLaunching);
        vm.ToastAlignment.Should().Be(HorizontalAlignment.Right);
        vm.ToastType.Should().Be(ToastNotificationType.Info);
    }

    [Fact]
    public async Task LaunchOrWakeCaptureAsyncWhenInactiveAndLaunchFailsShowsNotFoundToastOnRight()
    {
        var captureMock = new Mock<ICaptureAppService>();
        captureMock.SetupGet(c => c.IsRunning).Returns(false);
        captureMock.Setup(c => c.LaunchOrWakeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        using var vm = CreateViewModel(captureMock.Object);

        await vm.LaunchOrWakeCaptureCommand.ExecuteAsync(null);

        captureMock.Verify(c => c.LaunchOrWakeAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.ToastMessage.Should().Be(EditorConstants.ToastCaptureNotFound);
        vm.ToastAlignment.Should().Be(HorizontalAlignment.Right);
        vm.ToastType.Should().Be(ToastNotificationType.Error);
    }

    [Fact]
    public void DisposeDisposesCaptureServiceAndUnsubscribes()
    {
        var captureMock = new Mock<ICaptureAppService>();
        var vm = CreateViewModel(captureMock.Object);

        vm.Dispose();

        captureMock.Verify(c => c.Dispose(), Times.Once);
    }
}
