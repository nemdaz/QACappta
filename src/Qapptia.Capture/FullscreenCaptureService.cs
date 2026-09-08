using System.Diagnostics;
using System.Runtime.InteropServices;
using Qapptia.Core.Abstractions;
using Qapptia.Core.Capture;
using Qapptia.Core.Configuration;
using Serilog;
using SkiaSharp;

namespace Qapptia.Capture;

public sealed class FullscreenCaptureService : IFullscreenCaptureService
{
    private readonly IScreenCapture _screenCapture;
    private readonly ICursorCapture _cursorCapture;
    private readonly IClipboardService _clipboard;
    private readonly IDesktopService _desktop;
    private readonly IConfigService _config;
    private readonly ILogger _logger;

    public FullscreenCaptureService(
        IScreenCapture screenCapture,
        ICursorCapture cursorCapture,
        IClipboardService clipboard,
        IDesktopService desktop,
        IConfigService config,
        ILogger logger)
    {
        _screenCapture = screenCapture;
        _cursorCapture = cursorCapture;
        _clipboard = clipboard;
        _desktop = desktop;
        _config = config;
        _logger = logger;
    }

    public async Task<CaptureResult> CaptureAsync(CaptureJob job, CancellationToken ct = default)
    {
        if (job.DelayMs > 0) await Task.Delay(job.DelayMs, ct);

        var screen = await _screenCapture.CaptureScreenAsync(_config.Current.CaptureAllScreens, ct);

        using var image = SKBitmap.FromImage(SKImage.FromPixels(
            new SKImageInfo(screen.Width, screen.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            new IntPtr(Marshal.UnsafeAddrOfPinnedArrayElement(screen.BgraPixels, 0))));

        if (job.IncludeCursor && _config.Current.ShowMouse)
        {
            OverlayCursor(image, screen.OriginX, screen.OriginY);
        }

        using var fullPng = image.Encode(SKEncodedImageFormat.Png, 100);
        return await FinalizeAsync(fullPng.ToArray(), screen.Width, screen.Height, job, ct);
    }

    public async Task<ScreenCaptureResult> CaptureFrozenScreenAsync(bool includeCursor, CancellationToken ct = default)
    {
        var screen = await _screenCapture.CaptureScreenAsync(_config.Current.CaptureAllScreens, ct);

        if (includeCursor && _config.Current.ShowMouse)
        {
            using var bitmap = new SKBitmap();
            bitmap.InstallPixels(
                new SKImageInfo(screen.Width, screen.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                Marshal.UnsafeAddrOfPinnedArrayElement(screen.BgraPixels, 0),
                screen.Width * 4);
            OverlayCursor(bitmap, screen.OriginX, screen.OriginY);
        }

        return screen;
    }

    public async Task<CaptureResult> FinalizeFrozenAreaCaptureAsync(ScreenCaptureResult frozenScreen, AreaInfo area, CaptureJob job, CancellationToken ct = default)
    {
        using var fullBitmap = new SKBitmap();
        fullBitmap.InstallPixels(
            new SKImageInfo(frozenScreen.Width, frozenScreen.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
            Marshal.UnsafeAddrOfPinnedArrayElement(frozenScreen.BgraPixels, 0),
            frozenScreen.Width * 4);

        using var cropped = new SKBitmap(area.Width, area.Height);
        var sourceRect = new SKRectI(area.X - frozenScreen.OriginX, area.Y - frozenScreen.OriginY, area.X - frozenScreen.OriginX + area.Width, area.Y - frozenScreen.OriginY + area.Height);
        fullBitmap.ExtractSubset(cropped, sourceRect);

        using var pngData = cropped.Encode(SKEncodedImageFormat.Png, 100);
        return await FinalizeAsync(pngData.ToArray(), cropped.Width, cropped.Height, job, ct);
    }

    private void OverlayCursor(SKBitmap bitmap, int originX, int originY)
    {
        try
        {
            var cursor = _cursorCapture.CaptureCursorAsync().GetAwaiter().GetResult();
            if (cursor is null) return;

            using var cursorBmp = new SKBitmap();
            if (!cursorBmp.InstallPixels(
                    new SKImageInfo(cursor.Width, cursor.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul),
                    Marshal.UnsafeAddrOfPinnedArrayElement(cursor.BgraPixels, 0),
                    cursor.Width * 4))
                return;

            var (cursorX, cursorY) = _desktop.GetCursorPosition();
            var drawX = (cursorX - originX) - cursor.HotspotX;
            var drawY = (cursorY - originY) - cursor.HotspotY;

            using var canvas = new SKCanvas(bitmap);
            canvas.DrawBitmap(cursorBmp, drawX, drawY);

            if (_config.Current.HighlightMouse)
            {
                using var highlight = new SKPaint
                {
                    Color = new SKColor(255, 255, 0, 80),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                };
                var radius = Math.Max(cursor.Width, cursor.Height) * 0.6f + 8;
                canvas.DrawCircle(cursorX - originX, cursorY - originY, radius, highlight);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Fallo overlay cursor");
        }
    }

    private async Task<CaptureResult> FinalizeAsync(
        byte[] pngBytes, int w, int h, CaptureJob job, CancellationToken ct)
    {
        var path = BuildFilePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, pngBytes, ct);
        string mediaId = Guid.NewGuid().ToString();
        await Qapptia.Core.Services.ImageMetadataService.AppendMediaMetadataAsync(path, mediaId, Qapptia.Core.Constants.MediaTypePng, DateTime.UtcNow);

        if (job.Mode == CaptureMode.Fullscreen && _config.Current.CopyToClipboardScreen ||
            job.Mode == CaptureMode.Area && _config.Current.CopyToClipboardArea)
        {
            try
            { await _clipboard.SetImageAsync(pngBytes, ct); }
            catch (Exception ex) { _logger.Warning(ex, "Fallo clipboard"); }
        }

        return new CaptureResult(path, pngBytes, w, h);
    }

    private string BuildFilePath()
    {
        var cfg = _config.Current;
        string baseDir = string.IsNullOrWhiteSpace(cfg.SavePath)
            ? Qapptia.Core.Constants.DefaultSavePath
            : cfg.SavePath;

        var now = DateTime.Now;
        var parts = new List<string> { baseDir };

        if (cfg.SubfolderMonth) parts.Add(now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        if (cfg.SubfolderDay) parts.Add(now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (cfg.SubfolderHour) parts.Add(now.ToString("HH", System.Globalization.CultureInfo.InvariantCulture));

        var dir = Path.Combine(parts.ToArray());
        Directory.CreateDirectory(dir);

        var fmt = cfg.FilenameFormat
            .Replace("YYYYMMDD", now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("HHmmSS", now.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("HHmm", now.ToString("HHmm", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("YYYY", now.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("MM", now.ToString("MM", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("DD", now.ToString("dd", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("HH", now.ToString("HH", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("mm", now.ToString("mm", System.Globalization.CultureInfo.InvariantCulture))
            .Replace("SS", now.ToString("ss", System.Globalization.CultureInfo.InvariantCulture));

        var filename = $"{fmt}.png";
        var fullPath = Path.Combine(dir, filename);
        var counter = 1;
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(dir, $"{fmt}_{counter++}.png");
        }

        return fullPath;
    }
}
