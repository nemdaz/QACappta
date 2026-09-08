using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Qapptia.Editor.Models;
using Qapptia.Editor.Models.Geometry;
using Serilog;

namespace Qapptia.Editor.Services;

/// <summary>
/// Servicio de persistencia y serialización no destructiva del estado integral del lienzo en formato JSON.
/// </summary>
public sealed class CanvasStateService : ICanvasStateService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger? _logger;

    public CanvasStateService(ILogger? logger = null)
    {
        _logger = logger;
    }

    public string? GetJsonPath(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;

        string parentDir = Path.GetDirectoryName(imagePath) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(imagePath);
        string annotationDir = Path.Combine(parentDir, Qapptia.Core.Constants.DrawingExtension);
        return Path.Combine(annotationDir, $"{baseName}{Qapptia.Core.Constants.JsonFileExtension}");
    }

    public CanvasState Load(string imagePath) => Load(imagePath, null);

    public CanvasState Load(string imagePath, string? mediaId)
    {
        string? targetPath = GetJsonPath(imagePath);
        if (string.IsNullOrEmpty(targetPath)) return new CanvasState();

        // 1. Apertura nominal directa O(1)
        if (File.Exists(targetPath))
        {
            return DeserializeState(targetPath);
        }

        // 2. Si no existe, obtener mediaId de la imagen si no fue provisto
        if (string.IsNullOrEmpty(mediaId))
        {
            var (extractedId, _, _) = Qapptia.Core.Services.ImageMetadataService.GetImageMetadata(imagePath);
            mediaId = extractedId;
        }

        // 3. Si tenemos mediaId, buscar en el directorio de persistencia si hay un JSON huérfano con ese mediaId
        if (!string.IsNullOrEmpty(mediaId))
        {
            string parentDir = Path.GetDirectoryName(imagePath) ?? string.Empty;
            string annotationDir = Path.Combine(parentDir, Qapptia.Core.Constants.DrawingExtension);

            if (Directory.Exists(annotationDir))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(annotationDir, Qapptia.Core.Constants.JsonSearchPattern))
                    {
                        string? fileMediaId = FastExtractMediaId(file);
                        if (string.Equals(fileMediaId, mediaId, StringComparison.OrdinalIgnoreCase))
                        {
                            // Reconciliación: auto-renombrar al nombre de la imagen actual
                            try
                            {
                                File.Move(file, targetPath, overwrite: true);
                                _logger?.Information("Persistencia re-vinculada por MediaId: {OldPath} -> {NewPath}", file, targetPath);
                                return DeserializeState(targetPath);
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warning(ex, "No se pudo auto-renombrar {OldPath} a {NewPath}", file, targetPath);
                                return DeserializeState(file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, "Error al buscar JSON por MediaId en {Dir}", annotationDir);
                }
            }
        }

        return new CanvasState();
    }

    public string? FastExtractMediaId(string jsonPath) => FastExtractMediaId(jsonPath, _logger);

    public static string? FastExtractMediaId(string jsonPath, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return null;

        try
        {
            using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return null;

            int bytesToRead = (int)Math.Min(Qapptia.Core.Constants.JsonHeaderBufferSize, fs.Length);
            var buffer = new byte[bytesToRead];
            int bytesRead = fs.Read(buffer, 0, bytesToRead);
            if (bytesRead <= 0) return null;

            string header = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return ExtractJsonValue(header, Qapptia.Core.Constants.MetadataPropertyMediaId);
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Error al extraer cabecera rápida de {Path}", jsonPath);
            return null;
        }
    }

    private static string? ExtractJsonValue(string header, string key)
    {
        int keyIndex = header.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0) return null;

        int colonIndex = header.IndexOf(':', keyIndex);
        if (colonIndex < 0) return null;

        int quoteStart = header.IndexOf('"', colonIndex + 1);
        if (quoteStart < 0) return null;

        int quoteEnd = header.IndexOf('"', quoteStart + 1);
        if (quoteEnd <= quoteStart) return null;

        return header[(quoteStart + 1)..quoteEnd];
    }

    private CanvasState DeserializeState(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            string json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var shapeDtos = JsonSerializer.Deserialize<List<VectorShapeDto>>(json);
                return new CanvasState { Shapes = shapeDtos ?? new() };
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var state = JsonSerializer.Deserialize<CanvasState>(json, s_jsonOptions);
                return state ?? new CanvasState();
            }

            return new CanvasState();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Error al deserializar estado del lienzo desde {Path}. Devolviendo estado vacío.", path);
            return new CanvasState();
        }
    }

    public void Save(CanvasState state, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? path = GetJsonPath(imagePath);
        if (string.IsNullOrEmpty(path)) return;

        if (!state.HasModifications)
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
            return;
        }

        try
        {
            string dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
            {
                var dirInfo = Directory.CreateDirectory(dir);
                dirInfo.Attributes |= FileAttributes.Hidden;
            }

            // Si el JSON nominal no existe pero hay un archivo huérfano con el mismo MediaId, re-vincularlo renombrándolo a la ruta nominal
            if (!File.Exists(path) && !string.IsNullOrEmpty(state.MediaId))
            {
                foreach (var orphanFile in Directory.EnumerateFiles(dir, Qapptia.Core.Constants.JsonSearchPattern))
                {
                    if (!string.Equals(orphanFile, path, StringComparison.OrdinalIgnoreCase))
                    {
                        string? orphanMediaId = FastExtractMediaId(orphanFile, _logger);
                        if (string.Equals(orphanMediaId, state.MediaId, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                File.Move(orphanFile, path, overwrite: true);
                                _logger?.Information("Persistencia re-vinculada en guardado: {OldPath} -> {NewPath}", orphanFile, path);
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger?.Warning(ex, "No se pudo re-vincular huérfano {OldPath} a {NewPath}", orphanFile, path);
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(state.MediaType))
            {
                state.MediaType = Qapptia.Core.Constants.ResolveMediaType(imagePath);
            }

            string json = JsonSerializer.Serialize(state, s_jsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error al guardar estado del lienzo en {Path}", path);
        }
    }

    public List<VectorGeometry> CreateShapes(IEnumerable<VectorShapeDto> dtos)
    {
        ArgumentNullException.ThrowIfNull(dtos);

        var shapes = new List<VectorGeometry>();
        foreach (var dto in dtos)
        {
            VectorGeometry? shape = dto.Type switch
            {
                "rect" => new RectangleGeometry(),
                "arrow" => new ArrowGeometry(),
                "ellipse" => new EllipseGeometry(),
                "line" => new LineGeometry(),
                "highlighter" => new HighlighterGeometry(),
                "text" => new TextGeometry(),
                _ => null
            };

            if (shape != null && dto.Coords.Count >= 4)
            {
                shape.Start = new Point(dto.Coords[0], dto.Coords[1]);
                shape.End = new Point(dto.Coords[2], dto.Coords[3]);
                shape.Color = Qapptia.Editor.Core.Constants.ParseColorName(dto.Color);

                if (shape is TextGeometry textShape && dto.Payload != null)
                {
                    if (dto.Payload.TryGetValue("text", out var textVal))
                    {
                        if (textVal is JsonElement textElem && textElem.ValueKind == JsonValueKind.String)
                        {
                            textShape.Text = textElem.GetString() ?? string.Empty;
                        }
                        else if (textVal is string str)
                        {
                            textShape.Text = str;
                        }
                    }
                    if (dto.Payload.TryGetValue("text_size", out var sizeVal))
                    {
                        if (sizeVal is JsonElement sizeElem && sizeElem.ValueKind == JsonValueKind.Number && sizeElem.TryGetInt32(out int size))
                        {
                            textShape.TextSize = size;
                        }
                        else if (sizeVal is int intVal)
                        {
                            textShape.TextSize = intVal;
                        }
                        else if (sizeVal is float floatVal)
                        {
                            textShape.TextSize = floatVal;
                        }
                        else if (sizeVal is double doubleVal)
                        {
                            textShape.TextSize = (float)doubleVal;
                        }
                    }
                }

                shapes.Add(shape);
            }
        }

        return shapes;
    }

    public List<VectorShapeDto> CreateDtos(IEnumerable<VectorGeometry> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        return shapes.Select(s => new VectorShapeDto
        {
            Type = s switch
            {
                RectangleGeometry => "rect",
                ArrowGeometry => "arrow",
                EllipseGeometry => "ellipse",
                LineGeometry => "line",
                HighlighterGeometry => "highlighter",
                TextGeometry => "text",
                _ => "unknown"
            },
            Id = s.Id.ToString(),
            Coords = new List<double> { s.Start.X, s.Start.Y, s.End.X, s.End.Y },
            Color = Qapptia.Editor.Core.Constants.GetColorName(s.Color),
            Payload = s is TextGeometry ts ? new Dictionary<string, object>
            {
                { "text", ts.Text },
                { "text_size", ts.TextSize }
            } : null
        }).ToList();
    }
}
