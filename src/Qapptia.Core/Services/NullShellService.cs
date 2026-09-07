using Qapptia.Core.Abstractions;

namespace Qapptia.Core.Services;

/// <summary>
/// Implementación neutra (Null Object Pattern) de IShellService
/// para entornos de prueba o plataformas sin shell registrado.
/// </summary>
public sealed class NullShellService : IShellService
{
    public static readonly NullShellService Instance = new();

    public bool OpenFile(string filePath) => false;
    public bool ShowInFolder(string filePath) => false;
}
