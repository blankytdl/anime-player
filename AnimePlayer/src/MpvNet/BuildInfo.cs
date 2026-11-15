using System;

namespace MpvNet;

/// <summary>The AnimePlayer build number: one number in BUILD.txt (project root; build.bat copies it next to mpvnet.exe).
/// Shown in About, in the exe's file properties and for a moment at start-up, so it is easy to tell which build is running.</summary>
public static class BuildInfo
{
    public static string Number { get; } = Read();

    public static string Text => "AnimePlayer build " + Number;

    static string Read()
    {
        try
        {
            string f = System.IO.Path.Combine(AppContext.BaseDirectory, "BUILD.txt");
            if (System.IO.File.Exists(f))
            {
                string t = System.IO.File.ReadAllText(f).Trim();
                if (t.Length > 0 && t.Length < 12) return t;
            }
        }
        catch { }
        return "?";
    }
}
