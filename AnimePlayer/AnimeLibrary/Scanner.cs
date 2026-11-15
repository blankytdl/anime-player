using System.IO;
using System.Text.RegularExpressions;

namespace AnimeLibrary;

public static class Scanner
{
    static readonly HashSet<string> Vid = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".webm", ".mov", ".m4v", ".ts", ".wmv", ".flv", ".mpg", ".mpeg", ".m2ts" };
    static readonly HashSet<string> Aud = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".ape", ".mka", ".alac", ".wv" };
    public static bool IsVideo(string path) => Vid.Contains(System.IO.Path.GetExtension(path));
    public static bool IsAudio(string path) => Aud.Contains(System.IO.Path.GetExtension(path));
    public static bool IsMedia(string path) => IsVideo(path) || IsAudio(path);

    // ------------------------------------------------------------------ natural order (Episode 2 < Episode 10)
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer I = new();
        public int Compare(string? a, string? b)
        {
            if (a == null || b == null) return string.Compare(a, b, StringComparison.Ordinal);
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int c = string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }

    sealed class Item
    {
        public string Path = ""; public FileClass Cls = null!; public ParsedName P = null!;
        public string Key = ""; public string Title = ""; public int Season = 1; public int Lib; public NameRule? Rule; public bool Promoted;
        public string Dir => System.IO.Path.GetDirectoryName(Path) ?? "";
    }

    sealed class Group
    {
        public string Key = "", Title = ""; public int Season = 1; public int Lib; public NameRule? Rule;
        public List<Item> Items = new(); public HashSet<string> Aliases = new();
        public IEnumerable<Item> Episodes => Items.Where(i => i.Cls.Kind == FileKind.Episode);
    }

    static bool RuleHit(NameRule r, string key) =>
        r.Match.EndsWith('*') ? key.StartsWith(r.Match[..^1], StringComparison.Ordinal) : key == r.Match;

    static string Title(string t) => string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    // ------------------------------------------------------------------ clustering of names without a recognisable episode number
    static int? FirstNumber(IEnumerable<string> tokens)
    {
        foreach (string t in tokens) if (t.Length is >= 1 and <= 4 && t.All(char.IsDigit)) return int.Parse(t);
        return null;
    }

    /// <summary>"Show 01 The Marquess.mkv", "Show 02 A Visitor.mkv": no "- 01" and no S01E01, so every file looked like its own series.
    /// Inside one folder: a name that starts with the title of a reliable sibling joins it; the rest are grouped by their common word prefix.</summary>
    static void Recluster(List<Item> items)
    {
        foreach (var dirGroup in items.Where(i => i.Cls.Kind == FileKind.Episode).GroupBy(i => i.Dir, StringComparer.OrdinalIgnoreCase))
        {
            var list = dirGroup.ToList();
            var shaky = list.Where(i => i.P.Episode == null && !i.P.IsMovie).ToList();      // films stay separate: unrelated movies share one folder
            if (shaky.Count == 0) continue;
            var known = list.Where(i => i.P.Episode != null && i.P.Title.Length >= 2).Select(i => i.P.Title).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(t => t.Length).ToList();

            var rest = new List<Item>();
            foreach (var it in shaky)
            {
                var toks = NameParser.Tokens(it.Path);
                string? hit = null; int hitLen = 0;
                foreach (string t in known)
                {
                    var tt = NameParser.Tokens(t, false);
                    if (tt.Count > 0 && toks.Count > tt.Count && toks.Take(tt.Count).SequenceEqual(tt)) { hit = t; hitLen = tt.Count; break; }
                }
                if (hit != null) it.P = it.P with { Title = hit, Episode = FirstNumber(toks.Skip(hitLen)) };
                else rest.Add(it);
            }
            if (rest.Count < 2) continue;

            var tl = rest.Select(i => NameParser.Tokens(i.Path)).ToList();
            var parent = Enumerable.Range(0, rest.Count).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            int Lcp(List<string> a, List<string> b) { int n = 0; while (n < a.Count && n < b.Count && a[n] == b[n] && !a[n].All(char.IsDigit)) n++; return n; }
            for (int i = 0; i < rest.Count; i++)
                for (int j = i + 1; j < rest.Count; j++)
                {
                    int l = Lcp(tl[i], tl[j]);
                    if (l == 0) continue;
                    int min = Math.Min(tl[i].Count, tl[j].Count);
                    bool ok = l >= 2 ? l >= min * 0.4 : tl[i][0].Length >= 5 && l >= min * 0.5;
                    if (ok) parent[Find(i)] = Find(j);
                }
            foreach (var cluster in Enumerable.Range(0, rest.Count).GroupBy(Find).Where(g => g.Count() >= 2))
            {
                var members = cluster.ToList();
                int common = members.Skip(1).Aggregate(tl[members[0]].Count, (acc, m) => Math.Min(acc, Lcp(tl[members[0]], tl[m])));
                if (common == 0) continue;
                string title = Title(string.Join(' ', tl[members[0]].Take(common)));
                foreach (int m in members) rest[m].P = rest[m].P with { Title = title, Episode = FirstNumber(tl[m].Skip(common)) };
            }
        }
    }

    // ------------------------------------------------------------------ folders
    static string[] Segs(string p) => p.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Deepest folder that contains every given folder ("" when they share nothing, e.g. different drives).</summary>
    public static string CommonRoot(IEnumerable<string> dirs)
    {
        string[]? common = null; string prefix = "";
        foreach (string d in dirs)
        {
            if (common == null && d.StartsWith(@"\\")) prefix = @"\\";
            var s = Segs(d);
            if (common == null) { common = s; continue; }
            int n = 0;
            while (n < common.Length && n < s.Length && string.Equals(common[n], s[n], StringComparison.OrdinalIgnoreCase)) n++;
            common = common[..n];
            if (n == 0) break;
        }
        if (common == null || common.Length == 0) return "";
        string joined = string.Join(System.IO.Path.DirectorySeparatorChar, common);
        return joined.Length == 2 && joined[1] == ':' ? joined + System.IO.Path.DirectorySeparatorChar : prefix + joined;   // "D:" -> "D:\"
    }

    public static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && (path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                              path.StartsWith(folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    static int KindRank(FileKind k) => k switch { FileKind.Episode => 0, FileKind.Opening => 1, FileKind.Ending => 2, FileKind.Music => 3, _ => 4 };

    // ------------------------------------------------------------------ phase 1 (offline)
    /// <summary>Phase 1 - local only, works offline and is fast. Every media file is classified (episode / opening / ending / music / extra),
    /// grouped into series by the TITLE in its name (not by folder), then variants of the same title are merged. A series may span many folders.
    /// Existing entries keep their matches, progress and covers.</summary>
    public static List<Entry> ScanLocal(Settings s, List<Entry> existing)
    {
        var items = new List<Item>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        var rootsOk = new List<string>();
        for (int fi = 0; fi < s.Folders.Count; fi++)
        {
            string root = s.Folders[fi].Path;
            if (!Directory.Exists(root)) continue;
            rootsOk.Add(root);
            try
            {
                foreach (string f in Directory.EnumerateFiles(root, "*", opts))
                {
                    if (!IsMedia(f)) continue;
                    if (f.Contains("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || f.Contains("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                    var cls = NameParser.Classify(f);
                    var p = NameParser.ForFile(f);
                    if (p.Title.Length == 0) continue;
                    items.Add(new Item { Path = f, Cls = cls, P = p, Lib = fi });
                }
            }
            catch { /* unreadable folder: keep what we have */ }
        }

        Recluster(items);

        // "The Final Season" has no number in its name: it is the season after the highest one found for the same title
        string BaseKey(ParsedName p) => NameParser.KeyOf(p with { Season = 1, Final = false, Part = 0, IsMovie = false });
        var topSeason = items.Where(i => !i.P.Final && !i.P.IsMovie).GroupBy(i => BaseKey(i.P)).ToDictionary(g => g.Key, g => g.Max(i => i.P.Season));
        foreach (var it in items.Where(i => i.P.Final))
            it.P = it.P with { Season = Math.Max(2, (topSeason.TryGetValue(BaseKey(it.P), out int ts) ? ts : 1) + 1) };

        // keys: name rules first, then aliases remembered from earlier merges
        var aliasMap = new Dictionary<string, string>();
        foreach (var e0 in existing.Where(x => !x.Custom && !x.Detected))
            foreach (string a in e0.Aliases) aliasMap[a] = e0.Key;
        foreach (var it in items)
        {
            it.Key = NameParser.KeyOf(it.P); it.Season = it.P.Season;
            it.Title = (it.P.Season > 1 ? it.P.Title + " (Season " + it.P.Season + ")" : it.P.Title) + (it.P.Part > 1 ? " Part " + it.P.Part : "");
            var rule = s.NameRules.FirstOrDefault(r => RuleHit(r, it.Key));
            if (rule != null) { it.Rule = rule; it.Key = rule.TargetKey; it.Title = rule.Name; }
            else if (aliasMap.TryGetValue(it.Key, out var canon)) it.Key = canon;
        }

        var groups = new Dictionary<string, Group>();
        foreach (var it in items)
        {
            if (!groups.TryGetValue(it.Key, out var g)) groups[it.Key] = g = new Group { Key = it.Key, Title = it.Title, Season = it.Season, Lib = it.Lib, Rule = it.Rule };
            g.Items.Add(it);
            if (g.Rule == null && it.Rule != null) g.Rule = it.Rule;
            if (it.Key != NameParser.KeyOf(it.P) && it.Rule != null) g.Aliases.Add(NameParser.KeyOf(it.P));
        }

        // groups made only of OP / ED / music / extras: file them under the series that lives next to them, otherwise ignore them
        var withEpisodes = groups.Values.Where(x => x.Episodes.Any()).ToList();
        foreach (var g in groups.Values.Where(x => !x.Episodes.Any()).ToList())
        {
            foreach (var it in g.Items.ToList())
            {
                var target = withEpisodes
                    .Where(w => w.Items.Any(x => IsUnder(it.Dir, x.Dir) || IsUnder(x.Dir, it.Dir)))
                    .OrderByDescending(w => w.Items.Max(x => IsUnder(it.Dir, x.Dir) ? x.Dir.Length : 0)).FirstOrDefault();
                if (target != null) { target.Items.Add(it); g.Items.Remove(it); }
            }
            if (g.Items.Count == 0) groups.Remove(g.Key);
        }
        // a lone special / OVA ("Death Parade Special.mkv" with no series next to it) is a title of its own, not something to throw away
        foreach (var g in groups.Values.Where(x => !x.Episodes.Any()).ToList())
        {
            if (g.Items.Count > 0 && g.Items.All(i => i.Cls.Kind == FileKind.Extra))
            {
                foreach (var it in g.Items) { it.Cls = new FileClass(FileKind.Episode, null, ""); it.Promoted = true; }
                continue;
            }
            groups.Remove(g.Key);
        }

        MergeVariants(groups);

        // ---------------------------------------------------------------- entries
        var result = new List<Entry>();
        var used = new HashSet<Entry>();
        var roots = new Dictionary<string, string>();
        foreach (var (key, g) in groups)
        {
            var main = g.Episodes.Any() ? g.Episodes : g.Items.AsEnumerable();
            string topDir = main.GroupBy(i => i.Dir, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Count()).First().Key;
            string common = CommonRoot(main.Select(i => i.Dir).Distinct(StringComparer.OrdinalIgnoreCase));
            roots[key] = string.IsNullOrEmpty(common) ? topDir : common;
        }

        foreach (var (key, g) in groups)
        {
            var found = existing.Where(x => !x.Custom && !used.Contains(x) && (x.Key == key || g.Aliases.Contains(x.Key) || x.Aliases.Contains(key))).ToList();
            var e = found.OrderByDescending(x => x.Matched).ThenByDescending(x => x.Watched).FirstOrDefault() ?? new Entry { Key = key };
            foreach (var other in found) { used.Add(other); if (other != e) Absorb(e, other); }
            used.Add(e);
            var lib = s.Folders[g.Lib];

            e.Key = key; e.Detected = false;
            e.IsMovie = e.ProviderMovie || (g.Items.Count > 0 && g.Items.All(i => i.P.IsMovie));
            e.IsSpecial = g.Items.Count > 0 && g.Items.All(i => i.Promoted);
            if (e.IsMovie && e.TotalEpisodes == 0) e.TotalEpisodes = 1;
            e.Kind = Kind.Anime; e.Source = lib.Source; e.ExtensionId = lib.ExtensionId;
            foreach (string a in g.Aliases) if (a != key) e.Aliases.Add(a);
            e.Aliases.Remove(key);

            var ordered = g.Items.OrderBy(i => KindRank(i.Cls.Kind)).ThenBy(i => i.P.Episode ?? int.MaxValue).ThenBy(i => i.Path, NaturalComparer.I).ToList();
            e.Files = ordered.Select(i => i.Path).ToList();
            e.Episodes = ordered.Count(i => i.Cls.Kind == FileKind.Episode);
            e.Extras = ordered.Count - e.Episodes;
            e.Ovas = ordered.Count(i => i.Cls.Kind == FileKind.Extra && Regex.IsMatch(i.Cls.Label, "^(ova|oad|sp|special)", RegexOptions.IgnoreCase));
            e.MaxEp = ordered.Where(i => i.Cls.Kind == FileKind.Episode && i.P.Episode != null).Select(i => i.P.Episode!.Value).DefaultIfEmpty(0).Max();
            e.FolderPath = roots[key];
            e.RawName = System.IO.Path.GetFileName(e.FolderPath.TrimEnd('\\', '/'));
            if (e.RawName.Length == 0) e.RawName = e.FolderPath;

            // "shared": other series live inside this series' folder tree -> never write cover.jpg / folder icon there
            e.SharedFolder = groups.Any(o => o.Key != key && roots[o.Key].Length > 0 && (IsUnder(roots[o.Key], e.FolderPath) || IsUnder(e.FolderPath, roots[o.Key])));

            e.SmartName = g.Rule?.Name;
            e.LocalOnly = g.Rule?.LocalOnly ?? false;
            if (e.LocalOnly) e.Matched = true;
            if (g.Rule != null) e.Title = g.Rule.Name;
            else if (string.IsNullOrEmpty(e.Title) || !e.Matched) e.Title = g.Season > 1 ? g.Title.Replace(" (Season " + g.Season + ")", "") + " (Season " + g.Season + ")" : g.Title;

            string folderCover = System.IO.Path.Combine(e.FolderPath, "cover.jpg");
            if (!e.SharedFolder && File.Exists(folderCover) && new FileInfo(folderCover).Length > 1000 &&
                (string.Equals(e.CoverFile, folderCover, StringComparison.OrdinalIgnoreCase) || CoverTools.IsValidFile(folderCover)))
                e.CoverFile = folderCover;
            else if (e.CoverFile == null || !File.Exists(e.CoverFile) || (e.SharedFolder && e.CoverFile.Equals(folderCover, StringComparison.OrdinalIgnoreCase)))
            {
                string cache = CoverTools.CacheFile(e.Key);
                e.CoverFile = File.Exists(cache) && new FileInfo(cache).Length > 1000 ? cache : null;
            }
            result.Add(e);
        }

        // entries that were not found this time
        foreach (var e in existing)
        {
            if (used.Contains(e)) continue;
            if (e.Detected) continue;                       // entries made from files outside the library folders are dropped: the library only looks inside the folders you added
            if (e.Custom) { result.Add(e); continue; }
            // a library whose drive is unplugged keeps its entries; entries of a reachable library whose files are gone are dropped
            bool rootReachable = rootsOk.Any(r => IsUnder(e.FolderPath, r));
            if (!rootReachable) result.Add(e);
        }
        return result;
    }

    /// <summary>Folds spelling / fansub variants of one title into a single series ("Kyouran Reijou Nia Liston" + "... Nia Liston (Mad Noble)").</summary>
    static void MergeVariants(Dictionary<string, Group> groups)
    {
        var list = groups.Values.Where(x => x.Rule == null).OrderByDescending(x => x.Items.Count).ToList();
        var dead = new HashSet<Group>();
        for (int i = 0; i < list.Count; i++)
        {
            if (dead.Contains(list[i])) continue;
            for (int j = i + 1; j < list.Count; j++)
            {
                if (dead.Contains(list[j])) continue;
                var a = list[i]; var b = list[j];
                if (a.Season != b.Season || a.Lib != b.Lib) continue;
                if (PartOfKey(a.Key) != PartOfKey(b.Key) || a.Key.Contains("#movie") != b.Key.Contains("#movie") || a.Key.Contains("#final") != b.Key.Contains("#final")) continue;   // Part 2 / a film are never a spelling variant of the series
                string ka = NameParser.KeyOf(a.Title), kb = NameParser.KeyOf(b.Title);
                if (ka.Length < 3 || kb.Length < 3 || ka[0] != kb[0]) continue;                  // cheap pre-filter: first letter
                bool same = Matching.TitleSim(a.Title, b.Title) >= 0.93;
                if (!same)
                {
                    string sh = ka.Length <= kb.Length ? ka : kb, lg = ka.Length <= kb.Length ? kb : ka;
                    same = sh.Length >= 10 && lg.StartsWith(sh, StringComparison.Ordinal) && (lg.Length - sh.Length) / (double)lg.Length <= 0.4;
                }
                if (!same) continue;
                a.Items.AddRange(b.Items); a.Aliases.Add(b.Key); a.Aliases.UnionWith(b.Aliases);
                dead.Add(b); groups.Remove(b.Key);
            }
        }
    }

    /// <summary>Moves what is worth keeping from a duplicate entry into the one that survives.</summary>
    static void Absorb(Entry keep, Entry other)
    {
        keep.Aliases.Add(other.Key);
        foreach (string a in other.Aliases) keep.Aliases.Add(a);
        keep.Aliases.Remove(keep.Key);
        keep.Watched = Math.Max(keep.Watched, other.Watched);
        keep.NeedsSync |= other.NeedsSync;
        if (other.LastPlayed > keep.LastPlayed) { keep.LastPlayed = other.LastPlayed; keep.LastFile = other.LastFile; }
        if (!keep.Matched && other.Matched)
        {
            keep.Matched = true; keep.Title = other.Title; keep.MalId ??= other.MalId; keep.AniListId ??= other.AniListId; keep.ExternalId ??= other.ExternalId;
            keep.TotalEpisodes = Math.Max(keep.TotalEpisodes, other.TotalEpisodes); keep.CoverUrl ??= other.CoverUrl; keep.BannerUrl ??= other.BannerUrl;
        }
        if (keep.CoverFile == null || !File.Exists(keep.CoverFile)) keep.CoverFile = other.CoverFile;
        if (keep.Added > other.Added) keep.Added = other.Added;
        foreach (var kv in other.FolderCovers) keep.FolderCovers.TryAdd(kv.Key, kv.Value);
    }

    // ------------------------------------------------------------------ phase 2 (internet)
    static int NumAfter(string key, string tag)
    {
        int i = key.IndexOf(tag, StringComparison.Ordinal);
        if (i < 0) return 0;
        int j = i + tag.Length, k = j;
        while (k < key.Length && char.IsDigit(key[k])) k++;
        return k > j && int.TryParse(key[j..k], out int n) ? n : 0;
    }
    static int SeasonOfKey(string key) => Math.Max(1, NumAfter(key, "#s"));
    static int PartOfKey(string key) => NumAfter(key, "#p");

    static string BaseTitle(Entry e) => Regex.Replace(e.DisplayTitle, @"\s*\(Season \d+\)$", "");

    static bool DueForRetry(Entry e) =>
        e.MatchTries < 2 || e.LastTry == null || (DateTime.Now - e.LastTry.Value) > TimeSpan.FromHours(Math.Min(24, 2 * e.MatchTries));

    static async Task<MetaMatch?> MatchOne(Settings s, Entry e, IProgress<string> log)
    {
        string title = string.IsNullOrWhiteSpace(e.SearchName) ? BaseTitle(e) : e.SearchName!.Trim(); int season = SeasonOfKey(e.Key);
        int part = PartOfKey(e.Key);
        var hint = e.IsMovie ? Matching.MediaHint.Movie : e.IsSpecial ? Matching.MediaHint.Special : Matching.MediaHint.Series;
        if (e.Source == Source.Extension && Extensions.Get(e.ExtensionId) is { } ext && !s.DisabledExtensions.Contains(ext.Id))
        {
            var m = await Extensions.Search(ext, season > 1 ? title + " Season " + season : title);
            if (m != null) return m;
        }
        var first = await Matching.FindAsync(e.Source == Source.MyAnimeList ? Source.MyAnimeList : Source.AniList, title, season, Math.Max(1, e.Episodes), e.RawName, log, why => e.MatchNote = why, hint, part > 1 ? part - 1 : 0);
        if (first != null) return first;
        // fallback for titles nothing was found for: AniList first, with a slightly softer threshold (AniList knows romaji, English and synonyms)
        var second = await Matching.FindAsync(Source.AniList, title, season, Math.Max(1, e.Episodes), e.RawName, log, why => e.MatchNote = why, hint, part > 1 ? part - 1 : 0, true);
        if (second != null) { e.MatchNote = null; log.Report("\"" + title + "\": found by the AniList fallback"); }
        return second;
    }

    /// <summary>Tries every source until a picture really decodes: cached copy, known URL, AniList by id, Jikan by id, Kitsu by title,
    /// and finally a frame cut from the first episode (never blank). Never overwrites a valid cover.jpg the user already has.</summary>
    static async Task<bool> EnsureCoverAsync(Entry e, bool online, CancellationToken ct)
    {
        if (e.CoverFile != null && File.Exists(e.CoverFile) && new FileInfo(e.CoverFile).Length > 1000) return false;
        string cache = CoverTools.CacheFile(e.Key);
        bool got = CoverTools.IsValidFile(cache);

        if (!got && online)
        {
            if (e.CoverUrl != null) got = await Providers.DownloadImage(e.CoverUrl, cache);
            if (!got && e.AniListId is int aid)
            {
                var m = await Providers.AniListById(aid);
                if (m?.BannerUrl != null) e.BannerUrl ??= m.BannerUrl;
                if (m?.CoverUrl != null && m.CoverUrl != e.CoverUrl && await Providers.DownloadImage(m.CoverUrl, cache)) { e.CoverUrl = m.CoverUrl; got = true; }
            }
            if (!got && e.MalId is int mid)
            {
                string? u = await Providers.JikanCover(mid);
                if (u != null && await Providers.DownloadImage(u, cache)) { e.CoverUrl = u; got = true; }
            }
            if (!got && !e.Custom)
            {
                string? u = await Providers.KitsuCover(BaseTitle(e));
                if (u != null && await Providers.DownloadImage(u, cache)) { e.CoverUrl = u; got = true; }
            }
        }
        if (!got)
        {
            string? first = e.Files.FirstOrDefault(f => IsVideo(f) && File.Exists(f));
            if (first != null) got = await CoverTools.GrabPosterAsync(first, cache, ct);
        }
        if (!got) return false;

        string result = cache;
        if (!e.SharedFolder && Directory.Exists(e.FolderPath))
        {
            try
            {
                string dest = System.IO.Path.Combine(e.FolderPath, "cover.jpg");
                if (!File.Exists(dest) || !CoverTools.IsValidFile(dest)) { if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal); File.Copy(cache, dest, true); }
                result = dest;
            }
            catch { /* read-only folder: the cache copy is used */ }
        }
        e.CoverFile = result; e.IconSet = false;
        return true;
    }

    /// <summary>Phase 2 - needs the internet: finds provider matches (and episode totals) for entries without one, then makes sure every series
    /// has a picture. Series marked "local only" are never looked up. Returns how many entries changed.</summary>
    public static async Task<int> MatchAndCoversAsync(Settings s, List<Entry> entries, IProgress<string> log, CancellationToken ct = default, bool force = false)
    {
        int changed = 0, airingLeft = 40;      // airing status of already matched series is filled in 40 at a time
        foreach (var e in entries.ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (e.Kind != Kind.Anime) continue;
            if (e.Matched && e.Airing == null && e.AniListId is int aid0 && airingLeft > 0 && !e.LocalOnly)
            {
                try
                {
                    if (await Providers.IsOnlineAsync())
                    {
                        airingLeft--;
                        var am = await Providers.AniListById(aid0);
                        e.Airing = am?.Airing ?? "UNKNOWN"; changed++;
                        await Task.Delay(250, ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            bool online = !e.LocalOnly;

            if (!e.Matched && !e.Custom && !e.LocalOnly && (force || DueForRetry(e)))
            {
                if (!await Providers.IsOnlineAsync()) break;
                log.Report("Matching: " + e.DisplayTitle);
                try
                {
                    var m = await MatchOne(s, e, log);
                    e.LastTry = DateTime.Now;
                    if (m != null)
                    {
                        e.Title = m.Title.Length > 0 ? m.Title : e.Title;
                        e.MalId = m.MalId ?? e.MalId; e.AniListId = m.AniListId ?? e.AniListId; e.ExternalId = m.ExternalId ?? e.ExternalId;
                        if (m.Episodes is > 0) e.TotalEpisodes = m.Episodes.Value;
                        e.Airing = m.Airing ?? e.Airing; e.CoverUrl = m.CoverUrl ?? e.CoverUrl; e.BannerUrl = m.BannerUrl ?? e.BannerUrl;
                        if (string.Equals(m.Format, "MOVIE", StringComparison.OrdinalIgnoreCase)) { e.ProviderMovie = true; e.IsMovie = true; if (e.TotalEpisodes == 0) e.TotalEpisodes = 1; }
                        e.Matched = true; e.MatchTries = 0; e.MatchNote = null; changed++;
                    }
                    else e.MatchTries++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log.Report("Network error: " + ex.Message); }
                await Task.Delay(200, ct);
            }

            if (s.DownloadCovers)
            {
                try
                {
                    if (online && !await Providers.IsOnlineAsync()) online = false;
                    if (await EnsureCoverAsync(e, online, ct)) { changed++; await Task.Delay(online ? 250 : 0, ct); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log.Report("Cover failed for " + e.DisplayTitle + ": " + ex.Message); }
            }

            if (!e.LocalOnly && e.BannerUrl != null && (e.BannerFile == null || !File.Exists(e.BannerFile)) && await Providers.IsOnlineAsync())
            {
                string bf = CoverTools.BannerFile(e.Key);
                if (await Providers.DownloadImage(e.BannerUrl, bf)) { e.BannerFile = bf; changed++; }
            }
        }
        changed += MergeByProviderId(entries);
        return changed;
    }

    /// <summary>Two local titles that matched the same AniList / MAL entry are the same series: merge them (the next scan keeps them merged).</summary>
    public static int MergeByProviderId(List<Entry> entries)
    {
        int merged = 0;
        foreach (var grp in entries.Where(e => !e.Custom && e.Kind == Kind.Anime && e.AniListId is > 0).GroupBy(e => e.AniListId!.Value).Where(g => g.Count() > 1).ToList())
        {
            var keep = grp.OrderByDescending(e => e.Files.Count).ThenByDescending(e => e.Watched).First();
            foreach (var other in grp.Where(e => e != keep).ToList())
            {
                Absorb(keep, other);
                foreach (string f in other.Files) if (!keep.Files.Contains(f)) keep.Files.Add(f);
                keep.Episodes += other.Episodes; keep.Extras += other.Extras;
                entries.Remove(other); merged++;
            }
        }
        return merged;
    }

    /// <summary>Merges <paramref name="other"/> into <paramref name="keep"/> right now (the matching smart-name rule keeps it merged on later scans).</summary>
    public static void MergeEntries(List<Entry> entries, Entry keep, Entry other)
    {
        Absorb(keep, other);
        foreach (string f in other.Files) if (!keep.Files.Contains(f, StringComparer.OrdinalIgnoreCase)) keep.Files.Add(f);
        keep.Episodes += other.Episodes; keep.Extras += other.Extras;
        entries.Remove(other);
    }

    // ------------------------------------------------------------------ smart names
    /// <summary>Creates / replaces a smart-name rule. <paramref name="matchKeys"/> are the title keys of the files it should catch.</summary>
    public static void SetRule(Settings s, IEnumerable<string> matchKeys, string name, string targetKey, bool localOnly)
    {
        foreach (string k in matchKeys)
        {
            s.NameRules.RemoveAll(r => r.Match == k);
            s.NameRules.Add(new NameRule { Match = k, Name = name, TargetKey = targetKey, LocalOnly = localOnly });
        }
    }

    // ------------------------------------------------------------------ watch log
    static readonly char[] Sep = { '\t' };

    static bool Under(string file, string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        string f = folder.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        return file.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads what the player appended to watched.log since the last call. Works offline; sets NeedsSync on every entry whose
    /// progress grew. Files the library does not know become "Detected" entries when the player flagged them as anime.
    /// Openings, endings, music and extras never count as episodes.
    /// Format: time TAB path TAB start|stop|finished TAB pos TAB dur TAB anime (older logs: time TAB path = finished).</summary>
    public static bool ApplyWatchLog(Settings s, List<Entry> entries)
    {
        string log = s.WatchLog;
        if (string.IsNullOrEmpty(log) || !File.Exists(log)) return false;
        long len;
        try { len = new FileInfo(log).Length; } catch { return false; }
        if (len < s.WatchLogOffset) s.WatchLogOffset = 0;          // log was deleted / restarted
        if (len == s.WatchLogOffset) return false;

        string text;
        try
        {
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(s.WatchLogOffset, SeekOrigin.Begin);
            var buf = new byte[len - s.WatchLogOffset];
            int got = 0;
            while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
            text = System.Text.Encoding.UTF8.GetString(buf, 0, got);
        }
        catch { return false; }

        int lastNl = text.LastIndexOf('\n');
        if (lastNl < 0) return false;                               // only a partial line so far
        string complete = text[..(lastNl + 1)];
        s.WatchLogOffset += System.Text.Encoding.UTF8.GetByteCount(complete);

        bool changed = false;
        foreach (string raw in complete.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            var parts = line.Split(Sep);
            if (parts.Length < 2 || parts[1].Length == 0) continue;
            string path = parts[1];
            string ev = parts.Length > 2 ? parts[2] : "finished";
            double pos = parts.Length > 3 && double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pp) ? pp : 0;
            double dur = parts.Length > 4 && double.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 0;
            bool flagged = parts.Length < 6 || parts[5] == "1";
            DateTime when = long.TryParse(parts[0], out long unix) ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : DateTime.Now;
            if (path.Contains("://") || !IsMedia(path)) continue;
            // only what lives inside the folders you added (or a custom series' own folder) is the library's business
            if (!s.Folders.Any(f => Under(path, f.Path)) && !entries.Any(x => x.Custom && Under(path, x.FolderPath))) continue;

            var pn = NameParser.ForFile(path);
            var cls = NameParser.Classify(path);
            string key = NameParser.KeyOf(pn);
            var rule = s.NameRules.FirstOrDefault(r => RuleHit(r, key));
            if (rule != null) key = rule.TargetKey;
            var e = entries.FirstOrDefault(x => x.Key == key || x.Aliases.Contains(key))
                    ?? entries.Where(x => (x.Custom || x.Files.Contains(path, StringComparer.OrdinalIgnoreCase)) && Under(path, x.FolderPath)).OrderByDescending(x => x.FolderPath.Length).FirstOrDefault();
            if (e == null) continue;      // a file of a library folder that the last scan has not seen yet: the next scan creates its series (nothing is "detected" any more)

            if (e.LastPlayed == null || when > e.LastPlayed) { e.LastPlayed = when; e.LastFile = path; changed = true; }
            if (cls.Kind != FileKind.Episode) continue;                      // an opening / ending / song is not progress
            bool finished = ev == "finished" || (ev == "stop" && dur > 0 && pos >= dur * 0.85);
            if (!finished) continue;
            int ep = pn.Episode ?? Math.Max(1, e.Watched + (e.Episodes == 1 ? 1 : 0));
            if (!e.WatchedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) { e.WatchedFiles.Add(path); changed = true; }
            if (ep > e.Watched) { e.Watched = ep; e.NeedsSync = true; changed = true; }
            if (e.Episodes < e.Watched && e.Detected) e.Episodes = e.Watched;
        }
        return changed;
    }
}
