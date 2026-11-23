// End-to-end test of the iCloud sync against a local CalDAV server (Radicale).
// Usage: KALENDRA_CALDAV_URL=http://127.0.0.1:5232/ dotnet run
using System.Net.Http.Headers;
using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Kalendra;
using Kalendra.Models;
using Kalendra.Services;

const string Tz = "BEGIN:VTIMEZONE\r\nTZID:Europe/Berlin\r\nBEGIN:STANDARD\r\nDTSTART:19701025T030000\r\nTZOFFSETFROM:+0200\r\nTZOFFSETTO:+0100\r\nRRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU\r\nEND:STANDARD\r\nBEGIN:DAYLIGHT\r\nDTSTART:19700329T020000\r\nTZOFFSETFROM:+0100\r\nTZOFFSETTO:+0200\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU\r\nEND:DAYLIGHT\r\nEND:VTIMEZONE\r\n";
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();

var baseUrl = Environment.GetEnvironmentVariable("KALENDRA_CALDAV_URL")!;
var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("anna:x")));
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what); if (!ok) fails++; }
async Task<string?> Get(string path) { var r = await http.GetAsync(baseUrl + path); return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync() : null; }
async Task Put(string path, string ics) { var r = await http.PutAsync(baseUrl + path, new StringContent(ics, Encoding.UTF8, "text/calendar")); r.EnsureSuccessStatusCode(); }
async Task Del(string path) { (await http.DeleteAsync(baseUrl + path)).EnsureSuccessStatusCode(); }
async Task<string> Propfind(string path) { var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), baseUrl + path); req.Headers.Add("Depth", "0"); req.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\" xmlns:a=\"http://apple.com/ns/ical/\"><d:prop><d:displayname/><a:calendar-color/></d:prop></d:propfind>", Encoding.UTF8, "application/xml"); return await (await http.SendAsync(req)).Content.ReadAsStringAsync(); }

