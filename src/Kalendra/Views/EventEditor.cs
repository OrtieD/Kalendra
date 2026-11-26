using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        Width = 480; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;

        var title = new TextBox { Text = ev.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, Watermark = "Titel", BorderThickness = new Thickness(0, 0, 0, 1) };
        var location = new TextBox { Text = ev.Location, Watermark = "Ort hinzufügen" };
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
        var error = new TextBlock { Foreground = Look.Accent, FontSize = 12, IsVisible = false };
        // ── online meeting ──
        var meetingLink = ev.MeetingUrl;            // existing or pasted link
        var meetPending = ev.MeetPending;           // Google Meet requested, link not there yet
        var addMeet = false;                        // Google Meet chosen in this dialog
        var editingLink = false;
        var linkBox = new TextBox { Watermark = "Link einfügen, z. B. https://zoom.us/j/…", MinWidth = 250 };
        var meetingHost = new StackPanel { Spacing = 4 };
        CalendarSource SelectedCal() => cals[Math.Max(0, calBox.SelectedIndex)];
        void RenderMeeting()
        {
            meetingHost.Children.Clear();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (addMeet || meetPending && meetingLink.Length == 0)
            {
                row.Children.Add(VideoIcon());
                row.Children.Add(new TextBlock { Text = "Google Meet", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = addMeet ? "Link wird beim Sichern erstellt" : "Link wird erstellt …", Foreground = Look.Secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                if (!readOnly) row.Children.Add(RemoveButton(() => { addMeet = false; meetPending = false; RenderMeeting(); }));
                meetingHost.Children.Add(row);
            }
            else if (editingLink)
            {
                row.Children.Add(linkBox);
                row.Children.Add(RemoveButton(() => { editingLink = false; linkBox.Text = ""; RenderMeeting(); }));
                meetingHost.Children.Add(row);
            }
            else if (meetingLink.Length > 0)
            {
                var link = meetingLink;
                row.Children.Add(VideoIcon());
                row.Children.Add(new TextBlock { Text = Meeting.Name(link), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var join = new Button { Content = "Beitreten", Classes = { "accent" }, Padding = new Thickness(12, 3) };
                join.Click += (_, _) => { try { Meeting.Open(link); } catch (Exception ex) { Show(error, "Link konnte nicht geöffnet werden: " + ex.Message); } };
                row.Children.Add(join);
                var copy = new Button { Content = "Link kopieren", Padding = new Thickness(10, 3) };
                copy.Click += async (_, _) =>
                {
                    if (GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(link); copy.Content = "Kopiert ✓"; }
                };
                row.Children.Add(copy);
                if (!readOnly) row.Children.Add(RemoveButton(() => { meetingLink = ""; meetPending = false; RenderMeeting(); }));
                meetingHost.Children.Add(row);
                var text = new TextBlock { Text = link, FontSize = 11, Foreground = Look.Secondary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 310, HorizontalAlignment = HorizontalAlignment.Left };
                ToolTip.SetTip(text, link);
                meetingHost.Children.Add(text);
            }
            else if (!readOnly)
            {
                var isGoogle = store.IsGoogle(SelectedCal());
                var meetItem = new MenuItem { Header = isGoogle ? "Google Meet (Link automatisch erstellen)" : "Google Meet (nur in Google-Kalendern)", IsEnabled = isGoogle };
                meetItem.Click += (_, _) => { addMeet = true; RenderMeeting(); };
                var pasteItem = new MenuItem { Header = "Link einfügen (Zoom, Teams, Webex …)" };
                pasteItem.Click += (_, _) => { editingLink = true; RenderMeeting(); linkBox.Focus(); };
                var add = new Button
                {
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { VideoIcon(Look.Text), new TextBlock { Text = "Online-Meeting hinzufügen  ▾" } } },
                    Flyout = new MenuFlyout { Items = { meetItem, pasteItem }, Placement = PlacementMode.BottomEdgeAlignedLeft },
                    Padding = new Thickness(10, 4),
                };
                meetingHost.Children.Add(add);
            }
            else meetingHost.Children.Add(new TextBlock { Text = "–", Foreground = Look.Secondary });
        }
        calBox.SelectionChanged += (_, _) => { if (!addMeet && !editingLink && meetingLink.Length == 0) RenderMeeting(); };
        RenderMeeting();

        var notes = new TextBox { Text = ev.Notes, Watermark = "Notizen", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80 };

        void UpdateTimes() { startTime.IsVisible = endTime.IsVisible = allDay.IsChecked != true; }
        allDay.IsCheckedChanged += (_, _) => UpdateTimes();
        UpdateTimes();
        startDate.SelectedDateChanged += (_, _) =>
        {
            // keep the duration when the start date moves
            if (startDate.SelectedDate is { } s && endDate.SelectedDate is { } e && e < s) endDate.SelectedDate = s;
        };

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 6) };
        void Row(int r, string label, Control c)
        {
            var l = new TextBlock { Text = label, Foreground = Look.Secondary, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6) };
            Grid.SetRow(l, r); form.Children.Add(l);
            Grid.SetRow(c, r); Grid.SetColumn(c, 1); c.Margin = new Thickness(0, 4); form.Children.Add(c);
        }
        Row(0, "Ort", location);
        Row(1, "Online", meetingHost);
        Row(2, "", allDay);
        Row(3, "Beginn", HRow(startDate, startTime));
        Row(4, "Ende", HRow(endDate, endTime));
        Row(5, "Kalender", calBox);
        Row(6, "Notizen", notes);

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
            if (editingLink && !string.IsNullOrWhiteSpace(linkBox.Text))
            {
                if (Meeting.Normalize(linkBox.Text) is not { } pasted) { Show(error, "Bitte einen gültigen Meeting-Link eingeben (beginnt mit https://)."); return; }
                meetingLink = pasted;
            }
            if (addMeet && !store.IsGoogle(SelectedCal())) { Show(error, "Google Meet geht nur in Google-Kalendern. Bitte einen Google-Kalender wählen oder einen Link einfügen."); return; }
            ev.MeetingUrl = addMeet ? "" : meetingLink;
            ev.AddGoogleMeet = addMeet;
            ev.MeetPending = meetPending;
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

    static readonly Geometry VideoGeometry = Geometry.Parse("M1,5 C1,4 2,3 3,3 L13,3 C14,3 15,4 15,5 L15,15 C15,16 14,17 13,17 L3,17 C2,17 1,16 1,15 Z M16.5,8.5 L22,5 L22,15 L16.5,11.5 Z");

    /// <summary>Small video-camera symbol.</summary>
    public static Control VideoIcon(IBrush? fill = null, double size = 16) =>
        new PathIcon { Data = VideoGeometry, Width = size, Height = size * 0.8, Foreground = fill ?? Look.Solid("#34C759"), VerticalAlignment = VerticalAlignment.Center };

    static Button RemoveButton(Action onClick)
    {
        var b = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Look.Secondary, Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, "Online-Meeting entfernen");
        b.Click += (_, _) => onClick();
        return b;
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
