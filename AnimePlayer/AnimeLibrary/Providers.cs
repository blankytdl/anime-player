using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace AnimeLibrary;

public record MetaMatch(string Title, string? CoverUrl, int? MalId, int? AniListId, string? ExternalId, int? Episodes, string? BannerUrl = null, string? Airing = null, string? Format = null);

/// <summary>One search hit with everything needed to score how well it fits a local series.</summary>
public record Candidate(string Title, List<string> Names, string Format, int? Episodes, int? Year, int? AniListId, int? MalId, string? CoverUrl, string? BannerUrl, double Popularity, string? Airing = null)
{
    public MetaMatch ToMatch() => new(Title, CoverUrl, MalId, AniListId, null, Episodes, BannerUrl, Airing, Format);
}

public static class Providers
{
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static Providers() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeLibrary/2.0");

    static DateTime _checked = DateTime.MinValue;
    static bool _online;

    /// <summary>Real connectivity check (a network adapter being up does not mean the internet works). Cached for 10 s.</summary>
    public static async Task<bool> IsOnlineAsync(bool force = false)
    {
        try { if (!NetworkInterface.GetIsNetworkAvailable()) { _online = false; _checked = DateTime.UtcNow; return false; } } catch { }
        if (!force && (DateTime.UtcNow - _checked).TotalSeconds < 10) return _online;
        try
        {
            using var cts = new CancellationTokenSource(4000);
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://graphql.anilist.co");
            using var r = await Http.SendAsync(req, cts.Token);
            _online = true; // any answer at all = we reach the internet
        }
        catch { _online = false; }
        _checked = DateTime.UtcNow;
        return _online;
    }

