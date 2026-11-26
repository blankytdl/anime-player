using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using MpvNet.Windows.Anime;
using static MpvNet.Global;

namespace MpvNet.Windows.WPF.Views;

/// <summary>AnimePlayer settings. Works with both configs: with mpv-anime-build installed the GPU/decoder settings patch its
/// [Platform-Windows-Base] profile (its auto-profiles would override overrides.conf) and the upscaler box drives the build's own
/// anime-mode bindings; without it, overrides.conf and my built-in profiles are used.</summary>
public class AnimeSettingsWindow : Window
{
    readonly bool _anime = AnimeCore.AnimeBuildPresent;

    public AnimeSettingsWindow()
    {
        Title = "AnimePlayer Settings"; Width = 500; Height = 640; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 28));
        var sp = new StackPanel { Margin = new Thickness(16) };

        sp.Children.Add(new TextBlock { Text = _anime ? "Config: mpv-anime-build detected" : "Config: built-in basic profiles", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 8) });

        // ---- middle-click jump (read by scripts/midclick-jump.lua at startup) ----
        double jumpSec = 5;
        try
        {
            string f = System.IO.Path.Combine(Player.ConfigFolder, "script-opts", "midclick-jump.conf");
            if (File.Exists(f))
            {
                var m = Regex.Match(File.ReadAllText(f), @"seconds\s*=\s*([\d.]+)");
                if (m.Success) jumpSec = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }
        catch { }
        bool idleDbl = true;
        try
        {
            string f2 = System.IO.Path.Combine(Player.ConfigFolder, "script-opts", "ap-resume.conf");
            if (File.Exists(f2))
            {
                var m2 = Regex.Match(File.ReadAllText(f2), @"idle_dblclick\s*=\s*(\w+)", RegexOptions.IgnoreCase);
                if (m2.Success && (m2.Groups[1].Value.Equals("no", StringComparison.OrdinalIgnoreCase) || m2.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase))) idleDbl = false;
            }
        }
        catch { }
        var dblIdle = new CheckBox { Content = "Double-click on an empty player (no video) plays the last video", Foreground = Brushes.White, IsChecked = idleDbl, Margin = new Thickness(0, 8, 0, 2) };
        sp.Children.Add(dblIdle);

        var ff = (Slider)Row(sp, "Middle-click jump forward (seconds)", new Slider { Minimum = 1, Maximum = 300, Value = Math.Clamp(jumpSec, 1, 300), Width = 200, IsSnapToTickEnabled = true, TickFrequency = 1 });

        // ---- GPU / decoder ----
        var gpu = new ComboBox { Width = 220 };
        foreach (var g in new[] { "d3d11 (safe on RX 580)", "vulkan", "opengl" }) gpu.Items.Add(g);
        gpu.SelectedIndex = _anime ? 1 : 0; Row(sp, "GPU API (restart needed)", gpu);
        var hw = new ComboBox { Width = 220 };
        foreach (var g in new[] { "d3d11va-copy", "d3d11va", "auto-safe", "no (software)" }) hw.Items.Add(g);
        hw.SelectedIndex = 0; Row(sp, "Hardware decoding", hw);

        // ---- upscaler: applies immediately ----
        var up = new ComboBox { Width = 220 };
        foreach (var g in _anime
                     ? new[] { "Auto (anime / live detect)", "Force anime shaders", "Force live-action shaders", "Shaders off" }
                     : new[] { "Auto (anime / live detect)", "Anime HD pipeline", "Live-action pipeline", "Lite (low load)" })
            up.Items.Add(g);
        up.SelectedIndex = 0;
        bool ready = false;
        up.SelectionChanged += (_, _) =>
        {
            if (!ready) return;
            string[] cmds = _anime
                ? new[] { "script-binding anime-mode-auto", "script-binding anime-mode-on", "script-binding anime-mode-off", "no-osd change-list glsl-shaders clr \"\"" }
                : new[] { "script-message reapply", "apply-profile anime-hd", "apply-profile live-hd", "apply-profile lite" };
            Player.Command(cmds[up.SelectedIndex]);
        };
        Row(sp, "Real-time upscaler (applies now)", up);
        ready = true;

        // ---- behaviour ----
        var idle = new CheckBox { Content = "Stay open after the last video (keeps Resume available)", Foreground = Brushes.White, IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
        try { idle.IsChecked = !File.ReadAllText(AnimeCore.ConfigFile("overrides.conf")).Contains("idle=no"); } catch { }
        sp.Children.Add(idle);

        var pin = new CheckBox { Content = "Pin to top (always on top)", Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 2) };
        pin.IsChecked = Player.GetPropertyString("ontop") == "yes";
        pin.Click += (_, _) => Player.SetPropertyBool("ontop", pin.IsChecked == true);
        sp.Children.Add(pin);

        // ---- window + screenshots ----
        var free = new CheckBox { Content = "Free window resizing (any shape, no forced video aspect ratio)", Foreground = Brushes.White, IsChecked = AnimeOptions.FreeResize, Margin = new Thickness(0, 10, 0, 2) };
        sp.Children.Add(free);
        var snap = new CheckBox { Content = "Snap the window to the screen edges: drag it to the left / right edge for a half screen, to a corner for a quarter (drag it away to get the old size back)", Foreground = Brushes.White, IsChecked = AnimeOptions.WindowsSnap, Margin = new Thickness(0, 4, 0, 2) };
        sp.Children.Add(snap);
        var shots = new TextBox { Text = AnimeOptions.ScreenshotDir, Width = 230 };
        var shotsPick = new Button { Content = "...", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
        shotsPick.Click += (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = "Screenshot folder" };
            if (d.ShowDialog() == true) shots.Text = d.FolderName;
        };
        var shotsRow = new StackPanel { Orientation = Orientation.Horizontal };
        shotsRow.Children.Add(shots); shotsRow.Children.Add(shotsPick);
        Row(sp, "Screenshot folder (empty = Pictures)", shotsRow);

        // ---- cache ----
        var clrExit = new CheckBox { Content = "Delete cache/temp files on exit and at startup", Foreground = Brushes.White, IsChecked = AnimeOptions.ClearCacheOnExit, Margin = new Thickness(0, 10, 0, 2) };
        var clrShader = new CheckBox { Content = "Also delete GPU shader cache (slower first start)", Foreground = Brushes.White, IsChecked = AnimeOptions.ClearShaderCacheOnExit, Margin = new Thickness(0, 4, 0, 2) };
        var clrNow = new Button { Content = "Clear cache now", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4) };
        clrNow.Click += (_, _) => { int n = CacheCleaner.Clean(clrShader.IsChecked == true); MessageBox.Show($"Cache cleared ({n} items removed)."); };
        sp.Children.Add(clrExit); sp.Children.Add(clrShader); sp.Children.Add(clrNow);

        var save = new Button { Content = "Save", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            try
            {
                string cf = Player.ConfigFolder;
                Directory.CreateDirectory(System.IO.Path.Combine(cf, "script-opts"));
                // [AP25] keep the other options of ap-resume.conf (auto_resume, hint, interval ...): only idle_dblclick is changed
                {
                    string rcf = System.IO.Path.Combine(cf, "script-opts", "ap-resume.conf");
                    var rl = File.Exists(rcf) ? File.ReadAllLines(rcf).ToList() : new List<string>();
                    string want = "idle_dblclick=" + (dblIdle.IsChecked == true ? "yes" : "no");
                    int at = rl.FindIndex(l => Regex.IsMatch(l, @"^\s*idle_dblclick\s*="));
                    if (at >= 0) rl[at] = want; else rl.Add(want);
                    File.WriteAllLines(rcf, rl);
                }
                File.WriteAllText(System.IO.Path.Combine(cf, "script-opts", "midclick-jump.conf"), "seconds=" + ff.Value.ToString("0", CultureInfo.InvariantCulture) + "\n");

                string api = gpu.SelectedIndex == 1 ? "vulkan" : gpu.SelectedIndex == 2 ? "opengl" : "d3d11";
                string hwv = hw.SelectedIndex == 3 ? "no" : hw.SelectedItem!.ToString()!;

                var ov = new List<string> { "# written by AnimePlayer Settings", "idle=" + (idle.IsChecked == true ? "yes" : "no"), "hwdec=" + hwv, "vd-lavc-threads=4" };
                if (_anime) PatchPlatformProfile(api, hwv);   // the build's auto-profiles would override overrides.conf
                else ov.Add("gpu-api=" + api);
                File.WriteAllLines(System.IO.Path.Combine(cf, "overrides.conf"), ov);
                Player.SetPropertyString("hwdec", hwv);

                AnimeOptions.FreeResize = free.IsChecked == true;
                AnimeOptions.WindowsSnap = snap.IsChecked == true;
                AnimeOptions.ScreenshotDir = shots.Text.Trim();
                AnimeCore.ApplyRuntimeOptions();

                AnimeOptions.ClearCacheOnExit = clrExit.IsChecked == true;
                AnimeOptions.ClearShaderCacheOnExit = clrShader.IsChecked == true;
                AnimeOptions.Save();
                MessageBox.Show("Saved. Window resizing, screenshot folder and decoder apply now; GPU API, idle and the middle-click jump apply after a restart.");
            }
            catch (Exception ex) { MessageBox.Show("Could not save: " + ex.Message); }
        };
        sp.Children.Add(save);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    /// <summary>Rewrites gpu-api / gpu-context / hwdec inside the build's [Platform-Windows-Base] profile (backup: mpv.conf.bak).</summary>
    static void PatchPlatformProfile(string api, string hwdec)
    {
        string f = AnimeCore.ConfigFile("mpv.conf");
        string text = File.ReadAllText(f);
        int a = text.IndexOf("[Platform-Windows-Base]", StringComparison.Ordinal);
        if (a < 0) return;
        int b = text.IndexOf("\n[", a + 5, StringComparison.Ordinal);
        if (b < 0) b = text.Length;
        string block = text[a..b];
        (string gApi, string gCtx) = api switch
        {
            "d3d11" => ("d3d11", "d3d11"),
            "opengl" => ("opengl", "auto"),
            _ => ("vulkan,auto", "winvk,auto")
        };
        block = Regex.Replace(block, @"(?m)^gpu-api=.*$", "gpu-api=" + gApi);
        block = Regex.Replace(block, @"(?m)^gpu-context=.*$", "gpu-context=" + gCtx);
        block = Regex.Replace(block, @"(?m)^hwdec=.*$", "hwdec=" + hwdec);
        string bak = f + ".bak";
        if (!File.Exists(bak)) File.Copy(f, bak);
        AnimeCore.WriteAtomic(f, text[..a] + block + text[b..]);
    }

    static FrameworkElement Row(Panel parent, string label, FrameworkElement ctl)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        dp.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, Width = 240, VerticalAlignment = VerticalAlignment.Center });
        dp.Children.Add(ctl); parent.Children.Add(dp);
        return ctl;
    }
}
