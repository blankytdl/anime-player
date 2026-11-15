using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeLibrary;

public static class CoverTools
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    public static string CacheFile(string key) =>
        System.IO.Path.Combine(Store.Dir, "covers", string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".jpg");

    public static string BannerFile(string key) =>
        System.IO.Path.Combine(Store.Dir, "banners", string.Concat(key.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".jpg");

    /// <summary>Loads a picture from BYTES, never from a file URI: new Uri(path) treats '#' and '%' in a folder name as URI syntax
    /// ("C:\Anime\Show #1\cover.jpg"), which silently produced blank covers.</summary>
    public static BitmapImage? FromBytes(byte[] bytes, int decodeWidth)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static BitmapImage? Load(string file, int decodeWidth)
    {
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < 200) return null;
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }
            return FromBytes(bytes, decodeWidth);
        }
        catch { return null; }
    }

    /// <summary>True when the bytes are a picture WPF can show (rejects HTML error pages, truncated downloads, WebP without codec).</summary>
    public static bool IsImage(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 500) return false;
            using var ms = new MemoryStream(bytes);
            var f = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            return f.PixelWidth >= 40 && f.PixelHeight >= 40;
        }
        catch { return false; }
    }

    public static bool IsValidFile(string? file)
    {
        try { return !string.IsNullOrEmpty(file) && File.Exists(file) && IsImage(File.ReadAllBytes(file)); }
        catch { return false; }
    }

    static readonly string[] FolderCoverNames = { "cover.jpg", "cover.png", "cover.jpeg", "folder.jpg", "folder.png", "poster.jpg", "poster.png", "front.jpg", "thumb.jpg" };

    /// <summary>A picture that belongs to this folder: the one chosen by hand, else cover.jpg / folder.jpg / poster.jpg ... inside it.</summary>
    public static string? FolderCover(Entry e, string folder)
    {
        if (e.FolderCovers.TryGetValue(folder, out var chosen) && File.Exists(chosen)) return chosen;
        try
        {
            foreach (string n in FolderCoverNames)
            {
                string f = System.IO.Path.Combine(folder, n);
                if (File.Exists(f)) return f;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Cover of the first sub-folder (natural order) of a series that has one - used when the series folder itself has no picture.</summary>
    public static string? FirstSubfolderCover(Entry e)
    {
        try
        {
            var dirs = e.Files.Select(f => System.IO.Path.GetDirectoryName(f) ?? "").Where(d => d.Length > 0 && !d.Equals(e.FolderPath, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, Scanner.NaturalComparer.I);
            foreach (string d in dirs) { var c = FolderCover(e, d); if (c != null) return c; }
        }
        catch { }
        return null;
    }

    /// <summary>Gives a sub-folder its own cover (stored in the app cache; also written into the folder when it is writable).</summary>
    public static string? AssignFolder(Entry e, string folder, string source)
    {
        try
        {
            string dir = System.IO.Path.Combine(Store.Dir, "foldercovers");
            Directory.CreateDirectory(dir);
            string cache = System.IO.Path.Combine(dir, Hash(folder) + ".jpg");
            File.Copy(source, cache, true);
            string result = cache;
            try { string dest = System.IO.Path.Combine(folder, "cover.jpg"); if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal); File.Copy(source, dest, true); result = dest; } catch { }
            e.FolderCovers[folder] = result;
            return result;
        }
        catch { return null; }
    }

    public static string Hash(string s)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)))[..20];
    }

    // ------------------------------------------------------------------ ffmpeg (video thumbnails, poster fallback)
    static string? _ffmpeg; static bool _ffmpegChecked;
    public static string? FindFfmpeg()
    {
        if (_ffmpegChecked) return _ffmpeg;
        _ffmpegChecked = true;
        try
        {
            string b = AppContext.BaseDirectory;
            string direct = System.IO.Path.Combine(b, "ffmpeg.exe");
            if (File.Exists(direct)) return _ffmpeg = direct;
            foreach (var d in Directory.EnumerateDirectories(b, "ffmpeg*"))
            {
                var hit = Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return _ffmpeg = hit;
            }
        }
        catch { }
        try   // on PATH?
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            if (p != null) { p.WaitForExit(3000); if (p.HasExited && p.ExitCode == 0) return _ffmpeg = "ffmpeg"; }
        }
        catch { }
        return _ffmpeg = null;
    }

    static async Task<bool> RunFfmpegAsync(IEnumerable<string> args, string outFile, int timeoutMs, CancellationToken ct)
    {
        string? ff = FindFfmpeg();
        if (ff == null) return false;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outFile)!);
            var psi = new System.Diagnostics.ProcessStartInfo(ff) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("-y"); psi.ArgumentList.Add(outFile);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();   // never block on a full pipe
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0 && File.Exists(outFile) && new FileInfo(outFile).Length > 500;
        }
        catch { return false; }
    }

    static readonly string[] SeekTimes = { "00:01:30", "00:00:20", "00:00:03" };

    /// <summary>A portrait (2:3) picture cut from the middle of a video frame: the last-resort series cover.</summary>
    public static async Task<bool> GrabPosterAsync(string video, string outFile, CancellationToken ct = default)
    {
        foreach (string t in SeekTimes)
        {
            if (await RunFfmpegAsync(new[] { "-hide_banner", "-loglevel", "error", "-ss", t, "-i", video, "-frames:v", "1", "-vf", "scale=-2:720,crop=480:720", "-q:v", "3" }, outFile, 20000, ct)) return true;
            ct.ThrowIfCancellationRequested();
        }
        return false;
    }

    public static string ThumbFile(string video, int width)
    {
        long len = 0, tick = 0;
        try { var fi = new FileInfo(video); len = fi.Length; tick = fi.LastWriteTimeUtc.Ticks; } catch { }
        return System.IO.Path.Combine(Store.Dir, "thumbs", Hash(video + "|" + len + "|" + tick + "|" + width) + ".jpg");
    }

    static readonly SemaphoreSlim ThumbGate = new(2);

    /// <summary>Thumbnail of a video (or the embedded picture of an audio file), cached on disk. Null when ffmpeg is missing / fails.</summary>
    public static async Task<string?> ThumbAsync(string video, int width = 360, CancellationToken ct = default)
    {
        string f = ThumbFile(video, width);
        if (File.Exists(f) && new FileInfo(f).Length > 500) return f;
        if (FindFfmpeg() == null || !File.Exists(video)) return null;
        await ThumbGate.WaitAsync(ct);
        try
        {
            if (File.Exists(f) && new FileInfo(f).Length > 500) return f;
            foreach (string t in SeekTimes)
            {
                if (await RunFfmpegAsync(new[] { "-hide_banner", "-loglevel", "error", "-ss", t, "-i", video, "-frames:v", "1", "-vf", $"scale={width}:-2", "-q:v", "4" }, f, 15000, ct)) return f;
                ct.ThrowIfCancellationRequested();
            }
            return null;
        }
        finally { ThumbGate.Release(); }
    }

    /// <summary>Copies a user-chosen picture over the cover of an entry (folder cover.jpg + app cache).</summary>
    public static string? Assign(Entry e, string source)
    {
        try
        {
            string cache = CacheFile(e.Key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);
            File.Copy(source, cache, true);
            string result = cache;
            if (!string.IsNullOrEmpty(e.FolderPath) && Directory.Exists(e.FolderPath))
            {
                try
                {
                    string dest = System.IO.Path.Combine(e.FolderPath, "cover.jpg");
                    if (File.Exists(dest)) File.SetAttributes(dest, FileAttributes.Normal);
                    File.Copy(source, dest, true);
                    result = dest;
                }
                catch { /* read-only folder: the cache copy is used */ }
            }
            e.CoverFile = result;
            e.IconSet = false;
            return result;
        }
        catch { return null; }
    }

    /// <summary>Makes the cover the folder's Explorer icon (folder.ico + desktop.ini). Must run on the UI thread.</summary>
    public static bool SetFolderIcon(string folder, string cover)
    {
        try
        {
            if (!Directory.Exists(folder) || !File.Exists(cover)) return false;
            var bi = Load(cover, 0);
            if (bi == null) return false;

            // centre the poster on a transparent 256x256 square
            double scale = 256.0 / Math.Max(bi.PixelWidth, bi.PixelHeight);
            double w = bi.PixelWidth * scale, h = bi.PixelHeight * scale;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawImage(bi, new Rect((256 - w) / 2, (256 - h) / 2, w, h));
            var rtb = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            byte[] png;
            using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }

            string ico = System.IO.Path.Combine(folder, "folder.ico");
            string ini = System.IO.Path.Combine(folder, "desktop.ini");
            foreach (string f in new[] { ico, ini })
                if (File.Exists(f)) File.SetAttributes(f, FileAttributes.Normal);

            using (var fs = new FileStream(ico, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);   // ICONDIR: 1 image
                bw.Write((byte)0); bw.Write((byte)0);                          // 256 x 256
                bw.Write((byte)0); bw.Write((byte)0);                          // palette, reserved
                bw.Write((short)1); bw.Write((short)32);                       // planes, bpp
                bw.Write(png.Length); bw.Write(22);                            // size, offset
                bw.Write(png);
            }
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=folder.ico,0\r\n", new UTF8Encoding(false));
            File.SetAttributes(ico, FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var di = new DirectoryInfo(folder);
            di.Attributes |= FileAttributes.ReadOnly;                           // Explorer only reads desktop.ini of read-only folders
            SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0 /*SHCNF_IDLIST*/, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch { return false; }
    }
}
