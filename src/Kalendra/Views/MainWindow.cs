using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kalendra.Models;

namespace Kalendra.Views;

public class MainWindow : Window
{
    readonly AppState _s;
    readonly ContentControl _content = new();
    readonly StackPanel _segments = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _titleMain = new() { FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Look.Text };
    readonly TextBlock _titleSub = new() { FontSize = 26, Foreground = Look.Text, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _titleHint = new() { FontSize = 13, Foreground = Look.Secondary, Margin = new Thickness(12, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom };
    readonly Sidebar _sidebar;

    public MainWindow(AppState state)
    {
        _s = state;
        Title = "Kalendra";
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Kalendra/Assets/kalendra.ico"))); } catch (Exception ex) { CrashReport.Log(ex); }
        Width = 1280; Height = 820; MinWidth = 820; MinHeight = 560;
        Background = Look.Background;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _sidebar = new Sidebar(_s, this);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        var sideBorder = new Border
        {
            Background = Look.Sidebar, BorderBrush = Look.Line, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = _sidebar,
        };
        root.Children.Add(sideBorder);

        var main = new DockPanel();
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        // toolbar
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Height = 52,
            Background = Look.Toolbar,
        };
        var addLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        addLabel.Children.Add(new TextBlock { Text = "＋", FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        addLabel.Children.Add(new TextBlock { Text = "Neues Ereignis", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var addBtn = new Button
        {
            Content = addLabel, Classes = { "newevent" }, Padding = new Thickness(12, 4, 14, 4), MinHeight = 32,
            Background = Look.Accent, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(8),
        };
        ToolTip.SetTip(addBtn, "Neues Ereignis anlegen (Strg+N)");
        addBtn.Click += (_, _) => NewEventNow();
        addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addBtn.Margin = new Thickness(12, 0, 0, 0);
        toolbar.Children.Add(addBtn);

        var segBorder = new Border
        {
            Background = Look.SegmentBg, CornerRadius = new CornerRadius(7), Padding = new Thickness(2),
            Child = _segments, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(segBorder, 1);
        toolbar.Children.Add(segBorder);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0), Spacing = 2 };
        var prev = ToolButton("‹", "Zurück (Strg+←)"); prev.Click += (_, _) => _s.Step(-1);
        var today = ToolButton("Heute", "Heute (Strg+T)"); today.Click += (_, _) => _s.GoTo(DateTime.Today);
        today.FontSize = 13; today.Padding = new Thickness(12, 4);
        var next = ToolButton("›", "Weiter (Strg+→)"); next.Click += (_, _) => _s.Step(1);
        nav.Children.Add(prev); nav.Children.Add(today); nav.Children.Add(next);
        Grid.SetColumn(nav, 2);
        toolbar.Children.Add(nav);

        var toolbarBorder = new Border { BorderBrush = Look.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        main.Children.Add(toolbarBorder);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 14, 20, 10) };
        title.Children.Add(_titleMain); title.Children.Add(_titleSub); title.Children.Add(_titleHint);
        DockPanel.SetDock(title, Dock.Top);
        main.Children.Add(title);

        main.Children.Add(_content);
        Content = root;

        _s.Changed += Refresh;
        _s.EditRequested += (ev, isNew) => _ = OpenEditor(ev, isNew);
        _s.DeleteRequested += ev => _ = AskDelete(ev);
        _s.ErrorRaised += msg => _ = TextPrompt.Info(this, "Kalendra", msg);
        _s.Sync.StatusChanged += () => _sidebar.UpdateSyncStatus();
        Opened += async (_, _) =>
        {
            if (_s.Sync.FirstNeedingLogin is { } acc) await AccountDialog.Relogin(this, _s.Sync, acc);
            else if (!_s.Sync.IsConnected && _s.Store.Data.Items.Count == 0) await AccountDialog.AddAccount(this, _s.Sync);
            _s.Sync.Start();
        };

        // keep the "now" line current
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => { if (_s.Mode is ViewMode.Day or ViewMode.Week) Refresh(); };
        timer.Start();

        KeyDown += OnKey;
        Refresh();
    }

    static Button ToolButton(string text, string tip)
    {
        var b = new Button
        {
            Content = text, FontSize = 20, Padding = new Thickness(10, 0), MinHeight = 30,
            Background = Brushes.Transparent, Foreground = Look.Text, VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(6),
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    void BuildSegments()
    {
        _segments.Children.Clear();
        foreach (var (mode, label) in new[] { (ViewMode.Day, "Tag"), (ViewMode.Week, "Woche"), (ViewMode.Month, "Monat"), (ViewMode.Year, "Jahr") })
        {
            var active = _s.Mode == mode;
            var b = new Button
            {
                Content = label, FontSize = 13, Width = 76, Height = 26,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0), CornerRadius = new CornerRadius(5),
                Background = active ? Brushes.White : Brushes.Transparent,
                Foreground = Look.Text, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            };
            b.Click += (_, _) => _s.SetMode(mode);
            _segments.Children.Add(b);
        }
    }

    void Refresh()
    {
        BuildSegments();
        var c = _s.Current;
        var de = Look.De;
        switch (_s.Mode)
        {
            case ViewMode.Day:
                _titleMain.Text = c.ToString("d. MMMM", de);
                _titleSub.Text = c.Year.ToString();
                _titleHint.Text = c.ToString("dddd", de);
                break;
            case ViewMode.Week:
                var ws = Look.StartOfWeek(c);
                _titleMain.Text = ws.ToString("MMMM", de);
                _titleSub.Text = ws.Year.ToString();
                _titleHint.Text = $"KW {Look.IsoWeek(ws)}";
                break;
            case ViewMode.Month:
                _titleMain.Text = c.ToString("MMMM", de);
                _titleSub.Text = c.Year.ToString();
                _titleHint.Text = "";
                break;
            default:
                _titleMain.Text = c.Year.ToString();
                _titleSub.Text = "";
                _titleHint.Text = "";
                break;
        }
        _content.Content = _s.Mode switch
        {
            ViewMode.Day => new WeekView(_s, 1),
            ViewMode.Week => new WeekView(_s, 7),
            ViewMode.Month => new MonthView(_s),
            _ => (Control)new YearView(_s),
        };
        _sidebar.Refresh();
    }

    void NewEventNow()
    {
        var d = _s.Mode == ViewMode.Year ? DateTime.Today : _s.Current;
        var hour = Math.Clamp(DateTime.Now.Hour + 1, 8, 22);
        _s.RequestNew(d.Date.AddHours(hour));
    }

    async Task OpenEditor(CalendarEvent ev, bool isNew)
    {
        var original = isNew ? null : ev.Clone();
        var dlg = new EventEditor(_s.Store, ev, isNew, _s.IsReadOnly(ev));
        var result = await dlg.ShowDialog<EditorResult>(this);
        if (result == EditorResult.Save) _s.SaveEvent(ev, original);
        else if (result == EditorResult.Delete && original != null) await AskDelete(original);
    }

    async Task AskDelete(CalendarEvent occ)
    {
        if (_s.IsReadOnly(occ)) { await TextPrompt.Info(this, "Schreibgeschützt", "Dieser Kalender ist schreibgeschützt."); return; }
        if (occ.IsRecurring)
        {
            var choice = await TextPrompt.Choose(this, "Wiederholendes Ereignis löschen",
                $"„{occ.Title}“ ist ein wiederholendes Ereignis. Was möchtest du löschen?", "Nur dieses Ereignis", "Alle zukünftigen und vergangenen");
            if (choice == 0) _s.DeleteEvent(occ, onlyThis: true);
            else if (choice == 1) _s.DeleteEvent(occ, onlyThis: false);
        }
        else _s.DeleteEvent(occ, onlyThis: false);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.N when ctrl: NewEventNow(); break;
            case Key.T when ctrl: _s.GoTo(DateTime.Today); break;
            case Key.D1 when ctrl: _s.SetMode(ViewMode.Day); break;
            case Key.D2 when ctrl: _s.SetMode(ViewMode.Week); break;
            case Key.D3 when ctrl: _s.SetMode(ViewMode.Month); break;
            case Key.D4 when ctrl: _s.SetMode(ViewMode.Year); break;
            case Key.Left when ctrl: _s.Step(-1); break;
            case Key.Right when ctrl: _s.Step(1); break;
            case Key.Delete or Key.Back when _s.SelectedId != null:
                if (_s.Store.FindOccurrence(_s.SelectedId) is { } sel) _ = AskDelete(sel);
                break;
            case Key.Enter when _s.SelectedId != null:
                var ev = _s.Store.FindOccurrence(_s.SelectedId);
                if (ev != null) _s.RequestEdit(ev);
                break;
            case Key.Escape: _s.Select(null); break;
            default: return;
        }
        e.Handled = true;
    }
}
