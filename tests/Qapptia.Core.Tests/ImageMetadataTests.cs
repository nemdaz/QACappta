using System;
using System.IO;
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

    [Fact]
    public async Task EnsureImageMetadataInjectsMediaIdAndMediaType()
    {
        var filePath = Path.Combine(_testDir, "sample.png");
        await File.WriteAllBytesAsync(filePath, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); // PNG header

        var (mediaId, mediaType, createdAt) = await ImageMetadataService.EnsureImageMetadataAsync(filePath);

        mediaId.Should().NotBeNullOrWhiteSpace();
        mediaType.Should().Be("image/png");
        createdAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // Leer sincrónicamente
        var (readId, readType, readDate) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(mediaId);
        readType.Should().Be("image/png");
        readDate.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));

        // Leer asincrónicamente
        var (asyncId, asyncType, asyncDate) = await ImageMetadataService.GetImageMetadataAsync(filePath);
        asyncId.Should().Be(mediaId);
        asyncType.Should().Be("image/png");
        asyncDate.Should().BeCloseTo(createdAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EnsureImageMetadataDetectsJpegMimeType()
    {
        var filePath = Path.Combine(_testDir, "photo.jpg");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0xFF, 0xD8, 0xFF });

        var (mediaId, mediaType, _) = await ImageMetadataService.EnsureImageMetadataAsync(filePath);

        mediaId.Should().NotBeNullOrWhiteSpace();
        mediaType.Should().Be("image/jpeg");

        var (readId, readType, _) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(mediaId);
        readType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task AppendMediaMetadataPreservesExplicitCreatedAtTimestamp()
    {
        var filePath = Path.Combine(_testDir, "timestamp_test.png");
        await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3 });

        var explicitDate = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        string id = Guid.NewGuid().ToString();

        await ImageMetadataService.AppendMediaMetadataAsync(filePath, id, Constants.MediaTypePng, explicitDate);

        var (readId, _, readDate) = ImageMetadataService.GetImageMetadata(filePath);
        readId.Should().Be(id);
        readDate.Should().Be(explicitDate);
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
        await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3, 4 });

        string testMediaId = Guid.NewGuid().ToString();
        string backupPath = await ImageBurnService.CreateCompressedBackupAsync(filePath, testMediaId);

        File.Exists(backupPath).Should().BeTrue();
        backupPath.Should().Contain(testMediaId);
        backupPath.Should().Contain(Constants.DrawingExtension);
    }

    [Fact]
    public void EnsureImageMetadataUsesCreationTimeFallbackWhenNoMetadata()
    {
        var filePath = Path.Combine(_testDir, "legacy_file.png");
        File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var expectedCreationDate = new DateTime(2025, 12, 16, 10, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(filePath, expectedCreationDate);

        var (mediaId, _, createdAt) = ImageMetadataService.EnsureImageMetadata(filePath);

        mediaId.Should().NotBeNullOrEmpty();
        createdAt.Should().Be(expectedCreationDate);
    }

    [Fact]
    public void GetFileCreationTimeUtcReturnsSafeCreationDate()
    {
        var filePath = Path.Combine(_testDir, "creation_util_test.png");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

        var expectedCreationDate = new DateTime(2025, 11, 20, 8, 30, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(filePath, expectedCreationDate);

        var fileInfo = new FileInfo(filePath);
        ImageMetadataService.GetFileCreationTimeUtc(fileInfo).Should().Be(expectedCreationDate);
        ImageMetadataService.GetFileCreationTimeUtc(filePath).Should().Be(expectedCreationDate);
    }

    [Fact]
    public void AppendMediaMetadataPreservesOriginalFilesystemTimestamps()
    {
        var filePath = Path.Combine(_testDir, "preserve_timestamps.png");
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });

        var creation = new DateTime(2025, 5, 10, 8, 0, 0, DateTimeKind.Utc);
        var write = new DateTime(2025, 5, 12, 14, 30, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(filePath, creation);
        File.SetLastWriteTimeUtc(filePath, write);

        ImageMetadataService.AppendMediaMetadata(filePath, "test-id", "image/png");

        File.GetCreationTimeUtc(filePath).Should().Be(creation);
        File.GetLastWriteTimeUtc(filePath).Should().Be(write);
    }
}
