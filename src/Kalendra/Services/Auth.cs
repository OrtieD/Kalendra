using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kalendra.Services;

public interface IAuth
{
    Task<AuthenticationHeaderValue> GetAsync(bool forceRefresh);
    bool CanRefresh { get; }
}

public class BasicAuth : IAuth
{
    readonly AuthenticationHeaderValue _h;
    public BasicAuth(string user, string pass) =>
        _h = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
    public Task<AuthenticationHeaderValue> GetAsync(bool forceRefresh) => Task.FromResult(_h);
    public bool CanRefresh => false;
}

public record GoogleTokens(string AccessToken, DateTime ExpiresUtc, string? RefreshToken, string? Email);

/// <summary>OAuth access token for Google, renewed automatically with the stored refresh token.</summary>
public class GoogleAuth : IAuth
{
    readonly string _clientId, _clientSecret, _refreshToken;
    string? _access;
    DateTime _expires;

    public GoogleAuth(string clientId, string clientSecret, string refreshToken, string? access = null, DateTime expires = default)
    {
        _clientId = clientId; _clientSecret = clientSecret; _refreshToken = refreshToken;
        _access = access; _expires = expires;
    }

    public bool CanRefresh => true;

    public async Task<AuthenticationHeaderValue> GetAsync(bool forceRefresh)
    {
        if (forceRefresh || _access == null || DateTime.UtcNow > _expires.AddMinutes(-2))
        {
            var t = await GoogleOAuth.RefreshAsync(_clientId, _clientSecret, _refreshToken);
            _access = t.AccessToken; _expires = t.ExpiresUtc;
        }
        return new AuthenticationHeaderValue("Bearer", _access);
    }
}

/// <summary>
/// Google sign-in for desktop apps: opens the browser, receives the answer on a local port (127.0.0.1)
/// and exchanges the code for tokens (OAuth 2.0 with PKCE).
/// </summary>
public static class GoogleOAuth
{
    public const string Scope = "openid email https://www.googleapis.com/auth/calendar";
    static string AuthUrl => Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_AUTH_URL") ?? "https://accounts.google.com/o/oauth2/v2/auth";
    static string TokenUrl => Environment.GetEnvironmentVariable("KALENDRA_GOOGLE_TOKEN_URL") ?? "https://oauth2.googleapis.com/token";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Opens a URL in the default browser. Replaceable for tests.</summary>
    public static Action<string> OpenBrowser { get; set; } = url =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

    public static async Task<GoogleTokens> LoginAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://127.0.0.1:{port}/";
            var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = B64Url(RandomNumberGenerator.GetBytes(16));
            var url = AuthUrl + "?" + Query(new()
            {
                ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scope,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state,
                ["access_type"] = "offline", ["prompt"] = "consent",
            });
            SyncLog.Write($"Google-Anmeldung: Browser wird geöffnet, warte auf Antwort an {redirect}");
            OpenBrowser(url);

            string? code = null, error = null;
            while (code == null && error == null)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                using var stream = client.GetStream();
                var reqLine = await ReadRequestLineAsync(stream, ct);
                var path = reqLine.Split(' ').ElementAtOrDefault(1) ?? "/";
                var q = ParseQuery(path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "");
                string body;
                if (q.TryGetValue("state", out var st) && st == state && q.TryGetValue("code", out var c)) { code = c; body = Page("Anmeldung erfolgreich", "Du kannst dieses Fenster jetzt schließen und zu Kalendra zurückkehren."); }
                else if (q.TryGetValue("error", out var e)) { error = e; body = Page("Anmeldung abgebrochen", "Google meldet: " + WebUtility.HtmlEncode(e)); }
                else body = Page("Kalendra", "Warte auf die Google-Anmeldung …");
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, ct); await stream.WriteAsync(bytes, ct);
            }
            if (error != null) throw new CalDavException(error == "access_denied" ? "Die Anmeldung bei Google wurde abgebrochen." : "Google-Anmeldung fehlgeschlagen: " + error, HttpStatusCode.Unauthorized);

            var t = await TokenRequestAsync(new()
            {
                ["code"] = code!, ["client_id"] = clientId, ["client_secret"] = clientSecret, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            });
            if (t.RefreshToken == null) throw new CalDavException("Google hat keinen dauerhaften Zugang erteilt. Bitte erneut versuchen.", HttpStatusCode.Unauthorized);
            SyncLog.Write("Google-Anmeldung erfolgreich: " + t.Email);
            return t;
        }
        finally { listener.Stop(); }
    }

    public static Task<GoogleTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken) =>
        TokenRequestAsync(new()
        {
            ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token",
        });

    static async Task<GoogleTokens> TokenRequestAsync(Dictionary<string, string> form)
    {
        HttpResponseMessage resp;
        try { resp = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form)); }
        catch (Exception ex) { throw new CalDavException("Keine Verbindung zu Google: " + ex.Message, 0); }
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            SyncLog.Write($"Google-Token-Anfrage → {(int)resp.StatusCode}: " + SyncLog.Short(json));
            var err = "";
            try { err = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString() ?? ""; } catch { }
            throw new CalDavException(err switch
            {
                "invalid_grant" => "Der Google-Zugang ist abgelaufen oder wurde widerrufen. Bitte erneut anmelden.",
                "invalid_client" => "Client-ID oder Clientschlüssel stimmen nicht. Bitte in der Google Cloud Console prüfen.",
                _ => $"Google-Anmeldung fehlgeschlagen ({(int)resp.StatusCode} {err}).",
            }, HttpStatusCode.Unauthorized);
        }
        var root = JsonDocument.Parse(json).RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var expires = DateTime.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600);
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        string? email = null;
        if (root.TryGetProperty("id_token", out var idt) && idt.GetString() is { } jwt) email = EmailFromIdToken(jwt);
        return new GoogleTokens(access, expires, refresh, email);
    }

    static string? EmailFromIdToken(string jwt)
    {
        try
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            var doc = JsonDocument.Parse(Convert.FromBase64String(part));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    static async Task<string> ReadRequestLineAsync(NetworkStream s, CancellationToken ct)
    {
        var buf = new byte[8192]; var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n") && sb.Length < 64_000)
        {
            var n = await s.ReadAsync(buf, ct);
            if (n == 0) break;
            sb.Append(Encoding.ASCII.GetString(buf, 0, n));
        }
        var text = sb.ToString();
        var end = text.IndexOf("\r\n", StringComparison.Ordinal);
        return end > 0 ? text[..end] : text;
    }

    static Dictionary<string, string> ParseQuery(string q) =>
        q.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
         .GroupBy(p => Uri.UnescapeDataString(p[0])).ToDictionary(g => g.Key, g => Uri.UnescapeDataString((g.First().ElementAtOrDefault(1) ?? "").Replace('+', ' ')));

    static string Query(Dictionary<string, string> d) => string.Join("&", d.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string Page(string title, string text) =>
        $"<!doctype html><html lang=de><meta charset=utf-8><title>{title}</title><body style=\"font-family:Segoe UI,sans-serif;display:grid;place-items:center;height:90vh;color:#1d1d1f\"><div style=\"text-align:center\"><h1 style=\"font-weight:600\">{title}</h1><p>{text}</p></div></body></html>";
}
