using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Qapptia.Editor.Core;

public static class Constants
{
    // Metrics
    public const double DefaultStrokeWidth = 4.0;
    public const double ArrowWingLen = 25.0;
    public const double GripSize = 6.0;
    public const double DrawMinDistance = 8.0;
    public const byte HighlighterAlpha = 102; // 40% opacity

    // Navegación y sistema de archivos (filtros internos y ocultos)
    public const char HiddenPrefixChar = '.';
    public const string HiddenPrefix = ".";

    // Herramienta de recorte (Crop)
    public const double CropMinSize = 10.0; // Tamaño mínimo del rect de recorte (ancho/alto)

    // Herramienta de texto (Anotaciones - Monomotor Skia)
    public const string DefaultFontFileName = "Roboto-Regular.ttf";
    public const float TextToolMinFontSize = 8f; // Tamaño mínimo de fuente
    public const float TextToolMaxFontSize = 200f; // Tamaño máximo de fuente
    public const float TextToolDefaultFontSize = 24f; // Tamaño inicial predeterminado
    public const double TextToolOffset = 5.0; // Margen interno de texto
    public const double TextToolMinWidth = 100.0; // Ancho mínimo permitido para la caja de texto
    public const double TextToolDefaultWidth = 300.0; // Ancho inicial predeterminado
    public const double TextToolUsableWidth = TextToolDefaultWidth - (TextToolOffset * 2); // 290.0 px de área de texto base

    // Contornos de texto Skia (8 direcciones 360°)
    public static readonly SkiaSharp.SKColor TextToolDarkContourSKColor = new(0, 0, 0, 90);
    public static readonly SkiaSharp.SKColor TextToolLightContourSKColor = new(255, 255, 255, 72);
    public static readonly SkiaSharp.SKColor TextToolBorderSKColor = new(136, 136, 136, 180);
    public static readonly SkiaSharp.SKColor TextToolSelectionSKColor = new(0, 120, 215, 110); // Azul traslúcido de selección
    public static readonly SkiaSharp.SKColor TextToolCaretBlack = new(0, 0, 0, 255);
    public static readonly SkiaSharp.SKColor TextToolCaretWhite = new(255, 255, 255, 255);

    // Shadows
    public static SkiaSharp.SKImageFilter CreateEditingShadow() =>
        SkiaSharp.SKImageFilter.CreateDropShadow(0, 1, 2, 2, SkiaSharp.SKColors.Black.WithAlpha(120));

    public static SkiaSharp.SKImageFilter CreateBurnedShadow() =>
        SkiaSharp.SKImageFilter.CreateDropShadow(0, 1, 2, 2, SkiaSharp.SKColors.Black.WithAlpha(40));

    // Palette Colors matches Legacy App
    public static readonly IReadOnlyList<Color> FavoriteColors = new[]
    {
        Color.Parse("#00FF00"), // Green
        Color.Parse("#FF0000"), // Red
        Color.Parse("#0078D7"), // Blue
        Color.Parse("#00B7C3"), // Cyan
        Color.Parse("#F7EB0C"), // Yellow
        Color.Parse("#FFA500"), // Orange
        Color.Parse("#FFFFFF"), // White
        Color.Parse("#000000")  // Black
    };

    // Convierte el color de Avalonia a SkiaSharp
    public static SkiaSharp.SKColor ToSKColor(this Color c)
    {
        return new SkiaSharp.SKColor(c.R, c.G, c.B, c.A);
    }

    // GetColorFamily was removed per user request for simpler persistence

    public static string GetColorName(Color c)
    {
        return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static Color ParseColorName(string name)
    {
        if (string.IsNullOrEmpty(name)) return FavoriteColors[1]; // Red fallback

        if (Color.TryParse(name, out var parsedHex))
        {
            return parsedHex;
        }

        return FavoriteColors[1]; // Default fallback to Red
    }
}
