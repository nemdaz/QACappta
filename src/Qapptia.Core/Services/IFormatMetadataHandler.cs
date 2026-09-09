using System;
using System.IO;

namespace Qapptia.Core.Services;

/// <summary>
/// Contrato para la lectura e inyección de metadatos XMP en formatos específicos de imagen.
/// </summary>
public interface IFormatMetadataHandler
{
    /// <summary>
    /// Determina si el handler puede procesar la imagen a partir de los bytes iniciales de cabecera.
    /// </summary>
    bool CanHandle(ReadOnlySpan<byte> header);

    /// <summary>
    /// Lee en streaming el paquete XMP del stream sin decodificar píxeles.
    /// </summary>
    string? ReadXmp(Stream stream);

    /// <summary>
    /// Inyecta el paquete XMP en el flujo de destino a partir del flujo de origen.
    /// </summary>
    void InjectXmp(Stream sourceStream, Stream destinationStream, string xmpXml);

    /// <summary>
    /// Inyecta el paquete XMP directamente sobre un arreglo de bytes en memoria.
    /// </summary>
    byte[] InjectXmp(byte[] imageBytes, string xmpXml)
    {
        using var src = new MemoryStream(imageBytes, false);
        using var dst = new MemoryStream(imageBytes.Length + xmpXml.Length + 64);
        InjectXmp(src, dst, xmpXml);
        return dst.ToArray();
    }

    /// <summary>
    /// Copia una cantidad determinada de bytes entre flujos mediante un buffer en stack sin asignaciones en heap.
    /// </summary>
    internal static void CopyBytes(Stream source, Stream destination, int count)
    {
        Span<byte> buffer = stackalloc byte[Math.Min(count, 4096)];
        int remaining = count;
        while (remaining > 0)
        {
            int toRead = Math.Min(remaining, buffer.Length);
            int read = source.Read(buffer[..toRead]);
            if (read == 0) break;
            destination.Write(buffer[..read]);
            remaining -= read;
        }
    }
}
