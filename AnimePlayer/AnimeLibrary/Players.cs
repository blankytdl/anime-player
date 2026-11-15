using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AnimeLibrary;

/// <summary>External player support: AnimePlayer (mpv.net build), mpv, VLC, MPC-HC, or any exe.</summary>
public static class Players
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string? x, string? y);

    /// <summary>Windows "natural" order: Ep 2 before Ep 10.</summary>
    public static readonly IComparer<string> Natural = Comparer<string>.Create((a, b) => StrCmpLogicalW(a, b));

    static string? OnPath(string exe)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { string p = System.IO.Path.Combine(dir.Trim('"'), exe); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    static string? First(IEnumerable<string> paths) => paths.FirstOrDefault(File.Exists);

    public static string? Find(Settings s)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string P(params string[] parts) => System.IO.Path.Combine(parts);
        switch (s.Player)
        {
            case PlayerKind.Mpv:
                return !string.IsNullOrWhiteSpace(s.PlayerExe) && s.PlayerExe.EndsWith("mpv.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(s.PlayerExe) ? s.PlayerExe
                    : OnPath("mpv.exe") ?? First(new[] { P(pf, "mpv", "mpv.exe"), P(pf86, "mpv", "mpv.exe"), P(local, "Programs", "mpv", "mpv.exe") });
            case PlayerKind.Vlc:
                return First(new[] { P(pf, "VideoLAN", "VLC", "vlc.exe"), P(pf86, "VideoLAN", "VLC", "vlc.exe") }) ?? OnPath("vlc.exe");
            case PlayerKind.MpcHc:
                return First(new[] { P(pf, "MPC-HC", "mpc-hc64.exe"), P(pf86, "MPC-HC", "mpc-hc.exe"), P(pf, "MPC-HC", "mpc-hc.exe"), P(pf, "K-Lite Codec Pack", "MPC-HC64", "mpc-hc64.exe") })
                       ?? OnPath("mpc-hc64.exe") ?? OnPath("mpc-hc.exe");
            default:
                if (string.IsNullOrWhiteSpace(s.PlayerExe)) return null;
                return System.IO.Path.IsPathRooted(s.PlayerExe) ? (File.Exists(s.PlayerExe) ? s.PlayerExe : null)
                    : First(new[] { P(AppContext.BaseDirectory, s.PlayerExe) }) ?? OnPath(s.PlayerExe);
        }
    }

    public static string Describe(Settings s) => s.Player switch
    {
        PlayerKind.Mpv => "mpv", PlayerKind.Vlc => "VLC", PlayerKind.MpcHc => "MPC-HC", PlayerKind.Custom => "custom player", _ => "AnimePlayer"
    };

    /// <summary>Starts the player with the files (first one plays first). Returns an error text or null.</summary>
    /// <summary>Starts the player without a file (the "Open player" entry in the sidebar). Null = started, else the reason it did not.</summary>
    public static string? Open(Settings s)
    {
        string? exe = Find(s);
        if (exe == null) return Describe(s) + " was not found. Choose it in Settings.";
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? "" });
            return null;
        }
        catch (Exception ex) { return "Could not start " + Describe(s) + ": " + ex.Message; }
    }

    public static string? Launch(Settings s, IList<string> files)
    {
        if (files.Count == 0) return "No video files found.";
        string? exe = Find(s);
        if (exe == null) return Describe(s) + " was not found. Choose it in Settings.";
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? "" };
            foreach (string f in files) psi.ArgumentList.Add(f);
            Process.Start(psi);
            return null;
        }
        catch (Exception ex) { return "Could not start " + Describe(s) + ": " + ex.Message; }
    }
}
