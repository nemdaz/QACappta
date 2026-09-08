using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Qapptia.Core.Services;

/// <summary>
/// Servicio responsable de la inyección y extracción de metadatos estandarizados (MediaId, MediaType y CreatedAt) al final de los archivos de imagen.
/// </summary>
public static class ImageMetadataService
{
    /// <summary>
    /// Obtiene sincrónicamente los metadatos de la imagen verificando el final del archivo. Si no existen, genera un nuevo ID, los anexa y los retorna.
    /// </summary>
    public static (string MediaId, string MediaType, DateTime CreatedAt) EnsureImageMetadata(string filePath, string? mediaType = null, DateTime? createdAt = null)
    {
        var (existingId, existingType, existingDate) = GetImageMetadata(filePath);
        if (!string.IsNullOrEmpty(existingId))
        {
            var date = existingDate ?? GetFileCreationTimeUtc(filePath);
            return (existingId, existingType ?? Constants.ResolveMediaType(filePath), date);
        }

        string newId = Guid.NewGuid().ToString();
        string resolvedType = mediaType ?? Constants.ResolveMediaType(filePath);
        DateTime resolvedDate = createdAt ?? GetFileCreationTimeUtc(filePath);
        AppendMediaMetadata(filePath, newId, resolvedType, resolvedDate);
        return (newId, resolvedType, resolvedDate);
    }

    /// <summary>
    /// Obtiene asincrónicamente los metadatos de la imagen verificando el final del archivo. Si no existen, genera un nuevo ID, los anexa y los retorna.
    /// </summary>
    public static async Task<(string MediaId, string MediaType, DateTime CreatedAt)> EnsureImageMetadataAsync(string filePath, string? mediaType = null, DateTime? createdAt = null)
    {
        var (existingId, existingType, existingDate) = await GetImageMetadataAsync(filePath);
        if (!string.IsNullOrEmpty(existingId))
        {
            var date = existingDate ?? GetFileCreationTimeUtc(filePath);
            return (existingId, existingType ?? Constants.ResolveMediaType(filePath), date);
        }

        string newId = Guid.NewGuid().ToString();
        string resolvedType = mediaType ?? Constants.ResolveMediaType(filePath);
        DateTime resolvedDate = createdAt ?? GetFileCreationTimeUtc(filePath);
        await AppendMediaMetadataAsync(filePath, newId, resolvedType, resolvedDate);
        return (newId, resolvedType, resolvedDate);
    }

    private static readonly byte[] s_metadataPrefixBytes = Encoding.UTF8.GetBytes(Constants.MetadataBlockStart);

