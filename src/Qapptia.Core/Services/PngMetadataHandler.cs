using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Qapptia.Core.Services;

/// <summary>
/// Handler para la lectura e inyección binaria de metadatos XMP en contenedores PNG (ISO/IEC 15948).
/// </summary>
public sealed class PngMetadataHandler : IFormatMetadataHandler
{
    private static readonly byte[] s_pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] s_xmpChunkType = Encoding.ASCII.GetBytes("iTXt");
    private static readonly byte[] s_ihdrChunkType = Encoding.ASCII.GetBytes("IHDR");
    private static readonly byte[] s_idatChunkType = Encoding.ASCII.GetBytes("IDAT");
    private static readonly byte[] s_iendChunkType = Encoding.ASCII.GetBytes("IEND");
    private static readonly byte[] s_xmpKeywordWithNull = Encoding.ASCII.GetBytes(Constants.PngChunkXmpKeyword + "\0");

    // Tabla CRC32 estándar ISO 3309 / PNG
    private static readonly uint[] s_crcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    /// <inheritdoc />
    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        return header.Length >= s_pngSignature.Length && header[..s_pngSignature.Length].SequenceEqual(s_pngSignature);
    }

    /// <inheritdoc />
    public string? ReadXmp(Stream stream)
    {
        if (stream.Length < 8) return null;

        Span<byte> signature = stackalloc byte[8];
        if (stream.Read(signature) < 8 || !CanHandle(signature))
            return null;

        Span<byte> chunkHeader = stackalloc byte[8]; // 4 bytes Length + 4 bytes Type

        while (stream.Position + 12 <= stream.Length)
        {
            if (stream.Read(chunkHeader) < 8) break;

            uint length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            var chunkType = chunkHeader[4..8];

            if (chunkType.SequenceEqual(s_xmpChunkType))
            {
                if (length > 0 && length <= 10 * 1024 * 1024)
                {
                    byte[] data = new byte[length];
                    if (stream.Read(data, 0, (int)length) == length)
                    {
                        stream.Seek(4, SeekOrigin.Current); // Descartar CRC

                        if (data.AsSpan().StartsWith(s_xmpKeywordWithNull))
                        {
                            int textStart = s_xmpKeywordWithNull.Length + 4; // Keyword\0 + Flag + Method + Lang\0 + Trans\0
                            if (textStart < data.Length)
                            {
                                return Encoding.UTF8.GetString(data, textStart, data.Length - textStart);
                            }
                        }
                    }
                }
                break;
            }

            if (chunkType.SequenceEqual(s_idatChunkType) || chunkType.SequenceEqual(s_iendChunkType))
            {
                break;
            }

            stream.Seek(length + 4, SeekOrigin.Current);
        }

        return null;
    }

    /// <inheritdoc />
    public void InjectXmp(Stream sourceStream, Stream destinationStream, string xmpXml)
    {
        Span<byte> signature = stackalloc byte[8];
        if (sourceStream.Read(signature) < 8 || !CanHandle(signature))
            throw new InvalidDataException("El archivo fuente no es una imagen PNG válida.");

        destinationStream.Write(signature);

        byte[] xmpChunkBytes = BuildXmpChunk(xmpXml);
        Span<byte> chunkHeader = stackalloc byte[8];
        bool xmpInjected = false;

        while (sourceStream.Position + 12 <= sourceStream.Length)
        {
            if (sourceStream.Read(chunkHeader) < 8) break;

            uint length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            var chunkType = chunkHeader[4..8];

            if (chunkType.SequenceEqual(s_ihdrChunkType))
            {
                destinationStream.Write(chunkHeader);
                IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, (int)length + 4);

                if (!xmpInjected)
                {
                    destinationStream.Write(xmpChunkBytes);
                    xmpInjected = true;
                }
                continue;
            }

            if (chunkType.SequenceEqual(s_xmpChunkType))
            {
                byte[] checkData = new byte[Math.Min(length, (uint)s_xmpKeywordWithNull.Length)];
                int read = sourceStream.Read(checkData, 0, checkData.Length);
                if (checkData.AsSpan(0, read).SequenceEqual(s_xmpKeywordWithNull))
                {
                    sourceStream.Seek((length - read) + 4, SeekOrigin.Current);
                    continue;
                }

                destinationStream.Write(chunkHeader);
                destinationStream.Write(checkData, 0, read);
                IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, (int)(length - read) + 4);
                continue;
            }

            destinationStream.Write(chunkHeader);
            IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, (int)length + 4);

            if (chunkType.SequenceEqual(s_iendChunkType))
            {
                break;
            }
        }
    }

    private static byte[] BuildXmpChunk(string xmpXml)
    {
        byte[] xmlBytes = Encoding.UTF8.GetBytes(xmpXml);
        int dataLength = s_xmpKeywordWithNull.Length + 4 + xmlBytes.Length;

        byte[] chunk = new byte[4 + 4 + dataLength + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)dataLength);
        Buffer.BlockCopy(s_xmpChunkType, 0, chunk, 4, 4);

        int dataOffset = 8;
        Buffer.BlockCopy(s_xmpKeywordWithNull, 0, chunk, dataOffset, s_xmpKeywordWithNull.Length);
        dataOffset += s_xmpKeywordWithNull.Length;

        chunk[dataOffset++] = 0; // Uncompressed
        chunk[dataOffset++] = 0; // Method
        chunk[dataOffset++] = 0; // Lang null
        chunk[dataOffset++] = 0; // Trans null

        Buffer.BlockCopy(xmlBytes, 0, chunk, dataOffset, xmlBytes.Length);

        uint crc = CalculateCrc(chunk.AsSpan(4, 4 + dataLength));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4, 4), crc);

        return chunk;
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> buffer)
    {
        uint c = 0xFFFFFFFF;
        for (int i = 0; i < buffer.Length; i++)
        {
            c = s_crcTable[(c ^ buffer[i]) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFF;
    }
}
