using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qapptia.Editor.Models.Navigation;

namespace Qapptia.Editor.Services;

/// <summary>
/// Contrato para el servicio de exploración, construcción de árbol y monitoreo de archivos de captura.
/// </summary>
public interface INavigationService : IDisposable
{
    Task<FolderItem?> BuildTreeAsync(string rootPath, IReadOnlyList<string> expandedFolders, CancellationToken ct = default);
    Task<IReadOnlyList<GroupItem>> BuildCalendarTreeAsync(string rootPath, IReadOnlyList<string> expandedGroups, string? weekLabel = null, CancellationToken ct = default);
    NavigationItem? FindNodeByPath(IEnumerable<NavigationItem> nodes, string path);
    void StartWatching(string rootPath, Action onFileSystemChanged);
    void StopWatching();
}
