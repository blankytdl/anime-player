using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnimeLibrary;

/// <summary>MyAnimeList API v2 with OAuth2 PKCE. Create an app at myanimelist.net/apiconfig (type: "other",
/// redirect URL http://localhost:8765/cb) and paste its Client ID into Settings.
///
/// Fixes in this version (round 5):
///  - Token refresh is serialised. MAL refresh tokens are single-use: when the profile load and the sync both got a 401 at the same moment,
///    both refreshed, the second refresh was rejected (invalid_grant) and the stored login was thrown away - "I am logged in but nothing uploads".
///  - The access token is refreshed BEFORE it expires (expiry time is stored), not only after a failed request.
///  - A dead login (refresh token revoked / expired) is detected, cleared and reported ("click MAL to log in again") instead of failing silently forever.
///  - The OAuth "state" value is checked (a login answer that does not belong to this login is refused).
///  - After the login the user name is read from MAL itself, so "connected" really means the token works.
///  - Log out; a plain connection test; Client ID sanity check; 429 / 5xx retry for list updates.</summary>
public class MalClient
{
    const string Redirect = "http://localhost:8765/cb";
    readonly Settings _s;
    readonly SemaphoreSlim _refreshGate = new(1, 1);
    public MalClient(Settings s) => _s = s;

    /// <summary>True when a stored login exists (it may still turn out to be expired - see <see cref="NeedsLogin"/>).</summary>
    public bool LoggedIn => !string.IsNullOrEmpty(_s.MalAccess) || !string.IsNullOrEmpty(_s.MalRefresh);
    /// <summary>The stored login was rejected by MyAnimeList for good: the user has to click MAL and log in again.</summary>
    public bool NeedsLogin { get; private set; }
    public string? UserName => _s.MalUser;
    /// <summary>Raised (on a worker thread) when the stored login was found dead and has been cleared.</summary>
    public event Action? SessionLost;

    static string Rand(int n)
    {
        const string c = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var b = RandomNumberGenerator.GetBytes(n);
        return new string(b.Select(x => c[x % c.Length]).ToArray());
    }

    // letters and digits only: no character that any web form or URL parser could treat specially (a 400 after "Allow" can come from the check of these values)
    static string RandAlnum(int n)
    {
        const string c = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var b = RandomNumberGenerator.GetBytes(n);
        return new string(b.Select(x => c[x % c.Length]).ToArray());
    }

    /// <summary>Set by the window: shows a prompt and returns what the user pasted (used when the local redirect listener cannot start).</summary>
    public Func<string, string?>? Ask { get; set; }

    static string Page(string text) => "<html><head><meta charset='utf-8'></head><body style='font-family:sans-serif;padding:32px'>" + text + "</body></html>";

    public string LastError { get; private set; } = "";

    /// <summary>A MAL Client ID is 32 hexadecimal characters. Returns null when fine, else a readable problem.</summary>
    public static string? CheckClientId(string? id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return "No Client ID yet: Settings > Tracking, paste the Client ID of your app from myanimelist.net/apiconfig, press Save, then click MAL again.";
        if (id.Length != 32 || !Regex.IsMatch(id, "^[0-9a-fA-F]{32}$"))
            return "The Client ID does not look right: it is 32 letters/digits (0-9, a-f) and is shown on myanimelist.net/apiconfig under your app, NOT the app name or the Client Secret.";
        return null;
    }

