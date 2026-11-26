using System.Text.Json;
using Kalendra.Models;

namespace Kalendra.Services;

/// <summary>
/// Local storage of calendars and events (as .ics data) in one JSON file. Works offline;
/// the SyncService exchanges changes with iCloud. All access happens on the UI thread.
/// </summary>
public class EventStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly string _file;
    readonly Dictionary<int, List<CalendarEvent>> _yearCache = new();

    public CalendarData Data { get; private set; } = new();
    /// <summary>Anything changed (local edit or sync) – views redraw.</summary>
    public event Action? Changed;
    /// <summary>The user changed something that has to be uploaded.</summary>
    public event Action? LocalChanged;

    public EventStore(string dataDir)
    {
        _file = Path.Combine(dataDir, "kalender.json");
    }

    public void Load()
    {
        if (File.Exists(_file))
        {
            try { Data = JsonSerializer.Deserialize<CalendarData>(File.ReadAllText(_file)) ?? new(); }
            catch { File.Copy(_file, _file + ".defekt", true); Data = new(); }
        }
        if (Data.Events is { Count: > 0 } legacy)
        {
            File.Copy(_file, _file + ".v02-backup", true);
            foreach (var e in legacy)
            {
                var uid = Guid.NewGuid().ToString().ToUpperInvariant();
                var ev = new CalendarEvent { Title = e.Title, Start = e.Start, End = e.End, AllDay = e.AllDay, Location = e.Location, Notes = e.Notes };
                Data.Items.Add(new StoredItem { CalendarId = e.CalendarId, Uid = uid, Ics = Ics.Create(ev, uid) });
            }
        }
        if (Data.Events != null)
        {
            // file from version 0.2: drop its empty sample calendars ("Privat", "Arbeit")
            Data.Calendars.RemoveAll(c => !c.IsRemote && !Data.Items.Any(i => i.CalendarId == c.Id));
        }
        Data.Events = null;
        if (Data.Account != null)
        {
            // file from version 0.3: one iCloud account
            if (Data.Accounts.Count == 0) Data.Accounts.Add(Data.Account);
            foreach (var c in Data.Calendars.Where(c => c.IsRemote && c.AccountId == null)) c.AccountId = Data.Accounts[0].Id;
            Data.Account = null;
        }
        if (Data.Calendars.Count == 0)
            Data.Calendars.Add(new CalendarSource { Name = "Auf diesem PC", Color = "#1BADF8" });
        Save();
    }

    public void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
        File.Move(tmp, _file, true);
    }

    public AccountInfo? AccountOf(CalendarSource? cal) => cal?.AccountId == null ? null : Data.Accounts.FirstOrDefault(a => a.Id == cal.AccountId);
    /// <summary>Google calendars can get a Meet link created automatically.</summary>
    public bool IsGoogle(CalendarSource? cal) => cal is { IsRemote: true, ReadOnly: false } && AccountOf(cal)?.Kind == AccountKind.Google;

    public CalendarSource? Calendar(string id) => Data.Calendars.FirstOrDefault(c => c.Id == id);
    public StoredItem? Item(string id) => Data.Items.FirstOrDefault(i => i.Id == id);

    List<CalendarEvent> Year(int year)
    {
        if (_yearCache.TryGetValue(year, out var list)) return list;
        var from = new DateTime(year, 1, 1); var to = from.AddYears(1);
        list = Data.Items.Where(i => !i.Deleted).SelectMany(i => Ics.Expand(i, from, to)).ToList();
        _yearCache[year] = list;
        return list;
    }

    HashSet<string> VisibleIds() => Data.Calendars.Where(c => c.Visible).Select(c => c.Id).ToHashSet();

    public IEnumerable<CalendarEvent> VisibleEventsOn(DateTime day)
    {
        var visible = VisibleIds();
        return Year(day.Year).Where(e => visible.Contains(e.CalendarId) && e.OccursOn(day))
                             .OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ThenBy(e => e.Title);
    }

    public IEnumerable<CalendarEvent> VisibleEventsInYear(int year)
    {
        var visible = VisibleIds();
        return Year(year).Where(e => visible.Contains(e.CalendarId));
    }

    public CalendarEvent? FindOccurrence(string id) =>
        _yearCache.Values.SelectMany(v => v).FirstOrDefault(e => e.Id == id);

    public int CountEvents(string calendarId) => Data.Items.Count(i => i.CalendarId == calendarId && !i.Deleted);

    /// <summary>Saves a new or edited event. <paramref name="original"/> is the occurrence before editing (null for new).</summary>
    public string Save(CalendarEvent edited, CalendarEvent? original)
    {
        var targetCal = Calendar(edited.CalendarId) ?? Data.Calendars.First();
        if (targetCal.ReadOnly) throw new InvalidOperationException($"Der Kalender „{targetCal.Name}“ ist schreibgeschützt.");
        var wantMeet = edited.AddGoogleMeet && IsGoogle(targetCal);
        if (wantMeet) edited.MeetingUrl = "";
        StoredItem item;
        if (original == null || string.IsNullOrEmpty(edited.ItemId) || Item(edited.ItemId) is not { } existing)
        {
            var uid = Guid.NewGuid().ToString().ToUpperInvariant();
            item = new StoredItem { CalendarId = targetCal.Id, Uid = uid, Ics = Ics.Create(edited, uid), Dirty = targetCal.IsRemote };
            Data.Items.Add(item);
        }
        else
        {
            item = existing;
            var newIcs = Ics.Update(item.Ics, edited, original, existing.MeetingLink);
            if (existing.MeetingLink != null && edited.MeetingUrl != existing.MeetingLink) existing.MeetingLink = null;
            if (!wantMeet && edited.MeetingUrl.Length == 0) existing.MeetRequested = false;
            if (item.CalendarId != targetCal.Id)
            {
                // moving between calendars = delete in the old one, create in the new one
                if (item.Href != null) { item.Deleted = true; item.Dirty = true; }
                else Data.Items.Remove(item);
                item = new StoredItem
                {
                    CalendarId = targetCal.Id, Uid = existing.Uid, Ics = newIcs, Dirty = targetCal.IsRemote,
                    MeetingLink = existing.MeetingLink, MeetRequested = existing.MeetRequested && IsGoogle(targetCal),
                };
                Data.Items.Add(item);
            }
            else
            {
                item.Ics = newIcs;
                item.Dirty = targetCal.IsRemote;
            }
        }
        if (wantMeet) { item.MeetRequested = true; item.MeetTries = 0; item.MeetingLink = null; }
        LocalChange();
        return item.Id + "|" + edited.Start.Ticks;
    }

    public void Delete(CalendarEvent occ, bool onlyThisOccurrence)
    {
        if (Item(occ.ItemId) is not { } item) return;
        var cal = Calendar(item.CalendarId);
        if (cal?.ReadOnly == true) throw new InvalidOperationException($"Der Kalender „{cal.Name}“ ist schreibgeschützt.");
        if (onlyThisOccurrence && occ.IsRecurring)
        {
            item.Ics = Ics.ExcludeOccurrence(item.Ics, occ);
            item.Dirty = cal?.IsRemote == true;
        }
        else if (item.Href != null) { item.Deleted = true; item.Dirty = true; }
        else Data.Items.Remove(item);
        LocalChange();
    }

    /// <summary>Calendar list or visibility changed locally.</summary>
    public void Touch() { _yearCache.Clear(); Save(); Changed?.Invoke(); }

    void LocalChange()
    {
        _yearCache.Clear();
        Save();
        Changed?.Invoke();
        LocalChanged?.Invoke();
    }

    /// <summary>Called by the sync after applying server data.</summary>
    public void RemoteChanged()
    {
        _yearCache.Clear();
        Save();
        Changed?.Invoke();
    }

    public bool HasPendingChanges => Data.Items.Any(i => i.Dirty) || Data.Calendars.Any(c => c.PropsDirty);
}
