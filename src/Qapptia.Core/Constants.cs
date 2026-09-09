using System;
using System.IO;

namespace Qapptia.Core;

public static class Constants
{
    public const string AppName = "Qapptia";
    public const string ConfigFileName = "config.json";
    public const string EditorStateFileName = "editor_state.json";
    public const string ShortcutCopyClipboard = "Ctrl+C";
    public const string ShortcutCopyFile = "Ctrl+F";
    public const string DrawingExtension = ".dibujo";
    public static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

    // Propiedades de serialización JSON en archivos de estado (.dibujo)
    public const string MetadataPropertyMediaId = "Qapptia.mediaId";
    public const string MetadataPropertyMediaType = "Qapptia.mediaType";

    // Constantes de namespaces y cabeceras XMP (ISO 16684-1)
    public const string XmpNamespaceMediaManagement = "http://ns.adobe.com/xap/1.0/mm/";
    public const string XmpNamespaceDublinCore = "http://purl.org/dc/elements/1.1/";
    public const string XmpNamespaceAdobeBasic = "http://ns.adobe.com/xap/1.0/";
    public const string PngChunkXmpKeyword = "XML:com.adobe.xmp";
    public const string JpegXmpHeader = "http://ns.adobe.com/xap/1.0/\0";

    // Constantes de persistencia y buffers de lectura rápida
    public const string JsonFileExtension = ".json";
    public const string JsonSearchPattern = "*.json";
    public const int JsonHeaderBufferSize = 512;

    // Tipos MIME estándar (IANA / HTTP Content-Type)
    public const string MediaTypePng = "image/png";
    public const string MediaTypeJpeg = "image/jpeg";
    public const string DefaultMediaType = MediaTypePng;

    public static string ResolveMediaType(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => MediaTypePng,
            ".jpg" or ".jpeg" => MediaTypeJpeg,
            _ => MediaTypePng
        };
    }

    // Nombres de aplicaciones de la suite
    public const string CaptureAppName = "Qapptia Capture";
    public const string EditorAppName = "Qapptia Editor";
    public const string ConfigAppName = "Qapptia Config";

    // Nombres de ejecutables de la suite
    public const string CaptureExecutableName = "Qapptia.App.Capture.exe";
    public const string EditorExecutableName = "Qapptia.App.Editor.exe";
    public const string ConfigExecutableName = "Qapptia.App.Config.exe";

    // Argumentos de línea de comandos
    public const string ArgEditor = "--editor";
    public const string ArgConfig = "--config";
    public const string ArgCapture = "--capture";
    public const string ArgRestart = "--restart";

    // Notificaciones del sistema
    public const int NotificationDurationMs = 5000;
    public const string NotificationTitleCapture = CaptureAppName;
    public const string NotificationTitleEditor = EditorAppName;
    public const string NotificationTitleConfig = ConfigAppName;
    public const string NotificationMessageCaptureStarted = "El capturador está activo en segundo plano.";
    public const string NotificationMessageCaptureRestarted = "El capturador se ha reiniciado correctamente.";

    // Nombres de recursos y carpetas de assets
    public const string AssetsDirectoryName = "Assets";
    public const string TrayIconFileName = "tray_icon.ico";
    public const string AppIconFileName = "app_icon.ico";

#if DEBUG
    public static string DefaultConfigPath => Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    public static string DefaultLogDirectory => AppContext.BaseDirectory;
#else
    public static string DefaultConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName, 
        ConfigFileName);

    public static string DefaultLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);
#endif

    public static string DefaultSavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        AppName);
}