    /// <summary>Browser login (OAuth2 PKCE). Returns true when the login worked AND MyAnimeList confirmed the user name.</summary>
    public async Task<bool> LoginAsync()
    {
        LastError = "";
        string? bad = CheckClientId(_s.MalClientId);
        if (bad != null) { LastError = bad; return false; }
        string clientId = _s.MalClientId.Trim();
        string redirect = string.IsNullOrWhiteSpace(_s.MalRedirect) ? Redirect : _s.MalRedirect.Trim();
        string verifier = RandAlnum(64);      // MAL uses "plain" PKCE: challenge == verifier (43-128 characters)
        string state = RandAlnum(16);
        string url = "https://myanimelist.net/v1/oauth2/authorize?response_type=code&client_id=" + Uri.EscapeDataString(clientId) +
                     "&code_challenge=" + verifier + "&code_challenge_method=plain&state=" + state + (_s.MalNoRedirect ? "" : "&redirect_uri=" + Uri.EscapeDataString(redirect));

        HttpListener? l = null; string listenError = "";
        try
        {
            var u = new Uri(redirect);
            l = new HttpListener();
            l.Prefixes.Add($"http://{u.Host}:{u.Port}/");
            l.Start();
        }
        catch (Exception ex) { listenError = ex.Message; try { l?.Close(); } catch { } l = null; }

        string? code = null, err = null, gotState = null;
        try
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { LastError = "Could not open the browser: " + ex.Message; return false; }

            if (l != null)
            {
                var deadline = Task.Delay(TimeSpan.FromMinutes(3));
                while (code == null && err == null)
                {
                    var ctxTask = l.GetContextAsync();
                    if (await Task.WhenAny(ctxTask, deadline) != ctxTask)
                    {
                        // nothing arrived (MAL sent the browser to another address, or the login page showed an error): let the user paste where it ended up
                        string? pasted2 = Ask?.Invoke("No answer reached the app within 3 minutes.\nIf MyAnimeList took you to a page whose address contains code=..., copy that address and paste it here. Otherwise cancel and check the Client ID / redirect URL in Settings > Tracking.");
                        if (string.IsNullOrWhiteSpace(pasted2)) { LastError = "Login timed out (no answer from the browser within 3 minutes). If the MyAnimeList page said \"400 Bad Request\": open Settings > Tracking, tick \"Do not send a redirect URL\", make sure your app on myanimelist.net/apiconfig has the redirect URL " + redirect + ", then log in again."; return false; }
                        var m2 = Regex.Match(pasted2, @"[?&]code=([^&\s]+)");
                        code = m2.Success ? Uri.UnescapeDataString(m2.Groups[1].Value) : pasted2.Trim();
                        var ms2 = Regex.Match(pasted2, @"[?&]state=([^&\s]+)");
                        gotState = ms2.Success ? Uri.UnescapeDataString(ms2.Groups[1].Value) : state;
                        break;
                    }
                    HttpListenerContext ctx;
                    try { ctx = await ctxTask; } catch (Exception ex) { LastError = "Login listener stopped: " + ex.Message; return false; }
                    string? c = ctx.Request.QueryString["code"];
                    string? e = ctx.Request.QueryString["error_description"] ?? ctx.Request.QueryString["error"];
                    string? st = ctx.Request.QueryString["state"];
                    bool relevant = c != null || e != null;
                    var msg = Encoding.UTF8.GetBytes(Page(c != null ? "Logged in. You can close this tab and go back to Anime Library." : e != null ? "Login failed: " + WebUtility.HtmlEncode(e) : "Waiting for the login..."));
                    try { ctx.Response.ContentType = "text/html; charset=utf-8"; await ctx.Response.OutputStream.WriteAsync(msg); ctx.Response.Close(); } catch { }
                    if (!relevant) continue;       // favicon / probe request: keep waiting
                    code = c; err = e; gotState = st;
                }
            }
            else
            {
                string? pasted = Ask?.Invoke("The local redirect could not start (" + listenError + ").\nLog in in the browser, then copy the address of the page you land on (it contains code=...) and paste it here.");
                if (string.IsNullOrWhiteSpace(pasted)) { LastError = "Login cancelled (local listener failed: " + listenError + ")."; return false; }
                var m = Regex.Match(pasted, @"[?&]code=([^&\s]+)");
                code = m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : pasted.Trim();
                var ms = Regex.Match(pasted, @"[?&]state=([^&\s]+)");
                gotState = ms.Success ? Uri.UnescapeDataString(ms.Groups[1].Value) : state;     // a bare pasted code carries no state
            }
        }
        finally { try { l?.Close(); } catch { } }

