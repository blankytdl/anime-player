using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnimeLibrary;

/// <summary>A declarative metadata provider. The marketplace is a JSON list of these; installing one saves it to
/// %APPDATA%\AnimeLibrary\extensions\&lt;id&gt;.json and makes it selectable as a folder's metadata source.
/// {
///   "id": "my-source", "name": "My Source", "version": "1.0.0", "author": "me", "description": "...",
///   "searchUrl": "https://example.org/api/search?q={query}",
///   "resultsPath": "data",          // optional: array that holds the hits (empty = the response itself)
///   "titlePath": "title", "coverPath": "image.large", "idPath": "id", "episodesPath": "episodes",
///   "manifestUrl": "https://.../my-source.json"   // optional: marketplace entry points to the full manifest
/// }</summary>
public class ExtensionManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string SearchUrl { get; set; } = "";
    public string ResultsPath { get; set; } = "";
    public string TitlePath { get; set; } = "title";
    public string CoverPath { get; set; } = "";
    public string IdPath { get; set; } = "";
    public string EpisodesPath { get; set; } = "";
    public string ManifestUrl { get; set; } = "";
}

public static class Extensions
{
    static readonly JsonSerializerOptions J = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Dir => System.IO.Path.Combine(Store.Dir, "extensions");

    static string SafeId(string id) => Regex.Replace(id, @"[^A-Za-z0-9_.\-]", "_");

    public static List<ExtensionManifest> Installed()
    {
        var list = new List<ExtensionManifest>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(f), J);
                    if (m != null && m.Id.Length > 0 && m.SearchUrl.Length > 0) list.Add(m);
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    public static ExtensionManifest? Get(string? id) => id == null ? null : Installed().FirstOrDefault(m => m.Id == id);

    public static void Save(ExtensionManifest m)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(System.IO.Path.Combine(Dir, SafeId(m.Id) + ".json"), JsonSerializer.Serialize(m, J));
    }

    public static void Remove(string id)
    {
        try { File.Delete(System.IO.Path.Combine(Dir, SafeId(id) + ".json")); } catch { }
    }

    public static bool IsNewer(string candidate, string current)
    {
        return Version.TryParse(candidate, out var a) && Version.TryParse(current, out var b) ? a > b : candidate != current;
    }

    /// <summary>Downloads the marketplace list (a JSON array, or {"extensions":[...]}).</summary>
    public static async Task<List<ExtensionManifest>> FetchMarketplace(string url)
    {
        string json = await Providers.Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("extensions", out var arr)) root = arr;
        var list = new List<ExtensionManifest>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in root.EnumerateArray())
        {
            try
            {
                var m = e.Deserialize<ExtensionManifest>(J);
                if (m != null && m.Id.Length > 0) list.Add(m);
            }
            catch { }
        }
        return list;
    }

    /// <summary>Installs a marketplace entry (fetching its full manifest when manifestUrl is set).</summary>
    public static async Task<ExtensionManifest> Install(ExtensionManifest entry)
    {
        var m = entry;
        if (!string.IsNullOrWhiteSpace(entry.ManifestUrl))
        {
            string json = await Providers.Http.GetStringAsync(entry.ManifestUrl);
            m = JsonSerializer.Deserialize<ExtensionManifest>(json, J) ?? entry;
        }
        if (m.Id.Length == 0 || m.SearchUrl.Length == 0) throw new InvalidDataException("The extension has no id or searchUrl.");
        if (!m.SearchUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !m.SearchUrl.Contains("{query}"))
            throw new InvalidDataException("searchUrl must be an https:// URL containing {query}.");
        Save(m);
        return m;
    }

    // ---- searching ----
    static JsonElement? Walk(JsonElement e, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return e;
        JsonElement cur = e;
        foreach (string part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part; var idx = new List<int>();
            int br = part.IndexOf('[');
            if (br >= 0)
            {
                name = part[..br];
                foreach (Match m in Regex.Matches(part[br..], @"\[(\d+)\]")) idx.Add(int.Parse(m.Groups[1].Value));
            }
            if (name.Length > 0)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(name, out var next)) return null;
                cur = next;
            }
            foreach (int i in idx)
            {
                if (cur.ValueKind != JsonValueKind.Array || i >= cur.GetArrayLength()) return null;
                cur = cur[i];
            }
        }
        return cur;
    }

    static string? Str(JsonElement? e) =>
        e is JsonElement x ? x.ValueKind switch { JsonValueKind.String => x.GetString(), JsonValueKind.Number => x.ToString(), _ => null } : null;

    public static async Task<MetaMatch?> Search(ExtensionManifest ext, string title)
    {
        string json = await Providers.Http.GetStringAsync(ext.SearchUrl.Replace("{query}", Uri.EscapeDataString(title)));
        using var doc = JsonDocument.Parse(json);
        var hits = Walk(doc.RootElement, ext.ResultsPath);
        if (hits == null) return null;
        JsonElement hit = hits.Value;
        if (hit.ValueKind == JsonValueKind.Array)
        {
            if (hit.GetArrayLength() == 0) return null;
            hit = hit[0];
        }
        string? t = Str(Walk(hit, ext.TitlePath));
        if (string.IsNullOrWhiteSpace(t)) return null;
        int? eps = int.TryParse(Str(string.IsNullOrEmpty(ext.EpisodesPath) ? null : Walk(hit, ext.EpisodesPath)), out int n) ? n : null;
        return new MetaMatch(t, string.IsNullOrEmpty(ext.CoverPath) ? null : Str(Walk(hit, ext.CoverPath)), null, null,
            string.IsNullOrEmpty(ext.IdPath) ? null : Str(Walk(hit, ext.IdPath)), eps);
    }
}