    public static async Task<MetaMatch?> SearchAniList(string title)
    {
        const string q = "query($s:String){Media(search:$s,type:ANIME){id idMal episodes title{romaji english} coverImage{extraLarge large}}}";
        var body = JsonSerializer.Serialize(new { query = q, variables = new { s = title } });
        using var r = await Http.PostAsync("https://graphql.anilist.co", new StringContent(body, Encoding.UTF8, "application/json"));
        if (!r.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
        if (!d.TryGetProperty("Media", out var m) || m.ValueKind != JsonValueKind.Object) return null;
        var t = m.GetProperty("title");
        string? en = t.TryGetProperty("english", out var e1) && e1.ValueKind == JsonValueKind.String ? e1.GetString() : null;
        string? ro = t.TryGetProperty("romaji", out var e2) && e2.ValueKind == JsonValueKind.String ? e2.GetString() : null;
        string? cover = null;
        if (m.TryGetProperty("coverImage", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            if (c.TryGetProperty("extraLarge", out var x) && x.ValueKind == JsonValueKind.String) cover = x.GetString();
            else if (c.TryGetProperty("large", out var l) && l.ValueKind == JsonValueKind.String) cover = l.GetString();
        }
        return new MetaMatch(en ?? ro ?? title, cover,
            m.TryGetProperty("idMal", out var mal) && mal.ValueKind == JsonValueKind.Number ? mal.GetInt32() : null,
            m.GetProperty("id").GetInt32(), null,
            m.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number ? ep.GetInt32() : null);
    }

    public static async Task<MetaMatch?> SearchJikan(string title)
    {
        using var r = await Http.GetAsync("https://api.jikan.moe/v4/anime?limit=1&q=" + Uri.EscapeDataString(title));
        if (!r.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
        var a = arr[0];
        string? cover = null;
        try { cover = a.GetProperty("images").GetProperty("jpg").GetProperty("large_image_url").GetString(); } catch { }
        return new MetaMatch(a.GetProperty("title").GetString() ?? title, cover, a.GetProperty("mal_id").GetInt32(), null, null,
            a.TryGetProperty("episodes", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : null);
    }

    static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;

    /// <summary>Up to 10 AniList hits for a query, best text match first. The caller scores them (see Matching) instead of trusting hit #1.</summary>
    public static async Task<List<Candidate>> AniListCandidates(string query)
    {
        const string q = "query($s:String){Page(perPage:10){media(search:$s,type:ANIME,sort:SEARCH_MATCH){id idMal format status episodes seasonYear popularity synonyms title{romaji english native} coverImage{extraLarge large} bannerImage}}}";
        var list = new List<Candidate>();
        var body = JsonSerializer.Serialize(new { query = q, variables = new { s = query } });
        using var r = await Http.PostAsync("https://graphql.anilist.co", new StringContent(body, Encoding.UTF8, "application/json"));
        if (!r.IsSuccessStatusCode) return list;
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return list;
        if (!d.TryGetProperty("Page", out var page) || !page.TryGetProperty("media", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in arr.EnumerateArray())
        {
            var names = new List<string>();
            string? en = null, ro = null;
            if (m.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                en = Str(t, "english"); ro = Str(t, "romaji");
                foreach (string n in new[] { "romaji", "english", "native" }) { var v = Str(t, n); if (!string.IsNullOrWhiteSpace(v)) names.Add(v!); }
            }
            if (m.TryGetProperty("synonyms", out var syn) && syn.ValueKind == JsonValueKind.Array)
                foreach (var x in syn.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())) names.Add(x.GetString()!);
            string? cover = null;
            if (m.TryGetProperty("coverImage", out var c) && c.ValueKind == JsonValueKind.Object) cover = Str(c, "extraLarge") ?? Str(c, "large");
            int? id = Int(m, "id");
            if (id == null || names.Count == 0) continue;
            double pop = m.TryGetProperty("popularity", out var pp) && pp.ValueKind == JsonValueKind.Number ? pp.GetDouble() : 0;
            list.Add(new Candidate(en ?? ro ?? names[0], names, Str(m, "format") ?? "", Int(m, "episodes"), Int(m, "seasonYear"), id, Int(m, "idMal"), cover, Str(m, "bannerImage"), pop, Str(m, "status")));
        }
        return list;
    }

    /// <summary>Follows the SEQUEL relations of an AniList entry <paramref name="hops"/> times (TV / ONA entries first, then anything else).
    /// "Minami-ke s4" / "Zero no Tsukaima S3": the third sequel of season 1 IS that season, whatever its own title is.</summary>
    public static async Task<Candidate?> AniListSequelChain(int id, int hops)
    {
        const string q = "query($id:Int){Media(id:$id,type:ANIME){relations{edges{relationType node{id idMal type format status episodes popularity synonyms title{romaji english native} coverImage{extraLarge large} bannerImage}}}}}";
        for (int h = 0; h < hops; h++)
        {
            var body = JsonSerializer.Serialize(new { query = q, variables = new { id } });
            using var r = await Http.PostAsync("https://graphql.anilist.co", new StringContent(body, Encoding.UTF8, "application/json"));
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            if (!d.TryGetProperty("Media", out var m) || m.ValueKind != JsonValueKind.Object) return null;
            if (!m.TryGetProperty("relations", out var rel) || !rel.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array) return null;
            Candidate? tv = null, other = null;
            foreach (var ed in edges.EnumerateArray())
            {
                if (Str(ed, "relationType") != "SEQUEL" || !ed.TryGetProperty("node", out var n) || Str(n, "type") != "ANIME") continue;
                var names = new List<string>();
                string? en = null, ro = null;
                if (n.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.Object)
                {
                    en = Str(t, "english"); ro = Str(t, "romaji");
                    foreach (string k in new[] { "romaji", "english", "native" }) { var v = Str(t, k); if (!string.IsNullOrWhiteSpace(v)) names.Add(v!); }
                }
                int? nid = Int(n, "id"); if (nid == null || names.Count == 0) continue;
                string? cover = n.TryGetProperty("coverImage", out var c) && c.ValueKind == JsonValueKind.Object ? Str(c, "extraLarge") ?? Str(c, "large") : null;
                string fmt = Str(n, "format") ?? "";
                var cand = new Candidate(en ?? ro ?? names[0], names, fmt, Int(n, "episodes"), null, nid, Int(n, "idMal"), cover, Str(n, "bannerImage"), 0, Str(n, "status"));
                if (fmt is "TV" or "TV_SHORT" or "ONA") tv ??= cand; else other ??= cand;
            }
            var next = tv ?? other;
            if (next == null) return null;
            if (h == hops - 1) return next;
            id = next.AniListId!.Value;
            await Task.Delay(300);
        }
        return null;
    }

    /// <summary>Cover + banner of a known AniList id (used when a match exists but no picture could be downloaded).</summary>
    public static async Task<MetaMatch?> AniListById(int id)
    {
        const string q = "query($id:Int){Media(id:$id,type:ANIME){id idMal status episodes title{romaji english} coverImage{extraLarge large} bannerImage}}";
        var body = JsonSerializer.Serialize(new { query = q, variables = new { id } });
        using var r = await Http.PostAsync("https://graphql.anilist.co", new StringContent(body, Encoding.UTF8, "application/json"));
        if (!r.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var d) || !d.TryGetProperty("Media", out var m) || m.ValueKind != JsonValueKind.Object) return null;
        string? cover = null;
        if (m.TryGetProperty("coverImage", out var c) && c.ValueKind == JsonValueKind.Object) cover = Str(c, "extraLarge") ?? Str(c, "large");
        string title = "";
        if (m.TryGetProperty("title", out var t)) title = Str(t, "english") ?? Str(t, "romaji") ?? "";
        return new MetaMatch(title, cover, Int(m, "idMal"), id, null, Int(m, "episodes"), Str(m, "bannerImage"), Str(m, "status"));
    }

    public static async Task<List<Candidate>> JikanCandidates(string query)
    {
        var list = new List<Candidate>();
        using var r = await Http.GetAsync("https://api.jikan.moe/v4/anime?limit=10&sfw=false&q=" + Uri.EscapeDataString(query));
        if (!r.IsSuccessStatusCode) return list;
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var a in arr.EnumerateArray())
        {
            var names = new List<string>();
            foreach (string n in new[] { "title", "title_english", "title_japanese" }) { var v = Str(a, n); if (!string.IsNullOrWhiteSpace(v)) names.Add(v!); }
            if (a.TryGetProperty("title_synonyms", out var syn) && syn.ValueKind == JsonValueKind.Array)
                foreach (var x in syn.EnumerateArray()) if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())) names.Add(x.GetString()!);
            string? cover = null;
            try { cover = a.GetProperty("images").GetProperty("jpg").GetProperty("large_image_url").GetString(); } catch { }
            int? id = Int(a, "mal_id");
            if (id == null || names.Count == 0) continue;
            double pop = a.TryGetProperty("members", out var mm) && mm.ValueKind == JsonValueKind.Number ? mm.GetDouble() / 10 : 0;
            list.Add(new Candidate(Str(a, "title_english") ?? names[0], names, Str(a, "type") ?? "", Int(a, "episodes"), Int(a, "year"), null, id, cover, null, pop, (Str(a, "status") ?? "").StartsWith("Finished") ? "FINISHED" : (Str(a, "status") ?? "").StartsWith("Currently") ? "RELEASING" : (Str(a, "status") ?? "").StartsWith("Not yet") ? "NOT_YET_RELEASED" : null));
        }
        return list;
    }

