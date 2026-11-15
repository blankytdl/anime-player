using System.Text.Json;
using System.Windows.Threading;

using Microsoft.Win32;

namespace MpvNet.Windows.Anime;

/// <summary>Startup/shutdown glue for AnimePlayer.
/// Resume positions, the queue, speed persistence, auto-start and auto-stretch now live in Lua
/// (portable_config/scripts/ap-*.lua) so they work the same with or without the WPF windows and are written
/// the moment something changes - the C# side only handles cache cleanup and the settings file.</summary>
public static class AnimeCore
{
    public static Dispatcher? Ui;
    static bool? _animeBuild;
    static string? _origBorder, _origTitleBar;   // what the config had before AnimePlayer's window option changed it

    public static void Init()
    {
        Ui = Dispatcher.CurrentDispatcher;
        AnimeOptions.Load();
        ApplyRuntimeOptions();
        System.Threading.Tasks.Task.Run(() => CacheCleaner.Clean(false)); // leftovers from a crash / power-off

        // PC shutdown / log-off, normal exit and unhandled crash all clean up
        SystemEvents.SessionEnding += (_, _) => Flush();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Flush();
    }

    /// <summary>Settings that must win over mpv.conf and over mpv.net's own defaults, so they are applied after the player started.</summary>
    /// <summary>Screenshots go to the Windows Pictures folder. The anime build's template ("~~/screenshots/%F (%P) %n") contains a folder,
    /// and a folder inside the template beats screenshot-directory, so only the file-name part of the template is kept.</summary>
    public static void ApplyScreenshotFolder()
    {
        try
        {
            string dir = AnimeOptions.ScreenshotFolder;
            Directory.CreateDirectory(dir);
            Player.SetPropertyString("screenshot-directory", dir.Replace('\\', '/'));
            string tpl = Player.GetPropertyString("screenshot-template");
            int cut = Math.Max(tpl.LastIndexOf('/'), tpl.LastIndexOf('\\'));
            if (cut >= 0) Player.SetPropertyString("screenshot-template", tpl[(cut + 1)..]);
        }
        catch { }
    }

    public static void ApplyRuntimeOptions()
    {
        try
        {
            // 1) free window resizing: mpv.net's WM_SIZING handler forces the video's shape while keepaspect-window is on
            Player.KeepaspectWindow = !AnimeOptions.FreeResize;
            Player.SetPropertyString("keepaspect-window", AnimeOptions.FreeResize ? "no" : "yes");
        }
        catch { }

        try
        {
            // 2) screenshots go to the Windows Pictures folder (C:\Users\<you>\Pictures, also when it is moved / on OneDrive)
            ApplyScreenshotFolder();
        }
        catch { }
    }

    /// <summary>True when the mpv-anime-build config is installed (its mpv.conf has the Platform-*-Base profiles).</summary>
    public static bool AnimeBuildPresent
    {
        get
        {
            if (_animeBuild is bool b) return b;
            try { _animeBuild = File.ReadAllText(ConfigFile("mpv.conf")).Contains("[Platform-Windows-Base]"); }
            catch { _animeBuild = false; }
            return _animeBuild.Value;
        }
    }

    public static void Flush()
    {
        try { if (AnimeOptions.ClearCacheOnExit) CacheCleaner.Clean(AnimeOptions.ClearShaderCacheOnExit); } catch { }
    }

    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    /// <summary>Handle of the real player window (works with no-border / fullscreen).</summary>
    public static IntPtr MainHandle() => MpvNet.Windows.WinForms.MainForm.Instance?.Handle ?? IntPtr.Zero;

    public static string ConfigFile(string name) => System.IO.Path.Combine(Player.ConfigFolder, name);
}

public static class AnimeOptions
{
    public static bool ClearCacheOnExit { get; set; } = true;
    public static bool ClearShaderCacheOnExit { get; set; } = false;
    /// <summary>true = the window can be dragged to any shape (no forced video aspect ratio).</summary>
    public static bool FreeResize { get; set; } = true;
    /// <summary>true = real window frame without title bar: Windows snap (drag to an edge, Win+Arrows) and the shadow work.</summary>
    public static bool WindowsSnap { get; set; } = true;    // drag the window to a screen edge: left / right half, corners = quarters (see WindowSnap.cs)
    /// <summary>Empty = the Windows Pictures folder.</summary>
    public static string ScreenshotDir { get; set; } = "";

    public static string ScreenshotFolder =>
        string.IsNullOrWhiteSpace(ScreenshotDir) ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : ScreenshotDir;

    class Data
    {
        public bool ClearCacheOnExit { get; set; } = true;
        public bool ClearShaderCacheOnExit { get; set; }
        public bool FreeResize { get; set; } = true;
        public bool SnapEdges { get; set; } = true;
        public string ScreenshotDir { get; set; } = "";
    }

    public static void Load()
    {
        try
        {
            string f = AnimeCore.ConfigFile("anime-settings.json");
            if (!File.Exists(f)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(f)); // unknown old fields (ShowQuickMenu) are ignored
            if (d == null) return;
            ClearCacheOnExit = d.ClearCacheOnExit; ClearShaderCacheOnExit = d.ClearShaderCacheOnExit;
            FreeResize = d.FreeResize; WindowsSnap = d.SnapEdges; ScreenshotDir = d.ScreenshotDir ?? "";
        }
        catch { }
    }

    public static void Save()
    {
        try { AnimeCore.WriteAtomic(AnimeCore.ConfigFile("anime-settings.json"), JsonSerializer.Serialize(new Data { ClearCacheOnExit = ClearCacheOnExit, ClearShaderCacheOnExit = ClearShaderCacheOnExit, FreeResize = FreeResize, SnapEdges = WindowsSnap, ScreenshotDir = ScreenshotDir })); } catch { }
    }
}

/// <summary>Deletes everything the app can leave behind: ffmpeg temp audio/subtitle files from the subtitle syncer,
/// half-written *.tmp files from a crash and (optionally) mpv's GPU shader cache.
/// Runs at startup, on exit/shutdown and from Settings -> Clear cache now.</summary>
public static class CacheCleaner
{
    public static int Clean(bool includeShaderCache)
    {
        int n = 0;
        try { foreach (var f in Directory.EnumerateFiles(System.IO.Path.GetTempPath(), "ap_sync*")) n += Del(f); } catch { }
        try { foreach (var f in Directory.EnumerateFiles(Player.ConfigFolder, "*.tmp")) n += Del(f); } catch { }
        if (includeShaderCache)
        {
            try
            {
                string dir = AnimeCore.ConfigFile("cache");
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) n += Del(f);
                    foreach (var d in Directory.EnumerateDirectories(dir)) { try { Directory.Delete(d, true); } catch { } }
                }
            }
            catch { }
        }
        return n;
    }

    static int Del(string f) { try { File.Delete(f); return 1; } catch { return 0; } }
}
