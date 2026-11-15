using System.Text;
using System.Text.RegularExpressions;

namespace AnimeLibrary;

/// <summary>Scores search results against a local series so that "Re Zero kara - 2nd Season" does not end up on season 1, a movie, or a
/// completely different show that merely contains the same words. The first hit of a search is not trusted.</summary>
public static class Matching
{
    static readonly Regex NonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    static readonly Regex SeasonWords = new(@"\b(?:season\s*(\d{1,2})|(\d{1,2})(?:st|nd|rd|th)\s+season|s(\d{1,2})|part\s*(\d{1,2})|cour\s*(\d{1,2}))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RomanEnd = new(@"\s(II|III|IV|V|VI)$", RegexOptions.Compiled);

    public static string Norm(string s)
    {
        s = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return NonWord.Replace(sb.ToString(), " ").Trim();
    }

    static int Lev(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>0..1 similarity of two titles: edit distance, shared words and "one title is the other plus a subtitle".</summary>
    public static double TitleSim(string a, string b)
    {
        a = Norm(a); b = Norm(b);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        int max = Math.Max(a.Length, b.Length);
        double lev = 1.0 - (double)Lev(a.Length > 120 ? a[..120] : a, b.Length > 120 ? b[..120] : b) / Math.Min(max, 120);

        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries); var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sa = new HashSet<string>(ta); var sb = new HashSet<string>(tb);
        int common = sa.Count(sb.Contains);
        double f1 = common == 0 ? 0 : 2.0 * common / (sa.Count + sb.Count);

        double contain = 0;
        var shorter = sa.Count <= sb.Count ? sa : sb; var longer = sa.Count <= sb.Count ? sb : sa;
        if (shorter.Count >= 1 && shorter.All(longer.Contains))
            contain = 0.72 + 0.23 * ((double)shorter.Count / longer.Count);        // "Frieren" in "Sousou no Frieren"
        // folder names cut off by the file system ("Chi - Chikyuu no Undou ni Tsuit"): a long prefix of the other title is a strong hit
        string pa = a.Length <= b.Length ? a : b, pb = a.Length <= b.Length ? b : a;
        if (pa.Length >= 14 && pb.StartsWith(pa, StringComparison.Ordinal) && pb.Length - pa.Length <= 16) contain = Math.Max(contain, 0.90);
        return Math.Max(lev, Math.Max(0.45 * lev + 0.55 * f1, contain));
    }

    static int SeasonOf(string title)
    {
        var m = SeasonWords.Match(title);
        if (m.Success) foreach (int g in new[] { 1, 2, 3, 4, 5 }) if (m.Groups[g].Success) return int.Parse(m.Groups[g].Value);
        var r = RomanEnd.Match(title);
        if (r.Success) return r.Groups[1].Value switch { "II" => 2, "III" => 3, "IV" => 4, "V" => 5, _ => 6 };
        return 1;
    }

    static string StripSeason(string t) => SeasonWords.Replace(RomanEnd.Replace(t, ""), " ").Trim();

    /// <summary>Everything the scorer wants to know about the local side.</summary>
    public enum MediaHint { Series, Movie, Special }
    public record Local(string Title, int Season, int FileCount, MediaHint Hint = MediaHint.Series);

    public static double Score(Candidate c, Local l)
    {
        string baseQuery = StripSeason(l.Title);
        double best = 0;
        foreach (string n in c.Names)
        {
            double s1 = TitleSim(l.Title, n);                       // full query vs title
            double s2 = TitleSim(baseQuery, StripSeason(n)) * 0.93; // without season words on both sides
            best = Math.Max(best, Math.Max(s1, s2));
        }

        // season: asking for season 2 -> a candidate that says season 2 is better, one that says nothing about it is worse
        int wanted = l.Season;
        int cs = c.Names.Select(SeasonOf).DefaultIfEmpty(1).Max();
        if (wanted > 1) best += cs == wanted ? 0.14 : (cs == 1 ? -0.10 : -0.18);
        else best += cs > 1 ? -0.12 : 0.02;

        string f = c.Format.ToUpperInvariant();
        bool tv = f is "TV" or "TV_SHORT" or "ONA" or "TV SPECIAL";
        if (l.Hint == MediaHint.Movie)
        {
            // a file that says it is a film: only a film is right ("Kino no Tabi - Country of Illness" must not become the TV series)
            best += f == "MOVIE" ? 0.14 : (f is "SPECIAL" or "OVA" or "TV_SPECIAL" ? -0.06 : -0.20);
        }
        else if (l.Hint == MediaHint.Special)
        {
            best += f is "SPECIAL" or "OVA" or "ONA" or "TV_SPECIAL" or "MUSIC" ? 0.12 : (f == "MOVIE" ? -0.04 : -0.10);
        }
        else
        {
            if (tv) best += 0.03;
            if (f == "MOVIE") best += l.FileCount <= 3 ? 0.03 : -0.06;               // a movie has 1-3 files
        }
        if (f is "SPECIAL" or "OVA" or "MUSIC" or "TV_SPECIAL" or "SPECIAL ") best -= l.FileCount > 4 ? 0.10 : 0.0;
        if (c.Episodes is > 0 && l.FileCount > c.Episodes * 1.6 + 2) best -= 0.12;   // 24 files cannot be a 12-episode show
        best += Math.Min(0.04, Math.Log10(c.Popularity + 1) / 100);                  // tie-break: the better-known entry
        return best;
    }

    public static Candidate? Best(IEnumerable<Candidate> cands, Local l, out double score)
    {
        Candidate? best = null; score = 0;
        foreach (var c in cands)
        {
            double s = Score(c, l);
            if (s > score) { score = s; best = c; }
        }
        return best;
    }

    /// <summary>Search strings to try, most specific first: the parsed title, the same without a subtitle ("Title: Something" / "Title - Something"),
    /// the same without season words, and the folder/raw name. Noise like "TV + OVA + SP", "v2", "05 - " is removed from all of them.</summary>
    public static List<string> QueryVariants(string title, int season, string? raw)
    {
        var list = new List<string>();
        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s) && s!.Trim().Length >= 2 && !list.Contains(s.Trim(), StringComparer.OrdinalIgnoreCase)) list.Add(s.Trim()); }
        title = NameParser.Tidy(title);
        string noSeason = StripSeason(title);
        Add(season > 1 ? noSeason + " Season " + season : title);
        Add(noSeason);
        int cut = noSeason.IndexOfAny(new[] { ':', '-', '~', '(' });
        if (cut >= 4) Add(noSeason[..cut]);
        var words = noSeason.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 5) Add(string.Join(' ', words.Take(4)));
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string rc = NameParser.Tidy(NameParser.CleanTitle(raw!));
            Add(rc);
            var cm = Regex.Match(raw!, @"\((White|Black)\)", RegexOptions.IgnoreCase);        // Monogatari: "Nekomonogatari (White)" is "(Shiro)" on AniList
            if (cm.Success) { string baseName = Regex.Replace(rc, @"\s*(White|Black)$", "", RegexOptions.IgnoreCase); Add(baseName + " (" + (cm.Groups[1].Value.Equals("white", StringComparison.OrdinalIgnoreCase) ? "Shiro" : "Kuro") + ")"); Add(baseName); }
        }
        return list;
    }

    /// <summary>Last-chance queries for misspelled / truncated names: the name without its (possibly cut-off) last word, then single long words.
    /// Candidates are still scored by title similarity, so "Bogiepop phantom" finds "Boogiepop Phantom" through the word "phantom".</summary>
    static List<string> FuzzyVariants(string title)
    {
        var list = new List<string>();
        var words = StripSeason(NameParser.Tidy(title)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "-").ToList();
        if (words.Count >= 3) list.Add(string.Join(' ', words.Take(words.Count - 1)));
        foreach (string w in words.Where(w => w.Length >= 5).OrderByDescending(w => w.Length).Take(3)) list.Add(w);
        if (words.Count >= 2) list.Add(string.Join(' ', words.Take(2)));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public const double Accept = 0.66;

    /// <summary>Finds the best AniList / Jikan entry for a local series. Returns null when nothing is convincing - an honest "unmatched"
    /// is better than a wrong cover and a wrong tracker entry. <paramref name="why"/> receives a one-line reason when nothing was accepted.</summary>
    public static async Task<MetaMatch?> FindAsync(Source source, string title, int season, int fileCount, string? raw, IProgress<string>? log = null, Action<string>? why = null, MediaHint hint = MediaHint.Series, int extraHops = 0, bool relaxed = false)
    {
        double accept = relaxed ? 0.58 : Accept;   // relaxed = the AniList fallback for titles that were not found the first time
        title = NameParser.Tidy(title);
        if (season <= 1 && raw != null) { int rs = NameParser.SeasonFromBrackets(raw); if (rs > 1) season = rs; }   // "(Season 2 Part 2)" in the folder name
        var local = new Local(title, season, fileCount, hint);
        Candidate? best = null; double bestScore = 0; int hits = 0, errors = 0, searches = 0;

        async Task<bool> Run(IEnumerable<string> queries, Local loc, double stop, double soft)
        {
            foreach (string q in queries)
            {
                foreach (bool useJikan in source == Source.MyAnimeList ? new[] { true, false } : new[] { false, true })
                {
                    List<Candidate> cands;
                    searches++;
                    try { cands = useJikan ? await Providers.JikanCandidates(q) : await Providers.AniListCandidates(q); }
                    catch { errors++; continue; }
                    await Task.Delay(useJikan ? 420 : 250);
                    hits += cands.Count;
                    var b = Best(cands, loc, out double s);
                    if (b != null && s > bestScore) { best = b; bestScore = s; }
                    if (bestScore >= stop) return true;
                }
                if (bestScore >= soft) return true;
            }
            return false;
        }

        await Run(QueryVariants(title, season, raw), local, 0.92, 0.80);
        if (hint == MediaHint.Movie && bestScore < 0.80) await Run(new[] { title + " movie" }, local, 0.92, 0.80);

        // season 2/3/4 without a usable title ("minami ke s4", "Zero no Tsukaima S3"): season 1, then follow its sequels
        if ((season > 1 || extraHops > 0) && hint == MediaHint.Series && bestScore < 0.92)
        {
            string baseTitle = StripSeason(title);
            Candidate? s1 = best; double keep = bestScore;
            best = null; bestScore = 0;
            await Run(QueryVariants(baseTitle, 1, null).Take(3), new Local(baseTitle, 1, fileCount), 0.92, 0.80);
            var first = best; double firstScore = bestScore;
            best = s1; bestScore = keep;
            if (first?.AniListId is int id && firstScore >= 0.80)
            {
                try
                {
                    var seq = await Providers.AniListSequelChain(id, Math.Max(1, season - 1 + extraHops));
                    // only when the direct hit was weak, and only when the sequel at least looks like the same franchise
                    if (seq != null && keep < 0.86 && Score(seq, local) >= 0.45) { best = seq; bestScore = 0.86; log?.Report($"\"{title}\": season {season} found through the sequels of \"{first.Title}\""); }
                }
                catch { errors++; }
            }
        }

        if (bestScore < accept)
        {
            var save = (best, bestScore);
            await Run(FuzzyVariants(title), local, 0.92, 0.80);
            if (bestScore < save.bestScore) (best, bestScore) = save;
        }

        if (best == null || bestScore < accept)
        {
            string reason = errors >= searches && searches > 0 ? "the search services could not be reached"
                : hits == 0 ? "the name gave no results (try Smart name / search name)"
                : $"closest was \"{best?.Title}\" ({bestScore:0.00}), not similar enough";
            why?.Invoke(reason);
            log?.Report($"No convincing match for \"{title}\": {reason}");
            return null;
        }
        return best.ToMatch();
    }
}
