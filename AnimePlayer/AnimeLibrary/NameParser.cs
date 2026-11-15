using System.IO;
using System.Text.RegularExpressions;

namespace AnimeLibrary;

/// <summary><paramref name="Part"/> = "Part 2" / "Cour 2" (0 when absent or 1); <paramref name="Final"/> = "The Final Season" (its number is worked out from the other seasons);
/// <paramref name="IsMovie"/> = a film: "Movie 2 - ...", "... the Movie", a year in brackets and no episode number.</summary>
public record ParsedName(string Title, int Season, int? Episode, int Part = 0, bool Final = false, bool IsMovie = false);
/// <summary>What a media file is: an episode, an opening / ending, music, or an extra (PV, trailer, special ...).</summary>
public record FileClass(FileKind Kind, int? Number, string Label);

/// <summary>Reads titles, seasons, episode numbers and file kinds out of file / folder names without any naming convention:
/// "[SubsPlease] Frieren - 05 (1080p) [ABCD1234].mkv", "Show.Name.S02E07.1080p.WEB.mkv", "Show 2nd Season - 03.mp4",
/// "Show - NCOP1.mkv", "OST/01 Track.flac" ...</summary>
public static class NameParser
{
    static readonly Regex Brackets = new(@"[\[\(\{][^\]\)\}]*[\]\)\}]", RegexOptions.Compiled);
    static readonly Regex BracketChars = new(@"[\[\]\(\)\{\}]", RegexOptions.Compiled);
    static readonly Regex Junk = new(@"\b(2160p|1080p|720p|576p|480p|4k|bluray|blu-ray|bd|bdrip|brrip|dvd|dvdrip|web-?dl|web-?rip|webrip|hevc|x26[45]|h\.?26[45]|av1|aac|flac|opus|ac3|dts|10bit|8bit|dual[ -]?audio|multi[ -]?subs?|batch|complete|compete|uncensored|remux|proper|repack|raw)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SxE = new(@"\bS(\d{1,2})[ ._-]?E(\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NxM = new(@"\b(\d{1,2})x(\d{2,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex EpWord = new(@"(?:^|[\s_.\-])(?:ep|episode|e)\s*\.?(\d{1,4})(?:v\d)?(?=[\s_.\-]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DashEp = new(@"\s-\s*(\d{1,4})(?:v\d)?(?=\s|$|[\-_.])", RegexOptions.Compiled);
    static readonly Regex TrailNum = new(@"[\s_.](\d{1,3})(?:v\d)?$", RegexOptions.Compiled);
    static readonly Regex SeasonA = new(@"\b(?:season|s)\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SeasonB = new(@"\b(\d{1,2})(?:st|nd|rd|th)\s+season\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Roman = new(@"\s(II|III|IV|V|VI)$", RegexOptions.Compiled);
    static readonly Regex Year = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

    // folders that only organise files and never name the series
    static readonly Regex SkipFolder = new(
        @"^(season\s*\d+|s\d{1,2}|specials?|sp|extras?|ovas?|oads?|bd|bdrip|bdmv|dvd|complete|batch|videos?|episodes?|eps?|subs?|subtitles?|fonts?|" +
        @"ost|osts|soundtracks?|music|bgm|songs?|audio|cd\s*\d*|discs?\s*\d*|disk\s*\d*|nc|ncop|nced|op|ed|op\s*[&+and-]+\s*ed|openings?|endings?|creditless.*|" +
        @"pv|cm|cms|trailers?|previews?|menus?|scans?|bonus(es)?|featurettes?|promo.*|behind the scenes|interviews?|commentary|samples?|" +
        @"1080p|720p|480p|2160p|4k|x26[45]|hevc|web|webrip|web-dl|bluray|blu-ray|remux|raw|raws|dual audio|multi.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---- file kind detection
    static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".ape", ".mka", ".alac", ".wv" };
    static readonly Regex OpTag = new(@"(?<![A-Za-z0-9])((?:NC)?)(OP|OPENING)(?:[ _.\-]?(\d{1,2}))?(?:v\d)?(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex EdTag = new(@"(?<![A-Za-z0-9])((?:NC)?)(ED|ENDING|CLOSING)(?:[ _.\-]?(\d{1,2}))?(?:v\d)?(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MusicTag = new(@"(?<![A-Za-z0-9])(OST|soundtrack|BGM|insert song|character song|image song|theme song|drama cd)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ExtraTag = new(@"(?<![A-Za-z0-9])(PV\d*|CM\d*|trailers?|teasers?|previews?|menu|bonus|making of|behind the scenes|interview|commentary|recap|creditless|clean (?:op|ed)|SP\d{1,2}|OVA\d*|OAD\d*|specials?)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MusicFolder = new(@"^(ost|osts|soundtracks?|music|bgm|songs?|audio|cd\s*\d*|discs?\s*\d*|disk\s*\d*|character songs?|insert songs?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex OpEdFolder = new(@"^(nc|ncop|nced|nc ?op ?(?:&|and|\+|-)? ?ed|op|ed|op ?(?:&|and|\+|-) ?ed|openings?|endings?|creditless.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ExtraFolder = new(@"^(extras?|specials?|sp|ovas?|oads?|bonus(es)?|featurettes?|pv|cm|cms|trailers?|previews?|menus?|scans?|promo.*|behind the scenes|interviews?|commentary|samples?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Full-width characters that Japanese release names use (／ ： ！ ［ ］ （ ） ～ ＆ 　) become their normal ASCII forms.</summary>
    static string Fix(string s)
    {
        if (s.IndexOfAny(new[] { '／', '：', '！', '？', '［', '］', '（', '）', '～', '＆', '　', '－', '＋', '｜', '＿' }) < 0) return s;
        return s.Replace("／", " / ").Replace("：", ": ").Replace("！", "!").Replace("？", "?").Replace('［', '[').Replace('］', ']').Replace('（', '(').Replace('）', ')')
                .Replace('～', '~').Replace('＆', '&').Replace('　', ' ').Replace('－', '-').Replace('＋', '+').Replace('｜', '|').Replace('＿', '_');
    }

    static string Flat(string s) => Regex.Replace(Fix(s).Replace('_', ' ').Replace('.', ' '), @"\s+", " ").Trim();

    // fansub groups written WITHOUT brackets: "Commie_Show_Name_-_01", "HorribleSubs Show - 03"
    static readonly Regex LeadGroup = new(@"^(?:Commie|HorribleSubs|Coalgirls|UTW|Ohys-Raws|Erai-raws|SubsPlease|Judas|EMBER|ASW|Anime Time|Chihiro|Kaizoku|Doremi|Mezashite|FFF|Hakugetsu|Vivid|Underwater|Nii-sama|GJM|Aniplex|DameDesuYo|Beatrice-Raws|Moozzi2|Reaktor|Arid|Hatsuyuki|Gao|Zurako|S56|DmonHiro|Kawaiika-Raws|Lilith-Raws|SweetSub|Ayako|a4e|KiteSeeker|Tsundere|Cleo|WhyNot|Yameii|Almighty|Vodes)[ _.\-]+(?=\S)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MoviePrefix = new(@"^\s*(?:the\s+)?(?:movie|film|gekijouban)\s*\d{0,2}\s*[-:.~]*\s*(?=\S)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex MovieWord = new(@"(?<![A-Za-z0-9])(?:the\s+movie|movie\s*\d{1,2}|gekijouban|\bfilm)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex YearInBrackets = new(@"[\[\(]\s*(?:19|20)\d{2}\s*[\]\)]", RegexOptions.Compiled);
    static readonly Regex SeasonOrd = new(@"\b(second|third|fourth|fifth|sixth|seventh|eighth)\s+(?:season|series)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex FinalSeason = new(@"\b(?:the\s+)?final\s+season\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex PartWord = new(@"\b(?:part|cour)\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static int OrdinalNumber(string w) => w.ToLowerInvariant() switch { "second" => 2, "third" => 3, "fourth" => 4, "fifth" => 5, "sixth" => 6, "seventh" => 7, _ => 8 };

    static string CaseLabel(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>Opening / ending / music / extra / episode - from the file name, its extension and the folders above it.</summary>
    public static FileClass Classify(string path)
    {
        string file = System.IO.Path.GetFileName(path);
        string ext = System.IO.Path.GetExtension(file);
        string stem = System.IO.Path.GetFileNameWithoutExtension(file);
        string name = Flat(BracketChars.Replace(stem, " "));             // bracket CONTENT is kept: "[NCOP]" is a tag too
        bool atStart(Match m) => m.Index == 0;
        // "Episode 5 - Opening Night": the full words opening / ending / closing are only tags when the name has no episode number
        bool hasEp = SxE.IsMatch(name) || NxM.IsMatch(name) || DashEp.IsMatch(name) || EpWord.IsMatch(name);

        // folders above (nearest three)
        string? dir = System.IO.Path.GetDirectoryName(path);
        bool musicFolder = false, opedFolder = false, extraFolder = false;
        for (int i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
        {
            string leaf = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/')).Trim();
            if (leaf.Length == 0) break;
            if (MusicFolder.IsMatch(leaf)) musicFolder = true;
            else if (OpEdFolder.IsMatch(leaf)) opedFolder = true;
            else if (ExtraFolder.IsMatch(leaf)) extraFolder = true;
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        if (AudioExt.Contains(ext)) return new FileClass(FileKind.Music, null, "Music");

        var mo = OpTag.Match(name);
        if (mo.Success && !(atStart(mo) && mo.Groups[1].Length == 0 && mo.Groups[3].Length == 0) && !(mo.Groups[2].Length > 2 && hasEp))
            return new FileClass(FileKind.Opening, mo.Groups[3].Success && mo.Groups[3].Length > 0 ? int.Parse(mo.Groups[3].Value) : null,
                (mo.Groups[1].Length > 0 ? "Creditless opening" : "Opening") + (mo.Groups[3].Length > 0 ? " " + int.Parse(mo.Groups[3].Value) : ""));
        var me = EdTag.Match(name);
        if (me.Success && !(atStart(me) && me.Groups[1].Length == 0 && me.Groups[3].Length == 0) && !(me.Groups[2].Length > 2 && hasEp))
            return new FileClass(FileKind.Ending, me.Groups[3].Success && me.Groups[3].Length > 0 ? int.Parse(me.Groups[3].Value) : null,
                (me.Groups[1].Length > 0 ? "Creditless ending" : "Ending") + (me.Groups[3].Length > 0 ? " " + int.Parse(me.Groups[3].Value) : ""));

        var mm = MusicTag.Match(name);
        if (mm.Success) return new FileClass(FileKind.Music, null, "Music");
        if (musicFolder) return new FileClass(FileKind.Music, null, "Music");

        if (opedFolder)
        {
            if (Regex.IsMatch(name, @"(?<![A-Za-z0-9])(ED|ENDING|NCED)", RegexOptions.IgnoreCase)) return new FileClass(FileKind.Ending, null, "Ending");
            return new FileClass(FileKind.Opening, null, "Opening");
        }

        var mx = ExtraTag.Match(name);
        // a series called "Special A" / "Preview ..." must stay a series: a tag at the very start only counts when it carries a number (PV1, CM2, OVA1)
        if (mx.Success && !(atStart(mx) && !char.IsDigit(mx.Value[^1]))) return new FileClass(FileKind.Extra, null, CaseLabel(mx.Value));
        if (extraFolder) return new FileClass(FileKind.Extra, null, "Extra");
        return new FileClass(FileKind.Episode, null, "");
    }

    /// <summary>The same name without its OP / ED / OST / extra tag, so "Show - NCOP1" parses to the title "Show".</summary>
    static string StripTags(string fileName)
    {
        string s = System.IO.Path.GetFileNameWithoutExtension(fileName);
        s = OpTag.Replace(s, " "); s = EdTag.Replace(s, " "); s = MusicTag.Replace(s, " "); s = ExtraTag.Replace(s, " ");
        return s;
    }

    /// <summary>Parses a file name (with or without extension).</summary>
    static readonly Regex BracketSeason = new(@"\b(?:season\s*(\d{1,2})|(\d{1,2})(?:st|nd|rd|th)\s+season|S(\d{1,2})(?!\s*[-~]\s*\d)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex NoisePlus = new(@"\b(?:TV|OVAs?|OADs?|SP|Specials?|Movies?)(?:\s*[+&]\s*(?:TV|OVAs?|OADs?|SP|Specials?|Movies?)\b)+|\s\+\s*(?:OVAs?|OADs?|SP|Specials?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex VerTag = new(@"\s+v\d$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex LeadNum = new(@"^\s*\d{1,3}\s*[-.]\s+(?=\p{L})", RegexOptions.Compiled);

    /// <summary>Season written inside brackets: "(Season 2 Part 2)", "[S3]", "(2nd Season)". Ranges like "(S01-05)" (a batch of many seasons) give 1.</summary>
    public static int SeasonFromBrackets(string text)
    {
        foreach (Match b in Brackets.Matches(text ?? ""))
        {
            var m = BracketSeason.Match(b.Value);
            if (!m.Success) continue;
            foreach (int g in new[] { 1, 2, 3 }) if (m.Groups[g].Success && int.TryParse(m.Groups[g].Value, out int n) && n > 0) return n;
        }
        return 1;
    }

    /// <summary>Search-friendly title: no "05 - " numbering, no "TV + OVA + SP", no trailing "v2".</summary>
    public static string Tidy(string t)
    {
        t = LeadNum.Replace(t ?? "", "");
        t = NoisePlus.Replace(t, " ");
        t = VerTag.Replace(t.Trim(), "");
        return Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '_', '.', ',', '+');
    }

    public static ParsedName Parse(string name)
    {
        string s = Fix(System.IO.Path.GetFileNameWithoutExtension(name));
        int bracketSeason = SeasonFromBrackets(s);
        bool yearTag = YearInBrackets.IsMatch(s);
        s = Brackets.Replace(s, " ");
        s = Flat(s);
        s = LeadGroup.Replace(s, "");
        bool movie = false;
        var mp = MoviePrefix.Match(s);
        if (mp.Success && mp.Length < s.Length) { movie = true; s = s[mp.Length..]; }
        int season = 1; int? ep = null;
        string title = s;

        var m = SxE.Match(s);
        if (m.Success) { season = int.Parse(m.Groups[1].Value); ep = int.Parse(m.Groups[2].Value); title = s[..m.Index]; }
        else if ((m = NxM.Match(s)).Success) { season = int.Parse(m.Groups[1].Value); ep = int.Parse(m.Groups[2].Value); title = s[..m.Index]; }
        else if ((m = DashEp.Match(s)).Success) { ep = int.Parse(m.Groups[1].Value); title = s[..m.Index]; }
        else if ((m = EpWord.Match(s)).Success) { ep = int.Parse(m.Groups[1].Value); title = s[..m.Index]; }
        else
        {
            string j = Junk.Replace(s, " ").Trim();
            var t = TrailNum.Match(j);
            if (t.Success) { ep = int.Parse(t.Groups[1].Value); title = j[..t.Index]; }
        }

        title = Junk.Replace(title, " ");
        // "Show / Subtitle / Other": the series name is what comes before the first slash
        int slash = title.IndexOf(" / ", StringComparison.Ordinal);
        if (slash >= 4) title = title[..slash];
        bool final = false; int part = 0;
        var fm = FinalSeason.Match(title);
        if (fm.Success) { final = true; season = Math.Max(season, 2); title = title.Remove(fm.Index, fm.Length); }
        var po = SeasonOrd.Match(title);
        if (po.Success) { season = Math.Max(season, OrdinalNumber(po.Groups[1].Value)); title = title.Remove(po.Index, po.Length); }
        var pm = PartWord.Match(title);
        if (pm.Success && title.Remove(pm.Index, pm.Length).Trim(' ', '-', '_', '.', ',').Length >= 2)
        {
            if (int.TryParse(pm.Groups[1].Value, out int pn) && pn >= 2) part = pn;
            title = title.Remove(pm.Index, pm.Length);
        }
        var sa = SeasonA.Match(title);
        if (sa.Success) { season = Math.Max(season, int.Parse(sa.Groups[1].Value)); title = title.Remove(sa.Index, sa.Length); }
        else
        {
            var sb = SeasonB.Match(title);
            if (sb.Success) { season = Math.Max(season, int.Parse(sb.Groups[1].Value)); title = title.Remove(sb.Index, sb.Length); }
            else
            {
                // "Mob Psycho 100 II" -> season 2 (only with at least two words before it)
                var rm = Roman.Match(title.TrimEnd());
                if (rm.Success && title.Trim().Split(' ').Length >= 3)
                {
                    season = Math.Max(season, rm.Groups[1].Value switch { "II" => 2, "III" => 3, "IV" => 4, "V" => 5, _ => 6 });
                    title = title.TrimEnd()[..rm.Index];
                }
            }
        }
        title = Year.Replace(title, " ");
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-', '_', '.', ',');
        title = NoisePlus.Replace(title, " ").Trim(' ', '-', '_', '.', ',', '+');
        title = VerTag.Replace(title, "");
        if (bracketSeason > season) season = bracketSeason;
        // a film: "Movie 2 - ...", "... the Movie", or a year in brackets and no episode number
        bool movieWord = MovieWord.IsMatch(s);
        if (!movie && (movieWord || (ep == null && yearTag))) movie = true;
        if (movie)
        {
            season = 1; final = false; part = 0;
            if (movieWord) ep = null;                       // "Gintama Movie 1": the 1 is the film number, not an episode
            title = Regex.Replace(title, @"\s+(?:the\s+)?(?:movie|film)\s*\d{0,2}$", "", RegexOptions.IgnoreCase);
        }
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-', '_', '.', ',', ':', '~');
        return new ParsedName(title, season, ep, part, final, movie);
    }

    public static string CleanTitle(string name) => Parse(name).Title;

    public static int? EpisodeNumber(string fileName) => Parse(fileName).Episode;

    static bool Weak(string t) => t.Length < 2 || Regex.IsMatch(t, @"^\d") || SkipFolder.IsMatch(t.Trim());

    static int SeasonOfFolder(string leaf)
    {
        var a = Regex.Match(leaf, @"^(?:season|s)\s*(\d{1,2})$", RegexOptions.IgnoreCase);
        if (a.Success) return int.Parse(a.Groups[1].Value);
        var b = SeasonB.Match(leaf);
        if (b.Success) return int.Parse(b.Groups[1].Value);
        var c = SeasonA.Match(leaf);
        return c.Success ? int.Parse(c.Groups[1].Value) : 1;
    }

    /// <summary>Title for a file: from the file name; when that gives nothing usable, from the folder names above it.
    /// OP / ED / music / extra tags are removed first, a "Season 2" folder above the file sets the season.</summary>
    public static ParsedName ForFile(string path)
    {
        string fileName = System.IO.Path.GetFileName(path);
        var cls = Classify(path);
        var p = Parse(cls.Kind == FileKind.Episode ? fileName : StripTags(fileName));
        if (cls.Kind != FileKind.Episode) p = p with { Episode = null };

        string? dir = System.IO.Path.GetDirectoryName(path);
        int folderSeason = 1;
        // up to two levels of folders: "Show/Season 2/ep.mkv", "Show/Season 2/Extras/NCOP.mkv"
        string? d2 = dir;
        for (int i = 0; i < 2 && !string.IsNullOrEmpty(d2); i++)
        {
            string leaf = System.IO.Path.GetFileName(d2.TrimEnd('\\', '/'));
            if (Regex.IsMatch(leaf, @"^(?:season|s)\s*\d{1,2}$", RegexOptions.IgnoreCase)) { folderSeason = SeasonOfFolder(leaf); break; }
            d2 = System.IO.Path.GetDirectoryName(d2);
        }

        if (!Weak(p.Title)) return folderSeason > p.Season && p.Season == 1 ? p with { Season = folderSeason } : p;

        int season = Math.Max(p.Season, folderSeason);
        // a file called "01.mkv" / "Ep 03.mkv" / "12v2.mkv": the number IS the episode, the series comes from the folders
        int? bare = null;
        if (cls.Kind == FileKind.Episode)
        {
            var bm = Regex.Match(System.IO.Path.GetFileNameWithoutExtension(fileName), @"^\s*(?:ep|episode|e)?\s*(\d{1,4})(?:v\d)?(?=[\s_.\-\[(]|$)", RegexOptions.IgnoreCase);
            if (bm.Success) bare = int.Parse(bm.Groups[1].Value);
        }
        while (!string.IsNullOrEmpty(dir))
        {
            string leaf = System.IO.Path.GetFileName(dir.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(leaf)) break;
            var fp = Parse(leaf);
            if (SkipFolder.IsMatch(leaf.Trim()) || fp.Title.Length < 2)
            {
                season = Math.Max(season, SeasonOfFolder(leaf));
                dir = System.IO.Path.GetDirectoryName(dir);
                continue;
            }
            return new ParsedName(fp.Title, Math.Max(season, fp.Season), p.Episode ?? bare ?? fp.Episode);
        }
        return new ParsedName(System.IO.Path.GetFileNameWithoutExtension(path), p.Season, p.Episode ?? bare);
    }

    public static string SearchTitle(ParsedName p) =>
        p.Final ? p.Title + " Final Season" + (p.Part > 1 ? " Part " + p.Part : "")
        : (p.Season > 1 ? p.Title + " Season " + p.Season : p.Title) + (p.Part > 1 ? " Part " + p.Part : "");

    public static string KeyOf(ParsedName p)
    {
        string k = Regex.Replace(p.Title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");
        if (p.Season > 1) k += "#s" + p.Season;
        if (p.Final) k += "#final";
        if (p.Part > 1) k += "#p" + p.Part;
        if (p.IsMovie) k += "#movie";
        return k;
    }

    public static string KeyOf(string title) => Regex.Replace(title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "");

    /// <summary>Lower-case words of a file name without tags / numbers noise - used to find the common series prefix of a folder.</summary>
    public static List<string> Tokens(string fileName, bool hasExtension = true)
    {
        string s = hasExtension ? System.IO.Path.GetFileNameWithoutExtension(fileName) : fileName;
        s = Brackets.Replace(s, " ");
        s = Junk.Replace(s, " ");
        s = Regex.Replace(s.Replace('_', ' ').Replace('.', ' '), @"[^\p{L}\p{N}' ]+", " ");
        return s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToList();
    }
}
