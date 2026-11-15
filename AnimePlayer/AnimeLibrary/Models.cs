using System.IO;
using System.Text.Json;

namespace AnimeLibrary;

/// <summary>Where titles of a library folder are matched. Extension = a declarative provider from the marketplace.</summary>
public enum Source { AniList, MyAnimeList, Extension }
public enum Kind { Anime, Manga }
public enum PlayerKind { AnimePlayer, Mpv, Vlc, MpcHc, Custom }
/// <summary>What a file is. Only Episode files count as episodes (progress, totals, "next episode").</summary>
public enum FileKind { Episode, Opening, Ending, Music, Extra }
public enum SortMode { TitleAZ, TitleZA, RecentlyWatched, RecentlyAdded, Progress, EpisodeCount }
public enum LibView { Posters, List, Compact }
public enum DetailView { Tiles, List, Compact }

public class LibFolder
{
    public string Path { get; set; } = "";
    public Source Source { get; set; } = Source.AniList;
    public string? ExtensionId { get; set; }
}

/// <summary>"Smart name": files whose title key matches <see cref="Match"/> are filed under <see cref="Name"/> (and <see cref="TargetKey"/>).
/// Fixes name mismatches / merges variants into one series, and can keep a series OFFLINE (no cover / tracker look-ups at all).</summary>
public class NameRule
{
    public string Match { get; set; } = "";          // normalised title key; a trailing * means "starts with"
    public string Name { get; set; } = "";           // the series name to use
    public string TargetKey { get; set; } = "";      // key of the series the files are filed under
    public bool LocalOnly { get; set; }              // never look this series up online
}