async Task Run()
{
    var dir = Path.Combine(Path.GetTempPath(), "kalendra-synctest"); if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
    var store = new EventStore(dir); store.Load();
    var sync = new SyncService(store);
    var y = DateTime.Today.Year; var m = DateTime.Today.Month;
    var first = new DateTime(y, m, 1);
    var monday = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);

    Console.WriteLine("A. Verbinden und erste Synchronisierung");
    await sync.ConnectAsync("anna", "x");
    Check(!sync.IsError, "kein Fehler: " + sync.Status);
    var remote = store.Data.Calendars.Where(c => c.IsRemote).ToList();
    Check(remote.Count == 2, $"2 iCloud-Kalender gefunden ({string.Join(", ", remote.Select(c => c.Name + " " + c.Color))})");
    var privat = remote.First(c => c.Name == "Privat"); var arbeit = remote.First(c => c.Name == "Arbeit");
    Check(privat.Color == "#63DA38", "Farbe von Privat übernommen");
    var monthEvents = Enumerable.Range(0, 42).SelectMany(i => store.VisibleEventsOn(first.AddDays(i))).DistinctBy(e => e.Id).ToList();
    var weekly = monthEvents.Where(e => e.ItemId == store.Data.Items.First(i => i.Uid == "serie-1").Id).ToList();
    Check(weekly.Count >= 3, $"Wiederholung aufgelöst: {weekly.Count} Termine der Serie sichtbar");
    var moved = weekly.FirstOrDefault(e => e.Title == "Jour fixe (verschoben)");
    Check(moved != null && moved.Start.Hour == 14, "Einzeln verschobener Termin der Serie um 14:00");
    Check(!weekly.Any(e => e.Start.Date == monday.AddDays(7)), "EXDATE beachtet");
    var allday = monthEvents.FirstOrDefault(e => e.Title == "Urlaub");
    Check(allday is { AllDay: true } && (allday.End - allday.Start).Days == 3, "Ganztägiges Ereignis über 3 Tage");
    var berlin = monthEvents.FirstOrDefault(e => e.Title == "Zahnarzt");
    Check(berlin != null && berlin.Start.Hour == 10, $"Zeitzone Europe/Berlin korrekt ({berlin?.Start:HH:mm})");

    Console.WriteLine("B. Neues Ereignis auf dem PC → iCloud");
    var newEv = new CalendarEvent { CalendarId = arbeit.Id, Title = "Vom PC angelegt", Start = first.AddDays(3).AddHours(15), End = first.AddDays(3).AddHours(16), Location = "Büro" };
    store.Save(newEv, null);
    await sync.SyncAsync();
    var created = store.Data.Items.First(i => i.Ics.Contains("Vom PC angelegt"));
    Check(created.Href != null && !created.Dirty, "hochgeladen: " + created.Href);
    var serverIcs = await Get("anna/arbeit/" + created.Uid + ".ics");
    Check(serverIcs != null && serverIcs.Contains("SUMMARY:Vom PC angelegt") && serverIcs.Contains("LOCATION:Büro"), "auf dem Server vorhanden");

    Console.WriteLine("C. Änderung auf dem Mac (Server) → PC");
    await Put("anna/privat/einzel-1.ics", Ev("einzel-1", "Zahnarzt (neue Uhrzeit)", "DTSTART;TZID=Europe/Berlin:" + first.AddDays(9).ToString("yyyyMMdd") + "T113000", "DTEND;TZID=Europe/Berlin:" + first.AddDays(9).ToString("yyyyMMdd") + "T120000"));
    await sync.SyncAsync();
    var z = store.VisibleEventsOn(first.AddDays(9)).FirstOrDefault(e => e.Title.StartsWith("Zahnarzt"));
    Check(z != null && z.Title == "Zahnarzt (neue Uhrzeit)" && z.Start.Hour == 11 && z.Start.Minute == 30, "Änderung übernommen");

    Console.WriteLine("D. Auf dem PC bearbeiten (Drag & Drop) → iCloud");
    var zOrig = store.VisibleEventsOn(first.AddDays(9)).First(e => e.Title.StartsWith("Zahnarzt"));
    var zNew = zOrig.Clone(); zNew.Start = zNew.Start.AddDays(1); zNew.End = zNew.End.AddDays(1);
    store.Save(zNew, zOrig);
    await sync.SyncAsync();
    serverIcs = await Get("anna/privat/einzel-1.ics");
    var expectUtc = zNew.Start.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
    Check(serverIcs != null && serverIcs.Contains(expectUtc), "neue Zeit auf dem Server: " + expectUtc);
    Check(serverIcs != null && serverIcs.Contains("X-APPLE-TRAVEL-ADVISORY-BEHAVIOR"), "unbekannte Apple-Felder bleiben erhalten");

    Console.WriteLine("E. Einen Termin einer Serie löschen");
    var occ = store.VisibleEventsOn(monday.AddDays(14)).First(e => e.Title == "Jour fixe");
    store.Delete(occ, onlyThisOccurrence: true);
    await sync.SyncAsync();
    serverIcs = await Get("anna/privat/serie-1.ics");
    Check(serverIcs != null && serverIcs.Split('\n').Count(l => l.StartsWith("EXDATE")) >= 2, "EXDATE auf dem Server ergänzt");
    var left = store.VisibleEventsOn(monday.AddDays(14)).Where(e => e.Title == "Jour fixe").ToList();
    Check(left.Count == 0, "Termin lokal verschwunden" + string.Join(",", left.Select(e => $" {e.Start:dd.MM HH:mm} {e.Id}")) + " del=" + occ.Start.ToString("dd.MM HH:mm"));
    Console.WriteLine(string.Join("\n", store.Item(occ.ItemId)!.Ics.Split('\n').Where(l => l.StartsWith("EXDATE") || l.StartsWith("DTSTART"))));
    Check(store.VisibleEventsOn(monday.AddDays(28)).Any(e => e.Title == "Jour fixe"), "restliche Serie bleibt");

    Console.WriteLine("F. Ereignis auf dem PC löschen → iCloud");
    var toDelete = store.VisibleEventsOn(first.AddDays(3)).First(e => e.Title == "Vom PC angelegt");
    store.Delete(toDelete, false);
    await sync.SyncAsync();
    Check(await Get("anna/arbeit/" + created.Uid + ".ics") == null, "auf dem Server gelöscht");

    Console.WriteLine("G. Auf dem Mac gelöscht → PC");
    await Del("anna/arbeit/meeting-1.ics");
    await sync.SyncAsync();
    Check(!store.Data.Items.Any(i => i.Uid == "meeting-1"), "lokal entfernt");

    Console.WriteLine("H. Konflikt: beide Seiten ändern dasselbe Ereignis");
    var u = store.VisibleEventsInYear(y).First(e => e.Title == "Urlaub");
    var u2 = u.Clone(); u2.Title = "Urlaub (PC)"; store.Save(u2, u);
    await Put("anna/privat/urlaub-1.ics", AllDay("urlaub-1", "Urlaub (Mac)", first.AddDays(19), 3));
    await sync.SyncAsync();
    var after = store.VisibleEventsInYear(y).First(e => e.Title.StartsWith("Urlaub"));
    Check(after.Title == "Urlaub (Mac)", "iCloud-Version gewinnt: " + after.Title + " / Status: " + sync.Status);

    Console.WriteLine("I. Kalender umbenennen und Farbe ändern → iCloud");
    arbeit.Name = "Job"; arbeit.Color = "#FF9500"; arbeit.PropsDirty = true;
    await sync.SyncAsync();
    var pf = await Propfind("anna/arbeit/");
    Check(pf.Contains("Job") && pf.Contains("#FF9500"), "Name und Farbe auf dem Server");

    Console.WriteLine("J. Ereignis in anderen Kalender verschieben");
    var j = store.VisibleEventsInYear(y).First(e => e.Title.StartsWith("Zahnarzt"));
    var j2 = j.Clone(); j2.CalendarId = arbeit.Id; store.Save(j2, j);
    await sync.SyncAsync();
    Check(await Get("anna/privat/einzel-1.ics") == null && await Get("anna/arbeit/einzel-1.ics") != null, "von Privat nach Job verschoben");

    Console.WriteLine("K. Neu starten: Daten und Anmeldung bleiben");
    var store2 = new EventStore(dir); store2.Load();
    var sync2 = new SyncService(store2);
    Check(sync2.IsConnected && sync2.FirstNeedingLogin == null, "angemeldet nach Neustart");
    Check(store2.VisibleEventsInYear(y).Count() == store.VisibleEventsInYear(y).Count(), "gleiche Anzahl Termine nach Neustart");
    await sync2.SyncAsync();
    Check(!sync2.IsError, "Sync nach Neustart: " + sync2.Status);

    Console.WriteLine("L. Falsches Passwort");
    var store3 = new EventStore(Path.Combine(dir, "x")); Directory.CreateDirectory(Path.Combine(dir, "x")); store3.Load();
    try { await new SyncService(store3).ConnectAsync("falsch", "falsch"); Check(false, "Fehler erwartet"); }
    catch (CalDavException ex) { Check(ex.Message.Contains("Anmeldung abgelehnt"), "verständliche Meldung: " + ex.Message); }

    if (Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_CALDAV_URL") is { } gUrl) await GoogleTests(store, sync, dir, gUrl);
    await FeedTests(store, sync, dir);

    Console.WriteLine(fails == 0 ? "\nALLE TESTS BESTANDEN" : $"\n{fails} TEST(S) FEHLGESCHLAGEN");
}