    public static async Task<string?> JikanCover(int malId)
    {
        try
        {
            using var r = await Http.GetAsync("https://api.jikan.moe/v4/anime/" + malId);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("data").GetProperty("images").GetProperty("jpg").GetProperty("large_image_url").GetString();
        }
        catch { return null; }
    }

    /// <summary>Kitsu has a good poster for many titles AniList / MAL cannot supply; no key needed.</summary>
    public static async Task<string?> KitsuCover(string title)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://kitsu.io/api/edge/anime?page%5Blimit%5D=3&filter%5Btext%5D=" + Uri.EscapeDataString(title));
            req.Headers.Accept.ParseAdd("application/vnd.api+json");
            using var r = await Http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            foreach (var a in arr.EnumerateArray())
            {
                if (!a.TryGetProperty("attributes", out var at) || !at.TryGetProperty("posterImage", out var pi) || pi.ValueKind != JsonValueKind.Object) continue;
                string? u = Str(pi, "large") ?? Str(pi, "original") ?? Str(pi, "medium");
                if (!string.IsNullOrEmpty(u)) return u;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Downloads an image to <paramref name="file"/> (atomic, never leaves a half file). Returns false for anything WPF cannot show
    /// (HTML error page, truncated download, WebP) so the caller can try the next source.</summary>
    public static async Task<bool> DownloadImage(string? url, string file)
    {
        if (string.IsNullOrEmpty(url)) return false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (!CoverTools.IsImage(bytes)) return false;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                string tmp = file + ".part";
                await File.WriteAllBytesAsync(tmp, bytes);
                File.Move(tmp, file, true);
                return true;
            }
            catch { await Task.Delay(600); }
        }
        return false;
    }
}
