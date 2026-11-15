using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnimeLibrary;

/// <summary>The page of one series: header, its folders (each with a cover, any depth) and the files inside with thumbnails.
/// Episodes, openings / endings, music and extras are listed separately; three view modes.</summary>
public partial class MainWindow
{
    sealed class MediaFile
    {
        public string Path = ""; public string Dir = ""; public FileClass Cls = null!; public int? Ep; public long Size; public bool Exists;
        public string Name => System.IO.Path.GetFileName(Path);
    }

    Entry? _detail;
    List<MediaFile> _media = new();
    string _detailRoot = "", _detailDir = "";
    int _kindFilter;            // 0 all, 1 episodes, 2 openings + endings, 3 music, 4 extras
    bool _includeSub;
    int _detailToken;
    CancellationTokenSource? _thumbCts;

    // ------------------------------------------------------------------ navigation
    List<MediaFile> BuildMedia(Entry e)
    {
        var list = new List<MediaFile>();
        foreach (string f in e.Files)
        {
            var cls = NameParser.Classify(f);
            var m = new MediaFile { Path = f, Dir = System.IO.Path.GetDirectoryName(f) ?? "", Cls = cls, Ep = cls.Kind == FileKind.Episode ? NameParser.ForFile(f).Episode : cls.Number };
            try { var fi = new FileInfo(f); m.Exists = fi.Exists; m.Size = m.Exists ? fi.Length : 0; } catch { }
            list.Add(m);
        }
        return list;
    }

    double _libScroll;      // scroll position of the library list, restored when you come back from a series page

    void OpenDetail(Entry e)
    {
        if (_detail == null) _libScroll = _scroll.VerticalOffset;
        _detail = e;
        _media = BuildMedia(e);
        _detailRoot = _media.Count == 0 ? (e.FolderPath ?? "") : Scanner.CommonRoot(_media.Select(m => m.Dir).Distinct(StringComparer.OrdinalIgnoreCase));
        _detailDir = _detailRoot;
        _kindFilter = 0; _includeSub = false;
        _animate = true;
        _scroll.ScrollToTop();
        Render();
    }

