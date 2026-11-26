using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Kalendra.Models;

namespace Kalendra.Views;

public class MonthView : UserControl
{
    readonly AppState _s;
    readonly Grid _body = new();
    DateTime _first;
    int _weeks;

    // drag state
    CalendarEvent? _drag;
    Point _dragStart;
    bool _dragging;

    public MonthView(AppState s)
    {
        _s = s;
        var monthStart = new DateTime(s.Current.Year, s.Current.Month, 1);
        _first = Look.StartOfWeek(monthStart);
        var last = monthStart.AddMonths(1).AddDays(-1);
        _weeks = (int)Math.Ceiling(((last - _first).Days + 1) / 7.0);

        var root = new DockPanel();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), Margin = new Thickness(0, 0, 0, 4) };
        for (int i = 0; i < 7; i++)
        {
            var t = new TextBlock { Text = Look.ShortDay(_first.AddDays(i)), FontSize = 12, Foreground = Look.Secondary, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(t, i);
            header.Children.Add(t);
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _body.ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*");
        for (int r = 0; r < _weeks; r++) _body.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        for (int i = 0; i < _weeks * 7; i++)
        {
            var day = _first.AddDays(i);
            var cell = BuildCell(day, monthStart.Month, i % 7 >= 5);
            Grid.SetRow(cell, i / 7);
            Grid.SetColumn(cell, i % 7);
            _body.Children.Add(cell);
        }
        var bodyBorder = new Border { BorderBrush = Look.Line, BorderThickness = new Thickness(0, 0, 0, 0), Child = _body };
        root.Children.Add(bodyBorder);
        Content = root;

        _body.PointerMoved += OnDragMove;
        _body.PointerReleased += OnDragEnd;
    }

    Control BuildCell(DateTime day, int month, bool weekend)
    {
        var inMonth = day.Month == month;
        var stack = new OverflowStack();

        var numberRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 5, 2) };
        if (day.Day == 1)
            numberRow.Children.Add(new TextBlock { Text = day.ToString("MMM", Look.De).TrimEnd('.'), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = inMonth ? Look.Text : Look.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
        numberRow.Children.Add(Look.DayNumber(day, 13, !inMonth));

        var dock = new DockPanel();
        DockPanel.SetDock(numberRow, Dock.Top);
        dock.Children.Add(numberRow);
        dock.Children.Add(stack);

        foreach (var ev in _s.Store.VisibleEventsOn(day)) stack.Children.Add(Chip(ev, day));

        var cell = new Border
        {
            BorderBrush = Look.Line, BorderThickness = new Thickness(0, 1, day.DayOfWeek == DayOfWeek.Sunday ? 0 : 1, 0),
            Background = weekend ? Look.Weekend : Look.Background,
            Child = dock, ClipToBounds = true,
        };
        cell.DoubleTapped += (_, e) => { if (e.Source == cell || e.Source is Panel || e.Source is Border b && b == cell) _s.RequestNew(day.AddHours(9)); };
        cell.PointerPressed += (_, e) => { if (e.Source is Panel || e.Source == cell) _s.Select(null); };
        return cell;
    }

    Control Chip(CalendarEvent ev, DateTime day)
    {
        var cal = _s.Store.Calendar(ev.CalendarId);
        var color = cal?.Color ?? "#1BADF8";
        var selected = ev.Id == _s.SelectedId;
        Control content;
        Border chip;
        if (ev.AllDay || ev.End - ev.Start >= TimeSpan.FromHours(24))
        {
            content = new TextBlock { Text = ev.Title, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = selected ? Brushes.White : Look.Dark(color), TextTrimming = TextTrimming.CharacterEllipsis };
            chip = new Border { Background = selected ? Look.Solid(color) : Look.Tint(color, 0.28), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1), Margin = new Thickness(4, 1), Child = content };
        }
        else
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            g.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = selected ? Brushes.White : Look.Solid(color), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            var title = new TextBlock { Text = ev.Title, FontSize = 12, Foreground = selected ? Brushes.White : Look.Text, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(title, 1);
            g.Children.Add(title);
            if (ev.Start.Date == day.Date)
            {
                var time = new TextBlock { Text = ev.Start.ToString("HH:mm"), FontSize = 11, Foreground = selected ? Brushes.White : Look.Secondary, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(time, 2);
                g.Children.Add(time);
            }
            chip = new Border { Background = selected ? Look.Solid(color) : Brushes.Transparent, CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1), Margin = new Thickness(4, 0), Child = g };
        }
        ToolTip.SetTip(chip, ev.AllDay ? ev.Title : $"{ev.Title}\n{ev.Start:HH:mm} – {ev.End:HH:mm}{(string.IsNullOrEmpty(ev.Location) ? "" : "\n" + ev.Location)}{WeekView.MeetingTip(ev)}");
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { _s.RequestEdit(ev); return; }
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            _drag = ev; _dragStart = e.GetPosition(_body); _dragging = false;
            e.Pointer.Capture(_body);
            _s.SelectedId = ev.Id;
        };
        chip.Tag = ev.Id;
        return chip;
    }

    void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (_drag == null) return;
        var p = e.GetPosition(_body);
        if (!_dragging && Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) > 6) { _dragging = true; Cursor = new Cursor(StandardCursorType.SizeAll); }
    }

    void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == null) return;
        var ev = _drag; _drag = null;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
        if (!_dragging) { _s.Notify(); return; }
        _dragging = false;
        var from = CellIndex(_dragStart);
        var to = CellIndex(e.GetPosition(_body));
        if (from < 0 || to < 0 || from == to) { _s.Notify(); return; }
        var shift = TimeSpan.FromDays(to - from);
        _s.MoveEvent(ev, shift, TimeSpan.Zero);
    }

    int CellIndex(Point p)
    {
        var w = _body.Bounds.Width / 7; var h = _body.Bounds.Height / _weeks;
        if (p.X < 0 || p.Y < 0 || w <= 0 || h <= 0) return -1;
        int c = (int)(p.X / w), r = (int)(p.Y / h);
        if (c > 6 || r >= _weeks) return -1;
        return r * 7 + c;
    }
}

