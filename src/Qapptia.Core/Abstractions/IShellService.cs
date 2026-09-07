namespace Qapptia.Core.Abstractions;

/// <summary>
/// Servicio para interactuar con el Shell del sistema operativo: abrir archivos con aplicaciones
/// por defecto y revelar elementos dentro del gestor de archivos nativo (Explorador / Finder).
/// </summary>
public interface IShellService
{
    /// <summary>
    /// Abre el archivo especificado utilizando el programa predeterminado del sistema.
    /// </summary>
    /// <param name="filePath">Ruta absoluta del archivo a abrir.</param>
    /// <returns>True si el comando fue enviado con éxito al sistema, false en caso de error o si el archivo no existe.</returns>
    bool OpenFile(string filePath);

    /// <summary>
    /// Abre la carpeta contenedora en el explorador de archivos nativo con el elemento seleccionado/resaltado.
    /// </summary>
    /// <param name="filePath">Ruta absoluta del archivo a revelar.</param>
    /// <returns>True si el comando fue ejecutado con éxito, false en caso de error o si la ruta no existe.</returns>
    bool ShowInFolder(string filePath);
}
