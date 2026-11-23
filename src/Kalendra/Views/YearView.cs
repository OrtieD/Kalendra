using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kalendra.Models;

namespace Kalendra.Views;

public class YearView : UserControl
{
    public YearView(AppState s)
    {
        var year = s.Current.Year;
        var grid = new UniformGrid { Columns = 4, Rows = 3, Margin = new Thickness(16, 0, 16, 12) };
        var busy = s.Store.VisibleEventsInYear(year)
            .SelectMany(e => Enumerable.Range(0, Math.Max(1, (int)Math.Ceiling((e.End.Date - e.Start.Date).TotalDays) + (e.AllDay ? 0 : 1)))
            .Select(i => e.Start.Date.AddDays(i))).ToHashSet();

        for (int m = 1; m <= 12; m++)
        {
            var first = new DateTime(year, m, 1);
            var box = new StackPanel { Margin = new Thickness(10, 6), Spacing = 2 };
            var isCurrent = year == DateTime.Today.Year && m == DateTime.Today.Month;
            var title = new TextBlock
            {
                Text = first.ToString("MMMM", Look.De), FontSize = 17, FontWeight = FontWeight.SemiBold,
                Foreground = isCurrent ? Look.Accent : Look.Text, Margin = new Thickness(4, 0, 0, 4), Cursor = new Cursor(StandardCursorType.Hand),
            };
            title.PointerPressed += (_, _) => s.GoTo(first, ViewMode.Month);
            box.Children.Add(title);

            var days = new UniformGrid { Columns = 7 };
            var start = Look.StartOfWeek(first);
            for (int i = 0; i < 7; i++)
                days.Children.Add(new TextBlock { Text = Look.ShortDay(start.AddDays(i))[..1], FontSize = 10, Foreground = Look.Secondary, HorizontalAlignment = HorizontalAlignment.Center });
            for (int i = 0; i < 42; i++)
            {
                var d = start.AddDays(i);
                if (d.Month != m) { days.Children.Add(new Border()); continue; }
                var cellContent = new Panel();
                cellContent.Children.Add(Look.DayNumber(d, 11, false, bold: busy.Contains(d)));
                var cell = new Border { Child = cellContent, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Height = 22 };
                if (busy.Contains(d) && d != DateTime.Today)
                    cell.Background = Look.Tint("#8E8E93", 0.12);
                cell.CornerRadius = new CornerRadius(11);
                cell.DoubleTapped += (_, _) => s.GoTo(d, ViewMode.Day);
                days.Children.Add(cell);
            }
            box.Children.Add(days);
            grid.Children.Add(box);
        }
        Content = new ScrollViewer { Content = grid };
    }
}