        if (err != null) { LastError = "MyAnimeList refused the login: " + err + " (check that the redirect URL of your app is exactly " + redirect + ")."; return false; }
        if (code == null) return false;
        if (gotState != state) { LastError = "The login answer did not belong to this login attempt (state mismatch) - nothing was stored. Try again."; return false; }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["grant_type"] = "authorization_code",
            ["code"] = code, ["code_verifier"] = verifier
        };
        if (!_s.MalNoRedirect) form["redirect_uri"] = redirect;      // "no redirect URL" mode: MAL uses the one registered for the app
        if (!await TokenAsync(form, null)) return false;

        // confirm with MAL itself that the token works and remember who it belongs to
        var me = await GetProfileAsync();
        if (me == null)
        {
            string why = LastError;
            Clear(false);
            LastError = "MyAnimeList gave a token but rejected it when reading your profile" + (why.Length > 0 ? ": " + why : ".") + " Check the app type / redirect URL on myanimelist.net/apiconfig.";
            return false;
        }
        _s.MalUser = me.Name; Save();
        NeedsLogin = false;
        return true;
    }

    /// <summary>Forgets the stored login (the Client ID / secret stay).</summary>
    public void Logout() { Clear(false); LastError = ""; }

    void Clear(bool lost)
    {
        _s.MalAccess = null; _s.MalRefresh = null; _s.MalExpires = null; _s.MalUser = null;
        Save();
        NeedsLogin = lost;
        if (lost) { try { SessionLost?.Invoke(); } catch { } }
    }

    void Save() { try { Store.Save("settings.json", _s); } catch { } }

    /// <summary>Token endpoint. <paramref name="expectedRefresh"/> is only set for a refresh: when another caller already replaced the refresh token
    /// while we were waiting, this refresh is skipped (single-use tokens must not be spent twice).</summary>
    async Task<bool> TokenAsync(Dictionary<string, string> form, string? expectedRefresh)
    {
        if (!string.IsNullOrWhiteSpace(_s.MalClientSecret)) form["client_secret"] = _s.MalClientSecret.Trim();   // apps of type "web" need it
        try
        {
            using var r = await Providers.Http.PostAsync("https://myanimelist.net/v1/oauth2/token", new FormUrlEncodedContent(form));
            if (!r.IsSuccessStatusCode)
            {
                string body = "";
                try { body = await r.Content.ReadAsStringAsync(); } catch { }
                string e = await ReadErrorText(r, body);
                bool dead = (int)r.StatusCode == 400 || (int)r.StatusCode == 401 || (int)r.StatusCode == 403;
                if (expectedRefresh != null && dead && body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                {
                    Clear(true);
                    LastError = "Your MyAnimeList login expired or was revoked - click MAL to log in again.";
                    return false;
                }
                LastError = "Token request failed: " + e + (e.Contains("401") || e.Contains("invalid_client") ? " (if your MAL app type is \"web\", enter its Client Secret in Settings > Tracking)" : "");
                return false;
            }
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            _s.MalAccess = root.GetProperty("access_token").GetString();
            if (root.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(rt.GetString())) _s.MalRefresh = rt.GetString();
            int secs = root.TryGetProperty("expires_in", out var ex) && ex.ValueKind == JsonValueKind.Number ? ex.GetInt32() : 3600;
            _s.MalExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, secs));
            NeedsLogin = false;
            Save();
            return true;
        }
        catch (Exception ex) { LastError = "Token request failed: " + ex.Message; return false; }
    }

    /// <summary>Makes sure there is a usable access token: refreshes it when it is missing, expired or about to expire. One refresh at a time.</summary>
    async Task<bool> EnsureTokenAsync(bool force)
    {
        if (string.IsNullOrEmpty(_s.MalAccess) && string.IsNullOrEmpty(_s.MalRefresh)) return false;
        bool fresh = !force && !string.IsNullOrEmpty(_s.MalAccess) && (_s.MalExpires == null || _s.MalExpires.Value > DateTime.UtcNow.AddMinutes(2));
        if (fresh) return true;
        if (string.IsNullOrEmpty(_s.MalRefresh)) return !string.IsNullOrEmpty(_s.MalAccess) && !force;

        string before = _s.MalRefresh!;
        string beforeAccess = _s.MalAccess ?? "";
        await _refreshGate.WaitAsync();
        try
        {
            // somebody else refreshed while we waited: use their result
            if (_s.MalRefresh != before || (_s.MalAccess ?? "") != beforeAccess)
                return !string.IsNullOrEmpty(_s.MalAccess);
            if (string.IsNullOrEmpty(_s.MalRefresh)) return false;
            return await TokenAsync(new() { ["client_id"] = (_s.MalClientId ?? "").Trim(), ["grant_type"] = "refresh_token", ["refresh_token"] = _s.MalRefresh! }, before);
        }
        finally { _refreshGate.Release(); }
    }

    async Task<HttpResponseMessage> SendAuthed(Func<HttpRequestMessage> make)
    {
        if (!await EnsureTokenAsync(false))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        for (int attempt = 0; ; attempt++)
        {
            using var req = make();
            req.Headers.Authorization = new("Bearer", _s.MalAccess);
            var r = await Providers.Http.SendAsync(req);
            if (r.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                r.Dispose();
                if (!await EnsureTokenAsync(true)) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                continue;
            }
            if ((r.StatusCode == (HttpStatusCode)429 || (int)r.StatusCode >= 500) && attempt < 2)
            {
                r.Dispose();
                await Task.Delay(1500 * (attempt + 1));
                continue;
            }
            return r;
        }
    }

    public record MalStatus(int Watched, string? Status, string? Start, string? Finish, int Total);
    public record MalUpdate(bool Ok, int Total, string Error);

    /// <summary>Same error reading as Taiga: WWW-Authenticate error_description, else the JSON message (+ hint).</summary>
    static async Task<string> ReadError(HttpResponseMessage r)
    {
        string body = "";
        try { body = await r.Content.ReadAsStringAsync(); } catch { }
        return await ReadErrorText(r, body);
    }

    static Task<string> ReadErrorText(HttpResponseMessage r, string body)
    {
        try
        {
            if (r.Headers.WwwAuthenticate.Count > 0)
            {
                var m = Regex.Match(r.Headers.WwwAuthenticate.ToString(), "error_description=\"([^\"]*)\"");
                if (m.Success && m.Groups[1].Value.Length > 0) return Task.FromResult(m.Groups[1].Value);
            }
            using var doc = JsonDocument.Parse(body);
            string msg = doc.RootElement.TryGetProperty("message", out var m1) && m1.ValueKind == JsonValueKind.String ? m1.GetString() ?? "" : "";
            string hint = doc.RootElement.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "";
            if (msg.Length == 0 && doc.RootElement.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String) msg = er.GetString() ?? "";
            if (msg.Length > 0) return Task.FromResult(hint.Length > 0 ? msg + " (" + hint + ")" : msg);
        }
        catch { }
        return Task.FromResult("HTTP " + (int)r.StatusCode);
    }

    public record MalProfile(string Name, string? Picture);

    /// <summary>The logged-in MyAnimeList user (name + avatar url) for the profile card. Null on failure (LastError says why).</summary>
    public async Task<MalProfile?> GetProfileAsync()
    {
        if (!LoggedIn) return null;
        try
        {
            using var r = await SendAuthed(() => new HttpRequestMessage(HttpMethod.Get, "https://api.myanimelist.net/v2/users/@me"));
            if (!r.IsSuccessStatusCode)
            {
                LastError = r.StatusCode == HttpStatusCode.Unauthorized && NeedsLogin ? "Your MyAnimeList login expired - click MAL to log in again." : await ReadError(r);
                return null;
            }
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string? name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string? pic = root.TryGetProperty("picture", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (name != null && _s.MalUser != name) { _s.MalUser = name; Save(); }
            return name == null ? null : new MalProfile(name, pic);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    /// <summary>One call that answers "does my login work?" in words (used by the Settings page).</summary>
    public async Task<string> TestAsync()
    {
        string? bad = CheckClientId(_s.MalClientId);
        if (bad != null) return bad;
        if (!LoggedIn) return NeedsLogin ? "Your MyAnimeList login expired - click MAL to log in again." : "Not logged in yet - click MAL in the top bar.";
        var me = await GetProfileAsync();
        return me != null ? "Connected as " + me.Name + "." : (LastError.Length > 0 ? LastError : "MyAnimeList did not answer.");
    }

    /// <summary>The user's current MAL state for an anime. Watched = 0 and Status = null when it is not on the list. Null on failure.</summary>
    public async Task<MalStatus?> GetStatusAsync(int malId)
    {
        foreach (string fields in new[] { "num_episodes,my_list_status{num_watched_episodes,status,start_date,finish_date}", "num_episodes,my_list_status" })
        {
            using var r = await SendAuthed(() => new HttpRequestMessage(HttpMethod.Get, $"https://api.myanimelist.net/v2/anime/{malId}?fields={fields}"));
            if (!r.IsSuccessStatusCode)
            {
                LastError = r.StatusCode == HttpStatusCode.Unauthorized && NeedsLogin ? "Your MyAnimeList login expired - click MAL to log in again." : await ReadError(r);
                if ((int)r.StatusCode == 400) continue;
                return null;
            }
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            int total = root.TryGetProperty("num_episodes", out var ne) && ne.ValueKind == JsonValueKind.Number ? ne.GetInt32() : 0;
            if (!root.TryGetProperty("my_list_status", out var st) || st.ValueKind != JsonValueKind.Object) return new MalStatus(0, null, null, null, total);
            int w = 0;
            if (st.TryGetProperty("num_episodes_watched", out var a) && a.ValueKind == JsonValueKind.Number) w = a.GetInt32();
            else if (st.TryGetProperty("num_watched_episodes", out var b) && b.ValueKind == JsonValueKind.Number) w = b.GetInt32();
            string? S(string n) => st.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return new MalStatus(w, S("status"), S("start_date"), S("finish_date"), total);
        }
        return null;
    }

    /// <summary>Uploads the watched count (PATCH, like Taiga). Never lowers progress that is already higher on MAL (e.g. watched on a
    /// phone). "completed" only when the real episode count (from MAL itself) is reached; start / finish dates are filled in when empty.</summary>
    public async Task<MalUpdate> UpdateAsync(int malId, int watched, int knownTotal, int? score = null, string? forceStatus = null, bool allowLower = false)
    {
        if (!LoggedIn) return new MalUpdate(false, knownTotal, NeedsLogin ? "Your MyAnimeList login expired - click MAL to log in again." : "Not logged in to MyAnimeList.");
        try
        {
            var st = await GetStatusAsync(malId);
            if (st == null) return new MalUpdate(false, knownTotal, LastError);
            int total = st.Total > 0 ? st.Total : knownTotal;
            if (total > 0) watched = Math.Min(watched, total);
            bool hasStatus = st.Status != null && st.Status != "plan_to_watch";
            if (forceStatus == "completed" && total > 0) watched = total;
            if (st.Watched >= watched && hasStatus && !allowLower && score == null && forceStatus == null) return new MalUpdate(true, total, "");   // MAL is already at or beyond us

            string status = forceStatus ?? (total > 0 && watched >= total ? "completed" : (watched == 0 && allowLower ? "plan_to_watch" : "watching"));
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            var form = new Dictionary<string, string> { ["status"] = status, ["num_watched_episodes"] = (allowLower ? watched : Math.Max(watched, st.Watched)).ToString() };
            if (score != null) form["score"] = Math.Clamp(score.Value, 0, 10).ToString();   // 0 = remove the score
            if (st.Start == null && watched > 0) form["start_date"] = today;
            if (status == "completed" && st.Finish == null) form["finish_date"] = today;

            using var r = await SendAuthed(() => new HttpRequestMessage(HttpMethod.Patch, $"https://api.myanimelist.net/v2/anime/{malId}/my_list_status")
            {
                Content = new FormUrlEncodedContent(form)
            });
            if (r.IsSuccessStatusCode) { LastError = ""; await Task.Delay(400); return new MalUpdate(true, total, ""); } // gentle on MAL's rate limit
            LastError = (int)r.StatusCode == 404 ? "This anime is not on MyAnimeList (wrong match?)."
                      : r.StatusCode == HttpStatusCode.Unauthorized && NeedsLogin ? "Your MyAnimeList login expired - click MAL to log in again."
                      : await ReadError(r);
            return new MalUpdate(false, total, LastError);
        }
        catch (Exception ex) { LastError = ex.Message; return new MalUpdate(false, knownTotal, ex.Message); }
    }
}
