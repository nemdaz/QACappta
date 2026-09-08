using System;

namespace Qapptia.Editor.Models.Navigation;

/// <summary>
/// Representa una agrupación temporal virtual en la vista de calendario.
/// </summary>
public sealed class CalendarGroupItem : GroupItem
{
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? WeekNumber { get; set; }
    public DateTime? Date { get; set; }

    public CalendarGroupItem(GroupKind kind)
    {
        Kind = kind;
        IconKey = kind switch
        {
            GroupKind.Year or GroupKind.Month => "IconCalendarMonth",
            _ => "IconCalendarToday"
        };
    }
}