async Task GoogleTests(EventStore store, SyncService sync, string dir, string gUrl)
{
    var fake = new Uri(gUrl);
    var authUrl = Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_AUTH_URL")!;
    // "browser": follow the fake Google sign-in page, which redirects to Kalendra's local port
    GoogleOAuth.OpenBrowser = url => _ = Task.Run(async () =>
    {
        using var h = new HttpClient(new HttpClientHandler { UseProxy = false });
        await h.GetAsync(url);
    });
    var gHttp = new HttpClient(new HttpClientHandler { UseProxy = false });
    gHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("gmail:y")));
    async Task Control(string what) => (await gHttp.PostAsync(new Uri(fake, what), null)).EnsureSuccessStatusCode();
    var y = DateTime.Today.Year;
    var first = new DateTime(y, DateTime.Today.Month, 1);
    var icloudCals = store.Data.Calendars.Count(c => c.IsRemote);

    Console.WriteLine("M. Google: falscher Clientschlüssel");
    try { await sync.ConnectGoogleAsync("test-client.apps.googleusercontent.com", "falsch", CancellationToken.None); Check(false, "Fehler erwartet"); }
    catch (CalDavException ex) { Check(ex.Message.Contains("Clientschlüssel"), "verständliche Meldung: " + ex.Message); }
    Check(sync.Accounts.Count == 1, "kein halbes Google-Konto angelegt");

    Console.WriteLine("N. Google verbinden (Browser-Anmeldung mit PKCE)");
    await sync.ConnectGoogleAsync("test-client.apps.googleusercontent.com", "GOCSPX-test", CancellationToken.None);
    var g = sync.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Google);
    Check(g != null && g.AppleId == "test@gmail.example", "Google-Konto angelegt: " + g?.AppleId);
    Check(!sync.IsError, "kein Fehler: " + sync.Status);
    var gCals = store.Data.Calendars.Where(c => c.AccountId == g!.Id).ToList();
    Check(gCals.Count == 1 && gCals[0].Name == "test@gmail.example", "Google-Kalender gefunden: " + string.Join(", ", gCals.Select(c => c.Name)));
    Check(store.Data.Calendars.Count(c => c.IsRemote && c.AccountId != g!.Id) == icloudCals, "iCloud-Kalender bleiben daneben bestehen");
    Check(!g!.Secret.Contains("refresh-") && !File.ReadAllText(Path.Combine(dir, "kalender.json")).Contains("GOCSPX-test") || g.Secret.StartsWith("plain:"), "Zugang nur geschützt gespeichert (unter Windows DPAPI)");

    Console.WriteLine("O. Termin am PC im Google-Kalender anlegen");
    store.Save(new CalendarEvent { CalendarId = gCals[0].Id, Title = "Google-Termin vom PC", Start = first.AddDays(4).AddHours(11), End = first.AddDays(4).AddHours(12) }, null);
    await sync.SyncAsync();
    var gi = store.Data.Items.First(i => i.Ics.Contains("Google-Termin vom PC"));
    var onServer = await gHttp.GetAsync($"http://127.0.0.1:5232/gmail/termine/{gi.Uid}.ics");
    Check(onServer.IsSuccessStatusCode && !gi.Dirty, "bei Google hochgeladen");

    Console.WriteLine("P. Termin bei Google angelegt → erscheint am PC");
    (await gHttp.PutAsync("http://127.0.0.1:5232/gmail/termine/g-1.ics", new StringContent(Ev("g-1", "Vom Handy (Google)", "DTSTART:" + first.AddDays(5).ToString("yyyyMMdd") + "T090000Z", "DTEND:" + first.AddDays(5).ToString("yyyyMMdd") + "T100000Z"), Encoding.UTF8, "text/calendar"))).EnsureSuccessStatusCode();
    await sync.SyncAsync();
    Check(store.VisibleEventsInYear(y).Any(e => e.Title == "Vom Handy (Google)"), "Google-Termin sichtbar");

    Console.WriteLine("Q. Zugangstoken abgelaufen → wird automatisch erneuert");
    await Control("/_expire");
    await sync.SyncAsync();
    Check(!sync.IsError && sync.FirstNeedingLogin == null, "Sync ohne neue Anmeldung: " + sync.Status);

    Console.WriteLine("R. Neustart: beide Konten bleiben angemeldet");
    var s2 = new EventStore(dir); s2.Load();
    var sy2 = new SyncService(s2);
    Check(sy2.Accounts.Count == 2 && sy2.FirstNeedingLogin == null, "2 Konten angemeldet");
    await sy2.SyncAsync();
    Check(!sy2.IsError, "Sync nach Neustart: " + sy2.Status);

    Console.WriteLine("S. Google-Zugang widerrufen → nur Google braucht neue Anmeldung");
    await Control("/_revoke");
    await sy2.SyncAsync();
    var g2 = sy2.Accounts.First(a => a.Kind == AccountKind.Google);
    Check(sy2.NeedsLogin(g2) && !sy2.NeedsLogin(sy2.Accounts.First(a => a.Kind == AccountKind.ICloud)), "nur Google abgemeldet");
    Check(sy2.IsError && sy2.Status.StartsWith("Google"), "Meldung: " + sy2.Status);
    Check(s2.Data.Calendars.Any(c => c.AccountId == g2.Id), "Google-Termine bleiben lokal sichtbar");

    Console.WriteLine("T. Google neu anmelden, dann abmelden");
    await sy2.ConnectGoogleAsync("test-client.apps.googleusercontent.com", "GOCSPX-test", CancellationToken.None);
    Check(sy2.Accounts.Count == 2 && sy2.FirstNeedingLogin == null && !sy2.IsError, "wieder angemeldet, kein doppeltes Konto: " + sy2.Status);
    sy2.Disconnect(sy2.Accounts.First(a => a.Kind == AccountKind.Google));
    Check(!s2.Data.Calendars.Any(c => c.IsRemote && c.AccountId != sy2.Accounts[0].Id) && s2.Data.Calendars.Count(c => c.IsRemote) == icloudCals, "Google-Kalender entfernt, iCloud bleibt");
    Check(!s2.Data.Items.Any(i => i.Ics.Contains("Vom Handy (Google)")), "Google-Termine lokal entfernt");
}

