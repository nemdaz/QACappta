using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Qapptia.App.Editor.Converters;

/// <summary>
/// Resuelve un StreamGeometry de los recursos de la aplicación a partir de su clave de texto.
/// </summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public static readonly ResourceKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrWhiteSpace(key) && Application.Current != null)
        {
            if (Application.Current.TryGetResource(key, null, out var resource) && resource is Geometry geometry)
            {
                return geometry;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Convierte un indicador booleano de elemento atenuado en valor de opacidad visual.
/// </summary>
public sealed class BoolToDimmedOpacityConverter : IValueConverter
{
    public static readonly BoolToDimmedOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDimmed && isDimmed)
        {
            return 0.55;
        }
        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