    void CloseDetail()
    {
        _detail = null; _thumbCts?.Cancel();
        _animate = true;
        Render();
        double back = _libScroll;
        Dispatcher.BeginInvoke(new Action(() => { _scroll.UpdateLayout(); _scroll.ScrollToVerticalOffset(back); }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void GoDir(string dir)
    {
        _detailDir = dir; _kindFilter = 0; _animate = true;
        _scroll.ScrollToTop();
        Render();
    }

    void DetailUp()
    {
        if (_detail == null) return;
        if (string.Equals(_detailDir, _detailRoot, StringComparison.OrdinalIgnoreCase)) { CloseDetail(); return; }
        string? parent = System.IO.Path.GetDirectoryName(_detailDir.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent) && (string.Equals(parent, _detailRoot, StringComparison.OrdinalIgnoreCase) || Scanner.IsUnder(parent, _detailRoot))) GoDir(parent);
        else GoDir(_detailRoot);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_detail != null && !e.Handled && e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox) { DetailUp(); e.Handled = true; }
        else if (_detail == null && _collection != null && !e.Handled && e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox) { _collection = null; _animate = true; Render(); e.Handled = true; }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (_detail != null && e.ChangedButton == MouseButton.XButton1) { DetailUp(); e.Handled = true; }
        else if (_detail == null && _collection != null && e.ChangedButton == MouseButton.XButton1) { _collection = null; _animate = true; Render(); e.Handled = true; }
    }

    // ------------------------------------------------------------------ data helpers
    static string FmtSize(long bytes) => bytes <= 0 ? "" : bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0") + " GB" : (bytes / (double)(1L << 20)).ToString("0") + " MB";

    static string ShortBadge(MediaFile m) => m.Cls.Kind switch
    {
        FileKind.Episode => m.Ep != null ? "EP " + m.Ep : "EP",
        FileKind.Opening => "OP" + (m.Cls.Number != null ? " " + m.Cls.Number : ""),
        FileKind.Ending => "ED" + (m.Cls.Number != null ? " " + m.Cls.Number : ""),
        FileKind.Music => "MUSIC",
        _ => m.Cls.Label.ToUpperInvariant()
    };

    static string Heading(MediaFile m) => m.Cls.Kind switch
    {
        FileKind.Episode => m.Ep != null ? "Episode " + m.Ep : "Episode",
        _ => m.Cls.Label
    };

    string FolderLabel(string child, string cur)
    {
        string leaf = System.IO.Path.GetFileName(child.TrimEnd('\\', '/'));
        if (cur.Length > 0) return leaf.Length > 0 ? leaf : child;
        var segs = child.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segs.Length <= 2 ? child : "…\\" + string.Join("\\", segs[^2..]);
    }

    List<(string Path, string Name, int Count)> ChildFolders(string cur)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _media)
        {
            string? child = null;
            if (cur.Length == 0) child = m.Dir;                                    // several unrelated places: every one is a folder
            else if (!m.Dir.Equals(cur, StringComparison.OrdinalIgnoreCase) && Scanner.IsUnder(m.Dir, cur))
            {
                string rel = m.Dir[cur.TrimEnd('\\', '/').Length..].TrimStart('\\', '/');
                string first = rel.Split(new[] { '\\', '/' }, 2)[0];
                if (first.Length > 0) child = System.IO.Path.Combine(cur, first);
            }
            if (child == null) continue;
            dict[child] = dict.TryGetValue(child, out int n) ? n + 1 : 1;
        }
        return dict.OrderBy(kv => kv.Key, Scanner.NaturalComparer.I).Select(kv => (kv.Key, FolderLabel(kv.Key, cur), kv.Value)).ToList();
    }

    List<MediaFile> FilesInView(out List<MediaFile> unfiltered)
    {
        IEnumerable<MediaFile> q = _includeSub
            ? _media.Where(m => _detailDir.Length == 0 || Scanner.IsUnder(m.Dir, _detailDir))
            : _media.Where(m => m.Dir.Equals(_detailDir, StringComparison.OrdinalIgnoreCase));
        unfiltered = q.ToList();
        return unfiltered.Where(m => _kindFilter switch
        {
            1 => m.Cls.Kind == FileKind.Episode,
            2 => m.Cls.Kind is FileKind.Opening or FileKind.Ending,
            3 => m.Cls.Kind == FileKind.Music,
            4 => m.Cls.Kind == FileKind.Extra,
            _ => true
        }).ToList();
    }

    // ------------------------------------------------------------------ the page
    void RenderDetail()
    {
        var e = _detail!;
        _anim = _animate && _s.Animations; _animate = false; _cardIndex = 0; _shown = 1;
        int token = ++_detailToken;
        _thumbCts?.Cancel(); _thumbCts = new CancellationTokenSource();
        foreach (var kv in _railItem) kv.Value.Background = Brushes.Transparent;
        SetChrome(false, false, "Series");
        UpdateTabs();
        _page.Children.Clear();

        _page.Children.Add(BuildCrumbs(e));
        bool atRoot = string.Equals(_detailDir, _detailRoot, StringComparison.OrdinalIgnoreCase);
        _page.Children.Add(atRoot ? BuildSeriesHeader(e) : BuildFolderHeader(e));

        var kids = ChildFolders(_detailDir);
        if (kids.Count > 0)
        {
            _page.Children.Add(Header("Folders", kids.Count, "Open a folder to see its videos. Every folder has its own cover (right-click to change it)."));
            var wrap = new WrapPanel();
            foreach (var k in kids) wrap.Children.Add(FolderTile(e, k.Path, k.Name, k.Count, token));
            _page.Children.Add(wrap);
        }

        var shown = FilesInView(out var all);
        _page.Children.Add(BuildFileControls(all));
        if (shown.Count == 0)
        {
            _page.Children.Add(new TextBlock
            {
                Text = kids.Count > 0 && !_includeSub ? "No files directly in this folder - open one of the folders above, or tick \"Include subfolders\"." : "No files to show here.",
                Foreground = CMuted, FontSize = 13, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        switch (_s.DetailMode)
        {
            case DetailView.Tiles:
                var wrap = new WrapPanel();
                foreach (var m in shown) wrap.Children.Add(FileTile(e, m, shown, token));
                _page.Children.Add(wrap);
                break;
            default:
                var rows = new StackPanel();
                foreach (var m in shown) rows.Children.Add(FileRow(e, m, shown, token, _s.DetailMode == DetailView.Compact));
                _page.Children.Add(rows);
                break;
        }
    }

    UIElement BuildCrumbs(Entry e)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        TextBlock Link(string text, Action go, bool last)
        {
            var t = new TextBlock { Text = text, FontSize = 13, Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = last ? Brushes.White : CMuted, FontWeight = last ? FontWeights.SemiBold : FontWeights.Normal };
            if (!last)
            {
                t.Cursor = Cursors.Hand;
                t.MouseEnter += (_, _) => t.Foreground = _accent;
                t.MouseLeave += (_, _) => t.Foreground = CMuted;
                t.MouseLeftButtonUp += (_, _) => go();
            }
            return t;
        }
        TextBlock Sep() => new() { Text = "  ›  ", Foreground = CMuted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

        // the back chip goes one level up (folder -> series page -> folder collection -> library) and says where it goes
        bool rootNow = string.Equals(_detailDir, _detailRoot, StringComparison.OrdinalIgnoreCase);
        string backLabel = "Library";
        if (!rootNow)
        {
            string? up = System.IO.Path.GetDirectoryName(_detailDir.TrimEnd('\\', '/'));
            backLabel = !string.IsNullOrEmpty(up) && !string.Equals(up, _detailRoot, StringComparison.OrdinalIgnoreCase) && Scanner.IsUnder(up, _detailRoot) ? System.IO.Path.GetFileName(up.TrimEnd('\\', '/')) : e.DisplayTitle;
        }
        else if (_collection != null) backLabel = CollectionName(_collection);
        var back = MakeChip(new StackPanel { Orientation = Orientation.Horizontal, Children = { Icons.Make("Back", Brushes.White, 14), new TextBlock { Text = "  " + backLabel, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold } } }, false);
        back.Margin = new Thickness(0, 0, 12, 0);
        back.MouseLeftButtonUp += (_, _) => DetailUp();
        row.Children.Add(back);

        bool atRoot = string.Equals(_detailDir, _detailRoot, StringComparison.OrdinalIgnoreCase);
        row.Children.Add(Link(e.DisplayTitle, () => GoDir(_detailRoot), atRoot));
        if (!atRoot)
        {
            string baseDir = _detailRoot.Length > 0 && Scanner.IsUnder(_detailDir, _detailRoot) ? _detailRoot : "";
            string rel = baseDir.Length > 0 ? _detailDir[baseDir.TrimEnd('\\', '/').Length..].TrimStart('\\', '/') : _detailDir;
            var segs = baseDir.Length > 0 ? rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries) : new[] { FolderLabel(_detailDir, "") };
            string acc = baseDir;
            for (int i = 0; i < segs.Length; i++)
            {
                acc = baseDir.Length > 0 ? System.IO.Path.Combine(acc, segs[i]) : _detailDir;
                string go = acc; bool last = i == segs.Length - 1;
                row.Children.Add(Sep());
                row.Children.Add(Link(segs[i], () => GoDir(go), last));
            }
        }
        return row;
    }

    UIElement BuildSeriesHeader(Entry e)
    {
        var grid = new Grid { MinHeight = 230, Margin = new Thickness(0, 0, 0, 18), Background = CSurface };
        grid.SizeChanged += (_, a) => grid.Clip = new RectangleGeometry(new Rect(0, 0, a.NewSize.Width, a.NewSize.Height), 12, 12);
        var ac = AccentColor;
        var banner = Banner(e);
        if (banner != null) grid.Children.Add(new Image { Source = banner, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch });
        else grid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(150, ac.R, ac.G, ac.B), Color.FromRgb(16, 16, 20), 25) });
        grid.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(240, 7, 7, 7), 0), new GradientStop(Color.FromArgb(165, 7, 7, 7), 0.6), new GradientStop(Color.FromArgb(40, 7, 7, 7), 1)
            }, 0)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Poster(e, 136, 204, false));
        var info = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 700 };
        info.Children.Add(new TextBlock { Text = e.DisplayTitle, Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxHeight = 76, TextTrimming = TextTrimming.CharacterEllipsis });

        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        string meta = $"{e.Episodes} episode(s) on disk" + (e.ShownTotal > 0 ? $" of {e.ShownTotal}" : "") + (e.Extras > 0 ? $"  •  {e.Extras} OP / ED / music / extras" : "") + $"  •  watched {e.Watched}" + (e.Score > 0 ? $"  •  score {e.Score}/10" : "");
        info.Children.Add(new TextBlock { Text = meta, Foreground = CText, FontSize = 13, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        string src = e.LocalOnly ? "Local only - never looked up online"
            : e.Matched ? "Matched" + (e.AniListId != null ? $"  •  AniList #{e.AniListId}" : "") + (e.MalId != null ? $"  •  MAL #{e.MalId}" : "") + (e.Title != e.DisplayTitle ? $"  •  online title: {e.Title}" : "")
            : "Not matched online yet - right-click the series > Smart name... / Re-match";
        info.Children.Add(new TextBlock { Text = src, Foreground = CMuted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (total > 0)
            info.Children.Add(new ProgressBar { Height = 5, Maximum = total, Value = Math.Min(e.Watched, total), Margin = new Thickness(0, 12, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Width = 420 });

        var btns = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var play = MakeChip(new TextBlock { Text = $"▶  Watch episode {NextEpisode(e)}", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold }, true);
        play.Padding = new Thickness(18, 9, 18, 9); play.Margin = new Thickness(0, 0, 10, 8);
        play.MouseLeftButtonUp += (_, _) => Play(e);
        btns.Children.Add(play);
        Border B(string t, Action a) { var c = MakeChip(new TextBlock { Text = t, Foreground = Brushes.White, FontSize = 12 }, false); c.Padding = new Thickness(12, 9, 12, 9); c.Margin = new Thickness(0, 0, 8, 8); c.MouseLeftButtonUp += (_, _) => a(); return c; }
        btns.Children.Add(B("−1 ep", () => StepEpisode(e, -1)));
        btns.Children.Add(B("+1 ep", () => StepEpisode(e, 1)));
        btns.Children.Add(B(e.Score > 0 ? "★ " + e.Score + " / 10" : "☆ Score…", () => SetScore(e)));
        btns.Children.Add(B("✓ Complete", () => MarkCompleted(e)));
        btns.Children.Add(B("Open folder", () => { if (Directory.Exists(e.FolderPath)) Process.Start("explorer.exe", e.FolderPath); }));
        btns.Children.Add(B("Smart name…", () => ShowSmartName(e)));
        btns.Children.Add(B("Merge…", () => ShowMerge(e)));
        btns.Children.Add(B("Group…", () => ShowGroup(e)));
        btns.Children.Add(B("Change cover…", () => ChangeCover(e)));
        info.Children.Add(btns);
        row.Children.Add(info);
        grid.Children.Add(row);
        var slide = new TranslateTransform(); row.RenderTransform = slide;
        Reveal(grid, null, 0); Reveal(row, slide, 1, 14);
        return grid;
    }

    UIElement BuildFolderHeader(Entry e)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var cover = new Image { Stretch = Stretch.UniformToFill, Width = 176, Height = 99 };
        var host = new Border { Width = 176, Height = 99, CornerRadius = new CornerRadius(_s.CardRadius), ClipToBounds = true, Background = CCard, Child = cover, Margin = new Thickness(0, 0, 16, 0) };
        FillFolderCover(e, _detailDir, cover, _detailToken);
        sp.Children.Add(host);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = FolderLabel(_detailDir, _detailRoot.Length > 0 ? _detailRoot : _detailDir), Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold });
        int n = _media.Count(m => Scanner.IsUnder(m.Dir, _detailDir));
        info.Children.Add(new TextBlock { Text = $"{n} file(s)  •  {_detailDir}", Foreground = CMuted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 640 });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var cc = MakeChip(new TextBlock { Text = "Change folder cover…", Foreground = Brushes.White, FontSize = 12 }, false);
        cc.MouseLeftButtonUp += (_, _) => ChangeFolderCover(e, _detailDir);
        var ex = MakeChip(new TextBlock { Text = "Show in Explorer", Foreground = Brushes.White, FontSize = 12 }, false);
        ex.MouseLeftButtonUp += (_, _) => { if (Directory.Exists(_detailDir)) Process.Start("explorer.exe", _detailDir); };
        btns.Children.Add(cc); btns.Children.Add(ex);
        info.Children.Add(btns);
        sp.Children.Add(info);
        return sp;
    }

    UIElement BuildFileControls(List<MediaFile> all)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 16, 0, 12), LastChildFill = false };
        var left = new WrapPanel();
        Border FilterChip(string text, int code, int count)
        {
            bool on = _kindFilter == code;
            var c = MakeChip(new TextBlock { Text = count >= 0 ? $"{text}  {count}" : text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }, on);
            c.Margin = new Thickness(0, 0, 8, 6);
            c.MouseLeftButtonUp += (_, _) => { _kindFilter = code; Render(); };
            return c;
        }
        left.Children.Add(new TextBlock { Text = "Files", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(FilterChip("All", 0, all.Count));
        int ep = all.Count(m => m.Cls.Kind == FileKind.Episode), oe = all.Count(m => m.Cls.Kind is FileKind.Opening or FileKind.Ending);
        int mu = all.Count(m => m.Cls.Kind == FileKind.Music), ex = all.Count(m => m.Cls.Kind == FileKind.Extra);
        left.Children.Add(FilterChip("Episodes", 1, ep));
        if (oe > 0 || _kindFilter == 2) left.Children.Add(FilterChip("Openings / Endings", 2, oe));
        if (mu > 0 || _kindFilter == 3) left.Children.Add(FilterChip("Music", 3, mu));
        if (ex > 0 || _kindFilter == 4) left.Children.Add(FilterChip("Extras", 4, ex));
        if (ChildFolders(_detailDir).Count > 0)
        {
            var sub = new CheckBox { Content = "Include subfolders", IsChecked = _includeSub, Margin = new Thickness(8, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            sub.Click += (_, _) => { _includeSub = sub.IsChecked == true; Render(); };
            left.Children.Add(sub);
        }
        DockPanel.SetDock(left, Dock.Left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        Border Mode(string icon, DetailView v, string tip)
        {
            var b = new Border { Width = 32, Height = 30, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 0, 0), ToolTip = tip, Child = Icons.Make(icon, CText, 16) };
            var c = AccentColor;
            b.Background = _s.DetailMode == v ? new SolidColorBrush(Color.FromArgb(80, c.R, c.G, c.B)) : Brushes.Transparent;
            b.MouseLeftButtonUp += (_, _) => { _s.DetailMode = v; Persist(); Render(); };
            return b;
        }
        right.Children.Add(Mode("Tiles", DetailView.Tiles, "Thumbnails"));
        right.Children.Add(Mode("List", DetailView.List, "Detailed list"));
        right.Children.Add(Mode("Compact", DetailView.Compact, "Compact list"));
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(left); dock.Children.Add(right);
        return dock;
    }

    // ------------------------------------------------------------------ folder tiles (own cover)
    /// <summary>"Ep 1 to Ep 12" for the episodes below a folder ("Ep 5" for one, "N file(s)" when the names carry no numbers).</summary>
    string EpRange(string folder)
    {
        var eps = _media.Where(m => m.Cls.Kind == FileKind.Episode && Scanner.IsUnder(m.Dir, folder)).ToList();
        var nums = eps.Where(m => m.Ep != null).Select(m => m.Ep!.Value).ToList();
        if (nums.Count == 0) return eps.Count > 0 ? eps.Count + " file(s)" : _media.Count(m => Scanner.IsUnder(m.Dir, folder)) + " file(s)";
        int lo = nums.Min(), hi = nums.Max();
        return lo == hi ? "Ep " + lo : "Ep " + lo + " to Ep " + hi;
    }

    /// <summary>Own cover of the folder; a folder that has none but has sub-folders borrows the cover of its first sub-folder.</summary>
    string? CoverOrFirstChild(Entry e, string folder)
    {
        string? own = CoverTools.FolderCover(e, folder);
        if (own != null) return own;
        foreach (var k in ChildFolders(folder)) { string? c = CoverTools.FolderCover(e, k.Path); if (c != null) return c; }
        return null;
    }

    void FillFolderCover(Entry e, string folder, Image img, int token)
    {
        string? own = CoverOrFirstChild(e, folder);
        var ownImg = own != null ? CoverTools.Load(own, 440) : null;
        if (ownImg != null) { img.Source = ownImg; return; }
        img.Source = Cover(e, 440);                                          // until a thumbnail of its first video is ready
        var first = _media.Where(m => Scanner.IsVideo(m.Path) && m.Exists && Scanner.IsUnder(m.Dir, folder)).FirstOrDefault();
        if (first != null) _ = LoadThumbAsync(img, first.Path, token);
    }

    UIElement FolderTile(Entry e, string folder, string name, int count, int token)
    {
        const double w = 232, h = 130;
        var g = new Grid { Width = w, Height = h, Background = CCard, Clip = new RectangleGeometry(new Rect(0, 0, w, h), _s.CardRadius, _s.CardRadius) };
        var img = new Image { Stretch = Stretch.UniformToFill };
        g.Children.Add(img);
        FillFolderCover(e, folder, img, token);
        g.Children.Add(new Border { Height = h * 0.55, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(225, 0, 0, 0), 90) });
        var fi = Icons.Make("Folder", Brushes.White, 18); fi.HorizontalAlignment = HorizontalAlignment.Left; fi.VerticalAlignment = VerticalAlignment.Bottom; fi.Margin = new Thickness(10, 0, 0, 10);
        g.Children.Add(fi);
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(36, 0, 10, 8) };
        txt.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        txt.Children.Add(new TextBlock { Text = EpRange(folder), Foreground = CText, FontSize = 11 });
        g.Children.Add(txt);

        var c0 = AccentColor;
        var ringBrush = new SolidColorBrush(Color.FromArgb(0, c0.R, c0.G, c0.B));
        var slide = new TranslateTransform();
        var ring = new Border { CornerRadius = new CornerRadius(_s.CardRadius + 2), BorderThickness = new Thickness(2), BorderBrush = ringBrush, Padding = new Thickness(2), Child = g, Margin = new Thickness(0, 0, 14, 14), Cursor = Cursors.Hand, Background = Brushes.Transparent, RenderTransform = slide, ToolTip = folder };
        ring.MouseEnter += (_, _) => Hover(null, ringBrush, true);
        ring.MouseLeave += (_, _) => Hover(null, ringBrush, false);
        ring.MouseLeftButtonUp += (_, _) => GoDir(folder);
        var menu = new ContextMenu();
        MenuItem Mi(string t, Action a) { var m = new MenuItem { Header = t }; m.Click += (_, _) => a(); return m; }
        menu.Items.Add(Mi("Open", () => GoDir(folder)));
        menu.Items.Add(Mi("Change folder cover…", () => ChangeFolderCover(e, folder)));
        menu.Items.Add(Mi("Play this folder", () => PlayFiles(e, _media.Where(m => Scanner.IsVideo(m.Path) && m.Exists && m.Cls.Kind == FileKind.Episode && Scanner.IsUnder(m.Dir, folder)).Select(m => m.Path).ToList())));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi("Show in Explorer", () => { if (Directory.Exists(folder)) Process.Start("explorer.exe", folder); }));
        ring.ContextMenu = menu;
        Reveal(ring, slide, _cardIndex++);
        return ring;
    }

    void ChangeFolderCover(Entry e, string folder)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp", Title = "Cover for " + FolderLabel(folder, "") };
        if (d.ShowDialog() != true) return;
        if (!CoverTools.IsValidFile(d.FileName)) { Say("That file is not a picture this app can show."); return; }
        if (CoverTools.AssignFolder(e, folder, d.FileName) == null) { Say("Could not save the folder cover."); return; }
        Persist(); Render();
    }

    // ------------------------------------------------------------------ files
    async Task LoadThumbAsync(Image img, string path, int token)
    {
        try
        {
            string? f = await CoverTools.ThumbAsync(path, 360, _thumbCts?.Token ?? CancellationToken.None);
            if (f == null || token != _detailToken) return;
            var bi = CoverTools.Load(f, 360);
            if (bi != null) img.Source = bi;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    bool IsWatched(Entry e, MediaFile m) => m.Cls.Kind == FileKind.Episode && ((m.Ep is int n && n <= e.Watched) || e.WatchedFiles.Contains(m.Path, StringComparer.OrdinalIgnoreCase));

    UIElement Thumb(Entry e, MediaFile m, double w, double h, int token)
    {
        var g = new Grid { Width = w, Height = h, Background = CCard, Clip = new RectangleGeometry(new Rect(0, 0, w, h), Math.Min(_s.CardRadius, 8), Math.Min(_s.CardRadius, 8)) };
        var ph = Icons.Make(m.Cls.Kind == FileKind.Music ? "Music" : "Film", CMuted, Math.Min(34, h * 0.4));
        ph.HorizontalAlignment = HorizontalAlignment.Center; ph.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ph);
        var img = new Image { Stretch = Stretch.UniformToFill };
        bool watched = IsWatched(e, m);
        if (watched) img.Opacity = 0.55;
        g.Children.Add(img);
        if (m.Exists) _ = LoadThumbAsync(img, m.Path, token);
        if (watched)   // finished episode: a green check on the thumbnail
        {
            var ck = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Br("#E61FA971"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 6), ToolTip = "Watched", Child = Icons.Make("Check", Brushes.White, 14) };
            ((FrameworkElement)ck.Child).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)ck.Child).VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ck);
        }
        g.Children.Add(new Border
        {
            Background = Br("#CC101010"), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = ShortBadge(m), Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold }
        });
        if (watched)
        {
            var ck = Icons.Make("Check", Brushes.White, 12);
            g.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = _accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Child = ck });
        }
        if (!m.Exists)
            g.Children.Add(new Border { Background = Br("#AA000000"), Child = new TextBlock { Text = "missing", Foreground = Br("#F5A524"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 } });
        var play = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Br("#CC000000"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false, Child = Icons.Make("PlayRound", Brushes.White, 20) };
        g.Children.Add(play);
        g.MouseEnter += (_, _) => play.Opacity = m.Exists ? 1 : 0;
        g.MouseLeave += (_, _) => play.Opacity = 0;
        return g;
    }

    ContextMenu FileMenu(Entry e, MediaFile m, List<MediaFile> list)
    {
        var menu = new ContextMenu();
        MenuItem Mi(string t, Action a) { var x = new MenuItem { Header = t }; x.Click += (_, _) => a(); return x; }
        menu.Items.Add(Mi("Play", () => PlayFiles(e, new List<string> { m.Path })));
        menu.Items.Add(Mi("Play from here (this and the next files)", () => PlayFromHere(e, m, list)));
        if (m.Cls.Kind == FileKind.Episode && m.Ep != null)
            menu.Items.Add(Mi($"Mark watched up to episode {m.Ep}", () => { e.Watched = m.Ep.Value; e.NeedsSync = true; Persist(); Render(); UpdateSyncButton(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi("Show in Explorer", () => { if (File.Exists(m.Path)) Process.Start("explorer.exe", "/select,\"" + m.Path + "\""); }));
        menu.Items.Add(Mi("Copy path", () => { try { Clipboard.SetText(m.Path); } catch { } }));
        menu.Items.Add(Mi("Use thumbnail as cover of this folder", async () =>
        {
            string? t = await CoverTools.ThumbAsync(m.Path, 480);
            if (t == null) { Say("No thumbnail available (ffmpeg.exe next to the app is needed)."); return; }
            if (CoverTools.AssignFolder(e, m.Dir, t) != null) { Persist(); Render(); }
        }));
        return menu;
    }

    void PlayFromHere(Entry e, MediaFile m, List<MediaFile> list)
    {
        var paths = list.SkipWhile(x => x != m).Where(x => x.Exists).Select(x => x.Path).ToList();
        PlayFiles(e, paths);
    }

    UIElement FileTile(Entry e, MediaFile m, List<MediaFile> list, int token)
    {
        const double w = 232, h = 130;
        var sp = new StackPanel { Width = w + 8 };
        var c0 = AccentColor;
        var ringBrush = new SolidColorBrush(Color.FromArgb(0, c0.R, c0.G, c0.B));
        var ring = new Border { CornerRadius = new CornerRadius(_s.CardRadius + 2), BorderThickness = new Thickness(2), BorderBrush = ringBrush, Padding = new Thickness(2), Child = Thumb(e, m, w, h, token) };
        sp.Children.Add(ring);
        sp.Children.Add(new TextBlock { Text = Heading(m), Foreground = CText, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 5, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = m.Name + (m.Size > 0 ? "  •  " + FmtSize(m.Size) : ""), Foreground = CMuted, FontSize = 11, Margin = new Thickness(4, 1, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = m.Path });
        var slide = new TranslateTransform();
        var card = new Border { Margin = new Thickness(0, 0, 14, 16), Child = sp, Background = Brushes.Transparent, Cursor = Cursors.Hand, RenderTransform = slide };
        card.MouseEnter += (_, _) => Hover(null, ringBrush, true);
        card.MouseLeave += (_, _) => Hover(null, ringBrush, false);
        card.MouseLeftButtonDown += (_, a) => { if (a.ClickCount == 2 && m.Exists) PlayFromHere(e, m, list); };
        card.ToolTip = "Double-click to play";
        card.ContextMenu = FileMenu(e, m, list);
        Reveal(card, slide, _cardIndex++);
        return card;
    }

    UIElement FileRow(Entry e, MediaFile m, List<MediaFile> list, int token, bool compact)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!compact)
        {
            var t = Thumb(e, m, 112, 63, token);
            t.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            Grid.SetColumn(t, 0); grid.Children.Add(t);
        }
        else
        {
            var b = new TextBlock { Text = ShortBadge(m), Foreground = IsWatched(e, m) ? CMuted : _accent, FontSize = 11, FontWeight = FontWeights.Bold, Width = 74, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(b, 0); grid.Children.Add(b);
        }
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = Heading(m), Foreground = IsWatched(e, m) ? CMuted : Brushes.White, FontSize = compact ? 12.5 : 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = m.Name, Foreground = CMuted, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = m.Path });
        Grid.SetColumn(info, 1); grid.Children.Add(info);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = FmtSize(m.Size), Foreground = CMuted, FontSize = 12, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        var play = MakeChip(new TextBlock { Text = "▶", Foreground = Brushes.White, FontSize = 12 }, true);
        play.Padding = new Thickness(11, 5, 11, 5);
        play.MouseLeftButtonUp += (_, a) => { a.Handled = true; if (m.Exists) PlayFromHere(e, m, list); };
        right.Children.Add(play);
        Grid.SetColumn(right, 2); grid.Children.Add(right);

        var normal = new SolidColorBrush(Color.FromArgb(_hasBg ? (byte)150 : (byte)255, 20, 20, 20));
        var row = new Border { Child = grid, Margin = new Thickness(0, 0, 0, 5), Padding = new Thickness(compact ? 10 : 8, compact ? 6 : 6, 12, compact ? 6 : 6), CornerRadius = new CornerRadius(_s.CardRadius), Background = normal, Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => row.Background = CHover;
        row.MouseLeave += (_, _) => row.Background = normal;
        row.MouseLeftButtonDown += (_, a) => { if (a.ClickCount == 2 && m.Exists) PlayFromHere(e, m, list); };
        row.ContextMenu = FileMenu(e, m, list);
        var slide = new TranslateTransform(); row.RenderTransform = slide;
        Reveal(row, slide, _cardIndex++, 8);
        return row;
    }
}
