using System.Runtime.InteropServices;

namespace Kalendra;

/// <summary>Writes a log file and shows a plain Windows message box if the app cannot start.</summary>
public static class CrashReport
{
    public static string LogFile => Path.Combine(Path.GetTempPath(), "Kalendra-Fehler.log");

    public static void Write(Exception ex)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n\n"); } catch { }
        if (OperatingSystem.IsWindows())
        {
            try { MessageBoxW(IntPtr.Zero, $"Kalendra konnte nicht starten:\n\n{ex.Message}\n\nDetails: {LogFile}", "Kalendra – Fehler", 0x10); } catch { }
        }
    }

    /// <summary>Logs a non-fatal error without bothering the user.</summary>
    public static void Log(Exception ex)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (nicht kritisch)\n{ex}\n\n"); } catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
