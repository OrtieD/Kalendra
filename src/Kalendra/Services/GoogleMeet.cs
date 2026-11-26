using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kalendra.Services;

/// <summary>
/// Creates Google Meet links. CalDAV cannot do this, so Kalendra asks the Google Calendar API
/// (same sign-in, same permission) to add a Meet conference to the event it just uploaded.
/// Needs the "Google Calendar API" to be enabled in the user's Google Cloud project.
/// </summary>
public static class GoogleMeet
{
    static string ApiBase => Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_API_URL") ?? "https://www.googleapis.com/calendar/v3/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Google calendar id from the CalDAV address …/caldav/v2/{id}/events/.</summary>
    public static string CalendarIdFromHref(string href)
    {
        var segs = new Uri(href).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        var v2 = segs.IndexOf("v2");
        if (v2 >= 0 && v2 + 1 < segs.Count) return segs[v2 + 1];
        var ev = segs.LastIndexOf("events");
        return ev > 0 ? segs[ev - 1] : segs.Last();
    }

    /// <summary>Adds a Meet conference to the event with this UID. Returns the link (null if Google is still preparing it).</summary>
    public static async Task<string?> CreateAsync(IAuth auth, string calendarHref, string uid)
    {
        var cal = Uri.EscapeDataString(CalendarIdFromHref(calendarHref));
        var list = await SendAsync(auth, HttpMethod.Get, $"calendars/{cal}/events?iCalUID={Uri.EscapeDataString(uid)}&maxResults=5", null);
        var items = list.GetProperty("items");
        if (items.GetArrayLength() == 0)
            throw new CalDavException("Google-Meet-Link: Termin bei Google noch nicht gefunden, nächster Versuch beim nächsten Abgleich.", HttpStatusCode.NotFound);
        var ev = items[0];
        if (LinkOf(ev) is { } existing) return existing;
        var id = Uri.EscapeDataString(ev.GetProperty("id").GetString()!);
        var body = JsonSerializer.Serialize(new
        {
            conferenceData = new
            {
                createRequest = new { requestId = Guid.NewGuid().ToString("N"), conferenceSolutionKey = new { type = "hangoutsMeet" } },
            },
        });
        var updated = await SendAsync(auth, HttpMethod.Patch, $"calendars/{cal}/events/{id}?conferenceDataVersion=1", body);
        var link = LinkOf(updated);
        SyncLog.Write(link != null ? "Google-Meet-Link erstellt." : "Google-Meet-Link angefordert, Google bereitet ihn noch vor.");
        return link;
    }

    static string? LinkOf(JsonElement ev)
    {
        if (ev.TryGetProperty("hangoutLink", out var h) && h.GetString() is { Length: > 0 } link) return link;
        if (ev.TryGetProperty("conferenceData", out var cd) && cd.TryGetProperty("entryPoints", out var eps))
            foreach (var ep in eps.EnumerateArray())
                if (ep.TryGetProperty("entryPointType", out var t) && t.GetString() == "video" && ep.TryGetProperty("uri", out var u)) return u.GetString();
        return null;
    }

    static async Task<JsonElement> SendAsync(IAuth auth, HttpMethod method, string path, string? json)
    {
        for (var attempt = 0; ; attempt++)
        {
            var req = new HttpRequestMessage(method, ApiBase + path);
            req.Headers.Authorization = await auth.GetAsync(forceRefresh: attempt > 0);
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req); }
            catch (Exception ex) { throw new CalDavException("Google-Meet-Link: keine Verbindung zu Google (" + ex.Message + ").", 0); }
            var text = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && auth.CanRefresh) continue;
            if (resp.IsSuccessStatusCode) return JsonDocument.Parse(text).RootElement;
            SyncLog.Write($"Google Calendar API {method} → {(int)resp.StatusCode}: " + SyncLog.Short(text));
            if (text.Contains("accessNotConfigured") || text.Contains("SERVICE_DISABLED") || text.Contains("has not been used"))
                throw new CalDavException("Für Google-Meet-Links bitte in der Google Cloud Console die „Google Calendar API“ aktivieren.", HttpStatusCode.Forbidden);
            if (resp.StatusCode is HttpStatusCode.Forbidden && text.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
                throw new CalDavException("Google-Meet-Link: Kalendra fehlt die Berechtigung. Bitte das Google-Konto neu anmelden.", HttpStatusCode.Forbidden);
            // a 401 here must not sign the account out: CalDAV still works
            throw new CalDavException($"Google-Meet-Link konnte nicht erstellt werden ({(int)resp.StatusCode}).", resp.StatusCode == HttpStatusCode.Unauthorized ? HttpStatusCode.Forbidden : resp.StatusCode);
        }
    }
}
