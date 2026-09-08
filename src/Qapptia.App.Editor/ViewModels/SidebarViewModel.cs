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

/// <summary>
/// Modos de visualización del panel lateral de navegación.
/// </summary>
public enum SidebarViewMode
{
    Tree,
    Calendar
}

public partial class SidebarViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IEditorStateService _stateService;
    private readonly IShellService _shellService;
    private readonly string _savePath;
    private bool _isLoading;

    public ObservableCollection<GroupItem> SidebarGroups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTreeViewActive))]
    [NotifyPropertyChangedFor(nameof(IsCalendarViewActive))]
    private SidebarViewMode _viewMode = SidebarViewMode.Calendar;

    public bool IsTreeViewActive => ViewMode == SidebarViewMode.Tree;
    public bool IsCalendarViewActive => ViewMode == SidebarViewMode.Calendar;

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

        var state = _stateService.Load();
        if (Enum.TryParse<SidebarViewMode>(state.Layout.SidebarViewMode, true, out var parsedMode))
        {
            _viewMode = parsedMode;
        }
        else
        {
            _viewMode = SidebarViewMode.Calendar;
        }
    }

    [RelayCommand]
    public async Task SetViewMode(SidebarViewMode mode)
    {
        if (ViewMode == mode) return;

        ViewMode = mode;

        var state = _stateService.Load();
        state.Layout.SidebarViewMode = mode.ToString();
        _stateService.Save(state);

        await LoadSidebarImagesCoreAsync(expandAncestorsForSelected: true);
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

    public void StartWatching(Action onFolderChanged)
    {
        _navigationService.StartWatching(_savePath, onFolderChanged);
    }

    partial void OnSelectedNodeChanged(NavigationItem? value)
    {
        if (value is FileItem file)
        {
            var state = _stateService.Load();
            state.Session.LastSelectedFile = file.FullPath;
            _stateService.Save(state);
        }

        FileSelected?.Invoke(this, value as FileItem);
    }

    [RelayCommand]
    public async Task LoadSidebarImagesAsync()
    {
        await LoadSidebarImagesCoreAsync(expandAncestorsForSelected: false);
    }

    public async Task LoadSidebarImagesCoreAsync(bool expandAncestorsForSelected)
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            var savePath = _savePath;
            if (!Directory.Exists(savePath))
            {
                SidebarGroups.Clear();
                return;
            }

            var state = _stateService.Load();
            var selectedPath = (SelectedNode as FileItem)?.FullPath ?? state.Session.LastSelectedFile;

            if (ViewMode == SidebarViewMode.Tree)
            {
                var expandedFolders = state.Layout.ExpandedFolders;

                var rootFolder = await _navigationService.BuildTreeAsync(savePath, expandedFolders);
                if (rootFolder == null)
                {
                    SidebarGroups.Clear();
                    return;
                }

                AttachGroupExpandedEvents(rootFolder);

                SidebarGroups.Clear();
                SidebarGroups.Add(rootFolder);
            }
            else
            {
                var expandedCalendarGroups = state.Layout.ExpandedCalendarGroups;
                var calendarYears = await _navigationService.BuildCalendarTreeAsync(savePath, expandedCalendarGroups, Constants.CalendarWeekLabel);

                SidebarGroups.Clear();

                foreach (var yearGroup in calendarYears)
                {
                    AttachGroupExpandedEvents(yearGroup);
                    SidebarGroups.Add(yearGroup);
                }
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                var nodeToSelect = _navigationService.FindNodeByPath(SidebarGroups, selectedPath);
                if (nodeToSelect != null)
                {
                    if (expandAncestorsForSelected)
                    {
                        for (var current = nodeToSelect.Parent; current != null; current = current.Parent)
                        {
                            current.IsExpanded = true;
                        }
                    }
                    SelectedNode = nodeToSelect;
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    public NavigationItem? FindNodeByPath(string path)
    {
        return _navigationService.FindNodeByPath(SidebarGroups, NavigationService.NormalizePath(path));
    }

    private void AttachGroupExpandedEvents(GroupItem group)
    {
        group.PropertyChanged += OnGroupExpandedChanged;
        foreach (var item in group.Items)
        {
            if (item is GroupItem subGroup)
            {
                AttachGroupExpandedEvents(subGroup);
            }
        }
    }

    private void OnGroupExpandedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationItem.IsExpanded) && sender is GroupItem group)
        {
            var state = _stateService.Load();
            if (ViewMode == SidebarViewMode.Tree)
            {
                var normalizedPath = NavigationService.NormalizePath(group.FullPath);
                var exists = state.Layout.ExpandedFolders.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));

                if (group.IsExpanded && !exists)
                {
                    state.Layout.ExpandedFolders.Add(normalizedPath);
                    _stateService.Save(state);
                }
                else if (!group.IsExpanded && exists)
                {
                    state.Layout.ExpandedFolders.RemoveAll(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));
                    _stateService.Save(state);
                }
            }
            else
            {
                var uri = group.FullPath;
                var exists = state.Layout.ExpandedCalendarGroups.Any(p => string.Equals(p, uri, StringComparison.OrdinalIgnoreCase));

                if (group.IsExpanded && !exists)
                {
                    state.Layout.ExpandedCalendarGroups.Add(uri);
                    _stateService.Save(state);
                }
                else if (!group.IsExpanded && exists)
                {
                    state.Layout.ExpandedCalendarGroups.RemoveAll(p => string.Equals(p, uri, StringComparison.OrdinalIgnoreCase));
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