async Task FeedTests(EventStore store, SyncService sync, string dir)
{
    var y = DateTime.Today.Year;
    var first = new DateTime(y, DateTime.Today.Month, 1);
    string D(DateTime d, string t) => d.ToString("yyyyMMdd") + "T" + t;
    var feed = "";
    void MakeFeed(bool changed) => feed =
        "BEGIN:VCALENDAR\r\nPRODID:-//Google Inc//Google Calendar 70.9054//EN\r\nVERSION:2.0\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\nX-WR-CALNAME:anna.test@gmail.example\r\nX-WR-TIMEZONE:Europe/Berlin\r\n" + Tz +
        "BEGIN:VEVENT\r\nDTSTART;TZID=Europe/Berlin:" + D(first.AddDays(1), "180000") + "\r\nDTEND;TZID=Europe/Berlin:" + D(first.AddDays(1), "190000") + "\r\nRRULE:FREQ=WEEKLY;COUNT=4\r\nUID:abc123@google.com\r\nSUMMARY:Chor\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;TZID=Europe/Berlin:" + D(first.AddDays(8), "200000") + "\r\nDTEND;TZID=Europe/Berlin:" + D(first.AddDays(8), "210000") + "\r\nRECURRENCE-ID;TZID=Europe/Berlin:" + D(first.AddDays(8), "180000") + "\r\nUID:abc123@google.com\r\nSUMMARY:Chor (später)\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:" + first.AddDays(12).ToString("yyyyMMdd") + "\r\nDTEND;VALUE=DATE:" + first.AddDays(13).ToString("yyyyMMdd") + "\r\nUID:geb@google.com\r\nSUMMARY:Geburtstag\\, Anna\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nEND:VEVENT\r\n" +
        (changed ? "BEGIN:VEVENT\r\nDTSTART:" + D(first.AddDays(3), "080000Z") + "\r\nDTEND:" + D(first.AddDays(3), "090000Z") + "\r\nUID:neu@google.com\r\nSUMMARY:Neu bei Google\r\nEND:VEVENT\r\n"
                 : "BEGIN:VEVENT\r\nDTSTART:" + D(first.AddDays(2), "080000Z") + "\r\nDTEND:" + D(first.AddDays(2), "090000Z") + "\r\nUID:weg@google.com\r\nSUMMARY:Wird gelöscht\r\nEND:VEVENT\r\n") +
        "END:VCALENDAR\r\n";
    MakeFeed(false);
    var listener = new System.Net.HttpListener();
    listener.Prefixes.Add("http://127.0.0.1:5241/");
    listener.Start();
    _ = Task.Run(async () =>
    {
        while (listener.IsListening)
        {
            var ctx = await listener.GetContextAsync();
            var ok = ctx.Request.Url!.AbsolutePath == "/calendar/ical/geheim/private-xyz/basic.ics";
            ctx.Response.StatusCode = ok ? 200 : 404;
            var bytes = Encoding.UTF8.GetBytes(ok ? feed : "Not Found");
            ctx.Response.ContentType = "text/calendar; charset=UTF-8";
            await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
        }
    });
    var url = "http://127.0.0.1:5241/calendar/ical/geheim/private-xyz/basic.ics";
    foreach (var gAcc in sync.Accounts.Where(a => a.Kind == AccountKind.Google).ToList()) sync.Disconnect(gAcc); // left over from the Google tests
    var before = sync.Accounts.Count;

    Console.WriteLine("U. Kalender per Adresse abonnieren");
    try { await sync.ConnectSubscriptionAsync("kein link"); Check(false, "Fehler erwartet"); }
    catch (CalDavException ex) { Check(ex.Message.Contains("keine gültige Adresse"), "ungültige Adresse: " + ex.Message); }
    try { await sync.ConnectSubscriptionAsync("http://127.0.0.1:5241/falsch.ics"); Check(false, "Fehler erwartet"); }
    catch (CalDavException ex) { Check(ex.Message.Contains("nicht (mehr) gültig"), "falsche Adresse: " + ex.Message); }
    Check(sync.Accounts.Count == before, "kein halbes Abo angelegt");
    await sync.ConnectSubscriptionAsync(url);
    var abo = sync.Accounts.Last();
    var cal = store.Data.Calendars.First(c => c.AccountId == abo.Id);
    Check(abo.Kind == AccountKind.Subscription && cal.Name == "anna.test@gmail.example" && cal.ReadOnly, $"Kalender „{cal.Name}“ angelegt, nur lesen");
    var evs = store.VisibleEventsInYear(y).Where(e => e.CalendarId == cal.Id).ToList();
    Check(evs.Count(e => e.Title.StartsWith("Chor")) == 4 && evs.Any(e => e.Title == "Chor (später)" && e.Start.Hour == 20), $"Serie mit Ausnahme: {evs.Count(e => e.Title.StartsWith("Chor"))} Termine");
    Check(evs.Any(e => e.Title == "Chor" && e.Start.Hour == 18), "Zeitzone korrekt (18:00)");
    Check(evs.Any(e => e.Title == "Geburtstag, Anna" && e.AllDay), "ganztägig mit Erinnerung");
    Check(store.Data.Items.Count(i => i.CalendarId == cal.Id) == 3, "3 Ereignisse (Serie + Ausnahme als eines)");

    Console.WriteLine("V. Änderungen bei Google kommen an, PC kann nicht schreiben");
    MakeFeed(true);
    await sync.SyncAsync();
    evs = store.VisibleEventsInYear(y).Where(e => e.CalendarId == cal.Id).ToList();
    Check(evs.Any(e => e.Title == "Neu bei Google") && !evs.Any(e => e.Title == "Wird gelöscht"), "neu und gelöscht übernommen");
    try { store.Save(new CalendarEvent { CalendarId = cal.Id, Title = "x", Start = first, End = first.AddHours(1) }, null); Check(false, "Fehler erwartet"); }
    catch (InvalidOperationException) { Check(true, "Anlegen im Abo wird verhindert"); }
    cal.Name = "Google privat"; cal.PropsDirty = true; cal.Color = "#FF9500";
    await sync.SyncAsync();
    Check(cal.Name == "Google privat" && cal.Color == "#FF9500" && !sync.IsError, "eigener Name und Farbe bleiben: " + sync.Status);
    Check(!File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("KALENDRA_DATA_DIR")!, "sync-protokoll.txt")).Contains("private-xyz"), "geheime Adresse steht nicht im Protokoll");

    Console.WriteLine("W. Neustart und Abo entfernen");
    var s2 = new EventStore(dir); s2.Load();
    var sy2 = new SyncService(s2);
    var abo2 = sy2.Accounts.First(a => a.Kind == AccountKind.Subscription);
    Check(!sy2.NeedsLogin(abo2), "Abo nach Neustart aktiv");
    await sy2.SyncAsync();
    Check(!sy2.IsError && s2.VisibleEventsInYear(y).Any(e => e.Title == "Neu bei Google"), "Sync nach Neustart: " + sy2.Status);
    sy2.Disconnect(abo2);
    Check(!s2.Data.Calendars.Any(c => c.AccountId == abo2.Id) && !s2.VisibleEventsInYear(y).Any(e => e.Title == "Neu bei Google"), "Abo entfernt");
    Check(sy2.Accounts.Any(a => a.Kind == AccountKind.ICloud), "iCloud bleibt verbunden");
    listener.Stop();
}

