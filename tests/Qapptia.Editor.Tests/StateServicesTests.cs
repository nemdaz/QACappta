using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using FluentAssertions;
using Qapptia.Editor.Models;
using Qapptia.Editor.Services;
using Qapptia.Editor.Tools;
using Xunit;
using EditorGeometry = Qapptia.Editor.Models.Geometry;

namespace Qapptia.Editor.Tests;

public sealed class StateServicesTests : IDisposable
{
    private readonly string _testDir;
    private readonly EditorStateService _editorStateService;
    private readonly CanvasStateService _canvasStateService;

    public StateServicesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_StateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _editorStateService = new EditorStateService(_testDir, "editor_state.json");
        _canvasStateService = new CanvasStateService();
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
    public void EditorStateServiceSavesAndLoadsStateCorrectly()
    {
        var state = new EditorState();
        state.Layout.SidebarWidth = 320;
        state.Tools.ActiveTool = "Rectangle";
        state.Palette.ActiveFavoriteColor = "#FF0000";

        _editorStateService.Save(state);

        var loaded = _editorStateService.Load();
        loaded.Layout.SidebarWidth.Should().Be(320);
        loaded.Tools.ActiveTool.Should().Be("Rectangle");
        loaded.Palette.ActiveFavoriteColor.Should().Be("#FF0000");
    }

