using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Qapptia.Editor.Core;
using Qapptia.Editor.Models.Navigation;
using Serilog;

namespace Qapptia.Editor.Services;

/// <summary>
/// Servicio de dominio para exploración, ordenamiento cronológico y monitoreo del árbol de capturas en disco.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private static readonly HashSet<string> s_allowedExtensions = new(Qapptia.Core.Constants.SupportedImageExtensions, StringComparer.OrdinalIgnoreCase);

    private readonly ILogger? _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime LastWriteUtc, DateTime EffectiveDate)> _effectiveDateCache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _fileWatcher;
    private CancellationTokenSource? _watcherDebounceCts;
    private Action? _onFileSystemChanged;

    public NavigationService(ILogger? logger = null)
    {
        _logger = logger;
    }

    public static string NormalizePath(string path) => path.Replace('\\', '/');

    public async Task<FolderItem?> BuildTreeAsync(string rootPath, IReadOnlyList<string> expandedFolders, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return null;

        return await Task.Run(() =>
        {
            var dirInfo = new DirectoryInfo(rootPath);
            var normalizedRoot = NormalizePath(rootPath);

            var root = new FolderItem
            {
                Name = dirInfo.Name,
                FullPath = normalizedRoot,
                IsExpanded = expandedFolders.Any(p => string.Equals(p, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            };

            if (string.IsNullOrEmpty(root.Name)) root.Name = normalizedRoot;

            PopulateFolder(root, dirInfo, expandedFolders);
            return root;
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GroupItem>> BuildCalendarTreeAsync(string rootPath, IReadOnlyList<string> expandedGroups, string? weekLabel = null, DateTime? referenceToday = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<GroupItem>();

        return await Task.Run(() =>
        {
            var dirInfo = new DirectoryInfo(rootPath);
            var allFiles = new List<FileItem>();
            CollectFilesRecursively(dirInfo, allFiles);

            var culture = new CultureInfo("es-ES");
            var today = (referenceToday ?? DateTime.Today).Date;
            int currentYear = today.Year;
            int currentMonth = today.Month;

            var filesByDate = allFiles
                .GroupBy(f => f.EffectiveDateUtc.ToLocalTime().Date)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.EffectiveDateUtc).ToList());

            var targetYears = allFiles
                .Select(f => f.EffectiveDateUtc.ToLocalTime().Year)
                .Append(currentYear)
                .Distinct()
                .OrderByDescending(y => y);

            var years = new List<GroupItem>();

            foreach (int year in targetYears)
            {
                bool isTodayYear = (year == currentYear);
                var yearGroup = new CalendarGroupItem(GroupKind.Year)
                {
                    Name = year.ToString(CultureInfo.InvariantCulture),
                    FullPath = $"cal://{year}",
                    Year = year,
                    IsToday = isTodayYear
                };
                yearGroup.IsExpanded = expandedGroups.Any(p => string.Equals(p, yearGroup.FullPath, StringComparison.OrdinalIgnoreCase));

                int startMonth = (year >= currentYear ? today.Month : 12);
                for (int month = startMonth; month >= 1; month--)
                {
                    string rawMonthName = culture.DateTimeFormat.GetMonthName(month);
                    string monthName = char.ToUpper(rawMonthName[0], culture) + rawMonthName[1..];

                    bool isTodayMonth = (isTodayYear && month == currentMonth);
                    var monthGroup = new CalendarGroupItem(GroupKind.Month)
                    {
                        Name = monthName,
                        FullPath = $"cal://{year}/{month:D2}",
                        Year = year,
                        Month = month,
                        Parent = yearGroup,
                        IsToday = isTodayMonth
                    };
                    monthGroup.IsExpanded = expandedGroups.Any(p => string.Equals(p, monthGroup.FullPath, StringComparison.OrdinalIgnoreCase));

                    int daysInMonth = DateTime.DaysInMonth(year, month);
                    var monthDays = Enumerable.Range(1, daysInMonth)
                        .Select(dayNum => new DateTime(year, month, dayNum))
                        .ToList();

                    var weekGroups = monthDays
                        .GroupBy(d => ISOWeek.GetWeekOfYear(d))
                        .OrderByDescending(g => g.Key);

                    foreach (var weekGroupData in weekGroups)
                    {
                        int weekNum = weekGroupData.Key;
                        var sampleDay = weekGroupData.First();
                        int diffToMonday = (7 + ((int)sampleDay.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
                        DateTime monday = sampleDay.AddDays(-diffToMonday);
                        DateTime sunday = monday.AddDays(6);

                        string startMmm = GetShortMonthName(culture, monday.Month);
                        string endMmm = GetShortMonthName(culture, sunday.Month);
                        string resolvedWeekLabel = !string.IsNullOrWhiteSpace(weekLabel) ? weekLabel : "Semana";

                        bool isTodayWeek = (isTodayYear && isTodayMonth && today >= monday && today <= sunday);
                        var weekGroup = new CalendarGroupItem(GroupKind.Week)
                        {
                            Name = $"{monday:dd} {startMmm} - {sunday:dd} {endMmm} ({resolvedWeekLabel} {weekNum})",
                            FullPath = $"cal://{year}/{month:D2}/w{weekNum:D2}",
                            Year = year,
                            Month = month,
                            WeekNumber = weekNum,
                            Parent = monthGroup,
                            IsToday = isTodayWeek
                        };
                        weekGroup.IsExpanded = expandedGroups.Any(p => string.Equals(p, weekGroup.FullPath, StringComparison.OrdinalIgnoreCase));

                        // Toda semana contiene rigurosamente sus 7 días completos (Lunes a Domingo) conforme a ISO 8601
                        for (int dayOffset = 6; dayOffset >= 0; dayOffset--)
                        {
                            DateTime day = monday.AddDays(dayOffset);
                            string rawDayName = culture.DateTimeFormat.GetDayName(day.DayOfWeek);
                            string dayName = rawDayName.ToLower(culture);
                            string dayMmm = GetShortMonthName(culture, day.Month);

                            bool isTodayDay = (day == today);
                            var dayGroup = new CalendarGroupItem(GroupKind.Day)
                            {
                                Name = $"{day:dd} {dayMmm}, {dayName}",
                                FullPath = $"cal://{year}/{month:D2}/w{weekNum:D2}/{day:yyyy-MM-dd}",
                                Year = year,
                                Month = month,
                                WeekNumber = weekNum,
                                Date = day,
                                Parent = weekGroup,
                                IsToday = isTodayDay
                            };
                            dayGroup.IsExpanded = expandedGroups.Any(p => string.Equals(p, dayGroup.FullPath, StringComparison.OrdinalIgnoreCase));

                            if (filesByDate.TryGetValue(day, out var dayFiles) && dayFiles.Count > 0)
                            {
                                foreach (var file in dayFiles)
                                {
                                    file.Parent = dayGroup;
                                    dayGroup.Items.Add(file);
                                }
                                dayGroup.EffectiveDateUtc = dayFiles.Max(f => f.EffectiveDateUtc);
                            }
                            else
                            {
                                dayGroup.EffectiveDateUtc = day.ToUniversalTime();
                            }

                            weekGroup.Items.Add(dayGroup);
                        }

                        if (weekGroup.Items.Count > 0)
                        {
                            weekGroup.EffectiveDateUtc = weekGroup.Items.Max(i => i.EffectiveDateUtc);
                        }
                        monthGroup.Items.Add(weekGroup);
                    }

                    if (monthGroup.Items.Count > 0)
                    {
                        monthGroup.EffectiveDateUtc = monthGroup.Items.Max(i => i.EffectiveDateUtc);
                    }
                    yearGroup.Items.Add(monthGroup);
                }

                if (yearGroup.Items.Count > 0)
                {
                    yearGroup.EffectiveDateUtc = yearGroup.Items.Max(i => i.EffectiveDateUtc);
                }
                years.Add(yearGroup);
            }

            return years;
        }, ct).ConfigureAwait(false);
    }

    public NavigationItem? FindNodeByPath(IEnumerable<NavigationItem> nodes, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var normalizedTarget = NormalizePath(path);
        return FindNodeRecursive(nodes, normalizedTarget);
    }

    private static NavigationItem? FindNodeRecursive(IEnumerable<NavigationItem> nodes, string normalizedTarget)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                return node;

            if (node is GroupItem group && group.Items.Count > 0)
            {
                var found = FindNodeRecursive(group.Items, normalizedTarget);
                if (found != null) return found;
            }
        }
        return null;
    }

    public void StartWatching(string rootPath, Action onFileSystemChanged)
    {
        StopWatching();

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return;

        _onFileSystemChanged = onFileSystemChanged;

        try
        {
            _fileWatcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            _fileWatcher.Created += OnFileSystemEvent;
            _fileWatcher.Deleted += OnFileSystemEvent;
            _fileWatcher.Renamed += OnFileSystemRenamed;
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error al iniciar FileSystemWatcher en {RootPath}", rootPath);
        }
    }

    public void StopWatching()
    {
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= OnFileSystemEvent;
            _fileWatcher.Deleted -= OnFileSystemEvent;
            _fileWatcher.Renamed -= OnFileSystemRenamed;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        _watcherDebounceCts?.Cancel();
        _watcherDebounceCts?.Dispose();
        _watcherDebounceCts = null;
    }

    public void Dispose()
    {
        StopWatching();
    }

    private void CollectFilesRecursively(DirectoryInfo dirInfo, List<FileItem> results)
    {
        try
        {
            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (!s_allowedExtensions.Contains(file.Extension)) continue;

                results.Add(new FileItem
                {
                    Name = file.Name,
                    FullPath = NormalizePath(file.FullName),
                    EffectiveDateUtc = GetEffectiveDate(file)
                });
            }

            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                if ((subDir.Attributes & FileAttributes.Hidden) != 0 ||
                    (subDir.Attributes & FileAttributes.System) != 0 ||
                    subDir.Name.StartsWith(Constants.HiddenPrefixChar))
                {
                    continue;
                }

                CollectFilesRecursively(subDir, results);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Error al escanear directorio {Path} para calendario", dirInfo.FullName);
        }
    }

    private void PopulateFolder(FolderItem parentFolder, DirectoryInfo dirInfo, IReadOnlyList<string> expandedFolders)
    {
        var subFolders = new List<FolderItem>();
        try
        {
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                if ((subDir.Attributes & FileAttributes.Hidden) != 0 || (subDir.Attributes & FileAttributes.System) != 0 || subDir.Name.StartsWith(Constants.HiddenPrefixChar))
                    continue;

                var normalizedPath = NormalizePath(subDir.FullName);
                var folderItem = new FolderItem
                {
                    Name = subDir.Name,
                    FullPath = normalizedPath,
                    IsExpanded = expandedFolders.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase)),
                    Parent = parentFolder
                };

                PopulateFolder(folderItem, subDir, expandedFolders);
                subFolders.Add(folderItem);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "No se pudieron listar los subdirectorios de {Path}", dirInfo.FullName);
        }

        var files = new List<FileItem>();
        try
        {
            foreach (var file in dirInfo.EnumerateFiles())
            {
                if (!s_allowedExtensions.Contains(file.Extension)) continue;

                // En modo Árbol se usa directamente la fecha de creación en memoria provista por el sistema de archivos
                DateTime creationDate = Qapptia.Core.Services.ImageMetadataService.GetFileCreationTimeUtc(file);
                files.Add(new FileItem
                {
                    Name = file.Name,
                    FullPath = NormalizePath(file.FullName),
                    EffectiveDateUtc = creationDate,
                    Parent = parentFolder
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "No se pudieron listar los archivos de {Path}", dirInfo.FullName);
        }

        // Grupos ordenados descendente por nombre y archivos ordenados descendente por fecha de creación
        var sortedFolders = subFolders.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var sortedFiles = files.OrderByDescending(f => f.EffectiveDateUtc);

        foreach (var folder in sortedFolders)
        {
            parentFolder.Items.Add(folder);
        }

        foreach (var file in sortedFiles)
        {
            parentFolder.Items.Add(file);
        }
    }

    private DateTime GetEffectiveDate(FileInfo file)
    {
        if (_effectiveDateCache.TryGetValue(file.FullName, out var cached) &&
            cached.Length == file.Length &&
            cached.LastWriteUtc == file.LastWriteTimeUtc)
        {
            return cached.EffectiveDate;
        }

        DateTime resolvedDate;

        // 1. Metadato canónico embebido en el trailer del archivo
        var (_, _, createdAt) = Qapptia.Core.Services.ImageMetadataService.GetImageMetadata(file.FullName);
        if (createdAt.HasValue && createdAt.Value > DateTime.MinValue)
        {
            resolvedDate = createdAt.Value;
        }
        else
        {
            // 2. Fallback natural del sistema de archivos mediante método utilitario común sin redundancia
            resolvedDate = Qapptia.Core.Services.ImageMetadataService.GetFileCreationTimeUtc(file);
        }

        _effectiveDateCache[file.FullName] = (file.Length, file.LastWriteTimeUtc, resolvedDate);
        return resolvedDate;
    }

    private static bool IsNavigablePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        string normalized = fullPath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Cualquier directorio o archivo con prefijo interno u oculto queda fuera del dominio de navegación
        foreach (var segment in segments)
        {
            if (segment.StartsWith(Constants.HiddenPrefixChar)) return false;
        }

        string ext = Path.GetExtension(fullPath);
        if (!string.IsNullOrEmpty(ext))
        {
            return s_allowedExtensions.Contains(ext);
        }

        return true;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsNavigablePath(e.FullPath)) return;

        _effectiveDateCache.TryRemove(e.FullPath, out _);
        TriggerDebouncedChange();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsNavigablePath(e.FullPath) && !IsNavigablePath(e.OldFullPath)) return;

        try
        {
            string oldExt = Path.GetExtension(e.OldFullPath);
            string newExt = Path.GetExtension(e.FullPath);

            // Sincronizar archivo JSON correspondiente cuando una imagen admitida es renombrada
            if (s_allowedExtensions.Contains(oldExt) && s_allowedExtensions.Contains(newExt))
            {
                string parentDir = Path.GetDirectoryName(e.FullPath) ?? string.Empty;
                string oldBaseName = Path.GetFileNameWithoutExtension(e.OldFullPath);
                string newBaseName = Path.GetFileNameWithoutExtension(e.FullPath);

                string annotationDir = Path.Combine(parentDir, Qapptia.Core.Constants.DrawingExtension);
                string oldJsonPath = Path.Combine(annotationDir, $"{oldBaseName}{Qapptia.Core.Constants.JsonFileExtension}");
                string newJsonPath = Path.Combine(annotationDir, $"{newBaseName}{Qapptia.Core.Constants.JsonFileExtension}");

                if (File.Exists(oldJsonPath))
                {
                    File.Move(oldJsonPath, newJsonPath, overwrite: true);
                    _logger?.Information("Sincronización en tiempo real: JSON renombrado {Old} -> {New}", oldJsonPath, newJsonPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Error al sincronizar renombramiento de imagen en tiempo real para {Path}", e.FullPath);
        }

        _effectiveDateCache.TryRemove(e.OldFullPath, out _);
        _effectiveDateCache.TryRemove(e.FullPath, out _);

        TriggerDebouncedChange();
    }

    private void TriggerDebouncedChange()
    {
        _watcherDebounceCts?.Cancel();
        _watcherDebounceCts?.Dispose();
        _watcherDebounceCts = new CancellationTokenSource();

        var token = _watcherDebounceCts.Token;
        Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                _onFileSystemChanged?.Invoke();
            }
        }, token);
    }

    // Obtiene la abreviatura de 3 letras del mes en minúsculas conforme al formato {mmm}
    private static string GetShortMonthName(CultureInfo culture, int month)
    {
        string raw = culture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.');
        if (raw.Equals("sept", StringComparison.OrdinalIgnoreCase) || raw.Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            return "sep";
        }
        if (raw.Length > 3)
        {
            raw = raw[..3];
        }
        return raw.ToLower(culture);
    }
}
