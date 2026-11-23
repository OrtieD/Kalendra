using Kalendra.Models;
using Kalendra.Services;

namespace Kalendra;

/// <summary>Shared UI state: which date and view are shown, which event is selected.</summary>
public class AppState
{
    public EventStore Store { get; }
    public SyncService Sync { get; }
    public DateTime Current { get; private set; } = DateTime.Today;
    public ViewMode Mode { get; private set; } = ViewMode.Month;
    public string? SelectedId { get; set; }
    public double WeekScrollY { get; set; } = -1;

    public event Action? Changed;
    public event Action<CalendarEvent, bool>? EditRequested;
    public event Action<string>? ErrorRaised;
    public event Action<CalendarEvent>? DeleteRequested;

    public AppState(EventStore store, SyncService sync)
    {
        Store = store;
        Sync = sync;
        store.Changed += Notify;
    }

    public void Notify() => Changed?.Invoke();

    public void GoTo(DateTime date, ViewMode? mode = null)
    {
        Current = date.Date;
        if (mode is { } m) Mode = m;
        Notify();
    }

    public void SetMode(ViewMode mode) { Mode = mode; Notify(); }

    public void Step(int dir)
    {
        Current = Mode switch
        {
            ViewMode.Day => Current.AddDays(dir),
            ViewMode.Week => Current.AddDays(7 * dir),
            ViewMode.Month => new DateTime(Current.Year, Current.Month, 1).AddMonths(dir),
            _ => new DateTime(Current.Year + dir, Current.Month, 1),
        };
        Notify();
    }

    public void Select(string? id) { SelectedId = id; Notify(); }

    public CalendarSource DefaultCalendar() =>
        Store.Data.Calendars.FirstOrDefault(c => c.Visible && !c.ReadOnly && c.IsRemote)
        ?? Store.Data.Calendars.FirstOrDefault(c => c.Visible && !c.ReadOnly)
        ?? Store.Data.Calendars.First(c => !c.ReadOnly);

    public void RequestNew(DateTime start, bool allDay = false)
    {
        var ev = new CalendarEvent
        {
            CalendarId = DefaultCalendar().Id,
            Title = "Neues Ereignis",
            AllDay = allDay,
            Start = start,
            End = allDay ? start.Date.AddDays(1) : start.AddHours(1),
        };
        EditRequested?.Invoke(ev, true);
    }

    public void RequestEdit(CalendarEvent ev) => EditRequested?.Invoke(ev.Clone(), false);
    public void RequestDelete(CalendarEvent ev) => DeleteRequested?.Invoke(ev);

    public bool IsReadOnly(CalendarEvent ev) => Store.Calendar(ev.CalendarId)?.ReadOnly == true;

    public void SaveEvent(CalendarEvent edited, CalendarEvent? original)
    {
        try { SelectedId = Store.Save(edited, original); Notify(); }
        catch (Exception ex) { ErrorRaised?.Invoke(ex.Message); }
    }

    public void DeleteEvent(CalendarEvent occ, bool onlyThis)
    {
        try { SelectedId = null; Store.Delete(occ, onlyThis); }
        catch (Exception ex) { ErrorRaised?.Invoke(ex.Message); }
    }

    /// <summary>Drag &amp; drop: move by an offset and/or change the end.</summary>
    public void MoveEvent(CalendarEvent occ, TimeSpan shift, TimeSpan endExtra)
    {
        if (occ.IsRecurring) { ErrorRaised?.Invoke("Wiederholende Ereignisse bitte per Doppelklick bearbeiten. Verschieben mit der Maus würde die ganze Serie verschieben."); Notify(); return; }
        if (IsReadOnly(occ)) { ErrorRaised?.Invoke("Dieser Kalender ist schreibgeschützt."); Notify(); return; }
        var moved = occ.Clone();
        moved.Start += shift; moved.End += shift + endExtra;
        SaveEvent(moved, occ);
    }

    public void ShowError(string msg) => ErrorRaised?.Invoke(msg);
}
