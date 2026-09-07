using System;
using System.IO;
using FluentAssertions;
using Qapptia.App.Editor.Common;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Services;
using Qapptia.Editor.Models;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;
using Xunit;

namespace Qapptia.Editor.Tests;

public class MockShellService : IShellService
{
    public string? LastOpenedFile { get; private set; }
    public string? LastShownInFolderFile { get; private set; }
    public bool OpenFileResult { get; set; } = true;
    public bool ShowInFolderResult { get; set; } = true;

    public bool OpenFile(string filePath)
    {
        LastOpenedFile = filePath;
        return OpenFileResult;
    }

    public bool ShowInFolder(string filePath)
    {
        LastShownInFolderFile = filePath;
        return ShowInFolderResult;
    }
}

public sealed class ShellServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly EditorStateService _stateService;
    private readonly NavigationService _navigationService;
    private readonly MockShellService _mockShell;

    public ShellServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_ShellTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _stateService = new EditorStateService(_testDir, "state.json");
        _navigationService = new NavigationService();
        _mockShell = new MockShellService();
    }

    public void Dispose()
    {
        try
        {
            _navigationService.Dispose();
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void OpenFileCommandWithExistingFileCallsShellService()
    {
        var testFile = Path.Combine(_testDir, "test.png");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir, _mockShell);
        var fileItem = new FileItem { Name = "test.png", FullPath = testFile };

        vm.OpenFileCommand.Execute(fileItem);

        _mockShell.LastOpenedFile.Should().Be(testFile);
    }

    [Fact]
    public void OpenFileCommandWithNonExistentFileRaisesToastWarningAndDoesNotCallShell()
    {
        var nonExistentPath = Path.Combine(_testDir, "does_not_exist.png");
        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir, _mockShell);
        var fileItem = new FileItem { Name = "does_not_exist.png", FullPath = nonExistentPath };

        string? toastMessage = null;
        NotificationType? toastType = null;
        vm.ToastRequested += (msg, type) =>
        {
            toastMessage = msg;
            toastType = type;
        };

        vm.OpenFileCommand.Execute(fileItem);

        _mockShell.LastOpenedFile.Should().BeNull();
        toastMessage.Should().Be(Constants.ToastFileNotFound);
        toastType.Should().Be(NotificationType.Warning);
    }

    [Fact]
    public void ShowInFolderCommandWithExistingFileCallsShellService()
    {
        var testFile = Path.Combine(_testDir, "capture.png");
        File.WriteAllBytes(testFile, new byte[] { 4, 5, 6 });

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir, _mockShell);
        var fileItem = new FileItem { Name = "capture.png", FullPath = testFile };

        vm.ShowInFolderCommand.Execute(fileItem);

        _mockShell.LastShownInFolderFile.Should().Be(testFile);
    }

    [Fact]
    public void ShowInFolderCommandFallsBackToSelectedNodeWhenParameterIsNull()
    {
        var testFile = Path.Combine(_testDir, "selected.png");
        File.WriteAllBytes(testFile, new byte[] { 7, 8, 9 });

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir, _mockShell);
        var fileItem = new FileItem { Name = "selected.png", FullPath = testFile };
        vm.SelectedNode = fileItem;

        vm.ShowInFolderCommand.Execute(null);

        _mockShell.LastShownInFolderFile.Should().Be(testFile);
    }

    [Fact]
    public void NullShellServiceHandlesCallsSafely()
    {
        var service = NullShellService.Instance;

        service.OpenFile(string.Empty).Should().BeFalse();
        service.OpenFile("any_file.png").Should().BeFalse();
        service.ShowInFolder(string.Empty).Should().BeFalse();
        service.ShowInFolder("any_file.png").Should().BeFalse();
    }

#if WINDOWS
    [Fact]
    public void WindowsShellServiceHandlesEmptyAndNonExistentPathsSafely()
    {
        var service = new Qapptia.Platform.Windows.WindowsShellService();

        service.OpenFile(string.Empty).Should().BeFalse();
        service.OpenFile("non_existent_file_path_12345.png").Should().BeFalse();
        service.ShowInFolder(string.Empty).Should().BeFalse();
        service.ShowInFolder("C:\\non_existent_directory_12345\\file.png").Should().BeFalse();
    }

    [Fact]
    public void WindowsShellServiceNormalizeWindowsPathConvertsForwardSlashesToBackslashes()
    {
        Qapptia.Platform.Windows.WindowsShellService.NormalizeWindowsPath(string.Empty).Should().BeEmpty();
        Qapptia.Platform.Windows.WindowsShellService.NormalizeWindowsPath("   ").Should().BeEmpty();

        var result = Qapptia.Platform.Windows.WindowsShellService.NormalizeWindowsPath("C:/test/subfolder/file.png");
        result.Should().Be(@"C:\test\subfolder\file.png");
        result.Should().NotContain("/");
    }
#endif
}
