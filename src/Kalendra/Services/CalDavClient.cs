using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Kalendra.Services;

public class CalDavException : Exception
{
    public HttpStatusCode Status { get; }
    public CalDavException(string msg, HttpStatusCode status) : base(msg) { Status = status; }
}

public record RemoteCalendar(string Href, string Name, string? Color, string? CTag, bool ReadOnly);
public record RemoteObject(string Href, string? ETag, string? Ics);

/// <summary>Minimal CalDAV client for iCloud (RFC 4791).</summary>
public class CalDavClient
{
    static readonly XNamespace D = "DAV:";
    static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    static readonly XNamespace A = "http://apple.com/ns/ical/";
    static readonly XNamespace CS = "http://calendarserver.org/ns/";

    public static string ServerUrl => Environment.GetEnvironmentVariable("KALENDRA_CALDAV_URL") ?? "https://caldav.icloud.com/";
    public static string GoogleServerUrl => Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_CALDAV_URL") ?? "https://apidata.googleusercontent.com/caldav/v2/";

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Kalendra/0.4");
        return h;
    }

    readonly IAuth _auth;
    public string BaseUrl { get; }

    public CalDavClient(string baseUrl, IAuth auth)
    {
        BaseUrl = baseUrl;
        _auth = auth;
    }

    /// <summary>iCloud with Apple ID and app-specific password.</summary>
    public CalDavClient(string user, string pass) : this(ServerUrl, new BasicAuth(user, pass)) { }

    async Task<(HttpResponseMessage Resp, Uri Final)> SendAsync(string method, Uri url, string? body, string? depth,
        string contentType = "application/xml; charset=utf-8", Action<HttpRequestMessage>? extra = null, bool allow404 = false)
    {
        var refreshed = false;
        for (int hop = 0; hop < 6; hop++)
        {
            var req = new HttpRequestMessage(new HttpMethod(method), url);
            req.Headers.Authorization = await _auth.GetAsync(false);
            if (depth != null) req.Headers.Add("Depth", depth);
            if (body != null) req.Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType));
            extra?.Invoke(req);
            HttpResponseMessage resp;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { resp = await Http.SendAsync(req); }
            catch (HttpRequestException ex) { SyncLog.Write($"{method} {url} → keine Verbindung: {ex.Message}"); throw new CalDavException("Keine Verbindung zu iCloud: " + ex.Message, 0); }
            catch (TaskCanceledException) { SyncLog.Write($"{method} {url} → Zeitüberschreitung"); throw new CalDavException("iCloud antwortet nicht (Zeitüberschreitung).", HttpStatusCode.RequestTimeout); }
            SyncLog.Write($"{method} {url} Depth={depth ?? "-"} → {(int)resp.StatusCode} {resp.ReasonPhrase} ({sw.ElapsedMilliseconds} ms)");
            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode is not (301 or 302 or 307 or 308))
            {
                try { SyncLog.Write("   Antwort: " + SyncLog.Short(await resp.Content.ReadAsStringAsync())); } catch { }
            }
            if ((int)resp.StatusCode is 301 or 302 or 307 or 308 && resp.Headers.Location is { } loc)
            {
                url = loc.IsAbsoluteUri ? loc : new Uri(url, loc);
                continue;
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized && !refreshed && _auth.CanRefresh)
            {
                // access token expired: get a fresh one and try once more
                refreshed = true;
                await _auth.GetAsync(true);
                continue;
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
                throw new CalDavException(_auth.CanRefresh ? "Google hat die Anmeldung abgelehnt. Bitte erneut anmelden." : "Anmeldung abgelehnt. Bitte Apple-ID und app-spezifisches Passwort prüfen.", HttpStatusCode.Unauthorized);
            if (resp.StatusCode == HttpStatusCode.NotFound && allow404) return (resp, url);
            if (!resp.IsSuccessStatusCode)
                throw new CalDavException($"{method} fehlgeschlagen: {(int)resp.StatusCode} {resp.ReasonPhrase}", resp.StatusCode);
            return (resp, url);
        }
        throw new CalDavException("Zu viele Weiterleitungen.", 0);
    }

    async Task<(XDocument Doc, Uri Base)> PropfindAsync(Uri url, string depth, string props)
    {
        var body = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\" xmlns:a=\"http://apple.com/ns/ical/\" xmlns:cs=\"http://calendarserver.org/ns/\"><d:prop>{props}</d:prop></d:propfind>";
        var (resp, final) = await SendAsync("PROPFIND", url, body, depth);
        var text = await resp.Content.ReadAsStringAsync();
        SyncLog.Write("   Antwort: " + SyncLog.Short(text, 3000));
        return (XDocument.Parse(text), final);
    }

    /// <summary>Finds the URL of the folder that contains all calendars of the account.</summary>
    public async Task<string> DiscoverHomeAsync()
    {
        var (doc, b) = await PropfindAsync(new Uri(BaseUrl), "0", "<d:current-user-principal/>");
        var p = doc.Descendants(D + "current-user-principal").Elements(D + "href").FirstOrDefault()?.Value
                ?? throw new CalDavException("Benutzerkonto nicht gefunden.", 0);
        var principal = new Uri(b, p);
        var (doc2, b2) = await PropfindAsync(principal, "0", "<c:calendar-home-set/>");
        var h = doc2.Descendants(C + "calendar-home-set").Elements(D + "href").FirstOrDefault()?.Value
                ?? throw new CalDavException("Kalender-Ordner nicht gefunden.", 0);
        return new Uri(b2, h).ToString();
    }

    public async Task<List<RemoteCalendar>> ListCalendarsAsync(string home)
    {
        var (doc, b) = await PropfindAsync(new Uri(home), "1",
            "<d:displayname/><d:resourcetype/><c:supported-calendar-component-set/><a:calendar-color/><cs:getctag/><d:current-user-privilege-set/>");
        var list = new List<RemoteCalendar>();
        foreach (var r in doc.Root!.Elements(D + "response"))
        {
            var ok = r.Elements(D + "propstat").Where(ps => (ps.Element(D + "status")?.Value ?? "").Contains(" 200")).Elements(D + "prop").ToList();
            var isCal = ok.Elements(D + "resourcetype").Elements(C + "calendar").Any();
            var comps = ok.Descendants(C + "comp").Select(c => (string?)c.Attribute("name")).ToList();
            if (!isCal || comps.Count > 0 && !comps.Contains("VEVENT")) continue;
            var href = new Uri(b, r.Element(D + "href")!.Value).ToString();
            var name = ok.Elements(D + "displayname").FirstOrDefault()?.Value;
            var color = ok.Elements(A + "calendar-color").FirstOrDefault()?.Value;
            if (color is { Length: 9 } && color.StartsWith('#')) color = color[..7];
            var ctag = ok.Elements(CS + "getctag").FirstOrDefault()?.Value;
            var privs = ok.Elements(D + "current-user-privilege-set").ToList();
            var readOnly = privs.Count > 0 && !privs.Descendants().Any(x => x.Name == D + "write" || x.Name == D + "write-content" || x.Name == D + "all");
            list.Add(new RemoteCalendar(href, string.IsNullOrWhiteSpace(name) ? "Kalender" : name, string.IsNullOrWhiteSpace(color) ? null : color, ctag, readOnly));
        }
        return list;
    }

    /// <summary>All event resources of a calendar with their ETags (cheap: no content).</summary>
    public async Task<Dictionary<string, string?>> ListETagsAsync(string calendarHref)
    {
        var body = """
            <?xml version="1.0" encoding="utf-8"?>
            <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:prop><d:getetag/></d:prop>
              <c:filter><c:comp-filter name="VCALENDAR"><c:comp-filter name="VEVENT"/></c:comp-filter></c:filter>
            </c:calendar-query>
            """;
        var (resp, b) = await SendAsync("REPORT", new Uri(calendarHref), body, "1");
        var doc = XDocument.Parse(await resp.Content.ReadAsStringAsync());
        var result = new Dictionary<string, string?>();
        foreach (var r in doc.Root!.Elements(D + "response"))
        {
            var href = new Uri(b, r.Element(D + "href")!.Value).ToString();
            if (Key(href) == Key(calendarHref)) continue;
            result[href] = r.Descendants(D + "getetag").FirstOrDefault()?.Value;
        }
        return result;
    }

    public async Task<List<RemoteObject>> MultigetAsync(string calendarHref, IEnumerable<string> hrefs)
    {
        var all = new List<RemoteObject>();
        foreach (var chunk in hrefs.Chunk(50))
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><c:calendar-multiget xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><d:getetag/><c:calendar-data/></d:prop>");
            foreach (var h in chunk) sb.Append("<d:href>").Append(SecurityElement.Escape(new Uri(h).AbsolutePath)).Append("</d:href>");
            sb.Append("</c:calendar-multiget>");
            var (resp, b) = await SendAsync("REPORT", new Uri(calendarHref), sb.ToString(), "1");
            var doc = XDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var r in doc.Root!.Elements(D + "response"))
            {
                var href = new Uri(b, r.Element(D + "href")!.Value).ToString();
                var data = r.Descendants(C + "calendar-data").FirstOrDefault()?.Value;
                if (data == null) continue;
                all.Add(new RemoteObject(href, r.Descendants(D + "getetag").FirstOrDefault()?.Value, data));
            }
        }
        return all;
    }

    /// <summary>Uploads an event. Returns the new ETag if the server sends one.</summary>
    public async Task<string?> PutAsync(string href, string ics, string? ifMatchETag)
    {
        try
        {
            var (resp, _) = await SendAsync("PUT", new Uri(href), ics, null, "text/calendar; charset=utf-8", req =>
            {
                if (ifMatchETag == null) req.Headers.TryAddWithoutValidation("If-None-Match", "*");
                else req.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatchETag));
            });
            return resp.Headers.ETag?.ToString();
        }
        catch (CalDavException ex) when (ex.Status == HttpStatusCode.PreconditionFailed)
        {
            throw new CalDavException("Konflikt: Ereignis wurde inzwischen auf einem anderen Gerät geändert.", HttpStatusCode.PreconditionFailed);
        }
    }

    public async Task DeleteAsync(string href, string? ifMatchETag)
    {
        await SendAsync("DELETE", new Uri(href), null, null, extra: req =>
        {
            if (ifMatchETag != null) req.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatchETag));
        }, allow404: true);
    }

    public async Task UpdateCalendarPropsAsync(string calendarHref, string name, string color)
    {
        var body = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <d:propertyupdate xmlns:d="DAV:" xmlns:a="http://apple.com/ns/ical/">
              <d:set><d:prop><d:displayname>{SecurityElement.Escape(name)}</d:displayname><a:calendar-color>{color}FF</a:calendar-color></d:prop></d:set>
            </d:propertyupdate>
            """;
        await SendAsync("PROPPATCH", new Uri(calendarHref), body, null);
    }

    static string Quote(string etag) => etag.StartsWith('"') || etag.StartsWith("W/") ? etag : $"\"{etag}\"";

    /// <summary>Comparable key for a resource URL (host-independent, decoded path).</summary>
    public static string Key(string href) => Uri.UnescapeDataString(new Uri(href).AbsolutePath).TrimEnd('/');
}
