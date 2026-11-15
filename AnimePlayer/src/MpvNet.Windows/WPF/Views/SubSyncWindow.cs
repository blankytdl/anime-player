using System.Diagnostics;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using static MpvNet.Global;

namespace MpvNet.Windows.WPF.Views;

/// <summary>Visual subtitle syncer: audio waveform with subtitle blocks drawn on the same timeline.
/// Drag the offset slider until the blocks line up with speech, then it is applied live as sub-delay.</summary>
public class SubSyncWindow : Window
{
    record Cue(double Start, double End, string Text);

    const int PeaksPerSec = 20;
    float[] _peaks = Array.Empty<float>();
    List<Cue> _cues = new();
    readonly Canvas _canvas = new() { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(14, 14, 20)) };
    readonly Slider _offset = new() { Minimum = -15, Maximum = 15, Value = 0, TickFrequency = 1, IsSnapToTickEnabled = false, Width = 360 };
    readonly TextBlock _status = new() { Foreground = Brushes.White, Margin = new Thickness(8, 4, 8, 4) };
    readonly TextBlock _offLabel = new() { Foreground = Brushes.White, Width = 90, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    double WindowSec = 40;
    bool _syncing;

    public SubSyncWindow()
    {
        Title = "Subtitle Sync (visual)";
        Width = 980; Height = 340; MinWidth = 520; MinHeight = 240;
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 28));
        Topmost = true;

        var apply = new Button { Content = "Reset", Margin = new Thickness(6), Padding = new Thickness(10, 3, 10, 3) };
        apply.Click += (_, _) => _offset.Value = 0;
        var reload = new Button { Content = "Reload audio/subs", Margin = new Thickness(6), Padding = new Thickness(10, 3, 10, 3) };
        reload.Click += async (_, _) => await LoadAsync();

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(new TextBlock { Text = "Subtitle offset (s):", Foreground = Brushes.White, Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_offset); bar.Children.Add(_offLabel); bar.Children.Add(apply); bar.Children.Add(reload);

        var dp = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom); DockPanel.SetDock(_status, Dock.Top);
        dp.Children.Add(_status); dp.Children.Add(bar); dp.Children.Add(_canvas);
        Content = dp;

        _offset.ValueChanged += (_, _) =>
        {
            _offLabel.Text = _offset.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " s";
            if (!_syncing) Player.SetPropertyDouble("sub-delay", Math.Round(_offset.Value, 2));
        };
        _timer.Tick += (_, _) => Draw();
        Loaded += async (_, _) => { _offset.Value = Player.GetPropertyDouble("sub-delay", false); await LoadAsync(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _peaks = Array.Empty<float>(); _cues.Clear(); };
        SizeChanged += (_, _) => Draw();
    }

    async Task LoadAsync()
    {
        string path = Player.GetPropertyString("path");
        if (!File.Exists(path)) { _status.Text = "Open a local video first."; return; }
        _status.Text = "Analysing audio (ffmpeg)...";
        string ff = FindFfmpeg();
        string tmpPcm = Path.Combine(Path.GetTempPath(), "ap_sync.pcm");
        string tmpSub = Path.Combine(Path.GetTempPath(), "ap_sync.srt");
        try
        {
            await Task.Run(() =>
            {
                Run(ff, $"-y -v error -i \"{path}\" -vn -ac 1 -ar 8000 -f s16le \"{tmpPcm}\"");
                var bytes = File.ReadAllBytes(tmpPcm);
                int spp = 8000 / PeaksPerSec;
                int n = bytes.Length / 2 / spp;
                var pk = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float m = 0;
                    for (int j = 0; j < spp; j++)
                    {
                        int idx = (i * spp + j) * 2;
                        float v = Math.Abs(BitConverter.ToInt16(bytes, idx) / 32768f);
                        if (v > m) m = v;
                    }
                    pk[i] = m;
                }
                _peaks = pk;
                // current subtitle: external file if present, else extract the selected embedded track
                string ext = Player.GetPropertyString("current-tracks/sub/external-filename");
                if (!string.IsNullOrEmpty(ext) && File.Exists(ext))
                {
                    if (ext.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) File.Copy(ext, tmpSub, true);
                    else Run(ff, $"-y -v error -i \"{ext}\" \"{tmpSub}\"");
                }
                else
                {
                    int sid = Math.Max(0, Player.GetPropertyInt("current-tracks/sub/id") - 1);
                    Run(ff, $"-y -v error -i \"{path}\" -map 0:s:{sid} \"{tmpSub}\"");
                }
                _cues = File.Exists(tmpSub) ? ParseSrt(File.ReadAllText(tmpSub)) : new();
            });
            _status.Text = $"{_cues.Count} subtitle lines. Green = speech energy, blue = subtitle blocks, red = playhead. Align them with the slider.";
        }
        catch (Exception e) { _status.Text = "Could not analyse: " + e.Message + " (put ffmpeg.exe next to the app)"; }
        finally
        {
            try { File.Delete(tmpPcm); } catch { }
            try { File.Delete(tmpSub); } catch { }
        }
    }

    static string FindFfmpeg()
    {
        string b = AppContext.BaseDirectory;
        string direct = Path.Combine(b, "ffmpeg.exe");
        if (File.Exists(direct)) return direct;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(b, "ffmpeg*"))
            {
                var hit = Directory.EnumerateFiles(d, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return hit;
            }
        }
        catch { }
        return "ffmpeg"; // PATH
    }

    static void Run(string exe, string args)
    {
        var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
    }

    static List<Cue> ParseSrt(string s)
    {
        var list = new List<Cue>();
        var rx = new Regex(@"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)\s*\r?\n(.*?)(?:\r?\n\r?\n|\z)", RegexOptions.Singleline);
        foreach (Match m in rx.Matches(s))
        {
            double T(int o) => int.Parse(m.Groups[o].Value) * 3600 + int.Parse(m.Groups[o + 1].Value) * 60 + int.Parse(m.Groups[o + 2].Value) + int.Parse(m.Groups[o + 3].Value.PadRight(3, '0')[..3]) / 1000.0;
            list.Add(new Cue(T(1), T(5), m.Groups[9].Value.Trim()));
        }
        return list;
    }

    void Draw()
    {
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w < 10 || h < 10) return;
        double now = Player.GetPropertyDouble("time-pos", false);

        // sub-delay can also change from the keyboard ([ ] keys) or other scripts: follow it
        double delay = Player.GetPropertyDouble("sub-delay", false);
        if (!_offset.IsMouseCaptureWithin && Math.Abs(delay - _offset.Value) > 0.005)
        {
            _syncing = true; _offset.Value = Math.Clamp(delay, _offset.Minimum, _offset.Maximum); _syncing = false;
        }

        double t0 = now - WindowSec / 2, pxPerSec = w / WindowSec;
        _canvas.Children.Clear();

        // waveform as ONE geometry (cheap on an i5-3470) instead of hundreds of rectangles
        double mid = h * 0.35;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int x = 0; x < (int)w; x += 2)
            {
                int i = (int)((t0 + x / pxPerSec) * PeaksPerSec);
                if (i < 0 || i >= _peaks.Length) continue;
                double bh = Math.Max(0.5, _peaks[i] * mid);
                c.BeginFigure(new Point(x, mid - bh), false, false);
                c.LineTo(new Point(x, mid + bh), true, false);
            }
        }
        g.Freeze();
        _canvas.Children.Add(new System.Windows.Shapes.Path { Data = g, Stroke = new SolidColorBrush(Color.FromRgb(60, 200, 120)), StrokeThickness = 1.5 });

        var blue = new SolidColorBrush(Color.FromArgb(170, 70, 130, 255));
        foreach (var c in _cues)
        {
            double st = c.Start + delay, e = c.End + delay;
            if (e < t0 || st > t0 + WindowSec) continue;
            var r = new Border { Width = Math.Max(2, (e - st) * pxPerSec), Height = h * 0.45, Background = blue, CornerRadius = new CornerRadius(3),
                ToolTip = c.Text, Child = new TextBlock { Text = c.Text, Foreground = Brushes.White, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2) } };
            Canvas.SetLeft(r, (st - t0) * pxPerSec); Canvas.SetTop(r, h * 0.55);
            _canvas.Children.Add(r);
        }
        var head = new System.Windows.Shapes.Rectangle { Width = 2, Height = h, Fill = Brushes.Red };
        Canvas.SetLeft(head, w / 2);
        _canvas.Children.Add(head);
    }
}
