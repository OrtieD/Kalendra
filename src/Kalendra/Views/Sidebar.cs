using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kalendra.Models;
using Kalendra.Services;

namespace Kalendra.Views;

/// <summary>Left column: mini month for quick navigation and the list of calendars with on/off switches.</summary>
public class Sidebar : UserControl
{
    readonly AppState _s;
    readonly Window _owner;
    DateTime _miniMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    DateTime _lastCurrent = DateTime.MinValue;

    readonly TextBlock _status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2, 0, 0) };

    public Sidebar(AppState s, Window owner) { _s = s; _owner = owner; }

    static TextBlock Header(string text, double top) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Look.Secondary,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, top, 0, 0),
    };

    public void UpdateSyncStatus()
    {
        _status.Text = _s.Sync.Status;
        _status.Foreground = _s.Sync.IsError ? Look.Accent : Look.Secondary;
        _status.IsVisible = !string.IsNullOrEmpty(_s.Sync.Status);
        if (_logLink != null) _logLink.IsVisible = _s.Sync.IsConnected || _s.Sync.IsError;
        if (AccountSignature() != _accSig) Refresh();
    }
    string _accSig = "";
    string AccountSignature() => string.Join(";", _s.Sync.Accounts.Select(a => a.Id + (_s.Sync.NeedsLogin(a) ? "!" : "")));
    Button? _logLink;

    public void Refresh()
    {
        if (_s.Current != _lastCurrent) { _miniMonth = new DateTime(_s.Current.Year, _s.Current.Month, 1); _lastCurrent = _s.Current; }

        _accSig = AccountSignature();
        (_status.Parent as Panel)?.Children.Remove(_status);
        var root = new DockPanel { Margin = new Thickness(14, 16, 14, 12) };

        var add = new Button
        {
            Content = "＋  Neuer Kalender", Background = Brushes.Transparent, Foreground = Look.Secondary,
            FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 4),
        };
        add.Click += async (_, _) =>
        {
            var name = await TextPrompt.Ask(_owner, "Neuer Kalender", "Name des Kalenders:", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var used = _s.Store.Data.Calendars.Select(c => c.Color).ToHashSet();
            var color = Look.CalendarColors.FirstOrDefault(c => !used.Contains(c)) ?? Look.CalendarColors[0];
            _s.Store.Data.Calendars.Add(new CalendarSource { Name = name.Trim(), Color = color });
            _s.Store.Touch();
        };
        ToolTip.SetTip(add, "Neuer Kalender, der nur auf diesem PC gespeichert wird");

        var bottom = new StackPanel { Spacing = 4 };
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(add);
        bottom.Children.Add(_status);
        var logLink = new Button { Content = "Protokoll anzeigen", FontSize = 11, Padding = new Thickness(6, 0), Background = Brushes.Transparent, Foreground = Look.Secondary, HorizontalAlignment = HorizontalAlignment.Left };
        logLink.Click += (_, _) => SyncLog.Open();
        _logLink = logLink;
        bottom.Children.Add(logLink);
        var addAcc = new Button
        {
            Content = _s.Sync.IsConnected ? "＋  Konto hinzufügen …" : "☁  Konto verbinden …",
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, FontSize = 13,
        };
        if (!_s.Sync.IsConnected) addAcc.Classes.Add("accent");
        else { addAcc.Background = Brushes.Transparent; addAcc.Foreground = Look.Secondary; addAcc.HorizontalAlignment = HorizontalAlignment.Left; addAcc.Padding = new Thickness(6, 4); }
        addAcc.Click += async (_, _) => { await AccountDialog.AddAccount(_owner, _s.Sync); Refresh(); };
        bottom.Children.Add(addAcc);
        root.Children.Insert(0, bottom);

        var top = new StackPanel { Spacing = 6 };
        top.Children.Add(BuildMiniMonth());
        var local = _s.Store.Data.Calendars.Where(c => !c.IsRemote).ToList();
        foreach (var acc in _s.Sync.Accounts.Where(a => a.Kind != AccountKind.Subscription)) AccountSection(top, acc);
        SubscriptionSection(top);
        if (local.Count > 0)
        {
            var h = Header(_s.Sync.IsConnected ? "AUF DIESEM PC" : "MEINE KALENDER", 0);
            h.Margin = new Thickness(4, 22, 0, 4);
            top.Children.Add(h);
            foreach (var cal in local) top.Children.Add(CalendarRow(cal));
        }
        root.Children.Add(new ScrollViewer { Content = top, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        UpdateSyncStatus();
        Content = root;
    }

    void AccountSection(StackPanel top, AccountInfo acc)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 22, 0, 4) };
        var title = Header(acc.Label.ToUpperInvariant(), 0);
        ToolTip.SetTip(title, acc.AppleId);
        head.Children.Add(title);
        var syncBtn = new Button { Content = "⟳", FontSize = 14, Padding = new Thickness(6, 0), Background = Brushes.Transparent, Foreground = Look.Secondary };
        ToolTip.SetTip(syncBtn, "Jetzt synchronisieren (Rechtsklick: Konto)");
        syncBtn.Click += (_, _) => _ = _s.Sync.SyncAsync();
        var accMenu = new ContextMenu();
        var relogin = new MenuItem { Header = "Erneut anmelden …" };
        relogin.Click += async (_, _) => { await AccountDialog.Relogin(_owner, _s.Sync, acc); Refresh(); };
        var logout = new MenuItem { Header = $"Von {acc.Label} abmelden …" };
        logout.Click += async (_, _) =>
        {
            var ok = await TextPrompt.Confirm(_owner, "Abmelden", $"{acc.Label}-Kalender von diesem PC entfernen? Auf dem Mac und bei {acc.Label} bleibt alles erhalten.");
            if (ok) _s.Sync.Disconnect(acc);
        };
        var log = new MenuItem { Header = "Sync-Protokoll öffnen" };
        log.Click += (_, _) => SyncLog.Open();
        accMenu.ItemsSource = new object[] { new MenuItem { Header = acc.AppleId, IsEnabled = false }, new Separator(), log, relogin, logout };
        syncBtn.ContextMenu = accMenu;
        title.ContextMenu = accMenu;
        Grid.SetColumn(syncBtn, 1);
        head.Children.Add(syncBtn);
        top.Children.Add(head);
        if (_s.Sync.NeedsLogin(acc))
        {
            var again = new Button { Content = $"Erneut bei {acc.Label} anmelden …", Classes = { "accent" }, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 0, 4) };
            again.Click += async (_, _) => { await AccountDialog.Relogin(_owner, _s.Sync, acc); Refresh(); };
            top.Children.Add(again);
        }
        var cals = _s.Store.Data.Calendars.Where(c => c.IsRemote && c.AccountId == acc.Id).ToList();
        foreach (var cal in cals) top.Children.Add(CalendarRow(cal));
        if (cals.Count == 0 && !_s.Sync.NeedsLogin(acc)) top.Children.Add(new TextBlock { Text = "Wird geladen …", FontSize = 12, Foreground = Look.Secondary, Margin = new Thickness(6, 0) });
    }

    void SubscriptionSection(StackPanel top)
    {
        var subs = _s.Sync.Accounts.Where(a => a.Kind == AccountKind.Subscription).ToList();
        if (subs.Count == 0) return;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 22, 0, 4) };
        var title = Header("ABONNIERT (NUR ANZEIGE)", 0);
        head.Children.Add(title);
        var syncBtn = new Button { Content = "⟳", FontSize = 14, Padding = new Thickness(6, 0), Background = Brushes.Transparent, Foreground = Look.Secondary };
        ToolTip.SetTip(syncBtn, "Jetzt aktualisieren");
        syncBtn.Click += (_, _) => _ = _s.Sync.SyncAsync();
        Grid.SetColumn(syncBtn, 1);
        head.Children.Add(syncBtn);
        top.Children.Add(head);
        foreach (var acc in subs)
        {
            if (_s.Sync.NeedsLogin(acc))
            {
                var again = new Button { Content = $"„{acc.AppleId}“: Adresse neu eingeben …", Classes = { "accent" }, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 0, 4) };
                again.Click += async (_, _) => { await AccountDialog.Relogin(_owner, _s.Sync, acc); Refresh(); };
                top.Children.Add(again);
            }
            foreach (var cal in _s.Store.Data.Calendars.Where(c => c.AccountId == acc.Id)) top.Children.Add(CalendarRow(cal, acc));
        }
    }

    Control CalendarRow(CalendarSource cal, AccountInfo? subscription = null)
    {
        var box = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(4),
            Background = cal.Visible ? Look.Solid(cal.Color) : Brushes.Transparent,
            BorderBrush = Look.Solid(cal.Color), BorderThickness = new Thickness(1.5),
            Child = cal.Visible ? new TextBlock { Text = "✓", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        row.Children.Add(box);
        row.Children.Add(Look.Label(cal.Name, 13));
        var item = new Border { Child = row, Padding = new Thickness(6, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        item.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(item).Properties.IsLeftButtonPressed) return;
            cal.Visible = !cal.Visible; _s.Store.Touch();
        };

        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Umbenennen …" };
        rename.Click += async (_, _) =>
        {
            var name = await TextPrompt.Ask(_owner, "Kalender umbenennen", "Neuer Name:", cal.Name);
            if (!string.IsNullOrWhiteSpace(name)) { cal.Name = name.Trim(); CalendarEdited(cal); }
        };
        var colors = new MenuItem { Header = "Farbe" };
        var colorItems = new List<MenuItem>();
        for (int i = 0; i < Look.CalendarColors.Length; i++)
        {
            var hex = Look.CalendarColors[i];
            var mi = new MenuItem { Header = Look.CalendarColorNames[i], Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Look.Solid(hex) } };
            mi.Click += (_, _) => { cal.Color = hex; CalendarEdited(cal); };
            colorItems.Add(mi);
        }
        colors.ItemsSource = colorItems;
        var del = new MenuItem { Header = "Löschen …" };
        del.Click += async (_, _) =>
        {
            if (_s.Store.Data.Calendars.Count <= 1) return;
            var count = _s.Store.CountEvents(cal.Id);
            var ok = await TextPrompt.Confirm(_owner, "Kalender löschen", $"„{cal.Name}“ und {count} Ereignis(se) darin löschen?");
            if (!ok) return;
            _s.Store.Data.Items.RemoveAll(e => e.CalendarId == cal.Id);
            _s.Store.Data.Calendars.Remove(cal);
            _s.Store.Touch();
        };
        if (cal.IsRemote)
        {
            del.Header = "Löschen (bitte am Mac oder auf icloud.com)";
            del.IsEnabled = false;
        }
        if (subscription != null)
        {
            // name and colour stay on this PC; the subscription itself can be removed here
            del = new MenuItem { Header = "Abo entfernen …" };
            del.Click += async (_, _) =>
            {
                var ok = await TextPrompt.Confirm(_owner, "Abo entfernen", $"„{cal.Name}“ nicht mehr anzeigen? Bei Google bleibt alles erhalten.");
                if (ok) _s.Sync.Disconnect(subscription);
            };
        }
        else if (cal.ReadOnly) { rename.IsEnabled = false; colors.IsEnabled = false; }
        menu.ItemsSource = new object[] { rename, colors, new Separator(), del };
        item.ContextMenu = menu;
        return item;
    }

    void CalendarEdited(CalendarSource cal)
    {
        if (cal.IsRemote) cal.PropsDirty = true;
        _s.Store.Touch();
        if (cal.IsRemote && !cal.Href!.StartsWith("abo:")) _ = _s.Sync.SyncAsync();
    }

    Control BuildMiniMonth()
    {
        var panel = new StackPanel { Spacing = 2 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(4, 0, 0, 6) };
        head.Children.Add(Look.Label(_miniMonth.ToString("MMMM yyyy", Look.De), 13, FontWeight.SemiBold));
        var p = SmallButton("‹"); p.Click += (_, _) => { _miniMonth = _miniMonth.AddMonths(-1); Refresh(); };
        var n = SmallButton("›"); n.Click += (_, _) => { _miniMonth = _miniMonth.AddMonths(1); Refresh(); };
        Grid.SetColumn(p, 1); Grid.SetColumn(n, 2);
        head.Children.Add(p); head.Children.Add(n);
        panel.Children.Add(head);

        var grid = new UniformGrid { Columns = 7 };
        var start = Look.StartOfWeek(_miniMonth);
        for (int i = 0; i < 7; i++)
            grid.Children.Add(new TextBlock { Text = Look.ShortDay(start.AddDays(i))[..1], FontSize = 10, Foreground = Look.Secondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
        for (int i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var selected = day == _s.Current && day != DateTime.Today;
            var cell = new Border { Child = Look.DayNumber(day, 11, day.Month != _miniMonth.Month, selected: selected), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            cell.PointerPressed += (_, _) => _s.GoTo(day);
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        return panel;
    }

    static Button SmallButton(string t) => new()
    {
        Content = t, FontSize = 16, Padding = new Thickness(8, 0), Background = Brushes.Transparent, Foreground = Look.Secondary,
    };
}
