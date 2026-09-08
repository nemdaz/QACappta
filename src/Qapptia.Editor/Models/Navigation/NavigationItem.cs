using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Qapptia.Editor.Models.Navigation;

/// <summary>
/// Clase base para cualquier elemento navegable del explorador de capturas.
/// </summary>
public abstract partial class NavigationItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTime EffectiveDateUtc { get; set; } = DateTime.MinValue;
    public GroupItem? Parent { get; set; }

    [ObservableProperty]
    private bool _isExpanded;
}
