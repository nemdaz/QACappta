using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Qapptia.Core.Services;

/// <summary>
/// Handler para la lectura e inyección binaria de metadatos XMP en contenedores JPEG (ISO/IEC 10918-1).
/// </summary>
public sealed class JpegMetadataHandler : IFormatMetadataHandler
{
    private static readonly byte[] s_jpegSoi = { 0xFF, 0xD8 };
    private static readonly byte[] s_jpegEoi = { 0xFF, 0xD9 };
    private static readonly byte[] s_xmpHeaderBytes = Encoding.ASCII.GetBytes(Constants.JpegXmpHeader);

    /// <inheritdoc />
    public bool CanHandle(ReadOnlySpan<byte> header)
    {
        return header.Length >= s_jpegSoi.Length && header[0] == s_jpegSoi[0] && header[1] == s_jpegSoi[1];
    }

    /// <inheritdoc />
    public string? ReadXmp(Stream stream)
    {
        if (stream.Length < 4) return null;

        Span<byte> marker = stackalloc byte[2];
        if (stream.Read(marker) < 2 || !CanHandle(marker))
            return null;

        Span<byte> lenBytes = stackalloc byte[2];

        while (stream.Position + 4 <= stream.Length)
        {
            if (stream.Read(marker) < 2) break;
            if (marker[0] != 0xFF) break;

            while (marker[1] == 0xFF)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                marker[1] = (byte)b;
            }

            if (marker[1] is 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7))
            {
                if (marker[1] == 0xD9) break;
                continue;
            }

            if (marker[1] == 0xDA) // Start of Scan (SOS)
            {
                break;
            }

            if (stream.Read(lenBytes) < 2) break;
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);
            if (length < 2) break;

            int payloadLength = length - 2;

            if (marker[1] == 0xE1 && payloadLength > s_xmpHeaderBytes.Length)
            {
                byte[] payload = new byte[payloadLength];
                if (stream.Read(payload, 0, payloadLength) == payloadLength)
                {
                    if (payload.AsSpan().StartsWith(s_xmpHeaderBytes))
                    {
                        int textStart = s_xmpHeaderBytes.Length;
                        return Encoding.UTF8.GetString(payload, textStart, payload.Length - textStart);
                    }
                }
                break;
            }

            stream.Seek(payloadLength, SeekOrigin.Current);
        }

        return null;
    }

    /// <inheritdoc />
    public void InjectXmp(Stream sourceStream, Stream destinationStream, string xmpXml)
    {
        Span<byte> marker = stackalloc byte[2];
        if (sourceStream.Read(marker) < 2 || !CanHandle(marker))
            throw new InvalidDataException("El archivo fuente no es una imagen JPEG válida.");

        destinationStream.Write(s_jpegSoi);

        byte[] xmpSegmentBytes = BuildXmpSegment(xmpXml);
        bool xmpInjected = false;
        Span<byte> lenBytes = stackalloc byte[2];

        while (sourceStream.Position < sourceStream.Length)
        {
            int b1 = sourceStream.ReadByte();
            if (b1 < 0) break;

            if (b1 != 0xFF)
            {
                destinationStream.WriteByte((byte)b1);
                continue;
            }

            int b2 = sourceStream.ReadByte();
            if (b2 < 0)
            {
                destinationStream.WriteByte((byte)b1);
                break;
            }

            while (b2 == 0xFF)
            {
                destinationStream.WriteByte(0xFF);
                b2 = sourceStream.ReadByte();
                if (b2 < 0) break;
            }
            if (b2 < 0) break;

            if (b2 == 0xD9) // EOI
            {
                destinationStream.Write(s_jpegEoi);
                break;
            }

            if (b2 is 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                destinationStream.WriteByte(0xFF);
                destinationStream.WriteByte((byte)b2);
                continue;
            }

            if (sourceStream.Read(lenBytes) < 2) break;
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);
            int payloadLength = length - 2;

            if (b2 == 0xE1 && payloadLength >= s_xmpHeaderBytes.Length)
            {
                byte[] checkHeader = new byte[s_xmpHeaderBytes.Length];
                int read = sourceStream.Read(checkHeader, 0, checkHeader.Length);
                if (read == s_xmpHeaderBytes.Length && checkHeader.AsSpan().SequenceEqual(s_xmpHeaderBytes))
                {
                    sourceStream.Seek(payloadLength - read, SeekOrigin.Current);
                    continue;
                }

                destinationStream.WriteByte(0xFF);
                destinationStream.WriteByte((byte)b2);
                destinationStream.Write(lenBytes);
                destinationStream.Write(checkHeader, 0, read);
                IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, payloadLength - read);
                continue;
            }

            if (b2 == 0xE0) // APP0 (JFIF)
            {
                destinationStream.WriteByte(0xFF);
                destinationStream.WriteByte((byte)b2);
                destinationStream.Write(lenBytes);
                IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, payloadLength);

                if (!xmpInjected)
                {
                    destinationStream.Write(xmpSegmentBytes);
                    xmpInjected = true;
                }
                continue;
            }

            if (!xmpInjected)
            {
                destinationStream.Write(xmpSegmentBytes);
                xmpInjected = true;
            }

            destinationStream.WriteByte(0xFF);
            destinationStream.WriteByte((byte)b2);
            destinationStream.Write(lenBytes);
            IFormatMetadataHandler.CopyBytes(sourceStream, destinationStream, payloadLength);

            if (b2 == 0xDA) // Start of Scan (SOS)
            {
                CopyCompressedDataUntilEoi(sourceStream, destinationStream);
                break;
            }
        }
    }

    private static void CopyCompressedDataUntilEoi(Stream source, Stream destination)
    {
        while (source.Position < source.Length)
        {
            int b = source.ReadByte();
            if (b < 0) break;

            destination.WriteByte((byte)b);

            if (b == 0xFF)
            {
                int next = source.ReadByte();
                if (next < 0) break;

                destination.WriteByte((byte)next);

                if (next == 0xD9)
                {
                    break;
                }
            }
        }
    }

    private static byte[] BuildXmpSegment(string xmpXml)
    {
        byte[] xmlBytes = Encoding.UTF8.GetBytes(xmpXml);
        int payloadLength = s_xmpHeaderBytes.Length + xmlBytes.Length;
        ushort segmentLength = (ushort)(2 + payloadLength);

        byte[] segment = new byte[2 + 2 + payloadLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2, 2), segmentLength);

        Buffer.BlockCopy(s_xmpHeaderBytes, 0, segment, 4, s_xmpHeaderBytes.Length);
        Buffer.BlockCopy(xmlBytes, 0, segment, 4 + s_xmpHeaderBytes.Length, xmlBytes.Length);

        return segment;
    }
}
