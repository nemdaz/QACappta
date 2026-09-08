using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;
using Xunit;

namespace Qapptia.Editor.Tests.ViewModels;

public sealed class SidebarViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly EditorStateService _stateService;
    private readonly NavigationService _navigationService;

    public SidebarViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_SidebarTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _stateService = new EditorStateService(_testDir, "state.json");
        _navigationService = new NavigationService();
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
    public async Task SidebarViewModelLoadSidebarImagesAsyncPopulatesFolders()
    {
        var subDir = Path.Combine(_testDir, "Screenshots");
        Directory.CreateDirectory(subDir);
        var testFile = Path.Combine(subDir, "shot1.png");
        File.WriteAllBytes(testFile, new byte[] { 1, 2, 3 });

        var state = _stateService.Load();
        state.Layout.SidebarViewMode = "Tree";
        _stateService.Save(state);

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir);

        await vm.LoadSidebarImagesAsync();

        vm.SidebarGroups.Should().NotBeEmpty();
        var foundNode = vm.FindNodeByPath(testFile);
        foundNode.Should().NotBeNull();
        foundNode.Should().BeOfType<FileItem>();
    }

    [Fact]
    public void SidebarViewModelSelectedNodeChangeRaisesFileSelectedEvent()
    {
        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir);
        FileItem? selectedFile = null;
        vm.FileSelected += (s, file) => selectedFile = file;

        var dummyFile = new FileItem { Name = "test.png", FullPath = Path.Combine(_testDir, "test.png") };
        vm.SelectedNode = dummyFile;

        selectedFile.Should().Be(dummyFile);
    }

    [Fact]
    public async Task SetViewModeSwitchesBetweenTreeAndCalendarAndPersistsState()
    {
        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir);
        vm.ViewMode.Should().Be(SidebarViewMode.Calendar);
        vm.IsCalendarViewActive.Should().BeTrue();
        vm.IsTreeViewActive.Should().BeFalse();

        await vm.SetViewMode(SidebarViewMode.Tree);

        vm.ViewMode.Should().Be(SidebarViewMode.Tree);
        vm.IsTreeViewActive.Should().BeTrue();
        vm.IsCalendarViewActive.Should().BeFalse();

        var state = _stateService.Load();
        state.Layout.SidebarViewMode.Should().Be("Tree");
    }

    [Fact]
    public async Task SetViewModePreservesSelectedFileAndExpandsAncestors()
    {
        var subDir = Path.Combine(_testDir, "2026-09");
        Directory.CreateDirectory(subDir);
        var testFile = Path.Combine(subDir, "capture_20260907120000.png");
        File.WriteAllBytes(testFile, new byte[] { 1, 2 });
        var fileDate = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local);
        File.SetCreationTimeUtc(testFile, fileDate.ToUniversalTime());
        File.SetLastWriteTimeUtc(testFile, fileDate.ToUniversalTime());

        var state = _stateService.Load();
        state.Layout.SidebarViewMode = "Tree";
        _stateService.Save(state);

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir);
        await vm.LoadSidebarImagesAsync();

        var treeNode = vm.FindNodeByPath(testFile);
        treeNode.Should().NotBeNull();
        vm.SelectedNode = treeNode;

        // Conmutar a modo Calendario
        await vm.SetViewMode(SidebarViewMode.Calendar);

        // El archivo debe permanecer seleccionado
        vm.SelectedNode.Should().NotBeNull();
        vm.SelectedNode!.FullPath.Should().Be(NavigationService.NormalizePath(testFile));

        // Todos sus ancestros de calendario deben quedar expandidos
        var parent = vm.SelectedNode.Parent;
        while (parent != null)
        {
            parent.IsExpanded.Should().BeTrue();
            parent = parent.Parent;
        }
    }

    [Fact]
    public async Task LoadSidebarImagesAsyncDoesNotForceExpandWhenFoldersAreCollapsed()
    {
        var subDir = Path.Combine(_testDir, "2026-09");
        Directory.CreateDirectory(subDir);
        var testFile = Path.Combine(subDir, "shot.png");
        File.WriteAllBytes(testFile, new byte[] { 1, 2 });

        // Guardar estado con modo Árbol, carpetas contraídas (ExpandedFolders vacío) y un archivo seleccionado previamente
        var state = _stateService.Load();
        state.Layout.SidebarViewMode = "Tree";
        state.Layout.ExpandedFolders.Clear();
        state.Session.LastSelectedFile = NavigationService.NormalizePath(testFile);
        _stateService.Save(state);

        var vm = new SidebarViewModel(_navigationService, _stateService, _testDir);
        await vm.LoadSidebarImagesAsync();

        // Las carpetas deben permanecer contraídas respetando el estado del usuario
        vm.SidebarGroups.Should().NotBeEmpty();
        var root = vm.SidebarGroups[0];
        root.IsExpanded.Should().BeFalse();

        var subFolder = root.Items.OfType<FolderItem>().FirstOrDefault();
        subFolder.Should().NotBeNull();
        subFolder!.IsExpanded.Should().BeFalse();
    }
}