    /// <summary>
    /// Lee sincrónicamente los últimos bytes del archivo para extraer el bloque de metadatos Qapptia.
    /// </summary>
    public static (string? MediaId, string? MediaType, DateTime? CreatedAt) GetImageMetadata(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return (null, null, null);

            int bytesToRead = (int)Math.Min(Constants.MetadataBufferSize, fs.Length);
            fs.Seek(-bytesToRead, SeekOrigin.End);

            var buffer = new byte[bytesToRead];
            int bytesRead = fs.Read(buffer, 0, bytesToRead);
            if (bytesRead == 0 || buffer.AsSpan(0, bytesRead).IndexOf(s_metadataPrefixBytes) < 0)
                return (null, null, null);

            string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            string? mediaId = ExtractTagValue(content, Constants.MetadataMediaIdStart, Constants.MetadataMediaIdEnd);
            string? mediaType = ExtractTagValue(content, Constants.MetadataMediaTypeStart, Constants.MetadataMediaTypeEnd);
            string? createdAtStr = ExtractTagValue(content, Constants.MetadataCreatedAtStart, Constants.MetadataCreatedAtEnd);

            DateTime? createdAt = ParseDateTime(createdAtStr);
            return (mediaId, mediaType, createdAt);
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Lee asincrónicamente los últimos bytes del archivo para extraer el bloque de metadatos Qapptia.
    /// </summary>
    public static async Task<(string? MediaId, string? MediaType, DateTime? CreatedAt)> GetImageMetadataAsync(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return (null, null, null);

            int bytesToRead = (int)Math.Min(Constants.MetadataBufferSize, fs.Length);
            fs.Seek(-bytesToRead, SeekOrigin.End);

            var buffer = new byte[bytesToRead];
            int bytesRead = await fs.ReadAsync(buffer.AsMemory(0, bytesToRead));
            if (bytesRead == 0 || buffer.AsSpan(0, bytesRead).IndexOf(s_metadataPrefixBytes) < 0)
                return (null, null, null);

            string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            string? mediaId = ExtractTagValue(content, Constants.MetadataMediaIdStart, Constants.MetadataMediaIdEnd);
            string? mediaType = ExtractTagValue(content, Constants.MetadataMediaTypeStart, Constants.MetadataMediaTypeEnd);
            string? createdAtStr = ExtractTagValue(content, Constants.MetadataCreatedAtStart, Constants.MetadataCreatedAtEnd);

            DateTime? createdAt = ParseDateTime(createdAtStr);
            return (mediaId, mediaType, createdAt);
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Anexa sincrónicamente el bloque de metadatos estandarizado al final del archivo de imagen.
    /// </summary>
    public static void AppendMediaMetadata(string filePath, string mediaId, string mediaType, DateTime? createdAt = null)
    {
        if (!File.Exists(filePath) || string.IsNullOrEmpty(mediaId) || string.IsNullOrEmpty(mediaType)) return;

        try
        {
            var originalCreation = File.GetCreationTimeUtc(filePath);
            var originalWrite = File.GetLastWriteTimeUtc(filePath);

            using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                string payload = BuildPayload(mediaId, mediaType, createdAt ?? DateTime.UtcNow);
                var bytes = Encoding.UTF8.GetBytes(payload);
                fs.Write(bytes);
            }

            File.SetCreationTimeUtc(filePath, originalCreation);
            File.SetLastWriteTimeUtc(filePath, originalWrite);
        }
        catch
        {
            // Fallar silenciosamente si no hay permisos de escritura
        }
    }

    /// <summary>
    /// Anexa asincrónicamente el bloque de metadatos estandarizado al final del archivo de imagen.
    /// </summary>
    public static async Task AppendMediaMetadataAsync(string filePath, string mediaId, string mediaType, DateTime? createdAt = null)
    {
        if (!File.Exists(filePath) || string.IsNullOrEmpty(mediaId) || string.IsNullOrEmpty(mediaType)) return;

        try
        {
            var originalCreation = File.GetCreationTimeUtc(filePath);
            var originalWrite = File.GetLastWriteTimeUtc(filePath);

            using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                string payload = BuildPayload(mediaId, mediaType, createdAt ?? DateTime.UtcNow);
                var bytes = Encoding.UTF8.GetBytes(payload);
                await fs.WriteAsync(bytes.AsMemory());
            }

            File.SetCreationTimeUtc(filePath, originalCreation);
            File.SetLastWriteTimeUtc(filePath, originalWrite);
        }
        catch
        {
            // Fallar silenciosamente si no hay permisos de escritura
        }
    }

    private static string BuildPayload(string mediaId, string mediaType, DateTime createdAt)
    {
        string dateStr = createdAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return $"{Constants.MetadataBlockStart}" +
               $"{Constants.MetadataMediaIdStart}{mediaId}{Constants.MetadataMediaIdEnd}" +
               $"{Constants.MetadataMediaTypeStart}{mediaType}{Constants.MetadataMediaTypeEnd}" +
               $"{Constants.MetadataCreatedAtStart}{dateStr}{Constants.MetadataCreatedAtEnd}" +
               $"{Constants.MetadataBlockEnd}";
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDt) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsedDt))
        {
            return parsedDt.ToUniversalTime();
        }
        return null;
    }

    /// <summary>
    /// Obtiene de forma segura la fecha de creación UTC de un archivo sin abrir streams de disco.
    /// </summary>
    public static DateTime GetFileCreationTimeUtc(FileInfo fileInfo)
    {
        try
        {
            return fileInfo.CreationTimeUtc;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Obtiene de forma segura la fecha de creación UTC de un archivo a partir de su ruta.
    /// </summary>
    public static DateTime GetFileCreationTimeUtc(string filePath)
    {
        try
        {
            return File.Exists(filePath) ? File.GetCreationTimeUtc(filePath) : DateTime.UtcNow;
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static string? ExtractTagValue(string content, string startTag, string endTag)
    {
        int startIndex = content.LastIndexOf(startTag, StringComparison.Ordinal);
        if (startIndex < 0) return null;

        int valueStart = startIndex + startTag.Length;
        int endIndex = content.IndexOf(endTag, valueStart, StringComparison.Ordinal);
        if (endIndex <= valueStart) return null;

        return content[valueStart..endIndex];
    }
}
