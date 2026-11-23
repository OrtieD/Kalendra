using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kalendra.Models;

namespace Kalendra.Views;

/// <summary>Day (1 column) or week (7 columns) view with a time grid. Events can be dragged and resized.</summary>
public class WeekView : UserControl
{
    public const double HourH = 50;
    const double Gutter = 60;
    const int Snap = 15;

    readonly AppState _s;
    readonly int _days;
    readonly DateTime _start;
    readonly Grid _bodyGrid;
    readonly ScrollViewer _scroll;
    readonly DayPanel[] _cols;

    // drag state
    enum DragKind { None, Move, Resize }
    DragKind _kind;
    CalendarEvent? _drag;
    Control? _dragCtl;
    Point _origin;
    bool _moved;
    int _dayOffset, _minuteOffset;

    public WeekView(AppState s, int days)
    {
        _s = s; _days = days;
        _start = days == 7 ? Look.StartOfWeek(s.Current) : s.Current.Date;
        var colDefs = "60," + string.Join(",", Enumerable.Repeat("*", days));

        var root = new DockPanel();

        // header with day names
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(colDefs), Margin = new Thickness(0, 0, 14, 0) };
        for (int i = 0; i < days; i++)
        {
            var d = _start.AddDays(i);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = days == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center, Spacing = 4, Margin = new Thickness(days == 1 ? 10 : 0, 0, 0, 6) };
            sp.Children.Add(Look.Label(Look.ShortDay(d), 13, FontWeight.Normal, d == DateTime.Today ? Look.Accent : Look.Text));
            sp.Children.Add(Look.DayNumber(d, 13, false));
            var cell = new Border { Child = sp, Background = Brushes.Transparent, Cursor = days > 1 ? new Cursor(StandardCursorType.Hand) : Cursor.Default };
            if (days > 1) cell.PointerPressed += (_, _) => _s.GoTo(d, ViewMode.Day);
            Grid.SetColumn(cell, i + 1);
            header.Children.Add(cell);
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // all-day row
        var allDay = new Grid { ColumnDefinitions = new ColumnDefinitions(colDefs), Margin = new Thickness(0, 0, 14, 0), MinHeight = 24 };
        var adLabel = new TextBlock { Text = "ganztägig", FontSize = 10, Foreground = Look.Secondary, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 8, 0) };
        allDay.Children.Add(adLabel);
        for (int i = 0; i < days; i++)
        {
            var d = _start.AddDays(i);
            var stack = new StackPanel { Spacing = 1, Margin = new Thickness(1, 2) };
            foreach (var ev in _s.Store.VisibleEventsOn(d).Where(e => e.AllDay)) stack.Children.Add(AllDayChip(ev));
            var cell = new Border { Child = stack, BorderBrush = Look.Line, BorderThickness = new Thickness(1, 0, 0, 0), Background = Brushes.Transparent };
            cell.DoubleTapped += (_, e) => { if (e.Source == cell || e.Source == stack) _s.RequestNew(d, allDay: true); };
            Grid.SetColumn(cell, i + 1);
            allDay.Children.Add(cell);
        }
        var allDayBorder = new Border { BorderBrush = Look.Line, BorderThickness = new Thickness(0, 1, 0, 1), Child = allDay };
        DockPanel.SetDock(allDayBorder, Dock.Top);
        root.Children.Add(allDayBorder);

        // time grid
        _bodyGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(colDefs), Height = 24 * HourH };
        var lines = new GridLines(days, _start) { };
        Grid.SetColumnSpan(lines, days + 1);
        _bodyGrid.Children.Add(lines);

        var gutter = new Canvas();
        for (int h = 1; h < 24; h++)
        {
            var t = new TextBlock { Text = $"{h:00}:00", FontSize = 11, Foreground = Look.Secondary, Width = Gutter - 10, TextAlignment = TextAlignment.Right };
            Canvas.SetTop(t, h * HourH - 8);
            gutter.Children.Add(t);
        }
        _bodyGrid.Children.Add(gutter);

        _cols = new DayPanel[days];
        for (int i = 0; i < days; i++)
        {
            var d = _start.AddDays(i);
            var panel = new DayPanel();
            _cols[i] = panel;
            Grid.SetColumn(panel, i + 1);
            _bodyGrid.Children.Add(panel);
            LayoutDay(panel, d);
            panel.DoubleTapped += (_, e) =>
            {
                if (e.Source != panel) return;
                var y = e.GetPosition(panel).Y;
                var minutes = (int)(y / HourH * 60) / 30 * 30;
                _s.RequestNew(d.AddMinutes(minutes));
            };
            panel.PointerPressed += (_, e) => { if (e.Source == panel) _s.Select(null); };

            if (d == DateTime.Today)
            {
                var now = DateTime.Now;
                var y = (now.Hour * 60 + now.Minute) / 60.0 * HourH;
                var line = new Border { Height = 2, Background = Look.Accent, IsHitTestVisible = false };
                panel.Children.Add(line);
                DayPanel.SetSlot(line, new Slot(y - 1, y + 1, 0, 1, true));
                var dot = new Ellipse { Width = 10, Height = 10, Fill = Look.Accent, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
                panel.Children.Add(dot);
                DayPanel.SetSlot(dot, new Slot(y - 5, y + 5, 0, 1, true, -5, 10));
                var lbl = new Border { Background = Look.Background, Child = new TextBlock { Text = now.ToString("HH:mm"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Look.Accent, Width = Gutter - 10, TextAlignment = TextAlignment.Right } };
                Canvas.SetTop(lbl, y - 8);
                gutter.Children.Add(lbl);
            }
        }

        _scroll = new ScrollViewer { Content = _bodyGrid, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible };
        var restored = false;
        _scroll.ScrollChanged += (_, _) => { if (restored) _s.WeekScrollY = _scroll.Offset.Y; };
        root.Children.Add(_scroll);
        Content = root;

        _bodyGrid.PointerMoved += OnDragMove;
        _bodyGrid.PointerReleased += OnDragEnd;

        _scroll.LayoutUpdated += (_, _) =>
        {
            if (restored || _scroll.Extent.Height <= _scroll.Viewport.Height || _scroll.Viewport.Height <= 0) return;
            var y = _s.WeekScrollY >= 0 ? _s.WeekScrollY : Math.Min(7 * HourH, Math.Max(0, (DateTime.Now.Hour - 2) * HourH));
            restored = true;
            _scroll.Offset = new Vector(0, y);
        };
    }

    Control AllDayChip(CalendarEvent ev)
    {
        var color = _s.Store.Calendar(ev.CalendarId)?.Color ?? "#1BADF8";
        var sel = ev.Id == _s.SelectedId;
        var b = new Border
        {
            Background = sel ? Look.Solid(color) : Look.Tint(color, 0.28), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1),
            Child = new TextBlock { Text = ev.Title, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = sel ? Brushes.White : Look.Dark(color), TextTrimming = TextTrimming.CharacterEllipsis },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; if (e.ClickCount == 2) _s.RequestEdit(ev); else _s.Select(ev.Id); };
        return b;
    }

    void LayoutDay(DayPanel panel, DateTime day)
    {
        var dayStart = day.Date; var dayEnd = dayStart.AddDays(1);
        var items = _s.Store.VisibleEventsOn(day).Where(e => !e.AllDay)
            .Select(e => (ev: e, s: e.Start < dayStart ? dayStart : e.Start, en: e.End > dayEnd ? dayEnd : e.End))
            .Select(x => (x.ev, x.s, en: x.en <= x.s ? x.s.AddMinutes(15) : x.en))
            .OrderBy(x => x.s).ThenByDescending(x => x.en).ToList();

        // overlap clusters → columns
        var clusters = new List<List<(CalendarEvent ev, DateTime s, DateTime en, int col)>>();
        List<(CalendarEvent ev, DateTime s, DateTime en, int col)>? current = null;
        DateTime clusterEnd = DateTime.MinValue;
        foreach (var it in items)
        {
            if (current == null || it.s >= clusterEnd) { current = new(); clusters.Add(current); clusterEnd = it.en; }
            var used = current.Where(c => c.en > it.s).Select(c => c.col).ToHashSet();
            int col = 0; while (used.Contains(col)) col++;
            current.Add((it.ev, it.s, it.en, col));
            if (it.en > clusterEnd) clusterEnd = it.en;
        }
        foreach (var cl in clusters)
        {
            var cols = cl.Max(c => c.col) + 1;
            foreach (var c in cl)
            {
                var top = (c.s - dayStart).TotalMinutes / 60 * HourH;
                var bottom = (c.en - dayStart).TotalMinutes / 60 * HourH;
                var ctl = EventBlock(c.ev, bottom - top);
                panel.Children.Add(ctl);
                DayPanel.SetSlot(ctl, new Slot(top, bottom, c.col, cols, false));
            }
        }
    }

    Control EventBlock(CalendarEvent ev, double height)
    {
        var color = _s.Store.Calendar(ev.CalendarId)?.Color ?? "#1BADF8";
        var sel = ev.Id == _s.SelectedId;
        var fg = sel ? Brushes.White : Look.Dark(color);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = ev.Title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = height > 40 ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxHeight = Math.Max(16, height - 20) });
        if (height >= 34)
            text.Children.Add(new TextBlock { Text = ev.Start.ToString("HH:mm") + (string.IsNullOrEmpty(ev.Location) ? "" : " · " + ev.Location), FontSize = 11, Foreground = fg, Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis });
        var inner = new Border
        {
            Background = sel ? Look.Solid(color) : Look.Tint(color, 0.22),
            BorderBrush = Look.Solid(color), BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 4, 2), Child = text, ClipToBounds = true,
            Margin = new Thickness(1, 1, 2, 1),
        };
        var grip = new Border { Height = 6, VerticalAlignment = VerticalAlignment.Bottom, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        var host = new Panel { Children = { inner, grip } };
        ToolTip.SetTip(host, $"{ev.Title}\n{ev.Start:HH:mm} – {ev.End:HH:mm}{(string.IsNullOrEmpty(ev.Location) ? "" : "\n" + ev.Location)}");

        host.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { _s.RequestEdit(ev); return; }
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
            _drag = ev; _dragCtl = host; _moved = false; _dayOffset = 0; _minuteOffset = 0;
            _kind = e.Source == grip ? DragKind.Resize : DragKind.Move;
            _origin = e.GetPosition(_bodyGrid);
            _s.SelectedId = ev.Id;
            e.Pointer.Capture(_bodyGrid);
        };
        return host;
    }

    double ColWidth => (_bodyGrid.Bounds.Width - Gutter) / _days;

    void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_drag == null || _dragCtl == null) return;
        var p = e.GetPosition(_bodyGrid);
        var dx = p.X - _origin.X; var dy = p.Y - _origin.Y;
        if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 5) return;
        _moved = true;
        _minuteOffset = (int)Math.Round(dy / HourH * 60 / Snap) * Snap;
        if (_kind == DragKind.Move)
        {
            var origDay = (int)Math.Floor((_origin.X - Gutter) / ColWidth);
            var newDay = Math.Clamp((int)Math.Floor((p.X - Gutter) / ColWidth), 0, _days - 1);
            _dayOffset = newDay - origDay;
            _dragCtl.RenderTransform = new TranslateTransform(_dayOffset * ColWidth, _minuteOffset / 60.0 * HourH);
            _dragCtl.Opacity = 0.8;
        }
        else
        {
            var slot = DayPanel.GetSlot(_dragCtl);
            var orig = slot.Original ?? slot;
            var newBottom = Math.Max(orig.Top + HourH / 4, orig.Bottom + _minuteOffset / 60.0 * HourH);
            DayPanel.SetSlot(_dragCtl, orig with { Bottom = newBottom, Original = orig });
            (_dragCtl.Parent as Control)?.InvalidateArrange();
        }
    }

    void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == null) return;
        var ev = _drag; _drag = null; _dragCtl = null;
        e.Pointer.Capture(null);
        if (!_moved || (_dayOffset == 0 && _minuteOffset == 0)) { _s.Notify(); return; }
        if (_kind == DragKind.Move)
            _s.MoveEvent(ev, TimeSpan.FromDays(_dayOffset) + TimeSpan.FromMinutes(_minuteOffset), TimeSpan.Zero);
        else
        {
            var newEnd = ev.End.AddMinutes(_minuteOffset);
            if (newEnd <= ev.Start.AddMinutes(Snap)) newEnd = ev.Start.AddMinutes(Snap);
            _s.MoveEvent(ev, TimeSpan.Zero, newEnd - ev.End);
        }
    }
}

