namespace Qapptia.App.Editor.Common;

/// <summary>
/// Constantes de la aplicación de presentación Qapptia Editor.
/// </summary>
public static class Constants
{
    // Mensajes de notificación Toast
    public const string ToastImageCopied = "Imagen copiada al portapapeles";
    public const string ToastCopyError = "Error al copiar al portapapeles";
    public const string ToastFileCopied = "Archivo copiado al portapapeles";
    public const string ToastFileError = "Error al copiar el archivo";
    public const string ToastClipboardUnavailable = "Portapapeles no disponible en esta plataforma";
    public const string ToastImageSaved = "Imagen guardada";
    public const string ToastSaveErrorPrefix = "Error al guardar la imagen: ";
    public const string ToastImageRotated90 = "Imagen rotada 90°";
    public const string ToastConfigNotFound = "No se encontró la aplicación de configuración.";
    public const string ToastConfigError = "Error al abrir configuración.";
    public const string ToastFileNotFound = "El archivo no se encuentra en el disco.";
    public const string ToastFileCorrupted = "El archivo está dañado o no es una imagen válida.";
    public const string ToastOpenFileError = "No se pudo abrir el archivo con la aplicación del sistema.";
    public const string ToastFolderNotFound = "La ubicación del archivo no existe.";
    public const string ToastShowInFolderError = "No se pudo abrir el explorador de archivos.";
    public const string ToastCaptureActive = "El capturador está activo en segundo plano";
    public const string ToastCaptureLaunching = "Iniciando capturador en segundo plano...";
    public const string ToastCaptureNotFound = "No se encontró la aplicación de captura.";
    public const string ToastCaptureError = "Error al iniciar el capturador.";

    // ToolTips de estado del Capturador
    public const string ToolTipCaptureActive = "Capturador activo en segundo plano";
    public const string ToolTipCaptureInactive = "Capturador inactivo (clic para iniciar)";

    // Textos de navegación cronológica (Sidebar - Modo Calendario)
    public const string CalendarWeekLabel = "Semana";
}
