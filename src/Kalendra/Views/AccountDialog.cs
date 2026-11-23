using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kalendra.Models;
using Kalendra.Services;

namespace Kalendra.Views;

/// <summary>Sign-in dialogs: iCloud (Apple ID + app-specific password) and Google (own OAuth client, sign-in in the browser).</summary>
public static class AccountDialog
{
    /// <summary>Asks which kind of account to add, then shows the matching sign-in.</summary>
    public static async Task AddAccount(Window owner, SyncService sync)
    {
        var panel = Frame("Kalender hinzufügen", "Wo liegen deine Termine? Auf dem Mac siehst du das unter Kalender → Einstellungen → Accounts.");
        var choice = -1;
        Window? w = null;
        void Option(int n, string title, string text)
        {
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Look.Secondary, TextWrapping = TextWrapping.Wrap });
            var b = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8) };
            b.Click += (_, _) => { choice = n; w!.Close(); };
            panel.Children.Add(b);
        }
        Option(0, "Google-Kalender per Adresse (einfach)", "Termine werden nur angezeigt. Du brauchst nur die Privatadresse aus den Google-Kalender-Einstellungen.");
        Option(1, "Google-Konto (bearbeiten)", "Termine in beide Richtungen abgleichen. Braucht einen Zugang aus der Google Cloud Console.");
        Option(2, "iCloud", "Apple-ID und app-spezifisches Passwort.");
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(cancel);
        w = DialogWindow("Kalender hinzufügen", panel);
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
        if (choice == 0) await ShowSubscription(owner, sync, null);
        else if (choice == 1) await ShowGoogle(owner, sync, null);
        else if (choice == 2) await Show(owner, sync, null);
    }

    /// <summary>Signs in again to an existing account (password or token no longer valid).</summary>
    public static Task Relogin(Window owner, SyncService sync, AccountInfo acc) => acc.Kind switch
    {
        AccountKind.Google => ShowGoogle(owner, sync, acc),
        AccountKind.Subscription => ShowSubscription(owner, sync, acc),
        _ => Show(owner, sync, acc),
    };

    public static async Task ShowSubscription(Window owner, SyncService sync, AccountInfo? existing)
    {
        var address = new TextBox { Watermark = "https://calendar.google.com/calendar/ical/…/basic.ics", Width = 340 };
        var error = new TextBlock { Foreground = Look.Accent, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, IsVisible = false };
        var connect = new Button { Content = "Hinzufügen", IsDefault = true, Classes = { "accent" }, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        var later = new Button { Content = "Abbrechen", IsCancel = true };
        var busy = new ProgressBar { IsIndeterminate = true, IsVisible = false, Width = 340 };

        var panel = Frame("Google-Kalender per Adresse",
            "Kalendra zeigt die Termine an und holt Änderungen alle 5 Minuten. Bearbeiten geht weiter in Google oder am Mac.");
        panel.Children.Add(Hint("1. Im Browser calendar.google.com öffnen.\n2. Links bei deinem Kalender auf ⋮ → „Einstellungen und Freigabe“.\n3. Ganz unten „Privatadresse im iCal-Format“ kopieren.\n4. Hier einfügen. Für jeden weiteren Kalender (z. B. Familie) wiederholen."));
        panel.Children.Add(FieldLabel("Privatadresse im iCal-Format", 8));
        panel.Children.Add(address);
        panel.Children.Add(Hint("Die Adresse ist wie ein Passwort: Wer sie kennt, sieht deine Termine. Sie wird verschlüsselt nur auf diesem PC gespeichert."));
        panel.Children.Add(busy);
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(later); buttons.Children.Add(connect);
        panel.Children.Add(buttons);

        var w = DialogWindow("Kalender abonnieren", panel);
        later.Click += (_, _) => w.Close();
        connect.Click += async (_, _) =>
        {
            var text = address.Text?.Trim() ?? "";
            if (text.Length == 0) { error.Text = "Bitte die Adresse einfügen."; error.IsVisible = true; return; }
            if (text.Contains("calendar.google.com/calendar/embed"))
            { error.Text = "Das ist die Adresse zum Einbetten. Bitte weiter unten die „Privatadresse im iCal-Format“ kopieren."; error.IsVisible = true; return; }
            connect.IsEnabled = later.IsEnabled = false; busy.IsVisible = true; error.IsVisible = false;
            try { await sync.ConnectSubscriptionAsync(text, existing); w.Close(); }
            catch (CalDavException ex) { error.Text = ex.Message; error.IsVisible = true; }
            catch (Exception ex) { error.Text = "Fehler: " + ex.Message; error.IsVisible = true; }
            finally { connect.IsEnabled = later.IsEnabled = true; busy.IsVisible = false; }
        };
        w.Opened += (_, _) => address.Focus();
        await w.ShowDialog(owner);
    }

    static StackPanel Frame(string title, string intro)
    {
        var panel = new StackPanel { Margin = new Thickness(24, 20), Spacing = 8, Width = 360 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = intro, TextWrapping = TextWrapping.Wrap, Foreground = Look.Secondary, FontSize = 13, Margin = new Thickness(0, 0, 0, 8),
        });
        return panel;
    }

    static TextBlock FieldLabel(string text, double top = 0) => new() { Text = text, FontSize = 12, Foreground = Look.Secondary, Margin = new Thickness(0, top, 0, 0) };
    static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Look.Secondary, FontSize = 11 };

    static Window DialogWindow(string title, Control content) => new()
    {
        Title = title, Content = content, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = Brushes.White,
    };

    public static async Task Show(Window owner, SyncService sync, AccountInfo? existing)
    {
        var appleId = new TextBox { Text = existing?.AppleId ?? "", Watermark = "name@icloud.com", Width = 340 };
        var password = new TextBox { PasswordChar = '•', Watermark = "abcd-efgh-ijkl-mnop", Width = 340 };
        var error = new TextBlock { Foreground = Look.Accent, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, IsVisible = false };
        var connect = new Button { Content = "Verbinden", IsDefault = true, Classes = { "accent" }, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        var later = new Button { Content = "Später", IsCancel = true };
        var busy = new ProgressBar { IsIndeterminate = true, IsVisible = false, Width = 340 };

        var panel = Frame("Mit iCloud verbinden",
            "Damit erscheinen die iCloud-Kalender von deinem Mac und iPhone hier, und Änderungen werden in beide Richtungen abgeglichen.");
        panel.Children.Add(FieldLabel("Apple-ID"));
        panel.Children.Add(appleId);
        panel.Children.Add(FieldLabel("App-spezifisches Passwort", 6));
        panel.Children.Add(password);
        panel.Children.Add(Hint("Nicht dein normales Apple-Passwort. Erstellen unter account.apple.com → Anmeldung und Sicherheit → App-spezifische Passwörter. Es wird verschlüsselt nur auf diesem PC gespeichert."));
        panel.Children.Add(busy);
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(later); buttons.Children.Add(connect);
        panel.Children.Add(buttons);

        var w = DialogWindow("iCloud-Konto", panel);
        later.Click += (_, _) => w.Close();
        connect.Click += async (_, _) =>
        {
            var id = appleId.Text?.Trim() ?? "";
            var pw = (password.Text ?? "").Replace(" ", "").Trim();
            if (id.Length == 0 || pw.Length == 0) { error.Text = "Bitte Apple-ID und Passwort eingeben."; error.IsVisible = true; return; }
            connect.IsEnabled = later.IsEnabled = false; busy.IsVisible = true; error.IsVisible = false;
            try
            {
                await sync.ConnectAsync(id, pw);
                w.Close();
            }
            catch (CalDavException ex) { error.Text = ex.Message; error.IsVisible = true; }
            catch (Exception ex) { error.Text = "Fehler: " + ex.Message; error.IsVisible = true; }
            finally { connect.IsEnabled = later.IsEnabled = true; busy.IsVisible = false; }
        };
        w.Opened += (_, _) => { if (string.IsNullOrEmpty(appleId.Text)) appleId.Focus(); else password.Focus(); };
        await w.ShowDialog(owner);
    }

    public static async Task ShowGoogle(Window owner, SyncService sync, AccountInfo? existing)
    {
        // reuse the client of an existing Google account so a second sign-in needs no typing
        var known = existing ?? sync.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Google && a.ClientId != null);
        var clientId = new TextBox { Text = known?.ClientId ?? "", Watermark = "…apps.googleusercontent.com", Width = 340 };
        var clientSecret = new TextBox
        {
            PasswordChar = '•', Width = 340, Watermark = "GOCSPX-…",
            Text = known?.ClientSecret != null ? SecretStore.Unprotect(known.ClientSecret) ?? "" : "",
        };
        var error = new TextBlock { Foreground = Look.Accent, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, IsVisible = false };
        var connect = new Button { Content = "Im Browser anmelden", IsDefault = true, Classes = { "accent" }, MinWidth = 150, HorizontalContentAlignment = HorizontalAlignment.Center };
        var later = new Button { Content = "Später", IsCancel = true };
        var busy = new ProgressBar { IsIndeterminate = true, IsVisible = false, Width = 340 };
        var waiting = new TextBlock { Text = "Der Browser wurde geöffnet. Bitte dort mit deinem Google-Konto anmelden und den Zugriff auf den Kalender erlauben …", TextWrapping = TextWrapping.Wrap, FontSize = 12, IsVisible = false };

        var panel = Frame(existing != null ? "Erneut bei Google anmelden" : "Mit Google verbinden",
            "Damit erscheinen deine Google-Kalender (auch die vom Mac, wenn dort ein Google-Konto eingerichtet ist) hier, und Änderungen werden in beide Richtungen abgeglichen.");
        panel.Children.Add(FieldLabel("Client-ID"));
        panel.Children.Add(clientId);
        panel.Children.Add(FieldLabel("Clientschlüssel", 6));
        panel.Children.Add(clientSecret);
        panel.Children.Add(Hint("Beides stammt aus deinem eigenen Google-Cloud-Projekt (APIs & Dienste → Anmeldedaten → OAuth-Client vom Typ „Desktop-App“). Es wird verschlüsselt nur auf diesem PC gespeichert. Dein Google-Passwort gibst du nur im Browser bei Google ein."));
        panel.Children.Add(busy);
        panel.Children.Add(waiting);
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(later); buttons.Children.Add(connect);
        panel.Children.Add(buttons);

        var w = DialogWindow("Google-Konto", panel);
        CancellationTokenSource? cts = null;
        later.Click += (_, _) => { cts?.Cancel(); w.Close(); };
        w.Closing += (_, _) => cts?.Cancel();
        connect.Click += async (_, _) =>
        {
            var id = clientId.Text?.Trim() ?? "";
            var secret = clientSecret.Text?.Trim() ?? "";
            if (id.Length == 0 || secret.Length == 0) { error.Text = "Bitte Client-ID und Clientschlüssel eingeben."; error.IsVisible = true; return; }
            if (!id.EndsWith(".apps.googleusercontent.com")) { error.Text = "Die Client-ID endet normalerweise auf „.apps.googleusercontent.com“. Bitte prüfen."; error.IsVisible = true; return; }
            connect.IsEnabled = false; busy.IsVisible = waiting.IsVisible = true; error.IsVisible = false;
            later.Content = "Abbrechen";
            cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await sync.ConnectGoogleAsync(id, secret, cts.Token);
                w.Activate();
                w.Close();
            }
            catch (OperationCanceledException)
            {
                error.Text = "Anmeldung abgebrochen oder Zeit abgelaufen."; error.IsVisible = true;
            }
            catch (CalDavException ex) { error.Text = ex.Message; error.IsVisible = true; }
            catch (Exception ex) { error.Text = ex.Message; error.IsVisible = true; }
            finally
            {
                connect.IsEnabled = true; busy.IsVisible = waiting.IsVisible = false; later.Content = "Später";
                cts?.Dispose(); cts = null;
            }
        };
        w.Opened += (_, _) => { if (string.IsNullOrEmpty(clientId.Text)) clientId.Focus(); else connect.Focus(); };
        await w.ShowDialog(owner);
    }
}
