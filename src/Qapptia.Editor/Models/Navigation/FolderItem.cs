namespace Qapptia.Editor.Models.Navigation;

/// <summary>
/// Representa un directorio o carpeta física dentro de la jerarquía de navegación.
/// </summary>
public sealed class FolderItem : GroupItem
{
    public FolderItem()
    {
        Kind = GroupKind.Folder;
        IconKey = "IconFolder";
    }
}
