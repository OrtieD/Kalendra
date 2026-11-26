using Ical.Net;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Kalendra.Models;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using IcalCalendar = Ical.Net.Calendar;

namespace Kalendra.Services;

/// <summary>
/// Reading and writing iCalendar (.ics) data. Unknown properties (Apple extras, alarms, attendees)
/// are kept because we always edit the parsed original instead of writing a fresh file.
/// </summary>
public static class Ics
{
    public static IcalCalendar? TryLoad(string ics)
    {
        try { return IcalCalendar.Load(ics); }
        catch (Exception ex) { SyncLog.Write("Ereignis nicht lesbar: " + ex.Message + " | " + SyncLog.Short(ics, 400)); return null; }
    }

    static IcalEvent? Master(IcalCalendar cal) =>
        cal.Events.FirstOrDefault(e => e.RecurrenceId == null) ?? cal.Events.FirstOrDefault();

    public static string? GetUid(string ics) => TryLoad(ics) is { } c ? Master(c)?.Uid : null;

    /// <summary>All occurrences of the item that overlap [from, to), in local time.</summary>
    public static List<CalendarEvent> Expand(StoredItem item, DateTime from, DateTime to)
    {
        var list = new List<CalendarEvent>();
        var cal = TryLoad(item.Ics);
        if (cal == null) return list;
        var master = Master(cal);
        if (master == null) return list;
        var recurring = master.RecurrenceRules.Count > 0 || master.RecurrenceDates.Count > 0;

        // Occurrences: (source event, start period, end period)
        var raw = new List<(IcalEvent Src, IDateTime Start, IDateTime? End)>();
        if (!recurring)
        {
            // simple event: take start/end directly, no recurrence engine involved
            foreach (var e in cal.Events.Where(e => e.RecurrenceId == null))
                if (e.DtStart != null) raw.Add((e, e.DtStart, e.DtEnd ?? (e.Duration > TimeSpan.Zero ? e.DtStart.Add(e.Duration) : null)));
        }
        else
        {
            try
            {
                var occ = cal.GetOccurrences(new CalDateTime(from.AddDays(-2).ToUniversalTime(), "UTC"), new CalDateTime(to.AddDays(2).ToUniversalTime(), "UTC"));
                foreach (var o in occ)
                    if (o.Source is IcalEvent src) raw.Add((src, o.Period.StartTime, o.Period.EndTime));
            }
            catch (Exception ex)
            {
                LogOnce("Wiederholungen konnten nicht berechnet werden: " + ex.GetType().Name + ": " + ex.Message + " | " + ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
                foreach (var e in cal.Events)
                    if (e.DtStart != null) raw.Add((e, e.DtStart, e.DtEnd));
            }
        }

        foreach (var (src, ps, pe) in raw)
        {
            DateTime start, end, occStart;
            bool allDay;
            try
            {
                allDay = src.IsAllDay || !ps.HasTime;
                if (allDay)
                {
                    start = ps.Value.Date;
                    end = pe?.Value.Date ?? start.AddDays(1);
                    if (end <= start) end = start.AddDays(1);
                }
                else
                {
                    start = ToLocal(ps);
                    end = pe != null ? ToLocal(pe) : start + (src.Duration > TimeSpan.Zero ? src.Duration : TimeSpan.Zero);
                }
                occStart = src.RecurrenceId != null ? (allDay ? src.RecurrenceId.Value.Date : ToLocal(src.RecurrenceId)) : start;
            }
            catch (Exception ex)
            {
                LogOnce("Zeit konnte nicht umgerechnet werden: " + ex.GetType().Name + ": " + ex.Message);
                continue;
            }
            if (end <= from && start < from || start >= to) continue;
            list.Add(new CalendarEvent
            {
                Id = item.Id + "|" + occStart.Ticks,
                ItemId = item.Id,
                CalendarId = item.CalendarId,
                Title = string.IsNullOrEmpty(src.Summary) ? "(Ohne Titel)" : src.Summary,
                Location = src.Location ?? "",
                Notes = Meeting.CleanNotes(src.Description),
                MeetingUrl = MeetingLink(src) ?? item.MeetingLink ?? "",
                MeetPending = item.MeetRequested,
                AllDay = allDay,
                Start = start,
                End = end,
                IsRecurring = recurring,
                OccurrenceStart = occStart,
            });
        }
        return list;
    }

    const string GoogleConference = "X-GOOGLE-CONFERENCE";

    static string? GoogleConferenceLink(IcalEvent e) =>
        e.Properties.FirstOrDefault(p => p.Name == GoogleConference)?.Value?.ToString() is { Length: > 0 } v ? v.Trim() : null;

    /// <summary>The meeting link of an event: Google's conference, Kalendra's notes line, a known service in URL, location or notes.</summary>
    static string? MeetingLink(IcalEvent e) =>
        GoogleConferenceLink(e)
        ?? Meeting.FromNoteLine(e.Description)
        ?? (Meeting.ProviderName(e.Url?.ToString()) != null ? e.Url!.ToString() : null)
        ?? Meeting.FindIn(e.Location)
        ?? Meeting.FindIn(e.Description);

    static DateTime ToLocal(IDateTime dt)
    {
        // floating times (no TZID, no Z) are meant as local wall-clock time
        if (dt.TzId == null && !dt.IsUtc) return DateTime.SpecifyKind(dt.Value, DateTimeKind.Local);
        if (dt.IsUtc || string.Equals(dt.TzId, "UTC", StringComparison.OrdinalIgnoreCase) || dt.TzId == "Z")
            return DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc).ToLocalTime();
        // convert with Windows' own time zone data first, Ical.Net/NodaTime only as fallback
        if (FindZone(dt.TzId!) is { } zone)
        {
            var wall = DateTime.SpecifyKind(dt.Value, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(wall, zone).ToLocalTime();
        }
        return dt.AsUtc.ToLocalTime();
    }

    /// <summary>Year of DTSTART read directly from the text (diagnostics, independent of the parser).</summary>
    public static int? RawStartYear(string ics)
    {
        var m = System.Text.RegularExpressions.Regex.Match(ics, @"BEGIN:VEVENT[\s\S]*?\nDTSTART[^:\r\n]*:(\d{4})");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static bool RawIsRecurring(string ics) =>
        System.Text.RegularExpressions.Regex.IsMatch(ics, @"BEGIN:VEVENT[\s\S]*?\n(RRULE|RDATE)[:;][\s\S]*?END:VEVENT");

    static readonly HashSet<string> Logged = new();
    static void LogOnce(string msg)
    {
        lock (Logged) { if (Logged.Count > 20 || !Logged.Add(msg)) return; }
        SyncLog.Write(msg);
    }

    static CalDateTime ToIcal(DateTime local, bool allDay)
    {
        if (allDay) return new CalDateTime(local.Date) { HasTime = false };
        var utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return new CalDateTime(utc, "UTC");
    }

    /// <summary>A brand-new .ics for an event created in Kalendra.</summary>
    public static string Create(CalendarEvent ev, string uid)
    {
        var cal = new IcalCalendar();
        cal.ProductId = "-//Kalendra//Kalendra 0.3//DE";
        var e = new IcalEvent { Uid = uid, DtStamp = new CalDateTime(DateTime.UtcNow, "UTC") };
        Fill(e, ev, setTimes: true, null, null);
        cal.Events.Add(e);
        return Serialize(cal);
    }

    /// <summary>
    /// Applies edits to an existing .ics. For a series, time changes shift the whole series by the
    /// same amount; text changes apply to the series.
    /// </summary>
    /// <param name="serviceLink">Meet link Google created for this event; never written into the notes.</param>
    public static string Update(string ics, CalendarEvent edited, CalendarEvent original, string? serviceLink = null)
    {
        var cal = TryLoad(ics) ?? throw new InvalidOperationException("Ereignis kann nicht gelesen werden.");
        var master = Master(cal)!;
        var isSeries = master.RecurrenceRules.Count > 0;
        if (!isSeries)
        {
            Fill(master, edited, setTimes: true, original, serviceLink);
        }
        else
        {
            Fill(master, edited, setTimes: false, original, serviceLink);
            var delta = edited.Start - original.Start;
            var newDuration = edited.End - edited.Start;
            if (delta != TimeSpan.Zero || newDuration != original.End - original.Start || edited.AllDay != original.AllDay)
            {
                var masterStart = master.IsAllDay ? master.DtStart.Value : ToLocal(master.DtStart);
                var ns = masterStart + delta;
                master.DtStart = ToIcal(ns, edited.AllDay);
                master.DtEnd = ToIcal(ns + newDuration, edited.AllDay);
                master.Duration = default;
                // single-occurrence exceptions and overrides refer to old times; drop them when the series moves
                if (delta != TimeSpan.Zero)
                {
                    master.ExceptionDates.Clear();
                    foreach (var ov in cal.Events.Where(e => e.RecurrenceId != null).ToList()) cal.Events.Remove(ov);
                }
            }
        }
        master.DtStamp = new CalDateTime(DateTime.UtcNow, "UTC");
        master.LastModified = new CalDateTime(DateTime.UtcNow, "UTC");
        master.Sequence += 1;
        return Serialize(cal);
    }

    /// <summary>Removes a single occurrence from a series (EXDATE).</summary>
    public static string ExcludeOccurrence(string ics, CalendarEvent occ)
    {
        var cal = TryLoad(ics) ?? throw new InvalidOperationException("Ereignis kann nicht gelesen werden.");
        var master = Master(cal)!;
        // remove a possible override for that occurrence
        foreach (var ov in cal.Events.Where(e => e.RecurrenceId != null && Same(e.RecurrenceId, occ.OccurrenceStart, occ.AllDay)).ToList())
            cal.Events.Remove(ov);
        IDateTime ex;
        if (occ.AllDay) ex = new CalDateTime(occ.OccurrenceStart.Date) { HasTime = false };
        else if (master.DtStart.TzId != null && !master.DtStart.IsUtc)
        {
            // express the exception in the series' own time zone, as Apple does
            var local = TimeZoneInfo.ConvertTime(occ.OccurrenceStart, TimeZoneInfo.Local, FindZone(master.DtStart.TzId) ?? TimeZoneInfo.Local);
            ex = new CalDateTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), master.DtStart.TzId);
        }
        else ex = ToIcal(occ.OccurrenceStart, false);
        var pl = new PeriodList { new Period(ex) };
        if (ex.TzId != null && !ex.IsUtc) pl.TzId = ex.TzId;
        master.ExceptionDates.Add(pl);
        master.DtStamp = new CalDateTime(DateTime.UtcNow, "UTC");
        master.Sequence += 1;
        return Serialize(cal);
    }

