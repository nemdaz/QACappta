using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Qapptia.Editor.Models;

public sealed class LayoutState
{
    [JsonPropertyName("sidebar_width")]
    public int SidebarWidth { get; set; } = 250;

    [JsonPropertyName("sidebar_view_mode")]
    public string SidebarViewMode { get; set; } = "Calendar";

    [JsonPropertyName("expanded_folders")]
    public List<string> ExpandedFolders { get; set; } = new();

    [JsonPropertyName("expanded_calendar_groups")]
    public List<string> ExpandedCalendarGroups { get; set; } = new();
}

public sealed class SessionState
{
    [JsonPropertyName("last_selected_file")]
    public string? LastSelectedFile { get; set; }
}

public sealed class ToolsState
{
    [JsonPropertyName("active_tool")]
    public string ActiveTool { get; set; } = "Arrow";

    [JsonPropertyName("text_tool_size")]
    public float TextToolSize { get; set; } = 24f;
}

public sealed class PaletteState
{
    [JsonPropertyName("active_fav_color")]
    public string ActiveFavoriteColor { get; set; } = "#00FF00";

    [JsonPropertyName("tool_fav_colors")]
    public SortedDictionary<string, string> ToolFavoriteColors { get; set; } = new();
}

public sealed class EditorState
{
    [JsonPropertyName("layout")]
    public LayoutState Layout { get; set; } = new();

    [JsonPropertyName("session")]
    public SessionState Session { get; set; } = new();

    [JsonPropertyName("tools")]
    public ToolsState Tools { get; set; } = new();

    [JsonPropertyName("palette")]
    public PaletteState Palette { get; set; } = new();
}
