using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qapptia.App.Editor.Common;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Services;
using Qapptia.Editor.Models;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;

namespace Qapptia.App.Editor.ViewModels;

public partial class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IEditorStateService _stateService;
    private readonly IShellService _shellService;
    private readonly string _savePath;

    public ObservableCollection<FolderItem> SidebarFolders { get; } = new();

    [ObservableProperty]
    private NavigationItem? _selectedNode;

    public event EventHandler<FileItem?>? FileSelected;
    public event Action<string, NotificationType>? ToastRequested;

    public SidebarViewModel(
        INavigationService navigationService,
        IEditorStateService stateService,
        string savePath,
        IShellService? shellService = null)
    {
        _navigationService = navigationService;
        _stateService = stateService;
        _savePath = savePath;
        _shellService = shellService ?? NullShellService.Instance;
    }

    [RelayCommand]
    public void OpenFile(FileItem? item)
    {
        var target = item ?? SelectedNode as FileItem;
        if (target == null || string.IsNullOrWhiteSpace(target.FullPath)) return;

        if (!File.Exists(target.FullPath))
        {
            ToastRequested?.Invoke(Constants.ToastFileNotFound, NotificationType.Warning);
            return;
        }

        bool success = _shellService.OpenFile(target.FullPath);
        if (!success)
        {
            ToastRequested?.Invoke(Constants.ToastOpenFileError, NotificationType.Error);
        }
    }

    [RelayCommand]
    public void ShowInFolder(FileItem? item)
    {
        var target = item ?? SelectedNode as FileItem;
        if (target == null || string.IsNullOrWhiteSpace(target.FullPath)) return;

        var parentDir = Path.GetDirectoryName(target.FullPath);
        if (!File.Exists(target.FullPath) && (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir)))
        {
            ToastRequested?.Invoke(Constants.ToastFolderNotFound, NotificationType.Warning);
            return;
        }

        bool success = _shellService.ShowInFolder(target.FullPath);
        if (!success)
        {
            ToastRequested?.Invoke(Constants.ToastShowInFolderError, NotificationType.Error);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public void StartWatching(Action onFolderChanged)
    {
        _navigationService.StartWatching(_savePath, onFolderChanged);
    }

    partial void OnSelectedNodeChanged(NavigationItem? value)
    {
        FileSelected?.Invoke(this, value as FileItem);
    }

    [RelayCommand]
    public async Task LoadSidebarImagesAsync()
    {
        var savePath = _savePath;
        if (!Directory.Exists(savePath))
        {
            SidebarFolders.Clear();
            return;
        }

        var expandedFolders = _stateService.Load().Layout.ExpandedFolders;
        var normalizedSavePath = NormalizePath(savePath);

        var rootFolder = await _navigationService.BuildTreeAsync(savePath, expandedFolders);
        if (rootFolder == null)
        {
            SidebarFolders.Clear();
            return;
        }

        AttachFolderExpandedEvents(rootFolder);

        SidebarFolders.Clear();
        SidebarFolders.Add(rootFolder);

        if (rootFolder.IsExpanded && expandedFolders.Count == 0)
        {
            var stateToUpdate = _stateService.Load();
            if (!stateToUpdate.Layout.ExpandedFolders.Contains(normalizedSavePath))
            {
                stateToUpdate.Layout.ExpandedFolders.Add(normalizedSavePath);
                _stateService.Save(stateToUpdate);
            }
        }

        var selectedPath = (SelectedNode as FileItem)?.FullPath ?? _stateService.Load().Session.LastSelectedFile;
        if (!string.IsNullOrEmpty(selectedPath))
        {
            var nodeToSelect = _navigationService.FindNodeByPath(SidebarFolders, selectedPath);
            if (nodeToSelect != null)
            {
                SelectedNode = nodeToSelect;
            }
        }
    }

    public NavigationItem? FindNodeByPath(string path)
    {
        return _navigationService.FindNodeByPath(SidebarFolders, NormalizePath(path));
    }

    private void AttachFolderExpandedEvents(FolderItem folder)
    {
        folder.PropertyChanged += OnFolderExpandedChanged;
        foreach (var item in folder.Items)
        {
            if (item is FolderItem subFolder)
            {
                AttachFolderExpandedEvents(subFolder);
            }
        }
    }

    private void OnFolderExpandedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationItem.IsExpanded) && sender is FolderItem folder)
        {
            var state = _stateService.Load();
            var normalizedPath = NormalizePath(folder.FullPath);
            var exists = state.Layout.ExpandedFolders.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (folder.IsExpanded)
            {
                if (!exists)
                {
                    state.Layout.ExpandedFolders.Add(normalizedPath);
                    _stateService.Save(state);
                }
            }
            else
            {
                if (exists)
                {
                    state.Layout.ExpandedFolders.RemoveAll(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));
                    _stateService.Save(state);
                }
            }
        }
    }

    public void Dispose()
    {
        _navigationService.Dispose();
        GC.SuppressFinalize(this);
    }
}
