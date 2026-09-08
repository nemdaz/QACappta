using System.Collections.ObjectModel;
using System.Linq;

namespace Qapptia.Editor.Models.Navigation;

/// <summary>
/// Abstracción base para cualquier nodo contenedor de elementos en el árbol de navegación.
/// </summary>
public class GroupItem : NavigationItem
{
    public ObservableCollection<NavigationItem> Items { get; } = new();

    public GroupKind Kind { get; set; } = GroupKind.Folder;

    public string IconKey { get; set; } = "IconFolder";

    public bool HasFiles => Items.Any(i => i is FileItem || (i is GroupItem g && g.HasFiles));

    public bool IsDimmed => Kind == GroupKind.Day && !HasFiles;
}
