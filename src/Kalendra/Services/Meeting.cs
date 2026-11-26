using System.Text.RegularExpressions;

namespace Kalendra.Services;

/// <summary>
/// Online meetings (Google Meet, Zoom, Teams …): finding the link in an event, naming the service,
/// and the line Kalendra writes into the notes so the link also shows up on the Mac and on Google.
/// </summary>
public static class Meeting
{
    /// <summary>Kalendra writes this line at the top of the notes; it is hidden again when reading.</summary>
    public const string NotePrefix = "Online-Meeting: ";

    static readonly (string Host, string Name)[] Providers =
    {
        ("meet.google.com", "Google Meet"), ("zoom.us", "Zoom"), ("zoom.com", "Zoom"), ("zoomgov.com", "Zoom"),
        ("teams.microsoft.com", "Microsoft Teams"), ("teams.live.com", "Microsoft Teams"), ("teams.cloud.microsoft", "Microsoft Teams"),
        ("webex.com", "Webex"), ("facetime.apple.com", "FaceTime"), ("whereby.com", "Whereby"), ("meet.jit.si", "Jitsi Meet"),
        ("gotomeeting.com", "GoTo Meeting"), ("meet.goto.com", "GoTo Meeting"), ("app.goto.com", "GoTo Meeting"),
        ("skype.com", "Skype"), ("chime.aws", "Amazon Chime"), ("bluejeans.com", "BlueJeans"), ("discord.gg", "Discord"),
    };

    static readonly Regex LinkRx = new(@"https?://[^\s<>""'()\[\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Block Google appends to the description of events with a Meet conference.</summary>
    static readonly Regex GoogleBlockRx = new(@"\s*-::~[:~]*::-[\s\S]*?-::~[:~]*::-\s*", RegexOptions.Compiled);

    /// <summary>Name of the video service, or null if the host is not a known meeting service.</summary>
    public static string? ProviderName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return null;
        var host = u.Host.ToLowerInvariant();
        foreach (var (h, n) in Providers)
            if (host == h || host.EndsWith("." + h)) return n;
        return null;
    }

    public static string Name(string? url) => ProviderName(url) ?? "Online-Meeting";

    /// <summary>Turns user input into a link (adds https:// if missing). Null if it is not a web address.</summary>
    public static string? Normalize(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return null;
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http") || !u.Host.Contains('.')) return null;
        return s;
    }

    /// <summary>First link to a known meeting service in a text (location or notes).</summary>
    public static string? FindIn(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (Match m in LinkRx.Matches(text))
        {
            var link = m.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (ProviderName(link) != null) return link;
        }
        return null;
    }

    /// <summary>Link from Kalendra's own "Online-Meeting: …" line at the top of the notes.</summary>
    public static string? FromNoteLine(string? notes)
    {
        if (notes == null) return null;
        var first = notes.TrimStart().Split('\n')[0].Trim();
        return first.StartsWith(NotePrefix, StringComparison.Ordinal) ? Normalize(first[NotePrefix.Length..]) : null;
    }

    /// <summary>The notes as the user wrote them: without Kalendra's link line and without Google's Meet block.</summary>
    public static string CleanNotes(string? notes)
    {
        var s = (notes ?? "").Replace("\r\n", "\n");
        if (FromNoteLine(s) != null)
        {
            s = s.TrimStart();
            var nl = s.IndexOf('\n');
            s = nl < 0 ? "" : s[(nl + 1)..].TrimStart('\n');
        }
        s = GoogleBlockRx.Replace(s, "\n").Trim('\n');
        return s;
    }

    public static string WithNoteLine(string notes, string url) =>
        NotePrefix + url + (string.IsNullOrWhiteSpace(notes) ? "" : "\n\n" + notes);

    /// <summary>Opens a link in the default browser (or the meeting app). Replaceable for tests.</summary>
    public static Action<string> Open { get; set; } = url =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
