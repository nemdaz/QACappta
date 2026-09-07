using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Qapptia.App.Editor;
using Qapptia.App.Editor.ViewModels;
using Qapptia.Core.Abstractions;
using Qapptia.Editor.Core;
using Qapptia.Editor.Services;
using Xunit;

namespace Qapptia.Editor.Tests;

public sealed class EditorDependencyInjectionTests
{
    [Fact]
    public void ConfigureServicesResolvesAllRequiredEditorServices()
    {
        var services = Program.ConfigureServices();

        services.GetRequiredService<IFontProvider>().Should().NotBeNull();
        services.GetRequiredService<INavigationService>().Should().NotBeNull();
        services.GetRequiredService<ICanvasStateService>().Should().NotBeNull();
        services.GetRequiredService<IEditorStateService>().Should().NotBeNull();
        services.GetRequiredService<IShellService>().Should().NotBeNull();
        services.GetRequiredService<EditorViewModel>().Should().NotBeNull();
    }
}
