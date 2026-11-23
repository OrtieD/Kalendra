using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Kalendra.Services;

/// <summary>Read-only calendar subscription: an .ics address, e.g. Google's "secret address in iCal format".</summary>
public class FeedAuth : IAuth
{
    public string Url { get; }
    public FeedAuth(string url) => Url = url;
    public Task<AuthenticationHeaderValue> GetAsync(bool forceRefresh) => throw new NotSupportedException();
    public bool CanRefresh => false;
}

public record FeedContent(string? Name, string? Color, Dictionary<string, string> Events);

public static class IcsFeed
{
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(60) };
    static IcsFeed() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Kalendra/0.4");

    /// <summary>Accepts webcal:// and http(s):// addresses; returns null if it is no usable address.</summary>
    public static string? Normalize(string input)
    {
        var s = input.Trim().Trim('"', '<', '>');
        if (s.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s[9..];
        if (s.StartsWith("webcals://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s[10..];
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http") ? u.AbsoluteUri : null;
    }

    /// <summary>For the log: only the host, the rest of the address is secret.</summary>
    public static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?";

    public static async Task<FeedContent> FetchAsync(string url)
    {
        HttpResponseMessage resp;
        var t0 = DateTime.Now;
        try { resp = await Http.GetAsync(url); }
        catch (Exception ex) { throw new CalDavException("Keine Verbindung zur Kalender-Adresse: " + ex.Message, 0); }
        var text = await resp.Content.ReadAsStringAsync();
        SyncLog.Write($"GET {Host(url)}/… → {(int)resp.StatusCode} {resp.StatusCode}, {text.Length / 1024} KB ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
        if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new CalDavException("Die Kalender-Adresse ist nicht (mehr) gültig. Bitte die Privatadresse neu kopieren.", HttpStatusCode.Unauthorized);
        if (!resp.IsSuccessStatusCode) throw new CalDavException($"Kalender-Adresse antwortet mit Fehler {(int)resp.StatusCode}.", resp.StatusCode);
        if (!text.Contains("BEGIN:VCALENDAR"))
            throw new CalDavException("Unter dieser Adresse liegt kein Kalender. Bitte die Adresse im iCal-Format (endet auf .ics) kopieren.", HttpStatusCode.UnsupportedMediaType);
        return Parse(text);
    }

    public static FeedContent Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var timezones = new StringBuilder();
        var groups = new Dictionary<string, StringBuilder>();
        string? name = null, color = null, prodid = null;
        StringBuilder? block = null; string? blockKind = null;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (block == null)
            {
                if (line == "BEGIN:VTIMEZONE" || line == "BEGIN:VEVENT") { block = new StringBuilder(); blockKind = line[6..]; block.Append(line).Append("\r\n"); }
                else if (line.StartsWith("X-WR-CALNAME:")) name = Unescape(line[13..]);
                else if (line.StartsWith("X-APPLE-CALENDAR-COLOR:")) color = line[23..];
                else if (line.StartsWith("PRODID:")) prodid = line;
                continue;
            }
            block.Append(line).Append("\r\n");
            if (line != "END:" + blockKind) continue;
            var b = block.ToString();
            if (blockKind == "VTIMEZONE") timezones.Append(b);
            else
            {
                var uid = Uid(b) ?? "ohne-uid-" + groups.Count;
                if (!groups.TryGetValue(uid, out var g)) groups[uid] = g = new StringBuilder();
                g.Append(b);
            }
            block = null;
        }
        var head = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + (prodid ?? "PRODID:-//Kalendra//Abo//DE") + "\r\n" + timezones;
        var events = groups.ToDictionary(kv => kv.Key, kv => head + kv.Value + "END:VCALENDAR\r\n");
        if (color is { Length: 9 }) color = color[..7];
        return new FeedContent(name, color is { Length: 7 } && color[0] == '#' ? color : null, events);
    }

    static string? Uid(string vevent)
    {
        var unfolded = vevent.Replace("\r\n ", "").Replace("\r\n\t", "");
        foreach (var l in unfolded.Split("\r\n"))
            if (l.StartsWith("UID:")) return l[4..].Trim();
        return null;
    }

    static string Unescape(string s) => s.Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ").Replace("\\\\", "\\");
}
