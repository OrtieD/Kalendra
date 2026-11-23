namespace Kalendra.Models;

public class CalendarSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#1BADF8";
    public bool Visible { get; set; } = true;

    /// <summary>CalDAV URL of the calendar on iCloud. Null for calendars that only live on this PC.</summary>
    public string? Href { get; set; }
    /// <summary>Server "ctag": changes whenever anything in the calendar changes.</summary>
    public string? CTag { get; set; }
    public bool ReadOnly { get; set; }
    /// <summary>Name or colour changed here and must be sent to iCloud.</summary>
    public bool PropsDirty { get; set; }

    /// <summary>Account (iCloud or Google) this calendar belongs to; null for local calendars.</summary>
    public string? AccountId { get; set; }

    public bool IsRemote => Href != null;
}

/// <summary>One calendar object resource (an .ics file) as stored locally and on the server.</summary>
public class StoredItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CalendarId { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Ics { get; set; } = "";
    /// <summary>URL on the server, null until uploaded.</summary>
    public string? Href { get; set; }
    public string? ETag { get; set; }
    /// <summary>Changed locally, must be uploaded.</summary>
    public bool Dirty { get; set; }
    /// <summary>Deleted locally, must be deleted on the server (tombstone).</summary>
    public bool Deleted { get; set; }
}

/// <summary>A single occurrence of an event as shown in the views.</summary>
public class CalendarEvent
{
    /// <summary>Unique per occurrence: item id + occurrence start.</summary>
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsRecurring { get; set; }
    /// <summary>Start of this occurrence as calculated from the series (used to edit/delete single occurrences).</summary>
    public DateTime OccurrenceStart { get; set; }

    public bool OccursOn(DateTime day)
    {
        var d0 = day.Date;
        var d1 = d0.AddDays(1);
        if (AllDay) return Start.Date <= d0 && (End.Date > d0 || End.Date <= Start.Date && Start.Date == d0);
        return Start < d1 && End > d0 || Start == End && Start.Date == d0;
    }

    public CalendarEvent Clone() => (CalendarEvent)MemberwiseClone();
}

public enum AccountKind { ICloud, Google, Subscription }

public class AccountInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AccountKind Kind { get; set; } = AccountKind.ICloud;
    /// <summary>Apple ID or Google e-mail address.</summary>
    public string AppleId { get; set; } = "";
    /// <summary>iCloud: app-specific password. Google: refresh token. Abo: the secret .ics address. Encrypted for the current Windows user.</summary>
    public string Secret { get; set; } = "";
    /// <summary>Google only: OAuth client of the user's own Google Cloud project.</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? CalendarHome { get; set; }
    public DateTime? LastSync { get; set; }

    public string Label => Kind switch { AccountKind.Google => "Google", AccountKind.Subscription => "Abo", _ => "iCloud" };
}

public class CalendarData
{
    public int Version { get; set; } = 2;
    public List<CalendarSource> Calendars { get; set; } = new();
    public List<StoredItem> Items { get; set; } = new();
    public List<AccountInfo> Accounts { get; set; } = new();
    /// <summary>Only used to migrate files written by version 0.3 (single iCloud account).</summary>
    public AccountInfo? Account { get; set; }

    /// <summary>Only used to migrate files written by version 0.2.</summary>
    public List<LegacyEvent>? Events { get; set; }
}

public class LegacyEvent
{
    public string Id { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
}

public enum ViewMode { Day, Week, Month, Year }
