using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kalendra;

/// <summary>Colours and small UI helpers in a calm, Apple-like style.</summary>
public static class Look
{
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static readonly Color AccentColor = Color.Parse("#FF3B30");
    public static readonly IBrush Accent = new SolidColorBrush(AccentColor);
    public static readonly IBrush Text = new SolidColorBrush(Color.Parse("#1D1D1F"));
    public static readonly IBrush Secondary = new SolidColorBrush(Color.Parse("#8E8E93"));
    public static readonly IBrush Faint = new SolidColorBrush(Color.Parse("#C7C7CC"));
    public static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E5E5EA"));
    public static readonly IBrush LineSoft = new SolidColorBrush(Color.Parse("#F0F0F3"));
    public static readonly IBrush Background = Brushes.White;
    public static readonly IBrush Weekend = new SolidColorBrush(Color.Parse("#FAFAFB"));
    public static readonly IBrush Sidebar = new SolidColorBrush(Color.Parse("#F2F2F5"));
    public static readonly IBrush Toolbar = new SolidColorBrush(Color.Parse("#F7F7F9"));
    public static readonly IBrush SegmentBg = new SolidColorBrush(Color.Parse("#E4E4E9"));

    public static readonly string[] CalendarColors =
        { "#FF2968", "#FF9500", "#FFCC00", "#63DA38", "#1BADF8", "#CC73E1", "#A2845E" };
    public static readonly string[] CalendarColorNames =
        { "Rot", "Orange", "Gelb", "Grün", "Blau", "Lila", "Braun" };

    public static Color ParseColor(string hex) => Color.TryParse(hex, out var c) ? c : Color.Parse("#1BADF8");
    public static IBrush Solid(string hex) => new SolidColorBrush(ParseColor(hex));
    public static IBrush Tint(string hex, double alpha = 0.18)
    {
        var c = ParseColor(hex);
        // blend with white so the tint is opaque (looks cleaner on grid lines)
        byte Mix(byte v) => (byte)(255 - (255 - v) * alpha);
        return new SolidColorBrush(Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B)));
    }
    public static IBrush Dark(string hex, double factor = 0.55)
    {
        var c = ParseColor(hex);
        return new SolidColorBrush(Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)));
    }

    public static TextBlock Label(string text, double size = 13, FontWeight weight = FontWeight.Normal, IBrush? fg = null) =>
        new() { Text = text, FontSize = size, FontWeight = weight, Foreground = fg ?? Text, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Day number, drawn inside a red circle when it is today.</summary>
    public static Control DayNumber(DateTime day, double size, bool dim, bool bold = false, bool selected = false)
    {
        var isToday = day.Date == DateTime.Today;
        var tb = new TextBlock
        {
            Text = day.Day.ToString(),
            FontSize = size,
            FontWeight = isToday || bold ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = isToday ? Brushes.White : selected ? Brushes.White : dim ? Faint : Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var d = size * 1.85;
        return new Border
        {
            Width = d, Height = d, CornerRadius = new CornerRadius(d / 2),
            Background = isToday ? Accent : selected ? Text : Brushes.Transparent,
            Child = tb,
        };
    }

    public static DateTime StartOfWeek(DateTime d)
    {
        int diff = ((int)d.DayOfWeek + 6) % 7; // Monday first
        return d.Date.AddDays(-diff);
    }

    public static int IsoWeek(DateTime d) => ISOWeek.GetWeekOfYear(d);

    public static string ShortDay(DateTime d) => De.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek).TrimEnd('.');
}
