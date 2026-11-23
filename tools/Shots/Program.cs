using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Kalendra;
using Kalendra.Models;
using Kalendra.Services;

System.Globalization.CultureInfo.CurrentCulture = Look.De;
var outDir = args.Length > 0 ? args[0] : "shots";
Directory.CreateDirectory(outDir);
var data = Path.Combine(Path.GetTempPath(), "kalendra-shots"); if (Directory.Exists(data)) Directory.Delete(data, true); Directory.CreateDirectory(data);

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();

var store = new EventStore(data); store.Load();
var cals = store.Data.Calendars;
cals.Add(new CalendarSource { Name = "Arbeit", Color = "#FF2968" });
cals.Add(new CalendarSource { Name = "Familie", Color = "#63DA38" });
cals.Add(new CalendarSource { Name = "Sport", Color = "#FF9500" });
string P = cals[0].Id, A = cals[1].Id, F = cals[2].Id, S = cals[3].Id;
var ws = Look.StartOfWeek(DateTime.Today);
void Add(string cal, string t, DateTime s, double h, string loc = "", bool all = false) =>
    store.Save(new CalendarEvent { CalendarId = cal, Title = t, Start = s, End = all ? s.Date.AddDays(h) : s.AddHours(h), AllDay = all, Location = loc }, null);
Add(A, "Team-Meeting", ws.AddHours(9), 1, "Raum 2");
Add(A, "Projekt-Review", ws.AddDays(1).AddHours(10.5), 1.5);
Add(A, "Kundentermin", ws.AddDays(1).AddHours(11), 1, "Zoom");
Add(P, "Zahnarzt", ws.AddDays(2).AddHours(8), 0.75, "Praxis Dr. Weber");
Add(S, "Laufen", ws.AddDays(2).AddHours(18), 1);
Add(F, "Abendessen bei Oma", ws.AddDays(4).AddHours(19), 2.5);
Add(A, "Mittagessen mit Jana", ws.AddDays(3).AddHours(12.5), 1);
Add(S, "Fußball", ws.AddDays(5).AddHours(10), 2, "Sportplatz");
Add(F, "Wochenende Ostsee", ws.AddDays(5), 2, all: true);
Add(P, "Geburtstag Max", ws.AddDays(3), 1, all: true);
Add(A, "Quartalsplanung", ws.AddDays(7).AddHours(9), 3);
Add(P, "Werkstatt", ws.AddDays(9).AddHours(7.5), 1);
Add(F, "Elternabend", ws.AddDays(-5).AddHours(19), 1.5);
for (int i = 0; i < 5; i++) Add(A, $"Daily Stand-up", ws.AddDays(14 + i).AddHours(9), 0.25);
Add(P, "Friseur", ws.AddDays(15).AddHours(16), 1);
Add(S, "Yoga", ws.AddDays(15).AddHours(19), 1);
Add(P, "Steuer abgeben", ws.AddDays(16), 1, all: true);
// optional: two connected accounts (needs radicale-reset.sh running with the fake Google)
var withAccounts = Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_CALDAV_URL") != null;
if (withAccounts)
{
    store.Data.Accounts.Add(new AccountInfo { Kind = AccountKind.ICloud, AppleId = "anna", Secret = SecretStore.Protect("x") });
    store.Data.Accounts.Add(new AccountInfo { Kind = AccountKind.Google, AppleId = "test@gmail.example", Secret = SecretStore.Protect("refresh-1"), ClientId = "test-client.apps.googleusercontent.com", ClientSecret = SecretStore.Protect("GOCSPX-test") });
}
store.Save();

var state = new AppState(store, new SyncService(store));
var win = new Kalendra.Views.MainWindow(state) { Width = 1280, Height = 820 };
win.Show();
if (withAccounts)
{
    for (int i = 0; i < 400 && (state.Sync.IsRunning || state.Sync.Status.StartsWith("Synchron") == false); i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    for (int i = 0; i < 50; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    Console.WriteLine("Sync: " + state.Sync.Status);
}
foreach (var (mode, name) in new[] { (ViewMode.Month, "monat"), (ViewMode.Week, "woche"), (ViewMode.Day, "tag"), (ViewMode.Year, "jahr") })
{
    state.WeekScrollY = 7 * 50;
    state.GoTo(mode == ViewMode.Day ? ws.AddDays(1) : DateTime.Today, mode);
    for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
    var frame = win.CaptureRenderedFrame();
    frame!.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine("saved " + name);
}
// editor dialog
var ed = new Kalendra.Views.EventEditor(store, store.VisibleEventsInYear(DateTime.Today.Year).First(e => e.Title == "Zahnarzt").Clone(), false);
ed.Show(); for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
ed.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "dialog.png"));
Console.WriteLine("saved dialog");

// Google sign-in dialog
_ = Kalendra.Views.AccountDialog.ShowGoogle(win, state.Sync, null);
for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
var gd = win.OwnedWindows.Last();
gd.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "google-anmeldung.png"));
Console.WriteLine("saved google-anmeldung");
gd.Close();
_ = Kalendra.Views.AccountDialog.AddAccount(win, state.Sync);
for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
var chooser = win.OwnedWindows.Last();
chooser.CaptureRenderedFrame()!.Save(Path.Combine(outDir, "kalender-hinzufuegen.png"));
chooser.Close();
_ = Kalendra.Views.AccountDialog.ShowSubscription(win, state.Sync, null);
for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
win.OwnedWindows.Last().CaptureRenderedFrame()!.Save(Path.Combine(outDir, "google-adresse.png"));
Console.WriteLine("saved chooser + adresse");
