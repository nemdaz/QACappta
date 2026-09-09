using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Qapptia.Editor.Models.Navigation;
using Qapptia.Editor.Services;
using Xunit;

namespace Qapptia.Editor.Tests;

public sealed class NavigationServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly NavigationService _sut;

    private static readonly byte[] s_minimalPng = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    };

    public NavigationServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "Qapptia_NavigationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _sut = new NavigationService();
    }

    public void Dispose()
    {
        _sut.Dispose();
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Limpieza en temp
        }
    }

    [Fact]
    public async Task BuildTreeAsyncOrdersFilesNewestToOldestRegardlessOfName()
    {
        // Arrange: Crear archivo con nombre alfabéticamente posterior pero fecha más antigua
        var oldFile = Path.Combine(_testDir, "zzz_old_file.png");
        File.WriteAllBytes(oldFile, new byte[] { 1, 2, 3 });
        File.SetCreationTimeUtc(oldFile, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(oldFile, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        // Crear archivo con nombre alfabéticamente anterior pero fecha más reciente
        var newFile = Path.Combine(_testDir, "aaa_new_file.png");
        File.WriteAllBytes(newFile, new byte[] { 4, 5, 6 });
        File.SetCreationTimeUtc(newFile, new DateTime(2026, 8, 20, 15, 30, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 8, 20, 15, 30, 0, DateTimeKind.Utc));

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert
        tree.Should().NotBeNull();
        tree!.Items.Should().HaveCount(2);

        var firstFile = tree.Items[0].Should().BeOfType<FileItem>().Subject;
        var secondFile = tree.Items[1].Should().BeOfType<FileItem>().Subject;

        firstFile.Name.Should().Be("aaa_new_file.png");
        secondFile.Name.Should().Be("zzz_old_file.png");
    }

    [Fact]
    public async Task BuildTreeAsyncOrdersFoldersDescendingByName()
    {
        // Arrange: Carpetas ordenadas descendentemente por nombre (ej. 2026-09 > 2026-08 > 2025-12)
        var folder2025 = Path.Combine(_testDir, "2025-12");
        var folder202608 = Path.Combine(_testDir, "2026-08");
        var folder202609 = Path.Combine(_testDir, "2026-09");
        Directory.CreateDirectory(folder2025);
        Directory.CreateDirectory(folder202608);
        Directory.CreateDirectory(folder202609);

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert: 2026-09 debe ser el primero, luego 2026-08, y finalmente 2025-12
        tree.Should().NotBeNull();
        var folders = tree!.Items.OfType<FolderItem>().ToList();
        folders.Should().HaveCount(3);
        folders[0].Name.Should().Be("2026-09");
        folders[1].Name.Should().Be("2026-08");
        folders[2].Name.Should().Be("2025-12");
    }

    [Fact]
    public async Task BuildTreeAsyncIgnoresHiddenFoldersSuchAsAnnotations()
    {
        // Arrange
        var hiddenDir = Path.Combine(_testDir, ".annotations");
        Directory.CreateDirectory(hiddenDir);
        File.WriteAllBytes(Path.Combine(hiddenDir, "test.png"), new byte[] { 1 });

        var visibleDir = Path.Combine(_testDir, "2026-08");
        Directory.CreateDirectory(visibleDir);
        File.WriteAllBytes(Path.Combine(visibleDir, "capture.png"), new byte[] { 2 });

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert
        tree.Should().NotBeNull();
        tree!.Items.OfType<FolderItem>().Should().ContainSingle(f => f.Name == "2026-08");
        tree.Items.OfType<FolderItem>().Should().NotContain(f => f.Name == ".annotations");
    }

    [Fact]
    public async Task FindNodeByPathLocatesDeeplyNestedItems()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "sub1", "sub2");
        Directory.CreateDirectory(subDir);
        var targetFile = Path.Combine(subDir, "target.png");
        File.WriteAllBytes(targetFile, new byte[] { 1 });

        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Act
        var found = _sut.FindNodeByPath(new[] { tree! }, targetFile);

        // Assert
        found.Should().NotBeNull();
        found!.Name.Should().Be("target.png");
    }

    [Fact]
    public async Task BuildCalendarTreeAsyncGeneratesExhaustiveStructureIncludingEmptyDays()
    {
        // Arrange: Crear solo un archivo en una fecha específica
        var fileDate = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local);
        var filePath = Path.Combine(_testDir, "capture_20260907120000.png");
        File.WriteAllBytes(filePath, new byte[] { 1 });
        File.SetCreationTimeUtc(filePath, fileDate.ToUniversalTime());
        File.SetLastWriteTimeUtc(filePath, fileDate.ToUniversalTime());

        // Act
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>());

        // Assert
        calendar.Should().NotBeEmpty();
        var year2026 = calendar.FirstOrDefault(y => y.Name == "2026");
        year2026.Should().NotBeNull();
        year2026!.Kind.Should().Be(GroupKind.Year);

        // Mes Septiembre debe existir
        var sepMonth = year2026.Items.OfType<CalendarGroupItem>().FirstOrDefault(m => m.Month == 9);
        sepMonth.Should().NotBeNull();
        sepMonth!.Name.Should().Be("Septiembre");

        // Deben existir semanas en Septiembre
        sepMonth.Items.Should().NotBeEmpty();

        // Semana 37 debe contener días y formato '{dd} {mmm} - {dd} {mmm} (Semana {N})'
        var week37 = sepMonth.Items.OfType<CalendarGroupItem>().FirstOrDefault(w => w.WeekNumber == 37);
        week37.Should().NotBeNull();
        week37!.Kind.Should().Be(GroupKind.Week);
        week37.Name.Should().Be("07 sep - 13 sep (Semana 37)");

        // Toda semana generada debe contener rigurosamente sus 7 días completos
        week37.Items.Should().HaveCount(7);
        foreach (var month in calendar.SelectMany(y => y.Items.OfType<CalendarGroupItem>()))
        {
            foreach (var week in month.Items.OfType<CalendarGroupItem>())
            {
                week.Items.Should().HaveCount(7);
            }
        }

        // El día 7 de septiembre debe tener el archivo y nombre con formato '{dd} {mmm}, {día}' en minúsculas
        var day7 = week37.Items.OfType<CalendarGroupItem>().FirstOrDefault(d => d.Date?.Day == 7);
        day7.Should().NotBeNull();
        day7!.Name.Should().Be("07 sep, lunes");
        day7.HasFiles.Should().BeTrue();
        day7.IsDimmed.Should().BeFalse();
        day7.Items.Should().ContainSingle(i => i.Name == "capture_20260907120000.png");

        // Otros días sin capturas de la misma semana deben existir pero estar vacíos y atenuados
        var otherDays = week37.Items.OfType<CalendarGroupItem>().Where(d => d.Date?.Day != 7).ToList();
        otherDays.Should().NotBeEmpty();
        foreach (var emptyDay in otherDays)
        {
            emptyDay.Items.Should().BeEmpty();
            emptyDay.HasFiles.Should().BeFalse();
            emptyDay.IsDimmed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task BuildCalendarTreeAsyncUnifiesCapturesAcrossDifferentPhysicalFolders()
    {
        // Arrange: Dos archivos con fecha del mismo día guardados en subcarpetas físicas distintas
        var dirA = Path.Combine(_testDir, "FolderA");
        var dirB = Path.Combine(_testDir, "FolderB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        var fileA = Path.Combine(dirA, "shot_20260907100000.png");
        var fileB = Path.Combine(dirB, "shot_20260907150000.png");
        File.WriteAllBytes(fileA, new byte[] { 1 });
        File.WriteAllBytes(fileB, new byte[] { 2 });

        var targetDateUtc = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
        File.SetCreationTimeUtc(fileA, targetDateUtc);
        File.SetCreationTimeUtc(fileB, targetDateUtc);

        // Act
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>());

        // Assert: Ambos archivos deben encontrarse bajo el mismo nodo de Día
        var year2026 = calendar.First(y => y.Name == "2026");
        var sepMonth = year2026.Items.OfType<CalendarGroupItem>().First(m => m.Month == 9);
        var week37 = sepMonth.Items.OfType<CalendarGroupItem>().First(w => w.WeekNumber == 37);
        var day7 = week37.Items.OfType<CalendarGroupItem>().First(d => d.Date?.Day == 7);

        day7.Items.Should().HaveCount(2);
        var names = day7.Items.Select(i => i.Name);
        names.Should().Contain("shot_20260907100000.png");
        names.Should().Contain("shot_20260907150000.png");
    }

    [Fact]
    public async Task BuildCalendarTreeAsyncPreservesCalendarPositionAfterArbitraryFileRename()
    {
        // Arrange: Crear archivo con metadatos embebidos Qapptia para una fecha específica
        var localDate = new DateTime(2026, 9, 7, 10, 30, 0, DateTimeKind.Local);
        var originalDateUtc = localDate.ToUniversalTime();
        var originalPath = Path.Combine(_testDir, "screenshot_initial.png");
        File.WriteAllBytes(originalPath, s_minimalPng);
        Qapptia.Core.Services.ImageMetadataService.InjectMetadata(
            originalPath,
            Guid.NewGuid().ToString("N"),
            "image/png",
            originalDateUtc);

        // Renombrar el archivo en disco a un nombre arbitrario sin fechas ni números
        var renamedPath = Path.Combine(_testDir, "error_login_critico.png");
        File.Move(originalPath, renamedPath);

        // Act
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>());

        // Assert: El archivo renombrado debe seguir ubicándose en 2026 -> Septiembre -> Semana 37 -> Lunes 7
        var year2026 = calendar.FirstOrDefault(y => y.Name == "2026");
        year2026.Should().NotBeNull();

        var sepMonth = year2026!.Items.OfType<CalendarGroupItem>().FirstOrDefault(m => m.Month == 9);
        sepMonth.Should().NotBeNull();

        var week37 = sepMonth!.Items.OfType<CalendarGroupItem>().FirstOrDefault(w => w.WeekNumber == 37);
        week37.Should().NotBeNull();

        var day7 = week37!.Items.OfType<CalendarGroupItem>().FirstOrDefault(d => d.Date?.Day == 7);
        day7.Should().NotBeNull();
        day7!.HasFiles.Should().BeTrue();
        day7.Items.Should().ContainSingle(i => i.Name == "error_login_critico.png");
    }

    [Fact]
    public async Task BuildTreeAsyncOrdersFoldersCorrectlyWhenFilesAreCopied()
    {
        // Arrange: Simular carpetas copiadas donde CreationTime es hoy pero LastWriteTime y nombre reflejan 2025
        var folder2025 = Path.Combine(_testDir, "2025-12");
        var folder2026 = Path.Combine(_testDir, "2026-08");
        Directory.CreateDirectory(folder2025);
        Directory.CreateDirectory(folder2026);

        var file2025 = Path.Combine(folder2025, "20251216_020011_chrome.png");
        File.WriteAllBytes(file2025, new byte[] { 1 });
        File.SetCreationTimeUtc(file2025, DateTime.UtcNow); // Fecha de copia en Windows (hoy)
        File.SetLastWriteTimeUtc(file2025, new DateTime(2025, 12, 16, 7, 0, 0, DateTimeKind.Utc)); // Fecha original

        var file2026 = Path.Combine(folder2026, "20260820_120000.png");
        File.WriteAllBytes(file2026, new byte[] { 2 });
        File.SetCreationTimeUtc(file2026, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(file2026, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert: 2026-08 debe ubicarse arriba (más reciente) y 2025-12 abajo (más antiguo)
        tree.Should().NotBeNull();
        var folders = tree!.Items.OfType<FolderItem>().ToList();
        folders.Should().HaveCount(2);
        folders[0].Name.Should().Be("2026-08");
        folders[1].Name.Should().Be("2025-12");
    }

    [Fact]
    public async Task BuildCalendarTreeAsyncDetectsCreationTimeUtcFallbackWhenNoMetadata()
    {
        // Arrange: Archivo multimedia sin metadato Qapptia y con nombre arbitrario sin fecha, pero con CreationTimeUtc en 2025
        var subFolder = Path.Combine(_testDir, "2025-12");
        Directory.CreateDirectory(subFolder);

        var file2025 = Path.Combine(subFolder, "captura_sin_fecha_en_nombre.png");
        File.WriteAllBytes(file2025, new byte[] { 1, 2, 3 });
        var creationDate2025 = new DateTime(2025, 12, 16, 7, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(file2025, creationDate2025);

        // Act
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>());

        // Assert: El calendario debe detectar y generar el año 2025 mediante el fallback CreationTimeUtc
        calendar.Should().NotBeEmpty();
        var year2025 = calendar.FirstOrDefault(y => y.Name == "2025");
        year2025.Should().NotBeNull();

        // Debe contener el mes Diciembre
        var decMonth = year2025!.Items.OfType<CalendarGroupItem>().FirstOrDefault(m => m.Month == 12);
        decMonth.Should().NotBeNull();
        decMonth!.Name.Should().Be("Diciembre");

        // El archivo debe ubicarse en el día 16 de diciembre basándose puramente en su CreationTimeUtc
        var allDays = decMonth.Items.OfType<CalendarGroupItem>()
            .SelectMany(w => w.Items.OfType<CalendarGroupItem>())
            .ToList();

        var day16 = allDays.FirstOrDefault(d => d.Date?.Day == 16);
        day16.Should().NotBeNull();
        day16!.HasFiles.Should().BeTrue();
        day16.Items.Should().ContainSingle(i => i.Name == "captura_sin_fecha_en_nombre.png");
    }

    [Fact]
    public async Task BuildTreeAsyncOrdersFoldersByNameDescendingAndFilesByCreationDateDescending()
    {
        // Arrange: Carpetas A y Z para comprobar orden descendente por nombre
        var folderOld = Path.Combine(_testDir, "AAA_Carpeta");
        var folderNew = Path.Combine(_testDir, "ZZZ_Carpeta");
        Directory.CreateDirectory(folderOld);
        Directory.CreateDirectory(folderNew);

        // Archivos dentro de ZZZ_Carpeta con nombres invertidos respecto a su fecha de creación
        var fileOld = Path.Combine(folderNew, "zzz_antiguo.png");
        File.WriteAllBytes(fileOld, new byte[] { 1 });
        File.SetCreationTimeUtc(fileOld, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));

        var fileNew = Path.Combine(folderNew, "aaa_reciente.png");
        File.WriteAllBytes(fileNew, new byte[] { 2 });
        File.SetCreationTimeUtc(fileNew, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert: "ZZZ_Carpeta" debe estar primero por nombre descendente
        tree.Should().NotBeNull();
        var folders = tree!.Items.OfType<FolderItem>().ToList();
        folders.Should().HaveCount(2);
        folders[0].Name.Should().Be("ZZZ_Carpeta");
        folders[1].Name.Should().Be("AAA_Carpeta");

        // Y dentro de ZZZ_Carpeta, "aaa_reciente.png" debe estar primero por fecha de creación descendente
        var zzzFolder = folders[0];
        zzzFolder.Items.Should().HaveCount(2);
        zzzFolder.Items[0].Name.Should().Be("aaa_reciente.png");
        zzzFolder.Items[1].Name.Should().Be("zzz_antiguo.png");
    }

    [Fact]
    public async Task BuildTreeAsyncLeavesRootCollapsedWhenExpandedFoldersIsEmpty()
    {
        // Act: Construir el árbol con la lista de carpetas expandidas vacía (usuario contrajo todo)
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());

        // Assert: La raíz no debe forzarse a estar expandida
        tree.Should().NotBeNull();
        tree!.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public async Task BuildTreeAndCalendarIgnoreFoldersWithHiddenPrefix()
    {
        // Arrange
        var drawingDir = Path.Combine(_testDir, $"{Qapptia.Editor.Core.Constants.HiddenPrefixChar}dibujo");
        Directory.CreateDirectory(drawingDir);
        File.WriteAllBytes(Path.Combine(drawingDir, "meta.json"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(drawingDir, "ignored.png"), s_minimalPng);

        var normalDir = Path.Combine(_testDir, "capturas");
        Directory.CreateDirectory(normalDir);
        var testFile = Path.Combine(normalDir, "test.png");
        File.WriteAllBytes(testFile, s_minimalPng);

        // Act
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>());

        // Assert
        tree.Should().NotBeNull();
        tree!.Items.OfType<FolderItem>().Should().ContainSingle(f => f.Name == "capturas");
        tree.Items.OfType<FolderItem>().Should().NotContain(f => f.Name.StartsWith(Qapptia.Editor.Core.Constants.HiddenPrefixChar));

        var foundIgnored = _sut.FindNodeByPath(calendar, Path.Combine(drawingDir, "ignored.png"));
        foundIgnored.Should().BeNull();

        var foundTest = _sut.FindNodeByPath(calendar, testFile);
        foundTest.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildCalendarTreeAsyncMarksTodayOnYearMonthWeekAndDay()
    {
        // Arrange: fecha de referencia fijada en 2026-09-08 (martes, semana 37)
        var referenceToday = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Local);

        // Crear una captura en la fecha actual y otra en una fecha del año anterior (2025-05-15)
        var todayFile = Path.Combine(_testDir, "today_capture.png");
        File.WriteAllBytes(todayFile, s_minimalPng);
        File.SetCreationTimeUtc(todayFile, referenceToday.ToUniversalTime());
        File.SetLastWriteTimeUtc(todayFile, referenceToday.ToUniversalTime());

        var pastFile = Path.Combine(_testDir, "past_capture.png");
        File.WriteAllBytes(pastFile, s_minimalPng);
        var pastDate = new DateTime(2025, 5, 15, 10, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(pastFile, pastDate);
        File.SetLastWriteTimeUtc(pastFile, pastDate);

        // Act
        var calendar = await _sut.BuildCalendarTreeAsync(_testDir, Array.Empty<string>(), weekLabel: null, referenceToday: referenceToday);

        // Assert: Nivel Año
        var year2026 = calendar.OfType<CalendarGroupItem>().FirstOrDefault(y => y.Year == 2026);
        var year2025 = calendar.OfType<CalendarGroupItem>().FirstOrDefault(y => y.Year == 2025);
        year2026.Should().NotBeNull();
        year2026!.IsToday.Should().BeTrue();
        year2025.Should().NotBeNull();
        year2025!.IsToday.Should().BeFalse();

        // Nivel Mes
        var sepMonth = year2026.Items.OfType<CalendarGroupItem>().FirstOrDefault(m => m.Month == 9);
        var augMonth = year2026.Items.OfType<CalendarGroupItem>().FirstOrDefault(m => m.Month == 8);
        sepMonth.Should().NotBeNull();
        sepMonth!.IsToday.Should().BeTrue();
        if (augMonth != null) augMonth.IsToday.Should().BeFalse();

        // Nivel Semana (Semana 37 que contiene 2026-09-08)
        var todayWeek = sepMonth.Items.OfType<CalendarGroupItem>().FirstOrDefault(w => w.IsToday);
        todayWeek.Should().NotBeNull();
        todayWeek!.WeekNumber.Should().Be(System.Globalization.ISOWeek.GetWeekOfYear(referenceToday));

        var otherWeeks = sepMonth.Items.OfType<CalendarGroupItem>().Where(w => w != todayWeek).ToList();
        otherWeeks.Should().AllSatisfy(w => w.IsToday.Should().BeFalse());

        // Nivel Día (Día 08 de septiembre)
        var todayDay = todayWeek.Items.OfType<CalendarGroupItem>().FirstOrDefault(d => d.IsToday);
        todayDay.Should().NotBeNull();
        todayDay!.Date?.Date.Should().Be(referenceToday.Date);

        var otherDays = todayWeek.Items.OfType<CalendarGroupItem>().Where(d => d != todayDay).ToList();
        otherDays.Should().AllSatisfy(d => d.IsToday.Should().BeFalse());

        // Verificar que en el árbol de carpetas tradicional no se marque IsToday
        var tree = await _sut.BuildTreeAsync(_testDir, Array.Empty<string>());
        tree.Should().NotBeNull();
        tree!.IsToday.Should().BeFalse();
    }
}