    static bool Same(IDateTime rid, DateTime occLocal, bool allDay) =>
        allDay ? rid.Value.Date == occLocal.Date : Math.Abs((ToLocal(rid) - occLocal).TotalMinutes) < 1;

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TimeZoneInfo?> Zones = new();
    static TimeZoneInfo? FindZone(string id) => Zones.GetOrAdd(id, static id =>
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win))
            try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
        return null;
    });

    static void Fill(IcalEvent e, CalendarEvent ev, bool setTimes, CalendarEvent? original, string? serviceLink)
    {
        e.Summary = ev.Title;
        e.Location = string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location;
        var url = ev.MeetingUrl?.Trim() ?? "";
        // Leave notes and link untouched when neither changed: keeps Google's Meet text and Apple's formatting.
        if (original == null || url != original.MeetingUrl || ev.Notes != original.Notes)
        {
            var conference = GoogleConferenceLink(e);
            if (url != (original?.MeetingUrl ?? "") && conference != null && conference != url)
                e.Properties.Remove(GoogleConference); // the Google Meet was removed or replaced in Kalendra
            var notes = ev.Notes ?? "";
            // write the link into the notes so it shows on every device; Google's own Meet shows by itself
            if (url.Length > 0 && url != conference && url != serviceLink) notes = Meeting.WithNoteLine(notes, url);
            e.Description = string.IsNullOrWhiteSpace(notes) ? null : notes;
            if (url != (original?.MeetingUrl ?? ""))
            {
                if (url.Length > 0 && url != serviceLink && Uri.TryCreate(url, UriKind.Absolute, out var u)) e.Url = u;
                else if (e.Url != null && original != null && e.Url.ToString().TrimEnd('/') == original.MeetingUrl.TrimEnd('/')) e.Url = null;
            }
        }
        if (!setTimes) return;
        e.DtStart = ToIcal(ev.Start, ev.AllDay);
        var end = ev.AllDay && ev.End.Date <= ev.Start.Date ? ev.Start.Date.AddDays(1) : ev.End;
        e.DtEnd = ToIcal(end, ev.AllDay);
        e.Duration = default;
    }

    static string Serialize(IcalCalendar cal) => new CalendarSerializer().SerializeToString(cal);
}