string Ev(string uid, string title, string start, string end, string extra = "") =>
    $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Apple Inc.//macOS 15.0//EN\r\n{Tz}BEGIN:VEVENT\r\nUID:{uid}\r\nDTSTAMP:20261101T000000Z\r\n{start}\r\n{end}\r\nSUMMARY:{title}\r\nX-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC\r\n{extra}END:VEVENT\r\nEND:VCALENDAR\r\n";
string AllDay(string uid, string title, DateTime d, int days) =>
    $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Apple Inc.//macOS 15.0//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\nDTSTAMP:20261101T000000Z\r\nDTSTART;VALUE=DATE:{d:yyyyMMdd}\r\nDTEND;VALUE=DATE:{d.AddDays(days):yyyyMMdd}\r\nSUMMARY:{title}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

// seed the server like a Mac would have
async Task Seed()
{
    var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    var monday = first.AddDays(((int)DayOfWeek.Monday - (int)first.DayOfWeek + 7) % 7);
    string D(DateTime d, string t) => d.ToString("yyyyMMdd") + "T" + t;
    await Put("anna/privat/serie-1.ics", Ev("serie-1", "Jour fixe", "DTSTART;TZID=Europe/Berlin:" + D(monday, "090000"), "DTEND;TZID=Europe/Berlin:" + D(monday, "093000"),
        "RRULE:FREQ=WEEKLY;COUNT=10\r\nEXDATE;TZID=Europe/Berlin:" + D(monday.AddDays(7), "090000") + "\r\n").Replace("END:VCALENDAR",
        "BEGIN:VEVENT\r\nUID:serie-1\r\nDTSTAMP:20261101T000000Z\r\nRECURRENCE-ID;TZID=Europe/Berlin:" + D(monday.AddDays(21), "090000") + "\r\nDTSTART;TZID=Europe/Berlin:" + D(monday.AddDays(21), "140000") + "\r\nDTEND;TZID=Europe/Berlin:" + D(monday.AddDays(21), "143000") + "\r\nSUMMARY:Jour fixe (verschoben)\r\nEND:VEVENT\r\nEND:VCALENDAR"));
    await Put("anna/privat/einzel-1.ics", Ev("einzel-1", "Zahnarzt", "DTSTART;TZID=Europe/Berlin:" + D(first.AddDays(9), "100000"), "DTEND;TZID=Europe/Berlin:" + D(first.AddDays(9), "104500")));
    await Put("anna/privat/urlaub-1.ics", AllDay("urlaub-1", "Urlaub", first.AddDays(19), 3));
    await Put("anna/arbeit/meeting-1.ics", Ev("meeting-1", "Meeting", "DTSTART:" + D(first.AddDays(2), "080000Z"), "DTEND:" + D(first.AddDays(2), "090000Z")));
}

// Monday-based check above uses first.AddDays(14) etc.; make the seed's Monday the 1st-based anchor
var t = Task.Run(async () => { await Seed(); });
t.Wait();
var task = Dispatcher.UIThread.InvokeAsync(Run);
while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
if (task.IsFaulted) { Console.WriteLine(task.Exception); return 2; }
return fails == 0 ? 0 : 1;