public class Entry
{
    public string Key { get; set; } = "";              // normalised title (+season): identity inside the library
    public List<string> Aliases { get; set; } = new(); // other keys that were merged into this series
    public string FolderPath { get; set; } = "";       // folder where most of the files live
    public string RawName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SmartName { get; set; }             // set by a name rule
    public bool LocalOnly { get; set; }                // never looked up online
    public string? SearchName { get; set; }            // what to search for online when the file names are misleading (survives scans)
    public string? CoverFile { get; set; }
    public string? CoverUrl { get; set; }
    public string? BannerFile { get; set; }
    public string? BannerUrl { get; set; }
    public bool IconSet { get; set; }                  // Explorer folder icon already written
    public bool SharedFolder { get; set; }             // several titles live in this folder: no folder cover / icon, app cache only
    public int? MalId { get; set; }
    public int? AniListId { get; set; }
    public string? ExternalId { get; set; }
    public int Episodes { get; set; }                  // episode files found on disk (OP / ED / music / extras are NOT counted)
    public int Extras { get; set; }                    // OP / ED / music / extra files found on disk
    public int TotalEpisodes { get; set; }             // episode count of the series (provider), 0 = unknown
    public int Watched { get; set; }                   // highest finished episode number
    public string? Airing { get; set; }               // provider status: FINISHED | RELEASING | NOT_YET_RELEASED | CANCELLED | HIATUS
    public bool Matched { get; set; }
    public bool IsMovie { get; set; }                  // a film (found by its name: "Movie 2 - ...", "... the Movie", year in brackets): matched as a MOVIE, shown with a Movie tag
    public bool IsSpecial { get; set; }                // only specials / OVAs on disk (no regular episodes): matched against specials / OVAs
    public int Score { get; set; }                     // your rating 0-10 (0 = none); uploaded to MyAnimeList
    public bool ScoreDirty { get; set; }               // score changed here, not uploaded yet
    public string? ForceStatus { get; set; }           // "completed" etc.: upload this MAL list status next time
    public bool LowerOk { get; set; }                  // progress was lowered on purpose: MAL may be set back (normally it never goes down)
    public bool NeedsSync { get; set; }                // progress not uploaded to the tracker yet
    public bool Custom { get; set; }                   // added by hand (non-AniList anime / manga): never auto-matched
    public bool Detected { get; set; }                 // created from the player's watch log (file outside the library folders)
    public Kind Kind { get; set; }
    public Source Source { get; set; }
    public string? ExtensionId { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? LastFile { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
    public List<string> Files { get; set; } = new();   // every media file of the series (all folders), natural order
    public Dictionary<string, string> FolderCovers { get; set; } = new(StringComparer.OrdinalIgnoreCase); // folder -> cover picked by hand
    public string? MatchNote { get; set; }             // why the last look-up failed (shown in the Unmatched list)
    public int Ovas { get; set; }                      // OVA / OAD / special files found on disk (counted into the total shown)
    public int MaxEp { get; set; }                     // highest episode number among the episode files (numbered OVAs beyond the provider total)
    public bool ProviderMovie { get; set; }            // the provider says this is a MOVIE (survives scans)
    public string? Group { get; set; }                 // user-made folder: series with the same group name show as one folder card
    public List<string> WatchedFiles { get; set; } = new();   // files finished in the player (also the ones without an episode number)
    public int MatchTries { get; set; }                // failed look-ups so far (backs off, see Scanner)
    public DateTime? LastTry { get; set; }

    /// <summary>Episode total to show: the provider's count plus OVAs / numbered specials that are on disk in the same series folder.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ShownTotal => TotalEpisodes > 0 ? Math.Max(TotalEpisodes + Ovas, Math.Min(MaxEp, TotalEpisodes + 6)) : 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(SmartName) ? Title : SmartName!;
}

public class Settings
{
    public List<LibFolder> Folders { get; set; } = new();
    public List<NameRule> NameRules { get; set; } = new();
    public string MalClientId { get; set; } = "";
    public string MalClientSecret { get; set; } = "";      // only for MAL apps of type "web"
    public string MalRedirect { get; set; } = "";           // empty = http://localhost:8765/cb
    public bool MalNoRedirect { get; set; }                  // do not send redirect_uri at all: MyAnimeList uses the one registered for the app
    public string? MalAccess { get; set; }
    public string? MalRefresh { get; set; }
    public DateTime? MalExpires { get; set; }               // UTC time the access token stops working (MAL: 1 hour); refreshed a little before
    public string? MalUser { get; set; }                    // verified MyAnimeList user name of the stored login
    public PlayerKind Player { get; set; } = PlayerKind.AnimePlayer;
    public string PlayerExe { get; set; } = "mpvnet.exe";   // AnimePlayer exe (name next to this app or full path) / custom player
    public string Theme { get; set; } = "Seanime";
    public string Accent { get; set; } = "#6152DF";
    public string BackgroundImage { get; set; } = "";
    public int BackgroundDim { get; set; } = 70;            // 0-100 %
    public int BackgroundBlur { get; set; } = 0;            // 0-40 px
    public string BackgroundFit { get; set; } = "Fill";     // Fill | Fit | Stretch
    public bool AutoSyncOnline { get; set; } = true;
    public bool DownloadCovers { get; set; } = true;
    public bool SetFolderIcon { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool ShowBadges { get; set; } = true;
    public bool ShowTitles { get; set; } = true;
    public bool ShowHero { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool GroupByStatus { get; set; } = true;          // kept for old settings files; the library now uses status tabs
    public int StatusTab { get; set; }                       // 0 Watching, 1 Plan to Watch, 2 Completed, 3 All
    public bool SidebarHidden { get; set; }
    public string Surface { get; set; } = "Dark";           // Dark | AMOLED | Midnight | Slate | Warm (applied at start)
    public bool ShowIndicators { get; set; } = true;        // Completed / Airing / Ended / Movie / Watched chips under the posters
    public int CardRadius { get; set; } = 8;
    public int TileSize { get; set; } = 180;
    public int BorderSize { get; set; } = 1;                // custom window border, 0 = none
    public string BorderColor { get; set; } = "";           // empty = accent
    public SortMode Sort { get; set; } = SortMode.TitleAZ;
    public LibView View { get; set; } = LibView.Posters;
    public DetailView DetailMode { get; set; } = DetailView.Tiles;
    public string MarketplaceUrl { get; set; } = "";
    public List<string> DisabledExtensions { get; set; } = new();
    public string WatchLog { get; set; } = "";
    public long WatchLogOffset { get; set; }
}

public static class Store
{
    public static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnimeLibrary");
    public static string F(string n) => System.IO.Path.Combine(Dir, n);
    static readonly JsonSerializerOptions J = new() { WriteIndented = true };
    static readonly object Gate = new();

    public static T Load<T>(string name, T fallback)
    {
        foreach (string p in new[] { F(name), F(name) + ".bak" })
        {
            try
            {
                if (!File.Exists(p)) continue;
                var v = JsonSerializer.Deserialize<T>(File.ReadAllText(p));
                if (v != null) return v;
            }
            catch { /* damaged file: try the backup */ }
        }
        return fallback;
    }

    /// <summary>Atomic: write a temp file, keep the previous version as .bak, then swap.</summary>
    public static void Save<T>(string name, T value)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Dir);
            string path = F(name), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, J));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }
    }
}
