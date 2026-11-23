using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kalendra.Models;
using Kalendra.Services;

namespace Kalendra.Views;

public enum EditorResult { Cancel, Save, Delete }

/// <summary>Dialog to create or edit an event.</summary>
public class EventEditor : Window
{
    public EventEditor(EventStore store, CalendarEvent ev, bool isNew, bool readOnly = false)
    {
        Title = isNew ? "Neues Ereignis" : "Ereignis bearbeiten";
        Width = 440; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var title = new TextBox { Text = ev.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, Watermark = "Titel", BorderThickness = new Thickness(0, 0, 0, 1) };
        var location = new TextBox { Text = ev.Location, Watermark = "Ort oder Videoanruf hinzufügen" };
        var allDay = new CheckBox { Content = "Ganztägig", IsChecked = ev.AllDay };

        var startDate = new CalendarDatePicker { SelectedDate = ev.Start.Date, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "ddd, dd.MM.yyyy", Width = 170 };
        var endDate = new CalendarDatePicker { SelectedDate = ev.AllDay && ev.End.Date > ev.Start.Date ? ev.End.Date.AddDays(-1) : ev.End.Date, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "ddd, dd.MM.yyyy", Width = 170 };
        var startTime = TimeBox(ev.Start);
        var endTime = TimeBox(ev.End);

        var cals = store.Data.Calendars.Where(c => !c.ReadOnly || c.Id == ev.CalendarId).ToList();
        var calBox = new ComboBox
        {
            ItemsSource = cals.Select(c => CalItem(c)).ToList(),
            SelectedIndex = Math.Max(0, cals.FindIndex(c => c.Id == ev.CalendarId)),
            MinWidth = 200,
        };
        var notes = new TextBox { Text = ev.Notes, Watermark = "Notizen", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };
        var error = new TextBlock { Foreground = Look.Accent, FontSize = 12, IsVisible = false };

        void UpdateTimes() { startTime.IsVisible = endTime.IsVisible = allDay.IsChecked != true; }
        allDay.IsCheckedChanged += (_, _) => UpdateTimes();
        UpdateTimes();
        startDate.SelectedDateChanged += (_, _) =>
        {
            // keep the duration when the start date moves
            if (startDate.SelectedDate is { } s && endDate.SelectedDate is { } e && e < s) endDate.SelectedDate = s;
        };

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 6) };
        void Row(int r, string label, Control c)
        {
            var l = new TextBlock { Text = label, Foreground = Look.Secondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6) };
            Grid.SetRow(l, r); form.Children.Add(l);
            Grid.SetRow(c, r); Grid.SetColumn(c, 1); c.Margin = new Thickness(0, 4); form.Children.Add(c);
        }
        Row(0, "Ort", location);
        Row(1, "", allDay);
        Row(2, "Beginn", HRow(startDate, startTime));
        Row(3, "Ende", HRow(endDate, endTime));
        Row(4, "Kalender", calBox);
        Row(5, "Notizen", notes);

        var save = new Button { Content = isNew ? "Hinzufügen" : "Sichern", IsDefault = true, Classes = { "accent" }, MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var delete = new Button { Content = "Löschen", Foreground = Look.Accent, Background = Brushes.Transparent, IsVisible = !isNew && !readOnly };
        save.IsEnabled = !readOnly;
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(delete, Dock.Left);
        buttons.Children.Add(delete);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(cancel); right.Children.Add(save);
        buttons.Children.Add(right);

        var panel = new StackPanel { Margin = new Thickness(22, 18), Spacing = 6 };
        panel.Children.Add(title);
        panel.Children.Add(form);
        if (ev.IsRecurring)
            panel.Children.Add(new TextBlock { Text = "↻ Wiederholendes Ereignis: Änderungen gelten für die ganze Serie.", FontSize = 12, Foreground = Look.Secondary, TextWrapping = TextWrapping.Wrap });
        if (readOnly)
            panel.Children.Add(new TextBlock { Text = "🔒 Dieser Kalender ist schreibgeschützt.", FontSize = 12, Foreground = Look.Secondary });
        panel.Children.Add(error);
        panel.Children.Add(buttons);
        Content = panel;

        save.Click += (_, _) =>
        {
            var sd = startDate.SelectedDate ?? ev.Start.Date;
            var ed = endDate.SelectedDate ?? sd;
            var isAll = allDay.IsChecked == true;
            DateTime start, end;
            if (isAll) { start = sd.Date; end = ed.Date >= sd.Date ? ed.Date.AddDays(1) : sd.Date.AddDays(1); }
            else
            {
                if (!TryTime(startTime.Text, out var st) || !TryTime(endTime.Text, out var et)) { Show(error, "Bitte Uhrzeit als HH:MM eingeben, z. B. 09:30."); return; }
                start = sd.Date + st; end = ed.Date + et;
                if (end <= start) { Show(error, "Das Ende muss nach dem Beginn liegen."); return; }
            }
            ev.Title = string.IsNullOrWhiteSpace(title.Text) ? "Neues Ereignis" : title.Text.Trim();
            ev.Location = location.Text?.Trim() ?? "";
            ev.Notes = notes.Text ?? "";
            ev.AllDay = isAll; ev.Start = start; ev.End = end;
            ev.CalendarId = cals[Math.Max(0, calBox.SelectedIndex)].Id;
            Close(EditorResult.Save);
        };
        cancel.Click += (_, _) => Close(EditorResult.Cancel);
        delete.Click += (_, _) => Close(EditorResult.Delete);
        Opened += (_, _) => { title.Focus(); title.SelectAll(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(EditorResult.Cancel); };
    }

    static TextBox TimeBox(DateTime t) => new() { Text = t.ToString("HH:mm"), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Center };

    static Control HRow(Control a, Control b)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(a); sp.Children.Add(b);
        return sp;
    }

    static Control CalItem(CalendarSource c)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Look.Solid(c.Color), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    static bool TryTime(string? s, out TimeSpan t)
    {
        t = default;
        s = (s ?? "").Trim().Replace('.', ':');
        if (s.Length is 1 or 2 && int.TryParse(s, out var h) && h is >= 0 and < 24) { t = TimeSpan.FromHours(h); return true; }
        if (s.Length == 4 && !s.Contains(':')) s = s[..2] + ":" + s[2..];
        return TimeSpan.TryParseExact(s, new[] { @"h\:mm", @"hh\:mm" }, null, out t) && t < TimeSpan.FromDays(1);
    }

    static void Show(TextBlock tb, string msg) { tb.Text = msg; tb.IsVisible = true; }
}
