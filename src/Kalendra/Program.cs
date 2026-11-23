using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Kalendra.Services;

namespace Kalendra;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Note: do not touch Look (brushes) here. Creating Avalonia objects before the
        // platform is initialised breaks the UI thread and no window appears.
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = de;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = de;
        System.Globalization.CultureInfo.CurrentCulture = de;
        System.Globalization.CultureInfo.CurrentUICulture = de;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashReport.Write(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

public class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // red "new event" button: keep it red (slightly darker) on hover and press instead of Fluent's grey
        foreach (var (state, color) in new[] { (":pointerover", "#E0342A"), (":pressed", "#C42D24") })
        {
            var st = new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Button>().Class("newevent").Class(state).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>());
            st.Setters.Add(new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color))));
            st.Setters.Add(new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Avalonia.Media.Brushes.White));
            Styles.Add(st);
        }
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) CrashReport.Write(ex); };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new EventStore(AppPaths.DataDir);
            store.Load();
            desktop.MainWindow = new Views.MainWindow(new AppState(store, new SyncService(store)));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
