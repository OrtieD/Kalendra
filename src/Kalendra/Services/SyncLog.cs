using System.Text;

namespace Kalendra.Services;

/// <summary>
/// Plain-text protocol of every sync step (requests, answers, counts). Never contains the password.
/// File: "sync-protokoll.txt" in the data folder. Kept below ~1 MB.
/// </summary>
public static class SyncLog
{
    static readonly object Gate = new();
    public static string File => Path.Combine(AppPaths.DataDir, "sync-protokoll.txt");

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                var f = File;
                if (System.IO.File.Exists(f) && new FileInfo(f).Length > 1_000_000)
                {
                    var keep = System.IO.File.ReadAllText(f);
                    System.IO.File.WriteAllText(f, keep[^300_000..]);
                }
                System.IO.File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    public static string Short(string? s, int max = 600) =>
        s == null ? "" : (s.Length <= max ? s : s[..max] + " …").Replace("\r", "").Replace("\n", "⏎");

    public static void Open()
    {
        try
        {
            if (!System.IO.File.Exists(File)) Write("Protokoll angelegt.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(File) { UseShellExecute = true });
        }
        catch { }
    }
}
