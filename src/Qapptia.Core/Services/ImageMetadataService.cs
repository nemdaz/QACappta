using System;
using System.IO;
using System.Threading.Tasks;

namespace Qapptia.Core.Services;

/// <summary>
/// Servicio responsable de la inyección y extracción agnóstica de metadatos estandarizados XMP (ISO 16684-1).
/// </summary>
public static class ImageMetadataService
{
    private static readonly IFormatMetadataHandler[] s_handlers =
    {
        new PngMetadataHandler(),
        new JpegMetadataHandler()
    };

    private static IFormatMetadataHandler? ResolveHandler(ReadOnlySpan<byte> header)
    {
        foreach (var handler in s_handlers)
        {
            if (handler.CanHandle(header))
            {
                return handler;
            }
        }
        return null;
    }

    /// <summary>
    /// Obtiene sincrónicamente los metadatos de la imagen. Si no existen, genera un nuevo ID, los inyecta y los retorna.
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
        InjectMetadata(filePath, newId, resolvedType, resolvedDate);
        return (newId, resolvedType, resolvedDate);
    }

    /// <summary>
    /// Obtiene asincrónicamente los metadatos de la imagen. Si no existen, genera un nuevo ID, los inyecta y los retorna.
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
        await InjectMetadataAsync(filePath, newId, resolvedType, resolvedDate);
        return (newId, resolvedType, resolvedDate);
    }

    /// <summary>
    /// Lee sincrónicamente los metadatos XMP de la imagen sin decodificar píxeles.
    /// </summary>
    public static (string? MediaId, string? MediaType, DateTime? CreatedAt) GetImageMetadata(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return (null, null, null);

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 4) return (null, null, null);

            Span<byte> header = stackalloc byte[8];
            int read = fs.Read(header);
            fs.Position = 0;

            var handler = ResolveHandler(header[..read]);
            if (handler == null) return (null, null, null);

            string? xmpXml = handler.ReadXmp(fs);
            if (!string.IsNullOrEmpty(xmpXml))
            {
                var (mediaId, mediaType, createdAt) = XmpMetadataHelper.ParseXmpPacket(xmpXml);
                if (!string.IsNullOrEmpty(mediaId))
                {
                    return (mediaId, mediaType ?? Constants.ResolveMediaType(filePath), createdAt);
                }
            }

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Lee asincrónicamente los metadatos XMP de la imagen sin decodificar píxeles.
    /// </summary>
    public static async Task<(string? MediaId, string? MediaType, DateTime? CreatedAt)> GetImageMetadataAsync(string filePath)
    {
        return await Task.Run(() => GetImageMetadata(filePath));
    }

    /// <summary>
    /// Inyecta sincrónicamente los metadatos XMP en la imagen de forma atómica mediante el handler de formato correspondiente.
    /// </summary>
    public static void InjectMetadata(string filePath, string mediaId, string mediaType, DateTime? createdAt = null)
    {
        if (!File.Exists(filePath) || string.IsNullOrEmpty(mediaId) || string.IsNullOrEmpty(mediaType)) return;

        try
        {
            var originalCreation = File.GetCreationTimeUtc(filePath);
            var originalWrite = File.GetLastWriteTimeUtc(filePath);
            DateTime resolvedDate = createdAt ?? originalCreation;

            string xmpPayload = XmpMetadataHelper.BuildXmpPacket(mediaId, mediaType, resolvedDate);
            string tempFilePath = $"{filePath}.tmp.{Guid.NewGuid():N}";

            bool injected = false;

            using (var src = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Span<byte> header = stackalloc byte[8];
                int read = src.Read(header);
                src.Position = 0;

                var handler = ResolveHandler(header[..read]);
                if (handler != null)
                {
                    handler.InjectXmp(src, dst, xmpPayload);
                    injected = true;
                }
            }

            if (injected)
            {
                File.Move(tempFilePath, filePath, overwrite: true);
                File.SetCreationTimeUtc(filePath, originalCreation);
                File.SetLastWriteTimeUtc(filePath, originalWrite);
            }
            else if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
            // Fallar de forma segura sin interrumpir la operación
        }
    }

    /// <summary>
    /// Inyecta asincrónicamente los metadatos XMP en la imagen de forma atómica mediante el handler de formato correspondiente.
    /// </summary>
    public static async Task InjectMetadataAsync(string filePath, string mediaId, string mediaType, DateTime? createdAt = null)
    {
        await Task.Run(() => InjectMetadata(filePath, mediaId, mediaType, createdAt));
    }

    /// <summary>
    /// Inyecta metadatos XMP directamente sobre un arreglo de bytes en memoria utilizando el handler adecuado.
    /// </summary>
    public static byte[] InjectMetadata(byte[] imageBytes, string mediaId, string mediaType, DateTime? createdAt = null)
    {
        if (imageBytes == null || imageBytes.Length < 4) return imageBytes ?? Array.Empty<byte>();

        try
        {
            var handler = ResolveHandler(imageBytes.AsSpan(0, Math.Min(imageBytes.Length, 8)));
            if (handler != null)
            {
                string xmpPayload = XmpMetadataHelper.BuildXmpPacket(mediaId, mediaType, createdAt ?? DateTime.UtcNow);
                return handler.InjectXmp(imageBytes, xmpPayload);
            }
        }
        catch
        {
            // Retornar buffer original en caso de error
        }

        return imageBytes;
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
}
