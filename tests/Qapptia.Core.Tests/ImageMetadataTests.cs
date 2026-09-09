using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Qapptia.Core;
using Qapptia.Core.Services;
using Xunit;

namespace Qapptia.Core.Tests;

public sealed class ImageMetadataTests : IDisposable
{
    private readonly string _testDir;

    public ImageMetadataTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_MetaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private static byte[] CreateMinimalValidPng()
    {
        using var ms = new MemoryStream();
        // PNG Signature (8 bytes)
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // Chunk IHDR (13 bytes data)
        byte[] ihdrData = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00 };
        WriteChunk(ms, "IHDR", ihdrData);

        // Chunk IEND (0 bytes data)
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    private static byte[] CreateMinimalValidJpeg()
    {
        using var ms = new MemoryStream();
        // SOI (0xFF, 0xD8)
        ms.Write(new byte[] { 0xFF, 0xD8 });

        // APP0 (JFIF)
        byte[] app0Data = Encoding.ASCII.GetBytes("JFIF\0\x01\x01\0\0\x01\0\x01\0\0");
        ms.Write(new byte[] { 0xFF, 0xE0 });
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(app0Data.Length + 2));
        ms.Write(len);
        ms.Write(app0Data);

        // EOI (0xFF, 0xD9)
        ms.Write(new byte[] { 0xFF, 0xD9 });

        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lenBytes, (uint)data.Length);
        stream.Write(lenBytes);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);

        if (data.Length > 0)
        {
            stream.Write(data);
        }

        // CRC32 sobre Type + Data
        byte[] toCrc = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, toCrc, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, toCrc, typeBytes.Length, data.Length);

        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < toCrc.Length; i++)
        {
            crc ^= toCrc[i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        crc ^= 0xFFFFFFFF;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    [Fact]
    public async Task EnsureImageMetadataInjectsXmpInPngAndReadsSynchronouslyAndAsynchronously()
    {
        var filePath = Path.Combine(_testDir, "sample.png");
        await File.WriteAllBytesAsync(filePath, CreateMinimalValidPng());

        var (mediaId, mediaType, createdAt) = await ImageMetadataService.EnsureImageMetadataAsync(filePath);

        mediaId.Should().NotBeNullOrWhiteSpace();
        mediaType.Should().Be("image/png");
        createdAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Lectura sincrónica (cascada -> XMP nativo)
        var (readId, readType, readDate) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(mediaId);
        readType.Should().Be("image/png");
        readDate.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));

        // Lectura asincrónica
        var (asyncId, asyncType, asyncDate) = await ImageMetadataService.GetImageMetadataAsync(filePath);
        asyncId.Should().Be(mediaId);
        asyncType.Should().Be("image/png");
        asyncDate.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EnsureImageMetadataInjectsXmpInJpeg()
    {
        var filePath = Path.Combine(_testDir, "photo.jpg");
        await File.WriteAllBytesAsync(filePath, CreateMinimalValidJpeg());

        var (mediaId, mediaType, _) = await ImageMetadataService.EnsureImageMetadataAsync(filePath);

        mediaId.Should().NotBeNullOrWhiteSpace();
        mediaType.Should().Be("image/jpeg");

        var (readId, readType, _) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(mediaId);
        readType.Should().Be("image/jpeg");
    }

    [Fact]
    public void ExternalEditorTruncationSimulatedPreservesXmpMetadata()
    {
        // Simular archivo con metadatos XMP en la cabecera
        var filePath = Path.Combine(_testDir, "paint_saved.png");
        byte[] originalPng = CreateMinimalValidPng();
        string testId = Guid.NewGuid().ToString("N");
        DateTime testDate = new DateTime(2026, 9, 8, 15, 30, 0, DateTimeKind.Utc);

        byte[] withXmp = ImageMetadataService.InjectMetadata(originalPng, testId, Constants.MediaTypePng, testDate);
        File.WriteAllBytes(filePath, withXmp);

        // Simular que Paint abre el archivo, lo reescribe hasta IEND y descarta cualquier cosa posterior
        var (readId, readType, readDate) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(testId);
        readType.Should().Be(Constants.MediaTypePng);
        readDate.Should().BeCloseTo(testDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ExternalImageWithoutMetadataReturnsNullAndFallsBackToCreationDate()
    {
        var filePath = Path.Combine(_testDir, "external_image.png");
        byte[] validPng = CreateMinimalValidPng();
        File.WriteAllBytes(filePath, validPng);

        var expectedCreation = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(filePath, expectedCreation);

        // Al no tener XMP de Qapptia, GetImageMetadata debe retornar null
        var (mediaId, mediaType, createdAt) = ImageMetadataService.GetImageMetadata(filePath);
        mediaId.Should().BeNull();
        mediaType.Should().BeNull();
        createdAt.Should().BeNull();

        // EnsureImageMetadata debe inyectarle XMP y mantener la fecha del archivo
        var (ensuredId, ensuredType, ensuredDate) = ImageMetadataService.EnsureImageMetadata(filePath);
        ensuredId.Should().NotBeNullOrWhiteSpace();
        ensuredType.Should().Be("image/png");
        ensuredDate.Should().Be(expectedCreation);

        // Ahora GetImageMetadata debe leerlo con éxito
        var (readId, readType, readDate) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(ensuredId);
        readType.Should().Be("image/png");
        readDate.Should().Be(expectedCreation);
    }

    [Fact]
    public void PngAndJpegHandlersDirectlyCanBeExercised()
    {
        var pngHandler = new PngMetadataHandler();
        var jpegHandler = new JpegMetadataHandler();

        byte[] png = CreateMinimalValidPng();
        byte[] jpeg = CreateMinimalValidJpeg();

        pngHandler.CanHandle(png).Should().BeTrue();
        pngHandler.CanHandle(jpeg).Should().BeFalse();

        jpegHandler.CanHandle(jpeg).Should().BeTrue();
        jpegHandler.CanHandle(png).Should().BeFalse();
    }

    [Fact]
    public void InjectMetadataInMemoryReturnsValidPngWithXmp()
    {
        byte[] png = CreateMinimalValidPng();
        string testId = Guid.NewGuid().ToString();
        DateTime testDate = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        byte[] withMeta = ImageMetadataService.InjectMetadata(png, testId, Constants.MediaTypePng, testDate);

        withMeta.Length.Should().BeGreaterThan(png.Length);

        // Guardar temporalmente y verificar extracción
        string tempPath = Path.Combine(_testDir, "memory_injected.png");
        File.WriteAllBytes(tempPath, withMeta);

        var (readId, readType, readDate) = ImageMetadataService.GetImageMetadata(tempPath);
        readId.Should().Be(testId);
        readType.Should().Be(Constants.MediaTypePng);
        readDate.Should().BeCloseTo(testDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void XmpMetadataHelperSanitizesUuidPrefix()
    {
        string rawXmp =
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
            " <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
            "  <rdf:Description rdf:about=\"\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\">\n" +
            "   <xmpMM:DocumentID>uuid:12345678-abcd-1234-abcd-1234567890ab</xmpMM:DocumentID>\n" +
            "   <dc:format xmlns:dc=\"http://purl.org/dc/elements/1.1/\">image/png</dc:format>\n" +
            "  </rdf:Description>\n" +
            " </rdf:RDF>\n" +
            "</x:xmpmeta>";

        var (id, type, _) = XmpMetadataHelper.ParseXmpPacket(rawXmp);
        id.Should().Be("12345678-abcd-1234-abcd-1234567890ab");
        type.Should().Be("image/png");
    }

    [Fact]
    public void CorruptFileReturnsNullWithoutCrashing()
    {
        var corruptPath = Path.Combine(_testDir, "corrupt.png");
        File.WriteAllBytes(corruptPath, new byte[] { 0x01, 0x02, 0x03, 0x04 }); // No es un PNG válido

        var (id, type, date) = ImageMetadataService.GetImageMetadata(corruptPath);
        id.Should().BeNull();
        type.Should().BeNull();
        date.Should().BeNull();
    }

    [Fact]
    public void InjectMetadataPreservesFilesystemTimestamps()
    {
        var filePath = Path.Combine(_testDir, "preserve_timestamps.png");
        File.WriteAllBytes(filePath, CreateMinimalValidPng());

        var creation = new DateTime(2025, 5, 10, 8, 0, 0, DateTimeKind.Utc);
        var write = new DateTime(2025, 5, 12, 14, 30, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(filePath, creation);
        File.SetLastWriteTimeUtc(filePath, write);

        ImageMetadataService.InjectMetadata(filePath, "test-id", "image/png");

        File.GetCreationTimeUtc(filePath).Should().Be(creation);
        File.GetLastWriteTimeUtc(filePath).Should().Be(write);
    }

    [Fact]
    public void ResolveMediaTypeResolvesExpectedMimeTypes()
    {
        Constants.ResolveMediaType("file.png").Should().Be("image/png");
        Constants.ResolveMediaType("file.PNG").Should().Be("image/png");
        Constants.ResolveMediaType("file.jpg").Should().Be("image/jpeg");
        Constants.ResolveMediaType("file.jpeg").Should().Be("image/jpeg");
        Constants.ResolveMediaType("file.unknown").Should().Be("image/png");
    }

    [Fact]
    public async Task ImageBurnServiceCreatesBackupWithMediaId()
    {
        var filePath = Path.Combine(_testDir, "capture_burn.png");
        await File.WriteAllBytesAsync(filePath, CreateMinimalValidPng());

        string testMediaId = Guid.NewGuid().ToString();
        string backupPath = await ImageBurnService.CreateCompressedBackupAsync(filePath, testMediaId);

        File.Exists(backupPath).Should().BeTrue();
        backupPath.Should().Contain(testMediaId);
        backupPath.Should().Contain(Constants.DrawingExtension);
    }
}
