using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Qapptia.Core.Services;

/// <summary>
/// Servicio responsable de la persistencia de imágenes quemadas y generación de backups comprimidos.
/// </summary>
public static class ImageBurnService
{
    /// <summary>
    /// Crea un backup comprimido (.bak.gz) del archivo original antes de quemar anotaciones.
    /// </summary>
    public static async Task<string> CreateCompressedBackupAsync(string filePath, string mediaId)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("El archivo original no existe.", filePath);
        }

        string parentDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileName = Path.GetFileName(filePath);
        string dibujoDir = Path.Combine(parentDir, Constants.DrawingExtension);

        if (!Directory.Exists(dibujoDir))
        {
            Directory.CreateDirectory(dibujoDir);
            File.SetAttributes(dibujoDir, File.GetAttributes(dibujoDir) | FileAttributes.Hidden);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string backupName = $"{fileName}_{mediaId}_{timestamp}.bak.gz";
        string backupPath = Path.Combine(dibujoDir, backupName);

        using (var originalStream = File.OpenRead(filePath))
        using (var backupStream = File.Create(backupPath))
        using (var gzStream = new GZipStream(backupStream, CompressionLevel.Optimal))
        {
            await originalStream.CopyToAsync(gzStream);
        }

        return backupPath;
    }

    /// <summary>
    /// Guarda los bytes finales de la imagen quemada en disco y preserva sus metadatos MediaId y MediaType.
    /// </summary>
    public static async Task SaveBurnedImageAsync(string filePath, byte[] pngBytes, string? mediaId, string? mediaType = null)
    {
        byte[] finalBytes = pngBytes;
        if (!string.IsNullOrEmpty(mediaId))
        {
            string resolvedType = mediaType ?? Constants.ResolveMediaType(filePath);
            finalBytes = ImageMetadataService.InjectMetadata(pngBytes, mediaId, resolvedType);
        }

        await File.WriteAllBytesAsync(filePath, finalBytes);
    }
}