public record Slot(double Top, double Bottom, int Col, int Cols, bool FullWidth, double XOffset = 0, double FixedWidth = 0)
{
    public Slot? Original { get; init; }
}

/// <summary>Positions events inside one day column by time and overlap column.</summary>
public class DayPanel : Panel
{
    public static readonly AttachedProperty<Slot> SlotProperty =
        AvaloniaProperty.RegisterAttached<DayPanel, Control, Slot>("Slot", new Slot(0, 0, 0, 1, false));
    public static Slot GetSlot(Control c) => c.GetValue(SlotProperty);
    public static void SetSlot(Control c, Slot s) => c.SetValue(SlotProperty, s);

    public DayPanel() { Background = Brushes.Transparent; }

    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children)
        {
            var s = GetSlot(c);
            c.Measure(new Size(double.IsInfinity(available.Width) ? 200 : available.Width, Math.Max(0, s.Bottom - s.Top)));
        }
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in Children)
        {
            var s = GetSlot(c);
            if (s.FullWidth)
            {
                var w = s.FixedWidth > 0 ? s.FixedWidth : final.Width;
                c.Arrange(new Rect(s.XOffset, s.Top, w, Math.Max(0, s.Bottom - s.Top)));
                continue;
            }
            var colW = (final.Width - 4) / s.Cols;
            // slight overlap like Apple Calendar so narrow columns stay readable
            var width = s.Col == s.Cols - 1 ? colW : colW * 1.0;
            c.Arrange(new Rect(s.Col * colW, s.Top, width, Math.Max(14, s.Bottom - s.Top)));
        }
        return final;
    }
}

/// <summary>Background: hour lines, half-hour hairlines, column separators, weekend shading.</summary>
public class GridLines : Control
{
    readonly int _days;
    readonly DateTime _start;
    public GridLines(int days, DateTime start) { _days = days; _start = start; IsHitTestVisible = false; }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var gutter = 60.0;
        var colW = (w - gutter) / _days;
        for (int i = 0; i < _days; i++)
        {
            var d = _start.AddDays(i);
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && _days > 1)
                ctx.FillRectangle(Look.Weekend, new Rect(gutter + i * colW, 0, colW, Bounds.Height));
        }
        var hour = new Pen(Look.Line, 1);
        var half = new Pen(Look.LineSoft, 1);
        for (int h = 0; h < 24; h++)
        {
            var y = Math.Round(h * WeekView.HourH) + 0.5;
            ctx.DrawLine(hour, new Point(gutter - 6, y), new Point(w, y));
            var y2 = Math.Round(h * WeekView.HourH + WeekView.HourH / 2) + 0.5;
            ctx.DrawLine(half, new Point(gutter, y2), new Point(w, y2));
        }
        for (int i = 0; i < _days; i++)
        {
            var x = Math.Round(gutter + i * colW) + 0.5;
            ctx.DrawLine(hour, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }
}
