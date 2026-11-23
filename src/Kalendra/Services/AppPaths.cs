namespace Kalendra.Services;

/// <summary>
/// Decides where data lives. Portable mode: a file "portable.txt" next to the EXE
/// (or a "Daten" folder next to it) keeps everything beside the program, e.g. on a USB stick.
/// Otherwise data goes to %APPDATA%\Kalendra.
/// </summary>
public static class AppPaths
{
    public static string AppDir => AppContext.BaseDirectory;

    public static bool IsPortable =>
        File.Exists(Path.Combine(AppDir, "portable.txt")) || Directory.Exists(Path.Combine(AppDir, "Daten"));

    public static string DataDir
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("KALENDRA_DATA_DIR");
            var dir = !string.IsNullOrEmpty(overrideDir) ? overrideDir
                : IsPortable ? Path.Combine(AppDir, "Daten")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kalendra");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
