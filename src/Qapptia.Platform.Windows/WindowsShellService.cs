using System;
using System.Diagnostics;
using System.IO;
using Qapptia.Core.Abstractions;
using Serilog;

namespace Qapptia.Platform.Windows;

/// <summary>
/// Implementación de IShellService para Windows utilizando explorer.exe y el Shell del sistema.
/// </summary>
public sealed class WindowsShellService : IShellService
{
    private readonly ILogger _logger;

    public WindowsShellService(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsShellService requiere Windows.");

        _logger = (logger ?? Log.Logger).ForContext<WindowsShellService>();
    }

    /// <summary>
    /// Normaliza una ruta a formato absoluto de Windows con barras invertidas ('\')
    /// para evitar conflictos con los modificadores de línea de comandos de explorer.exe.
    /// </summary>
    public static string NormalizeWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return Path.GetFullPath(path).Replace('/', '\\');
    }

    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.Warning("Ruta de archivo vacía al intentar abrir en Windows.");
            return false;
        }

        var fullPath = NormalizeWindowsPath(filePath);
        if (!File.Exists(fullPath))
        {
            _logger.Warning("El archivo no existe en la ruta: {FilePath}", fullPath);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(fullPath)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error al abrir el archivo en Windows: {FilePath}", fullPath);
            return false;
        }
    }

    public bool ShowInFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.Warning("Ruta de archivo vacía al intentar mostrar en carpeta en Windows.");
            return false;
        }

        var fullPath = NormalizeWindowsPath(filePath);

        try
        {
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select, \"{fullPath}\"")
                {
                    UseShellExecute = true
                });
                return true;
            }

            if (Directory.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{fullPath}\"")
                {
                    UseShellExecute = true
                });
                return true;
            }

            var parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parentDir}\"")
                {
                    UseShellExecute = true
                });
                return true;
            }

            _logger.Warning("No existe el archivo ni la carpeta contenedora para: {FilePath}", fullPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error al mostrar en carpeta en Windows: {FilePath}", fullPath);
            return false;
        }
    }
}