/// <summary>Vertical stack that hides what does not fit and shows "n weitere" instead.</summary>
public class OverflowStack : Panel
{
    readonly TextBlock _more = new() { FontSize = 11, Foreground = Look.Secondary, Margin = new Thickness(10, 0, 0, 0) };

    public OverflowStack() { Background = Brushes.Transparent; Children.Add(_more); }

    protected override Size MeasureOverride(Size available)
    {
        foreach (var c in Children) c.Measure(new Size(available.Width, double.PositiveInfinity));
        _more.Measure(new Size(available.Width, double.PositiveInfinity));
        return new Size(available.Width, double.IsInfinity(available.Height) ? Children.Sum(c => c.DesiredSize.Height) : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        int shown = 0;
        var total = Children.Count(c => c != _more);
        var items = Children.Where(c => c != _more).ToList();
        var moreH = _more.DesiredSize.Height;
        for (int i = 0; i < items.Count; i++)
        {
            var h = items[i].DesiredSize.Height;
            var remainingAfter = items.Count - i - 1;
            var need = y + h + (remainingAfter > 0 ? moreH : 0);
            if (need > final.Height && i < items.Count) break;
            items[i].Arrange(new Rect(0, y, final.Width, h));
            y += h; shown++;
        }
        for (int i = shown; i < items.Count; i++) items[i].Arrange(new Rect(0, 0, 0, 0));
        if (shown < total)
        {
            _more.Text = $"{total - shown} weitere …";
            _more.Arrange(new Rect(0, y, final.Width, moreH));
        }
        else _more.Arrange(new Rect(0, 0, 0, 0));
        return final;
    }
}