    [Fact]
    public void CanvasStateServiceSavesAndLoadsFullCanvasState()
    {
        var imagePath = Path.Combine(_testDir, "test_full_canvas.png");
        File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3 });

        var canvasState = new CanvasState
        {
            Crop = new List<double> { 10, 20, 800, 600 },
            Rotation = 90,
            Shapes = new List<VectorShapeDto>
            {
                new VectorShapeDto
                {
                    Type = "arrow",
                    Id = Guid.NewGuid().ToString(),
                    Coords = new List<double> { 15, 15, 150, 150 },
                    Color = "Blue"
                }
            }
        };

        _canvasStateService.Save(canvasState, imagePath);

        var jsonPath = _canvasStateService.GetJsonPath(imagePath);
        jsonPath.Should().NotBeNull();
        File.Exists(jsonPath).Should().BeTrue();

        var loadedState = _canvasStateService.Load(imagePath);

        loadedState.Crop.Should().Equal(10, 20, 800, 600);
        loadedState.Rotation.Should().Be(90);
        loadedState.Shapes.Should().HaveCount(1);
        loadedState.Shapes[0].Type.Should().Be("arrow");
    }

    [Fact]
    public void CanvasStateServiceConvertsShapesBidirectionally()
    {
        var shapes = new List<EditorGeometry.VectorGeometry>
        {
            new EditorGeometry.RectangleGeometry { Start = new Point(10, 10), End = new Point(50, 50), Color = Colors.Red },
            new EditorGeometry.LineGeometry { Start = new Point(0, 0), End = new Point(100, 100), Color = Colors.Green },
            new EditorGeometry.TextGeometry { Start = new Point(20, 20), End = new Point(200, 50), Text = "Hola", TextSize = 20 }
        };

        var dtos = _canvasStateService.CreateDtos(shapes);
        dtos.Should().HaveCount(3);
        dtos[0].Type.Should().Be("rect");
        dtos[1].Type.Should().Be("line");
        dtos[2].Type.Should().Be("text");

        var reconstructed = _canvasStateService.CreateShapes(dtos);
        reconstructed.Should().HaveCount(3);
        reconstructed[0].Should().BeOfType<EditorGeometry.RectangleGeometry>();
        reconstructed[1].Should().BeOfType<EditorGeometry.LineGeometry>();
        var textRecon = reconstructed[2].Should().BeOfType<EditorGeometry.TextGeometry>().Subject;
        textRecon.Text.Should().Be("Hola");
        textRecon.TextSize.Should().Be(20);
    }

    [Fact]
    public void CanvasStateServiceSupportsLegacyArrayJson()
    {
        var imagePath = Path.Combine(_testDir, "test_legacy.png");
        File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3 });

        var jsonPath = _canvasStateService.GetJsonPath(imagePath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

        string legacyJson = @"[
            { ""type"": ""line"", ""coords"": [10.0, 10.0, 100.0, 100.0], ""color"": ""Red"" }
        ]";
        File.WriteAllText(jsonPath, legacyJson);

        var loaded = _canvasStateService.Load(imagePath);
        loaded.Rotation.Should().Be(0);
        loaded.Crop.Should().BeNull();
        loaded.Shapes.Should().HaveCount(1);
        loaded.Shapes[0].Type.Should().Be("line");
    }

    [Fact]
    public void CanvasStateServiceDeletesJsonWhenClean()
    {
        var imagePath = Path.Combine(_testDir, "test_clean.png");
        File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3 });

        var state = new CanvasState
        {
            Shapes = new List<VectorShapeDto>
            {
                new VectorShapeDto { Type = "line", Coords = new List<double> { 0, 0, 10, 10 } }
            }
        };
        _canvasStateService.Save(state, imagePath);

        var jsonPath = _canvasStateService.GetJsonPath(imagePath)!;
        File.Exists(jsonPath).Should().BeTrue();

        // Guardamos estado vacío
        _canvasStateService.Save(new CanvasState(), imagePath);
        File.Exists(jsonPath).Should().BeFalse();
    }

    [Fact]
    public void CanvasStateServiceSerializesMediaIdAndMediaTypeAtHeader()
    {
        var imagePath = Path.Combine(_testDir, "test_metadata.png");
        File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3 });

        string testMediaId = Guid.NewGuid().ToString();
        var canvasState = new CanvasState
        {
            MediaId = testMediaId,
            MediaType = Qapptia.Core.Constants.MediaTypePng,
            Crop = new List<double> { 5, 5, 200, 100 }
        };

        _canvasStateService.Save(canvasState, imagePath);

        var jsonPath = _canvasStateService.GetJsonPath(imagePath)!;
        File.Exists(jsonPath).Should().BeTrue();

        string jsonContent = File.ReadAllText(jsonPath);
        jsonContent.Should().Contain("\"Qapptia.mediaId\": \"" + testMediaId + "\"");
        jsonContent.Should().Contain("\"Qapptia.mediaType\": \"image/png\"");

        string? fastId = _canvasStateService.FastExtractMediaId(jsonPath);
        fastId.Should().Be(testMediaId);
    }

    [Fact]
    public async Task CanvasStateServiceRecoversOrphanJsonWhenImageIsRenamed()
    {
        // 1. Crear imagen original y guardar estado
        var oldImagePath = Path.Combine(_testDir, "original_capture.png");
        await File.WriteAllBytesAsync(oldImagePath, new byte[] { 10, 20, 30 });

        var (mediaId, mediaType, _) = await Qapptia.Core.Services.ImageMetadataService.EnsureImageMetadataAsync(oldImagePath);

        var canvasState = new CanvasState
        {
            MediaId = mediaId,
            MediaType = mediaType,
            Rotation = 180,
            Shapes = new List<VectorShapeDto>
            {
                new VectorShapeDto { Type = "line", Coords = new List<double> { 0, 0, 50, 50 }, Color = "Red" }
            }
        };

        _canvasStateService.Save(canvasState, oldImagePath);
        var oldJsonPath = _canvasStateService.GetJsonPath(oldImagePath)!;
        File.Exists(oldJsonPath).Should().BeTrue();

        // 2. Renombrar imagen (simulando Windows Explorer fuera de la app)
        var newImagePath = Path.Combine(_testDir, "renamed_report.png");
        File.Move(oldImagePath, newImagePath);
        File.Exists(oldJsonPath).Should().BeTrue(); // El JSON sigue llamándose original_capture.json

        // 3. Cargar con el nuevo nombre de imagen
        var recoveredState = _canvasStateService.Load(newImagePath);

        // 4. Verificar que se recuperó el estado y se auto-renombró el JSON
        recoveredState.Rotation.Should().Be(180);
        recoveredState.Shapes.Should().HaveCount(1);

        var newJsonPath = _canvasStateService.GetJsonPath(newImagePath)!;
        File.Exists(newJsonPath).Should().BeTrue();
        File.Exists(oldJsonPath).Should().BeFalse();
    }

    [Fact]
    public async Task CanvasStateServiceSaveCleansOrphanJsonWithSameMediaId()
    {
        var imagePath = Path.Combine(_testDir, "save_cleanup.png");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 1, 2, 3 });

        var (mediaId, mediaType, _) = await Qapptia.Core.Services.ImageMetadataService.EnsureImageMetadataAsync(imagePath);

        // Guardar estado inicial
        var state = new CanvasState
        {
            MediaId = mediaId,
            MediaType = mediaType,
            Rotation = 90
        };
        _canvasStateService.Save(state, imagePath);

        var nominalJson = _canvasStateService.GetJsonPath(imagePath)!;
        File.Exists(nominalJson).Should().BeTrue();

        // Renombrar el JSON a otro nombre (simulando acción externa)
        var orphanJson = Path.Combine(Path.GetDirectoryName(nominalJson)!, "orphan_random.json");
        File.Move(nominalJson, orphanJson);
        File.Exists(orphanJson).Should().BeTrue();
        File.Exists(nominalJson).Should().BeFalse();

        // Guardar nuevo estado sobre la imagen
        state.Rotation = 270;
        _canvasStateService.Save(state, imagePath);

        // El JSON nominal debe existir con la rotación actualizada y el huérfano debe ser eliminado
        File.Exists(nominalJson).Should().BeTrue();
        File.Exists(orphanJson).Should().BeFalse();

        var loaded = _canvasStateService.Load(imagePath);
        loaded.Rotation.Should().Be(270);
    }

    [Fact]
    public void ToolsDeclareCorrectTargetShapeTypes()
    {
        ShapeFactory.Line.TargetShapeType.Should().Be<EditorGeometry.LineGeometry>();
        ShapeFactory.Arrow.TargetShapeType.Should().Be<EditorGeometry.ArrowGeometry>();
        ShapeFactory.Rectangle.TargetShapeType.Should().Be<EditorGeometry.RectangleGeometry>();
        ShapeFactory.Ellipse.TargetShapeType.Should().Be<EditorGeometry.EllipseGeometry>();
        ShapeFactory.Highlighter.TargetShapeType.Should().Be<EditorGeometry.HighlighterGeometry>();
        ShapeFactory.Text.TargetShapeType.Should().Be<EditorGeometry.TextGeometry>();

        ShapeFactory.Crop.TargetShapeType.Should().BeNull();
        ShapeFactory.Crop.AltersCanvasGeometry.Should().BeTrue();
    }
}
