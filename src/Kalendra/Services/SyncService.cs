using System.Net;
using Avalonia.Threading;
using Kalendra.Models;

namespace Kalendra.Services;

/// <summary>
/// Two-way sync between the local store and one or more CalDAV accounts (iCloud, Google).
/// Order per account: calendar list → upload local deletions and changes → download what changed on the server.
/// Conflicts: if an event was changed on both sides, the server version wins.
/// </summary>
public class SyncService
{
    readonly EventStore _store;
    readonly DispatcherTimer _debounce;
    readonly DispatcherTimer _periodic;
    /// <summary>Per account: ready-to-use authentication, null if the secret could not be read (login needed).</summary>
    readonly Dictionary<string, IAuth?> _auth = new();
    bool _running, _again;

    public string Status { get; private set; } = "";
    public bool IsError { get; private set; }
    public bool IsRunning => _running;
    public event Action? StatusChanged;

    public SyncService(EventStore store)
    {
        _store = store;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = SyncAsync(); };
        _periodic = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _periodic.Tick += (_, _) => _ = SyncAsync();
        store.LocalChanged += () => { if (IsConnected) { _debounce.Stop(); _debounce.Start(); } };

        SyncLog.Write($"Start: {Accounts.Count} Konto/Konten, Zeitzone {TimeZoneInfo.Local.Id}, Sprache {System.Globalization.CultureInfo.CurrentCulture.Name}");
        foreach (var acc in Accounts)
        {
            _auth[acc.Id] = BuildAuth(acc);
            SyncLog.Write($"  {acc.Label} {acc.AppleId}: Zugang {(_auth[acc.Id] != null ? "entschlüsselt" : "NICHT lesbar, Anmeldung nötig")}");
        }
        UpdateIdleStatus();
    }

    public IReadOnlyList<AccountInfo> Accounts => _store.Data.Accounts;
    public bool IsConnected => Accounts.Count > 0;
    public bool NeedsLogin(AccountInfo acc) => !_auth.TryGetValue(acc.Id, out var a) || a == null;
    public AccountInfo? FirstNeedingLogin => Accounts.FirstOrDefault(NeedsLogin);

    static IAuth? BuildAuth(AccountInfo acc)
    {
        var secret = SecretStore.Unprotect(acc.Secret);
        if (secret == null) return null;
        if (acc.Kind == AccountKind.ICloud) return new BasicAuth(acc.AppleId, secret);
        if (acc.Kind == AccountKind.Subscription) return new FeedAuth(secret);
        var cs = acc.ClientSecret != null ? SecretStore.Unprotect(acc.ClientSecret) : null;
        return acc.ClientId != null && cs != null ? new GoogleAuth(acc.ClientId, cs, secret) : null;
    }

    static string ServerFor(AccountInfo acc) => acc.Kind == AccountKind.Google ? CalDavClient.GoogleServerUrl : CalDavClient.ServerUrl;

    public void Start()
    {
        if (!IsConnected) return;
        _periodic.Start();
        _ = SyncAsync();
    }

    /// <summary>Signs in to iCloud, stores the account and runs the first sync. Throws with a readable message on failure.</summary>
    public async Task ConnectAsync(string appleId, string password)
    {
        SyncLog.Write($"=== Anmeldung iCloud als {appleId} an {CalDavClient.ServerUrl} ===");
        var auth = new BasicAuth(appleId, password);
        string home;
        try { home = await new CalDavClient(CalDavClient.ServerUrl, auth).DiscoverHomeAsync(); }
        catch (Exception ex) { SyncLog.Write("Anmeldung fehlgeschlagen: " + ex.Message); throw; }
        var acc = Accounts.FirstOrDefault(a => a.Kind == AccountKind.ICloud && string.Equals(a.AppleId, appleId, StringComparison.OrdinalIgnoreCase));
        if (acc == null) { acc = new AccountInfo { Kind = AccountKind.ICloud }; _store.Data.Accounts.Add(acc); }
        acc.AppleId = appleId;
        acc.Secret = SecretStore.Protect(password);
        acc.CalendarHome = home;
        _auth[acc.Id] = auth;
        await AfterConnectAsync();
    }

    /// <summary>Opens the browser for Google sign-in, stores the account and runs the first sync.</summary>
    public async Task ConnectGoogleAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        SyncLog.Write($"=== Anmeldung Google an {CalDavClient.GoogleServerUrl} ===");
        var tokens = await GoogleOAuth.LoginAsync(clientId, clientSecret, ct);
        var auth = new GoogleAuth(clientId, clientSecret, tokens.RefreshToken!, tokens.AccessToken, tokens.ExpiresUtc);
        var email = tokens.Email ?? "Google";
        string home;
        try { home = await new CalDavClient(CalDavClient.GoogleServerUrl, auth).DiscoverHomeAsync(); }
        catch (CalDavException ex) when (tokens.Email != null && ex.Status != HttpStatusCode.Unauthorized)
        {
            // Google's discovery is not always available: use the documented address of the user's calendars
            SyncLog.Write("Google-Kalenderordner nicht automatisch gefunden (" + ex.Message + "), nutze Standardadresse.");
            home = CalDavClient.GoogleServerUrl + Uri.EscapeDataString(email) + "/";
        }
        var acc = Accounts.FirstOrDefault(a => a.Kind == AccountKind.Google && string.Equals(a.AppleId, email, StringComparison.OrdinalIgnoreCase));
        if (acc == null) { acc = new AccountInfo { Kind = AccountKind.Google }; _store.Data.Accounts.Add(acc); }
        acc.AppleId = email;
        acc.Secret = SecretStore.Protect(tokens.RefreshToken!);
        acc.ClientId = clientId;
        acc.ClientSecret = SecretStore.Protect(clientSecret);
        acc.CalendarHome = home;
        _auth[acc.Id] = auth;
        await AfterConnectAsync();
    }

    /// <summary>Subscribes to a calendar by its .ics address (read-only), e.g. Google's secret iCal address.</summary>
    public async Task ConnectSubscriptionAsync(string address, AccountInfo? existing = null)
    {
        var url = IcsFeed.Normalize(address) ?? throw new CalDavException("Das ist keine gültige Adresse. Sie beginnt mit https:// oder webcal://.", 0);
        SyncLog.Write($"=== Kalender abonnieren von {IcsFeed.Host(url)} ===");
        var feed = await IcsFeed.FetchAsync(url);
        var acc = existing ?? Accounts.FirstOrDefault(a => a.Kind == AccountKind.Subscription && SecretStore.Unprotect(a.Secret) == url);
        if (acc == null) { acc = new AccountInfo { Kind = AccountKind.Subscription }; _store.Data.Accounts.Add(acc); }
        acc.AppleId = feed.Name ?? (IcsFeed.Host(url).Contains("google") ? "Google-Kalender" : "Abonnierter Kalender");
        acc.Secret = SecretStore.Protect(url);
        _auth[acc.Id] = new FeedAuth(url);
        await AfterConnectAsync();
    }

    async Task SyncSubscriptionAsync(AccountInfo acc, FeedAuth feedAuth)
    {
        var feed = await IcsFeed.FetchAsync(feedAuth.Url);
        var cal = _store.Data.Calendars.FirstOrDefault(c => c.AccountId == acc.Id);
        if (cal == null)
        {
            var used = _store.Data.Calendars.Select(c => c.Color).ToHashSet();
            cal = new CalendarSource
            {
                Name = feed.Name ?? acc.AppleId, Href = "abo:" + acc.Id, ReadOnly = true, AccountId = acc.Id,
                Color = feed.Color ?? Look.CalendarColors.FirstOrDefault(c => !used.Contains(c)) ?? Look.CalendarColors[0],
            };
            _store.Data.Calendars.Add(cal);
        }
        else if (!cal.PropsDirty && feed.Name != null) cal.Name = feed.Name; // PropsDirty = renamed here, keep the user's name
        var existing = _store.Data.Items.Where(i => i.CalendarId == cal.Id).ToDictionary(i => i.Uid);
        int added = 0, changed = 0, removed = 0;
        foreach (var (uid, ics) in feed.Events)
        {
            if (existing.Remove(uid, out var item)) { if (item.Ics != ics) { item.Ics = ics; changed++; } }
            else { _store.Data.Items.Add(new StoredItem { CalendarId = cal.Id, Uid = uid, Ics = ics }); added++; }
        }
        foreach (var gone in existing.Values) { _store.Data.Items.Remove(gone); removed++; }
        SyncLog.Write($"Abo „{cal.Name}“: {feed.Events.Count} Ereignisse ({added} neu, {changed} geändert, {removed} entfernt)");
    }

    async Task AfterConnectAsync()
    {
        _store.Save();
        _periodic.Start();
        await SyncAsync();
        // the empty placeholder calendar is not needed once server calendars are there
        var placeholder = _store.Data.Calendars.FirstOrDefault(c => !c.IsRemote && c.Name == "Auf diesem PC" && _store.CountEvents(c.Id) == 0);
        if (placeholder != null && _store.Data.Calendars.Any(c => c.IsRemote && !c.ReadOnly)) { _store.Data.Calendars.Remove(placeholder); _store.RemoteChanged(); }
    }

    public void Disconnect(AccountInfo acc)
    {
        RemoveAccountData(acc.Id);
        _store.Data.Accounts.Remove(acc);
        _auth.Remove(acc.Id);
        if (!IsConnected) { _periodic.Stop(); _debounce.Stop(); }
        if (_store.Data.Calendars.Count == 0)
            _store.Data.Calendars.Add(new CalendarSource { Name = "Auf diesem PC", Color = "#1BADF8" });
        _store.RemoteChanged();
        UpdateIdleStatus();
    }

    void RemoveAccountData(string accountId)
    {
        var cals = _store.Data.Calendars.Where(c => c.AccountId == accountId).Select(c => c.Id).ToHashSet();
        _store.Data.Items.RemoveAll(i => cals.Contains(i.CalendarId));
        _store.Data.Calendars.RemoveAll(c => c.AccountId == accountId);
    }

    public async Task SyncAsync()
    {
        var ready = Accounts.Where(a => !NeedsLogin(a)).ToList();
        if (ready.Count == 0) { SyncLog.Write($"Sync übersprungen: {Accounts.Count} Konto/Konten, keines angemeldet"); UpdateIdleStatus(); return; }
        if (_running) { _again = true; return; }
        _running = true;
        SetStatus("Synchronisiere …", false);
        var problems = new List<string>();
        try
        {
            foreach (var acc in ready)
            {
                SyncLog.Write($"=== Sync startet (Kalendra {typeof(SyncService).Assembly.GetName().Version}, {acc.Label} {(acc.Kind == AccountKind.Subscription ? "" : acc.AppleId)}) ===");
                try { await SyncAccountAsync(acc, problems); acc.LastSync = DateTime.Now; }
                catch (CalDavException ex)
                {
                    SyncLog.Write("FEHLER: " + ex.Message);
                    if (ex.Status == HttpStatusCode.Unauthorized) _auth[acc.Id] = null;
                    problems.Add($"{acc.Label}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    SyncLog.Write("FEHLER: " + ex);
                    CrashReport.Log(ex);
                    problems.Add($"{acc.Label}: Sync-Fehler: {ex.Message}");
                }
            }
            _store.RemoteChanged();
            LogSummary();
            if (problems.Count > 0) SetStatus(problems[0], true);
            else UpdateIdleStatus();
        }
        finally
        {
            _running = false;
            if (_again) { _again = false; _debounce.Stop(); _debounce.Start(); }
        }
    }

    async Task SyncAccountAsync(AccountInfo acc, List<string> problems)
    {
        if (_auth[acc.Id] is FeedAuth feedAuth) { await SyncSubscriptionAsync(acc, feedAuth); return; }
        var client = new CalDavClient(ServerFor(acc), _auth[acc.Id]!);
        acc.CalendarHome ??= await client.DiscoverHomeAsync();
        var serverName = acc.Label;

        // 1. calendar list
        SyncLog.Write("Kalender-Ordner: " + acc.CalendarHome);
        var remoteCals = await client.ListCalendarsAsync(acc.CalendarHome);
        SyncLog.Write($"{remoteCals.Count} Kalender auf dem Server: " + string.Join(", ", remoteCals.Select(r => $"{r.Name}{(r.ReadOnly ? " (nur lesen)" : "")}")));
        var mine = () => _store.Data.Calendars.Where(c => c.AccountId == acc.Id && c.IsRemote);
        var seen = new HashSet<string>();
        foreach (var rc in remoteCals)
        {
            var key = CalDavClient.Key(rc.Href);
            seen.Add(key);
            var local = mine().FirstOrDefault(c => CalDavClient.Key(c.Href!) == key);
            if (local == null)
            {
                _store.Data.Calendars.Add(new CalendarSource
                {
                    Name = rc.Name, Color = rc.Color ?? Look.CalendarColors[_store.Data.Calendars.Count % Look.CalendarColors.Length],
                    Href = rc.Href.EndsWith('/') ? rc.Href : rc.Href + "/", ReadOnly = rc.ReadOnly, AccountId = acc.Id,
                });
            }
            else
            {
                if (!local.PropsDirty) { local.Name = rc.Name; if (rc.Color != null) local.Color = rc.Color; }
                local.ReadOnly = rc.ReadOnly;
            }
        }
        foreach (var gone in mine().Where(c => !seen.Contains(CalDavClient.Key(c.Href!))).ToList())
        {
            _store.Data.Items.RemoveAll(i => i.CalendarId == gone.Id);
            _store.Data.Calendars.Remove(gone);
        }
        var myCalIds = mine().Select(c => c.Id).ToHashSet();

        // 2. upload calendar renames / colours
        foreach (var cal in mine().Where(c => c.PropsDirty))
        {
            try { await client.UpdateCalendarPropsAsync(cal.Href!, cal.Name, cal.Color); cal.PropsDirty = false; }
            catch (CalDavException ex) when (ex.Status != HttpStatusCode.Unauthorized) { problems.Add($"Kalender „{cal.Name}“: {ex.Message}"); cal.PropsDirty = false; cal.CTag = null; }
        }

        // 3. upload deletions
        foreach (var item in _store.Data.Items.Where(i => i.Dirty && i.Deleted && myCalIds.Contains(i.CalendarId)).ToList())
        {
            try
            {
                if (item.Href != null) await client.DeleteAsync(item.Href, item.ETag);
                _store.Data.Items.Remove(item);
            }
            catch (CalDavException ex) when (ex.Status == HttpStatusCode.PreconditionFailed)
            {
                item.Deleted = false; item.Dirty = false; item.ETag = null;
                problems.Add("Ein gelöschtes Ereignis wurde inzwischen woanders geändert und bleibt erhalten.");
            }
            catch (CalDavException ex) when (ex.Status is HttpStatusCode.Forbidden)
            {
                item.Deleted = false; item.Dirty = false; item.ETag = null;
                problems.Add("Ein Ereignis konnte nicht gelöscht werden (keine Berechtigung).");
            }
        }

        // 4. upload new and changed events
        foreach (var item in _store.Data.Items.Where(i => i.Dirty && !i.Deleted && myCalIds.Contains(i.CalendarId)).ToList())
        {
            var cal = _store.Calendar(item.CalendarId)!;
            var href = item.Href ?? cal.Href + Uri.EscapeDataString(item.Uid) + ".ics";
            var icsSent = item.Ics;
            try
            {
                var etag = await client.PutAsync(href, icsSent, item.Href == null ? null : item.ETag);
                item.Href = href;
                item.ETag = etag;
                if (item.Ics == icsSent) item.Dirty = false; // edited again while uploading → next run
                cal.CTag = null; // read back the server's version
            }
            catch (CalDavException ex) when (ex.Status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Forbidden)
            {
                item.Dirty = false; item.ETag = null;
                if (item.Href == null) _store.Data.Items.Remove(item);
                problems.Add(ex.Status == HttpStatusCode.Forbidden ? $"„{cal.Name}“ ist schreibgeschützt, Änderung verworfen." : $"{ex.Message} Die {serverName}-Version wurde übernommen.");
                cal.CTag = null;
            }
        }

        // 5. download changes
        foreach (var cal in mine().ToList())
        {
            var rc = remoteCals.First(r => CalDavClient.Key(r.Href) == CalDavClient.Key(cal.Href!));
            if (rc.CTag != null && rc.CTag == cal.CTag) continue;

            var etags = await client.ListETagsAsync(cal.Href!);
            SyncLog.Write($"„{cal.Name}“: {etags.Count} Ereignisse auf dem Server");
            var byKey = etags.ToDictionary(kv => CalDavClient.Key(kv.Key), kv => (Href: kv.Key, ETag: kv.Value));
            var localItems = _store.Data.Items.Where(i => i.CalendarId == cal.Id && i.Href != null).ToList();
            var localByKey = new Dictionary<string, StoredItem>();
            foreach (var li in localItems)
            {
                var k = CalDavClient.Key(li.Href!);
                if (!byKey.ContainsKey(k)) { if (!li.Dirty || li.Deleted) _store.Data.Items.Remove(li); else { li.Href = null; li.ETag = null; } }
                else localByKey[k] = li;
            }
            var toFetch = byKey.Where(kv => !localByKey.TryGetValue(kv.Key, out var li) || li.ETag == null || li.ETag != kv.Value.ETag && !li.Dirty)
                               .Select(kv => kv.Value.Href).ToList();
            SyncLog.Write($"„{cal.Name}“: {toFetch.Count} neu oder geändert, werden geladen");
            if (toFetch.Count > 0)
            {
                foreach (var obj in await client.MultigetAsync(cal.Href!, toFetch))
                {
                    var k = CalDavClient.Key(obj.Href);
                    if (localByKey.TryGetValue(k, out var li))
                    {
                        if (li.Dirty) continue;
                        li.Ics = obj.Ics!; li.ETag = obj.ETag;
                    }
                    else
                    {
                        _store.Data.Items.Add(new StoredItem
                        {
                            CalendarId = cal.Id, Href = obj.Href, ETag = obj.ETag, Ics = obj.Ics!,
                            Uid = Ics.GetUid(obj.Ics!) ?? Path.GetFileNameWithoutExtension(k),
                        });
                    }
                }
            }
            cal.CTag = rc.CTag;
        }
    }

    void LogSummary()
    {
        var broken = _store.Data.Items.Count(i => !i.Deleted && Ics.TryLoad(i.Ics) == null);
        SyncLog.Write($"Fertig. {_store.Data.Items.Count} Ereignisse lokal gespeichert, {broken} davon nicht lesbar.");
        var year = DateTime.Today.Year;
        var visibleList = _store.VisibleEventsInYear(year).OrderBy(e => e.Start).ToList();
        SyncLog.Write($"Anzeige {year}: {visibleList.Count} Termine sichtbar; je Kalender: " +
            string.Join(", ", _store.Data.Calendars.Select(c =>
            {
                var items = _store.Data.Items.Where(i => i.CalendarId == c.Id && !i.Deleted).ToList();
                var startYear = items.Count(i => Ics.RawStartYear(i.Ics) == year);
                var recurring = items.Count(i => Ics.RawIsRecurring(i.Ics));
                var acc = Accounts.FirstOrDefault(a => a.Id == c.AccountId)?.Label ?? "PC";
                return $"{c.Name} [{acc}]={items.Count} (Beginn {year}: {startYear}, wiederholend: {recurring}){(c.Visible ? "" : " ausgeblendet")}";
            })));
    }

    void UpdateIdleStatus()
    {
        if (!IsConnected) { SetStatus("", false); return; }
        if (FirstNeedingLogin is { } a) { SetStatus($"{a.Label}: Anmeldung erforderlich.", true); return; }
        var last = Accounts.Select(x => x.LastSync).Where(t => t != null).Min();
        if (last is { } t)
            SetStatus(t.Date == DateTime.Today ? $"Synchronisiert um {t:HH:mm}" : $"Synchronisiert am {t:dd.MM. HH:mm}", false);
        else SetStatus("Noch nicht synchronisiert", false);
    }

    void SetStatus(string s, bool error)
    {
        Status = s; IsError = error;
        StatusChanged?.Invoke();
    }
}
