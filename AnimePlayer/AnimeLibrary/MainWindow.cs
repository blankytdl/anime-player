using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AnimeLibrary;

/// <summary>Main window - laid out like Seanime's web UI: dark #070707 canvas, icon rail on the left, "continue watching" hero + carousel,
/// then poster grids grouped by status with episode badges, brand color #6152DF, 8 px corners.</summary>
public partial class MainWindow : Window
{
    public static readonly Dictionary<string, string> Themes = new()
    {
        ["Seanime"] = "#6152DF", ["Violet"] = "#8B5CF6", ["Crimson"] = "#E5484D", ["Ocean"] = "#2D9CDB", ["Cyan"] = "#22D3EE", ["Emerald"] = "#2EB67D",
        ["Mint"] = "#34D399", ["Amber"] = "#F5A524", ["Orange"] = "#FB8C00", ["Gold"] = "#FACC15", ["Rose"] = "#F472B6", ["Sakura"] = "#FF7EB6",
        ["Mono"] = "#D4D4D8", ["Custom"] = ""
    };

    /// <summary>Surface palettes (background, side panel, surface, card, line, text, muted text, hover). Applied once at start - change it in Settings, restart the library.</summary>
    public static readonly Dictionary<string, string[]> Surfaces = new()
    {
        ["Dark"]     = new[] { "#070707", "#0B0B0B", "#101010", "#141414", "#282828", "#D1D1D1", "#8F8F8F", "#1C1C1C" },
        ["AMOLED"]   = new[] { "#000000", "#000000", "#060606", "#0C0C0C", "#1E1E1E", "#D8D8D8", "#8A8A8A", "#141414" },
        ["Midnight"] = new[] { "#070A12", "#0A0E18", "#0D121D", "#121826", "#222B3D", "#D6DCEB", "#8591A8", "#19212F" },
        ["Slate"]    = new[] { "#0F1115", "#14171C", "#181B21", "#1D2128", "#2C313B", "#D4D8E0", "#8D94A3", "#252A33" },
        ["Warm"]     = new[] { "#0D0A09", "#120E0C", "#171210", "#1D1714", "#322822", "#E0D6CE", "#9A8D82", "#251D19" },
    };

    static void ApplySurface(string? name)
    {
        var p = Surfaces.TryGetValue(name ?? "", out var v) ? v : Surfaces["Dark"];
        CBg = Br(p[0]); CRail = Br(p[1]); CSurface = Br(p[2]); CCard = Br(p[3]); CLine = Br(p[4]); CText = Br(p[5]); CMuted = Br(p[6]); CHover = Br(p[7]);
    }

    // Seanime palette (globals.css): gray-950 .. gray-200
    static SolidColorBrush Br(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    static SolidColorBrush CBg = Br("#070707"), CRail = Br("#0B0B0B"), CSurface = Br("#101010"), CCard = Br("#141414"),
        CLine = Br("#282828"), CText = Br("#D1D1D1"), CMuted = Br("#8F8F8F"), CHover = Br("#1C1C1C");

    enum View { Library, Unmatched, Pending }
    string? _collection;       // key of the franchise folder that is open (a folder that holds several series), null = the library itself

    static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    bool _animate = true;      // entrance animations run on view changes / scans / start - not on every live update
    bool _anim;                // the flag of the Render() that is running now
    bool _suppressSearch;
    int _cardIndex;
    readonly RotateTransform _spin = new();
    readonly TextBlock _spinner = new() { Text = "⟳", Foreground = Br("#8F8F8F"), FontSize = 15, Margin = new Thickness(14, 3, 0, 3), Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5) };

    readonly Settings _s = Store.Load("settings.json", new Settings());
    List<Entry> _entries = Store.Load("entries.json", new List<Entry>());
    readonly MalClient _mal;
    readonly StackPanel _page = new() { Margin = new Thickness(28, 8, 28, 28) };
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = CBg };
    readonly TextBox _search = new() { Width = 260, Padding = new Thickness(8, 5, 8, 5), Background = CCard, Foreground = CText, BorderBrush = CLine, CaretBrush = CText, VerticalContentAlignment = VerticalAlignment.Center };
    readonly TextBlock _title = new() { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
    readonly TextBlock _status = new() { Foreground = CMuted, Margin = new Thickness(14, 6, 14, 6), FontSize = 12 };
    readonly TextBlock _net = new() { Foreground = CMuted, Margin = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12 };
    readonly TextBlock _syncText = new() { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
    readonly SemaphoreSlim _busy = new(1, 1);                  // one scan / sync at a time
    readonly Dictionary<string, BitmapImage?> _imgCache = new();
    readonly HashSet<string> _iconTried = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly Dictionary<View, TextBlock> _railBadge = new();
    readonly Dictionary<View, Border> _railItem = new();
    Brush _accent = Br("#6152DF");
    View _view = View.Library;
    int _ticks;

    public MainWindow()
    {
        Title = "Anime Library"; Width = 1280; Height = 800; MinWidth = 860; MinHeight = 520;
        ApplySurface(_s.Surface);
        Background = CBg; Foreground = CText;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/app.ico")); } catch { }
        _mal = new MalClient(_s) { Ask = msg => Dispatcher.Invoke(() => AskPaste(msg)) };
        _mal.SessionLost += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            _profileName = null; _profileChip.Child = null; UpdateProfileCard();
            Say("Your MyAnimeList login expired - click the profile card (bottom left) to log in again. Progress stays saved here.");
        }));
        if (string.IsNullOrEmpty(_s.WatchLog)) _s.WatchLog = System.IO.Path.Combine(PlayerConfigDir(), "watched.log");
        ApplyTheme();

        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); _builtCards = 0; Render(); if (_detail == null) _scroll.ScrollToVerticalOffset(0); };
        _search.TextChanged += (_, _) => { if (_suppressSearch) return; _searchDelay.Stop(); _searchDelay.Start(); };

        BuildUi();

        if (Scanner.ApplyWatchLog(_s, _entries)) Persist();
        Render(); UpdateSyncButton();

        // internet came back -> match new titles, download covers, upload progress
        NetworkChange.NetworkAvailabilityChanged += (_, e) =>
        {
            if (e.IsAvailable) Dispatcher.InvokeAsync(async () => { try { await Task.Delay(3000); await AutoSyncAsync(); } catch { } });
        };
        _tick.Tick += async (_, _) => { try { await TickAsync(); } catch { } };
        _tick.Start();
        Loaded += async (_, _) =>
        {
            try
            {
                await RefreshNetAsync();
                // offline and cheap: re-read the folders on every start so new files, renamed files and entries made by an older version
                // (without file lists) are picked up; the scan also continues with the online part when the internet is there
                if (_s.Folders.Count > 0) await ScanAsync();
                else await AutoSyncAsync();
            }
            catch { }
        };
        Closing += (_, _) => Persist();
    }

    // ------------------------------------------------------------------ small UI helpers
    static string PlayerConfigDir()
    {
        string portable = System.IO.Path.Combine(AppContext.BaseDirectory, "portable_config");
        if (Directory.Exists(portable)) return portable;
        string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string mpv = System.IO.Path.Combine(app, "mpv");
        return Directory.Exists(mpv) ? mpv : System.IO.Path.Combine(app, "mpv.net");
    }

    // ------------------------------------------------------------------ animation helpers
    Color AccentColor => _accent is SolidColorBrush b ? b.Color : Colors.MediumPurple;

    void SetBusy(bool on)
    {
        _spinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) _spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        else _spin.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    /// <summary>Fade + rise-in, staggered by index (capped so a long list is not slow).</summary>
    void Reveal(FrameworkElement el, TranslateTransform? slide, int index, double rise = 18)
    {
        if (!_anim) return;
        var begin = TimeSpan.FromMilliseconds(Math.Min(index * 30, 650));
        var dur = TimeSpan.FromMilliseconds(340);
        el.Opacity = 0;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { BeginTime = begin, EasingFunction = EaseOut });
        if (slide != null)
        {
            slide.Y = rise;
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, dur) { BeginTime = begin, EasingFunction = EaseOut });
        }
    }

    void Hover(ScaleTransform? scale, SolidColorBrush ring, bool on)
    {
        var t = TimeSpan.FromMilliseconds(150);
        if (scale != null)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.04 : 1.0, t) { EasingFunction = EaseOut });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.04 : 1.0, t) { EasingFunction = EaseOut });
        }
        var c = AccentColor;
        ring.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? c : Color.FromArgb(0, c.R, c.G, c.B), t));
    }

    void ApplyTheme()
    {
        try { _accent = (Brush)new BrushConverter().ConvertFromString(_s.Accent)!; } catch { _accent = Br("#6152DF"); }
        PushTheme();
    }

    Border RailItem(View v, string icon, string label)
    {
        var badge = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        var badgeBox = new Border { Background = _accent, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1), Child = badge, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _railBadge[v] = badge;
        var b = NavRow(icon, label, badgeBox);
        b.Tag = badgeBox;
        b.MouseEnter += (_, _) => { if (_view != v || _detail != null) b.Background = CHover; };
        b.MouseLeave += (_, _) => { if (_view != v || _detail != null) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => { _detail = null; _collection = null; _view = v; _suppressSearch = true; _search.Text = ""; _suppressSearch = false; _animate = true; Render(); };
        _railItem[v] = b;
        return b;
    }

    Border RailAction(string icon, string label, Action act)
    {
        var b = NavRow(icon, label, null);
        b.MouseEnter += (_, _) => b.Background = CHover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => { try { act(); } catch (Exception ex) { Say("Error: " + ex.Message); } };
        return b;
    }

    /// <summary>One sidebar row: icon, label, optional count badge on the right.</summary>
    Border NavRow(string icon, string label, UIElement? right)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = Icons.Make(icon, CText, 20);
        ic.Margin = new Thickness(0, 0, 12, 0); ic.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ic, 0); g.Children.Add(ic);
        var t = new TextBlock { Text = label, Foreground = CText, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 1); g.Children.Add(t);
        if (right != null) { Grid.SetColumn(right, 2); g.Children.Add(right); }
        return new Border { Child = g, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(10, 1, 10, 1), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
    }

    Border MakeChip(UIElement label, bool primary)
    {
        var normal = primary ? _accent : CCard;
        var b = new Border { Child = label, Background = normal, BorderBrush = CLine, BorderThickness = new Thickness(primary ? 0 : 1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Opacity = 0.85;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        return b;
    }

    Border Chip(string text, Action act, bool primary = false)
    {
        var c = MakeChip(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }, primary);
        c.MouseLeftButtonUp += (_, _) => { try { act(); } catch (Exception ex) { Say("Error: " + ex.Message); } };
        return c;
    }

    Border ChipAsync(string text, Func<Task> act, bool primary = false) =>
        ChipAsync(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }, act, primary);

    Border ChipAsync(TextBlock label, Func<Task> act, bool primary)
    {
        var c = MakeChip(label, primary);
        c.MouseLeftButtonUp += async (_, _) => { try { await act(); } catch (Exception ex) { Say("Error: " + ex.Message); } };
        return c;
    }

    void Say(string m) => _status.Text = m;
    void Persist() { try { Store.Save("settings.json", _s); Store.Save("entries.json", _entries); } catch (Exception ex) { Say("Could not save: " + ex.Message); } }

    int Pending => _entries.Count(e => e.NeedsSync && e.Kind == Kind.Anime);
    int UnmatchedCount => _entries.Count(e => !e.Matched && !e.Custom && e.Kind == Kind.Anime);

    void UpdateSyncButton()
    {
        _syncText.Text = Pending > 0 ? $"⇅ Update MAL ({Pending})" : "⇅ Update MAL";
        void Badge(View v, int n)
        {
            if (!_railBadge.TryGetValue(v, out var t)) return;
            t.Text = n.ToString();
            ((Border)t.Parent).Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        Badge(View.Library, _entries.Count);
        Badge(View.Unmatched, UnmatchedCount);
        Badge(View.Pending, Pending);
    }

    void AddFolder()
    {
        var d = new Microsoft.Win32.OpenFolderDialog();
        if (d.ShowDialog() != true) return;
        if (_s.Folders.Any(f => f.Path.Equals(d.FolderName, StringComparison.OrdinalIgnoreCase))) { Say("That folder is already in the library."); return; }
        _s.Folders.Add(new LibFolder { Path = d.FolderName, Source = Source.AniList });
        Persist();
        _ = ScanAsync();
    }

    async Task RefreshNetAsync()
    {
        bool on = await Providers.IsOnlineAsync();
        int p = Pending;
        _net.Text = on ? (p > 0 ? $"● Online - {p} update(s) waiting" : "● Online") : (p > 0 ? $"○ Offline - {p} update(s) saved, they upload when you are back online" : "○ Offline");
        _net.Foreground = on ? Br("#4ADE80") : Br("#F5A524");
        if (on && _mal.LoggedIn && _profileName == null) _ = LoadProfileAsync();
    }

    async Task LoadProfileAsync()
    {
        var pr = await _mal.GetProfileAsync();
        if (pr == null)
        {
            // a dead login is cleared by the client (SessionLost updates the card); any other failure (offline, MAL busy) just retries later
            if (_mal.NeedsLogin) UpdateProfileCard();
            return;
        }
        _profileName = pr.Name;
        _profileChip.ToolTip = "MyAnimeList: " + pr.Name;
        _profileChip.Child = null;
        UpdateProfileCard();
        if (pr.Picture != null)
        {
            try
            {
                var bytes = await Providers.Http.GetByteArrayAsync(pr.Picture);
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(bytes); bi.EndInit(); bi.Freeze();
                _profileChip.Child = new Image { Source = bi, Stretch = Stretch.UniformToFill };
            }
            catch { /* the letter stays */ }
        }
    }

    // ------------------------------------------------------------------ background work
    async Task TickAsync()
    {
        _ticks++;
        if (_busy.CurrentCount == 0) return;                      // a scan / sync is running
        bool changed = false;
        try { changed = ApplyLogAnnounce(); } catch { }
        if (changed) { Persist(); Render(); UpdateSyncButton(); await RefreshNetAsync(); }
        if (_ticks % 15 == 0)                                      // every ~30 s: connectivity + auto upload
        {
            await RefreshNetAsync();
            if (Pending > 0 || _entries.Any(e => !e.Matched && !e.Custom && e.Kind == Kind.Anime)) await AutoSyncAsync();
        }
    }

    async Task AutoSyncAsync()
    {
        if (!_s.AutoSyncOnline) return;
        if (!await Providers.IsOnlineAsync()) return;
        await SyncAsync(false);
    }

    async Task ScanAsync()
    {
        if (!await _busy.WaitAsync(0)) { Say("Already working - please wait."); return; }
        SetBusy(true);
        try
        {
            Say("Scanning folders...");
            var snapshot = _entries.ToList();
            _entries = await Task.Run(() => Scanner.ScanLocal(_s, snapshot));
            Scanner.ApplyWatchLog(_s, _entries);
            _animate = true;
            Persist(); Render(); UpdateSyncButton();
            string done = $"Scan finished: {_entries.Count} titles ({UnmatchedCount} not matched yet).";
            Say(done);
            if (await Providers.IsOnlineAsync(true)) await SyncCoreAsync(false, false);   // titles + covers only: the MyAnimeList upload is its own button
            else Say(done + " Offline: matching and covers continue when the internet is back.");
        }
        catch (Exception ex) { Say("Scan failed: " + ex.Message); }
        finally { SetBusy(false); _busy.Release(); await RefreshNetAsync(); }
    }

    async Task SyncAsync(bool manual)
    {
        if (!await _busy.WaitAsync(0)) { if (manual) Say("Already working - please wait."); return; }
        SetBusy(true);
        try
        {
            if (!await Providers.IsOnlineAsync(manual))
            {
                if (manual) Say(Pending > 0 ? $"Offline: {Pending} update(s) are saved and upload as soon as you are online." : "Offline.");
                return;
            }
            await SyncCoreAsync(manual);
        }
        catch (Exception ex) { Say("Sync failed: " + ex.Message); }
        finally { SetBusy(false); _busy.Release(); await RefreshNetAsync(); }
    }

    /// <summary>Matches new titles, downloads covers, sets folder icons and uploads watched progress. Caller holds _busy.</summary>
    async Task SyncCoreAsync(bool manual, bool upload = true)
    {
        Scanner.ApplyWatchLog(_s, _entries);
        int changed = await Scanner.MatchAndCoversAsync(_s, _entries, new Progress<string>(Say), default, manual);
        if (changed > 0) _imgCache.Clear();
        ApplyFolderIcons();

        string result = "";
        var pending = upload ? _entries.Where(e => e.NeedsSync && e.Kind == Kind.Anime).ToList() : new List<Entry>();
        if (pending.Count > 0)
        {
            if (!_mal.LoggedIn) result = $"{pending.Count} update(s) waiting - log in to MyAnimeList (MAL) to upload them.";
            else
            {
                int ok = 0, fail = 0, noId = 0;
                string lastErr = "";
                foreach (var e in pending)
                {
                    if (e.MalId == null) { noId++; continue; }
                    var u = await _mal.UpdateAsync(e.MalId.Value, e.Watched, e.TotalEpisodes, e.ScoreDirty ? e.Score : null, e.ForceStatus, e.LowerOk);
                    if (u.Ok) { e.NeedsSync = false; e.ScoreDirty = false; e.ForceStatus = null; e.LowerOk = false; if (u.Total > 0) e.TotalEpisodes = u.Total; ok++; }
                    else { fail++; lastErr = u.Error; }
                }
                result = $"MyAnimeList: {ok} updated" + (fail > 0 ? $", {fail} failed ({lastErr}) - will retry" : "") + (noId > 0 ? $", {noId} not matched to a MAL entry yet" : "") + ".";
            }
        }
        else if (manual && upload) result = "Everything is up to date.";
        Persist(); Render(); UpdateSyncButton();
        if (result.Length > 0) Say(result);
    }

    void ApplyFolderIcons()
    {
        if (!_s.SetFolderIcon) return;
        foreach (var e in _entries)
        {
            if (e.IconSet || e.SharedFolder || e.Detected || e.CoverFile == null || (e.Kind != Kind.Anime && !e.Custom)) continue;
            if (!Directory.Exists(e.FolderPath) || !_iconTried.Add(e.FolderPath + "|" + e.CoverFile)) continue;
            e.IconSet = CoverTools.SetFolderIcon(e.FolderPath, e.CoverFile);
        }
    }

    // ------------------------------------------------------------------ pages
    static bool Completed(Entry e)
    {
        int total = e.ShownTotal;     // unknown total (not matched online yet) is never "Completed": the show may have more episodes than the files on disk
        return total > 0 && e.Watched >= total;
    }

    static int NextEpisode(Entry e) => e.Watched + 1;

    int _shown;

    void Render()
    {
        if (_detail != null && !_entries.Contains(_detail)) _detail = null;
        if (_detail != null) { RenderDetail(); return; }

        bool viewChange = _animate;     // a new page / tab / scan starts at the top; a live update (+1, upload done) keeps the scroll position and the cards already built
        _anim = _animate && _s.Animations; _animate = false; _cardIndex = 0; _shown = 0; _lazy = null;
        _keepCards = viewChange ? 0 : _builtCards; _builtCards = 0;
        foreach (var kv in _railItem)
        {
            bool on = kv.Key == _view;
            var ac = AccentColor;
            kv.Value.Background = on ? new SolidColorBrush(Color.FromArgb(60, ac.R, ac.G, ac.B)) : Brushes.Transparent;
        }
        if (!_tabInit)      // first page: if nothing is being watched yet, open on All instead of an empty tab
        {
            _tabInit = true;
            if (_entries.Count > 0 && CurrentTab == LibTab.Watching && !_entries.Any(x => InTab(x, LibTab.Watching))) _s.StatusTab = (int)LibTab.All;
        }
        UpdateTabs();
        bool inCollection = _view == View.Library && _collection != null;
        SetChrome(_view == View.Library && !inCollection, !inCollection, inCollection ? CollectionName(_collection!) : _view switch { View.Unmatched => "Unmatched", View.Pending => "Waiting for upload", _ => "Library" });
        if (_anim) _title.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });

        _page.Children.Clear();
        if (viewChange) _scroll.ScrollToVerticalOffset(0);
        var all = SearchFiltered();

        if (_view == View.Unmatched)
        {
            var unm = Sorted(all.Where(e => !e.Matched && !e.Custom)).ToList();
            if (unm.Count > 0)
            {
                var retry = new Button { Content = "Retry matching", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
                retry.Click += async (_, _) => { foreach (var u in unm) { u.MatchTries = 0; u.LastTry = null; } await SyncAsync(true); Persist(); Render(); };
                _page.Children.Add(retry);
                var why = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                foreach (var u in unm.Where(x => !string.IsNullOrEmpty(x.MatchNote)))
                    why.Children.Add(new TextBlock { Text = "•  " + u.DisplayTitle + " — " + u.MatchNote, Foreground = CMuted, FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = u.FolderPath });
                _page.Children.Add(why);
            }
            Section("Not matched online yet", unm,
                "Titles that could not be found on AniList / MyAnimeList (offline, or the name is unusual). Right-click > Smart name... fixes the name, or mark the series local only.");
            EmptyHint("Everything is matched.");
            AfterRender();
            return;
        }
        if (_view == View.Pending)
        {
            Section("Progress that is saved here and not uploaded yet", Sorted(all.Where(e => e.NeedsSync)).ToList(),
                "They upload automatically when you are online, or press Update list.");
            EmptyHint("Nothing is waiting - your list is up to date.");
            AfterRender();
            return;
        }
        // Library: one tab at a time (Watching / Plan to Watch / Completed / All)
        if (inCollection) { RenderCollection(); AfterRender(); return; }
        var list = Sorted(all.Where(e => InTab(e, CurrentTab))).ToList();
        if (_s.View == LibView.Posters) AddCards(GroupCollections(list));
        else
        {
            _shown += list.Count;
            var rows = new StackPanel();
            foreach (var e in list) rows.Children.Add(MakeRow(e, _s.View == LibView.Compact));
            _page.Children.Add(rows);
        }
        if (_entries.Count == 0) EmptyHint("No anime yet. Press \"＋ Folder\" and choose the folder that holds your anime - it is scanned offline in seconds.");
        else if (_shown == 0) EmptyHint(_search.Text.Trim().Length > 0 ? "Nothing matches your search." : CurrentTab switch
        {
            LibTab.Watching => "Nothing is being watched right now. Play an episode, or look under Plan to Watch.",
            LibTab.Completed => "No series completed yet.",
            LibTab.NotStarted => "Every series has been started.",
            _ => "Nothing here."
        });
        AfterRender();
    }

    /// <summary>Applies the player's watch log and tells you when an episode was finished: "Finished episode 5 of Bakemonogatari".</summary>
    bool ApplyLogAnnounce()
    {
        var before = _entries.ToDictionary(x => x, x => x.Watched);
        bool changed = Scanner.ApplyWatchLog(_s, _entries);
        var done = _entries.Where(x => before.TryGetValue(x, out int w0) && x.Watched > w0).ToList();
        if (done.Count > 0)
            Say(done.Count == 1 ? $"Finished episode {done[0].Watched} of {done[0].DisplayTitle}" : $"Finished episodes of {done.Count} series: " + string.Join(", ", done.Take(3).Select(x => x.DisplayTitle)) + (done.Count > 3 ? " ..." : ""));
        return changed;
    }

    void AfterRender()
    {
        if (_lazy != null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FillViewport));
    }

    void EmptyHint(string text)
    {
        if (_shown > 0) return;
        _page.Children.Add(new TextBlock { Text = text, Foreground = CMuted, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 60, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, MaxWidth = 520 });
    }

    UIElement Header(string text, int count, string? sub = null)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold });
        row.Children.Add(new TextBlock { Text = "  " + count, Foreground = CMuted, FontSize = 14, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        sp.Children.Add(row);
        if (sub != null) sp.Children.Add(new TextBlock { Text = sub, Foreground = CMuted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        return sp;
    }

    void Section(string title, List<Entry> list, string? sub = null)
    {
        if (list.Count == 0) return;
        _shown += list.Count;
        _page.Children.Add(Header(title, list.Count, sub));
        if (_s.View == LibView.Posters)
        {
            _shown -= list.Count;       // AddCards counts them
            AddCards(list);
        }
        else
        {
            var rows = new StackPanel();
            foreach (var e in list) rows.Children.Add(MakeRow(e, _s.View == LibView.Compact));
            _page.Children.Add(rows);
        }
    }

    // ---- covers ----
    BitmapImage? Cover(Entry e, int w)
    {
        string? cf = e.CoverFile != null && File.Exists(e.CoverFile) ? e.CoverFile : CoverTools.FirstSubfolderCover(e);
        if (cf == null) return null;
        string k = cf + "|" + w;
        if (!_imgCache.TryGetValue(k, out var bi))
        {
            _imgCache[k] = bi = CoverTools.Load(cf, w);
            if (bi == null) ForgetBadCover(e);
        }
        return bi;
    }

    /// <summary>The file exists but is not a picture (HTML error page, truncated download...): forget it so the next sync fetches a good one.</summary>
    void ForgetBadCover(Entry e)
    {
        try
        {
            string? f = e.CoverFile;
            e.CoverFile = null; e.IconSet = false;
            if (f != null && f.StartsWith(Store.Dir, StringComparison.OrdinalIgnoreCase)) File.Delete(f);
        }
        catch { }
    }

    BitmapImage? Banner(Entry e)
    {
        if (e.BannerFile == null || !File.Exists(e.BannerFile)) return null;
        string k = "banner|" + e.BannerFile;
        if (!_imgCache.TryGetValue(k, out var bi)) _imgCache[k] = bi = CoverTools.Load(e.BannerFile, 1400);
        return bi;
    }

    /// <summary>The picture and nothing else: no text, no symbols, no badges, no bars on a poster. (Everything that used to sit on it - episode
    /// count, +1 / -1, done / airing / ended, score, unmatched - is drawn under the poster by <see cref="MakeCard"/>.) <paramref name="badge"/> is kept for old callers.</summary>
    UIElement Poster(Entry e, double w, double h, bool badge)
    {
        var g = new Grid { Width = w, Height = h, Background = CCard, Clip = new RectangleGeometry(new Rect(0, 0, w, h), _s.CardRadius, _s.CardRadius) };
        var img = Cover(e, (int)(w * 2));
        if (img != null) g.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill });
        else
        {
            var ph = Icons.Make("Film", Br("#3A3A3A"), Math.Max(24, w * 0.3));
            ph.HorizontalAlignment = HorizontalAlignment.Center; ph.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ph);
        }
        return g;
    }

    static string When(DateTime t)
    {
        int d = (int)(DateTime.Now.Date - t.Date).TotalDays;
        return d <= 0 ? "today" : d == 1 ? "yesterday" : d + " days ago";
    }

    Border MiniChip(string text, string bg, string fg, string tip) => new()
    {
        Background = Br(bg), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 5, 4), ToolTip = tip,
        Child = new TextBlock { Text = text, Foreground = Br(fg), FontSize = 10, FontWeight = FontWeights.SemiBold }
    };

    /// <summary>One segmented control for episode -1 / +1: two buttons of exactly the same size, drawn icons instead of "−" / "+1" text.</summary>
    UIElement Stepper(Entry e)
    {
        // two separate round buttons: a quiet "-" and an accent "+", both pop a little on hover and press
        Border Btn(string icon, string tip, int delta, bool plus)
        {
            var scale = new ScaleTransform(1, 1);
            var b = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, ToolTip = tip,
                Margin = new Thickness(plus ? 6 : 0, 0, 0, 0),
                Background = plus ? _accent : Br("#26FFFFFF"), BorderBrush = plus ? Brushes.Transparent : CLine, BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
                Child = Icons.Make(icon, plus ? Brushes.White : CText, 12)
            };
            ((FrameworkElement)b.Child).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)b.Child).VerticalAlignment = VerticalAlignment.Center;
            void To(double v) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(110))); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(110))); }
            b.MouseEnter += (_, _) => To(1.12);
            b.MouseLeave += (_, _) => To(1);
            b.PreviewMouseLeftButtonDown += (_, _) => To(0.88);
            b.MouseLeftButtonUp += (_, a) => { a.Handled = true; To(1.12); StepEpisode(e, delta); };
            return b;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Btn("Minus", "One episode back", -1, false));
        row.Children.Add(Btn("Plus", e.IsMovie ? "Mark as watched" : "Mark the next episode watched", 1, true));
        return row;
    }

    UIElement MakeCard(Entry e, int w)
    {
        double h = w * 1.5;
        var c0 = AccentColor;
        var ringBrush = new SolidColorBrush(Color.FromArgb(0, c0.R, c0.G, c0.B));
        var scale = new ScaleTransform(1, 1);
        var ring = new Border
        {
            CornerRadius = new CornerRadius(_s.CardRadius + 3), BorderThickness = new Thickness(2), BorderBrush = ringBrush, Padding = new Thickness(2),
            Child = Poster(e, w, h, false), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
        };
        var sp = new StackPanel { Width = w + 8 };
        sp.Children.Add(ring);

        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        bool anime = e.Kind == Kind.Anime;
        bool done = Completed(e);

        // progress strip UNDER the poster
        if (_s.ShowProgress && total > 0 && e.Watched > 0)
        {
            var pb = new ProgressBar { Height = 4, Maximum = total, Value = Math.Min(e.Watched, total), Foreground = done ? Br("#1FA971") : _accent, Background = Br("#26FFFFFF"), BorderThickness = new Thickness(0), Margin = new Thickness(4, 6, 4, 0) };
            if (_anim)
            {
                double target = pb.Value;
                pb.Value = 0;
                pb.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                    new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(800)) { BeginTime = TimeSpan.FromMilliseconds(Math.Min(_cardIndex * 30, 650) + 200), EasingFunction = EaseOut });
            }
            sp.Children.Add(pb);
        }

        if (_s.ShowTitles)
            sp.Children.Add(new TextBlock { Text = e.DisplayTitle, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 7, 4, 0), ToolTip = e.DisplayTitle });

        // progress text + quick -1 / +1 (uploaded to MyAnimeList automatically)
        string prog = !anime ? "Manga"
            : e.IsMovie ? (e.Watched > 0 ? "Watched" : "Movie")
            : e.ShownTotal > 0 ? $"Ep {e.Watched} / {e.ShownTotal}"
            : e.Watched > 0 ? $"Ep {e.Watched} / ?"
            : e.Episodes > 0 ? $"{e.Episodes} on disk" : "no files found";
        var info = new Grid { Margin = new Thickness(4, 5, 4, 0) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var epText = new TextBlock { Text = prog, Foreground = done ? Br("#4ADE80") : CText, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(epText, 0); info.Children.Add(epText);
        if (anime)
        {
            var st = Stepper(e);
            Grid.SetColumn(st, 1); info.Children.Add(st);
        }
        sp.Children.Add(info);

        if (_s.ShowTitles)
        {
            string sub = anime && e.Episodes > 0 && e.TotalEpisodes > 0 ? $"{e.Episodes} on disk" : "";
            if (e.Detected) sub += (sub.Length > 0 ? " • " : "") + "detected";
            if (e.LocalOnly) sub += (sub.Length > 0 ? " • " : "") + "local";
            if (sub.Length > 0) sp.Children.Add(new TextBlock { Text = sub, Foreground = CMuted, FontSize = 11, Margin = new Thickness(4, 2, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        // indicators, under the poster: movie / completed / airing / ended / just watched / score / unmatched / waiting for upload
        var chips = new WrapPanel { Margin = new Thickness(4, 6, 4, 0) };
        if (_s.ShowIndicators)
        {
            if (anime && e.IsMovie) chips.Children.Add(MiniChip("Movie", "#33A855F7", "#D8B4FE", "A film"));
            if (anime && e.IsSpecial) chips.Children.Add(MiniChip("Special", "#33A855F7", "#D8B4FE", "Specials / OVA"));
            if (anime && done) chips.Children.Add(MiniChip(e.IsMovie ? "Watched" : "Completed", "#331FA971", "#4ADE80", e.IsMovie ? "You watched this movie" : "You watched every episode"));
            if (anime && !e.IsMovie && e.Airing == "RELEASING") chips.Children.Add(MiniChip("Airing", "#332F6FDB", "#7FB0FF", "Still airing"));
            else if (anime && !e.IsMovie && e.Airing == "FINISHED") chips.Children.Add(MiniChip("Ended", "#33888888", "#B5B5B5", "The series has ended"));
            if (anime && e.Watched > 0 && !done && e.LastPlayed is DateTime lp && (DateTime.Now - lp).TotalDays <= 14)
                chips.Children.Add(MiniChip("✓ Ep " + e.Watched + " watched · " + When(lp), "#2622C55E", "#86EFAC", "You finished episode " + e.Watched + " " + When(lp)));
            if (anime && e.Score > 0) chips.Children.Add(MiniChip("★ " + e.Score, "#33F5A524", "#F5A524", "Your score"));
        }
        if (!e.Matched && !e.Custom) chips.Children.Add(MiniChip("Unmatched", "#33F5A524", "#F5A524", "Not found online yet"));
        if (e.NeedsSync) chips.Children.Add(MiniChip("Waiting for upload", "#336152DF", "#A99BFF", "Progress saved here - uploads when you are online"));
        if (chips.Children.Count > 0) sp.Children.Add(chips);

        var slide = new TranslateTransform(0, 0);
        var card = new Border { Margin = new Thickness(0, 0, 14, 20), Child = sp, Cursor = Cursors.Hand, Background = Brushes.Transparent, RenderTransform = slide };
        card.MouseEnter += (_, _) => Hover(scale, ringBrush, true);
        card.MouseLeave += (_, _) => Hover(scale, ringBrush, false);
        card.MouseLeftButtonUp += (_, _) => OpenDetail(e);
        card.ContextMenu = BuildMenu(e);
        Reveal(card, slide, _cardIndex++);
        return card;
    }

    // ---- continue watching: hero banner + wide cards ----
    UIElement MakeHero(Entry e)
    {
        var grid = new Grid { Height = 250, Margin = new Thickness(0, 4, 0, 20), Background = CSurface };
        grid.SizeChanged += (_, a) => grid.Clip = new RectangleGeometry(new Rect(0, 0, a.NewSize.Width, a.NewSize.Height), 12, 12);
        // a crisp AniList banner when there is one, otherwise a clean accent gradient (a blurred, dimmed poster looked smeared)
        var banner = Banner(e);
        var ac = AccentColor;
        if (banner != null) grid.Children.Add(new Image { Source = banner, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Center });
        else grid.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(150, ac.R, ac.G, ac.B), Color.FromRgb(16, 16, 20), 25) });
        // scrim on the text side only
        grid.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(235, 7, 7, 7), 0), new GradientStop(Color.FromArgb(150, 7, 7, 7), 0.55), new GradientStop(Color.FromArgb(20, 7, 7, 7), 1)
            }, 0)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Poster(e, 128, 192, false));
        var info = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 640 };
        info.Children.Add(new TextBlock { Text = "CONTINUE WATCHING", Foreground = _accent, FontSize = 11, FontWeight = FontWeights.Bold });
        info.Children.Add(new TextBlock { Text = e.DisplayTitle, Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxHeight = 80, Margin = new Thickness(0, 4, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        info.Children.Add(new TextBlock { Text = $"Episode {NextEpisode(e)}" + (total > 0 ? $" of {total}" : "") + $"  •  {e.Episodes} on disk" + (e.LastPlayed != null ? $"  •  last watched {e.LastPlayed:d MMM}" : ""), Foreground = CText, FontSize = 13 });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var play = MakeChip(new TextBlock { Text = $"▶  Watch episode {NextEpisode(e)}", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold }, true);
        play.Padding = new Thickness(18, 9, 18, 9); play.Margin = new Thickness(0);
        play.MouseLeftButtonUp += (_, _) => Play(e);
        var open = MakeChip(new TextBlock { Text = "Episodes & folders", Foreground = Brushes.White, FontSize = 13 }, false);
        open.Padding = new Thickness(14, 9, 14, 9); open.Margin = new Thickness(10, 0, 0, 0);
        open.MouseLeftButtonUp += (_, _) => OpenDetail(e);
        btns.Children.Add(play); btns.Children.Add(open);
        info.Children.Add(btns);
        row.Children.Add(info);
        grid.Children.Add(row);
        var rowSlide = new TranslateTransform(0, 0);
        row.RenderTransform = rowSlide;
        Reveal(grid, null, 0);
        Reveal(row, rowSlide, 1, 14);
        return grid;
    }

    UIElement Carousel(List<Entry> list)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in list.Take(24)) strip.Children.Add(MakeWide(e));
        var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = strip, Margin = new Thickness(0, 0, 0, 12) };
        // the wheel scrolls the PAGE, shift+wheel the strip (a horizontal ScrollViewer would otherwise swallow the wheel)
        sv.PreviewMouseWheel += (_, a) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift) { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - a.Delta); a.Handled = true; return; }
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - a.Delta);
            a.Handled = true;
        };
        return sv;
    }

    UIElement MakeWide(Entry e)
    {
        const double w = 250, h = 140;
        var g = new Grid { Width = w, Height = h, Background = CCard, Clip = new RectangleGeometry(new Rect(0, 0, w, h), 10, 10) };
        var img = Cover(e, 500);
        if (img != null) g.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
        g.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(235, 0, 0, 0), 90), IsHitTestVisible = false });
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 12) };
        txt.Children.Add(new TextBlock { Text = e.DisplayTitle, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        txt.Children.Add(new TextBlock { Text = $"Episode {NextEpisode(e)}" + (total > 0 ? $" / {total}" : ""), Foreground = CText, FontSize = 11 });
        g.Children.Add(txt);
        if (total > 0) g.Children.Add(new ProgressBar { Height = 4, Maximum = total, Value = Math.Min(e.Watched, total), Foreground = _accent, Background = Br("#33FFFFFF"), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Bottom });
        var c0 = AccentColor;
        var wideBrush = new SolidColorBrush(Color.FromArgb(0, c0.R, c0.G, c0.B));
        var wideSlide = new TranslateTransform(0, 0);
        var ring = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = wideBrush, Padding = new Thickness(2), Child = g, Margin = new Thickness(0, 0, 12, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, RenderTransform = wideSlide };
        ring.MouseEnter += (_, _) => Hover(null, wideBrush, true);
        ring.MouseLeave += (_, _) => Hover(null, wideBrush, false);
        ring.MouseLeftButtonUp += (_, _) => OpenDetail(e);
        Reveal(ring, wideSlide, _cardIndex++);
        ring.ToolTip = "Click to open - next up: episode " + NextEpisode(e);
        ring.ContextMenu = BuildMenu(e);
        return ring;
    }

    // ---- context menu ----
    ContextMenu BuildMenu(Entry e)
    {
        var menu = new ContextMenu();
        MenuItem Mi(string h, Action act) { var m = new MenuItem { Header = h }; m.Click += (_, _) => act(); return m; }
        menu.Items.Add(Mi("Open (episodes & folders)", () => OpenDetail(e)));
        menu.Items.Add(Mi("Play (next episode)", () => Play(e)));
        menu.Items.Add(Mi("Episode +1 (mark next watched)", () => StepEpisode(e, 1)));
        menu.Items.Add(Mi("Episode −1", () => StepEpisode(e, -1)));
        menu.Items.Add(Mi("Set score...", () => SetScore(e)));
        menu.Items.Add(Mi("Mark as completed", () => MarkCompleted(e)));
        menu.Items.Add(Mi("Set watched episodes...", () =>
        {
            var t = Prompt("Episodes watched", e.Watched.ToString());
            if (t != null && int.TryParse(t, out int n) && n >= 0) { e.Watched = n; e.NeedsSync = true; Persist(); Render(); UpdateSyncButton(); }
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi("Smart name...", () => ShowSmartName(e)));
        menu.Items.Add(Mi("Merge into another series...", () => ShowMerge(e)));
        menu.Items.Add(Mi(string.IsNullOrEmpty(e.Group) ? "Group into a folder..." : "Group: " + e.Group + " (change...)", () => ShowGroup(e)));
        menu.Items.Add(Mi(e.LocalOnly ? "✓ Local only - click to allow online look-ups" : "Local only (never look up online)", () => ToggleLocalOnly(e)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi("Change cover image...", () => ChangeCover(e)));
        menu.Items.Add(Mi("Download cover again", async () =>
        {
            try { File.Delete(CoverTools.CacheFile(e.Key)); } catch { }
            e.CoverFile = null; _imgCache.Clear();
            await SyncAsync(true);
        }));
        menu.Items.Add(Mi("Re-match title...", async () =>
        {
            var name = Prompt("Search online as", e.SearchName ?? e.DisplayTitle); if (name == null) return;
            e.SearchName = name; e.Matched = false; e.Custom = false; e.LocalOnly = false; e.MatchTries = 0;
            _s.NameRules.RemoveAll(r => r.TargetKey == e.Key && r.LocalOnly);
            await SyncAsync(true);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi("Open folder", () => { if (Directory.Exists(e.FolderPath)) Process.Start("explorer.exe", e.FolderPath); }));
        if (e.Custom || e.Detected)
            menu.Items.Add(Mi("Remove from library", () => { _entries.Remove(e); Persist(); Render(); UpdateSyncButton(); }));
        return menu;
    }

    void ChangeCover(Entry e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp", Title = "Choose the cover for " + e.DisplayTitle };
        if (d.ShowDialog() != true) return;
        string? file = e.SharedFolder ? CoverFileOnlyCache(e, d.FileName) : CoverTools.Assign(e, d.FileName);
        if (file == null) { Say("Could not save the cover."); return; }
        _imgCache.Clear();
        if (_s.SetFolderIcon && !e.SharedFolder && !e.Detected) { e.IconSet = CoverTools.SetFolderIcon(e.FolderPath, file); }
        Persist(); Render();
    }

    static string? CoverFileOnlyCache(Entry e, string source)
    {
        try
        {
            string cache = CoverTools.CacheFile(e.Key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);
            File.Copy(source, cache, true);
            e.CoverFile = cache;
            return cache;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ playing
    /// <summary>The series' episodes in watching order across ALL its folders (openings, endings, music and extras are not episodes).</summary>
    static List<string> EpisodeFiles(Entry e) =>
        e.Files.Where(f => Scanner.IsVideo(f) && NameParser.Classify(f).Kind == FileKind.Episode && File.Exists(f)).ToList();

    void Play(Entry e)
    {
        if (e.Kind == Kind.Manga || string.IsNullOrEmpty(e.FolderPath))
        {
            if (Directory.Exists(e.FolderPath)) Process.Start("explorer.exe", e.FolderPath);
            return;
        }
        var files = EpisodeFiles(e);
        if (files.Count == 0 && Directory.Exists(e.FolderPath))      // detected / custom entries have no file list: read the folder
        {
            files = Directory.EnumerateFiles(e.FolderPath, "*", (e.SharedFolder || e.Detected) ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories).Where(Scanner.IsVideo).ToList();
            if (e.SharedFolder || e.Detected) files = files.Where(f => NameParser.KeyOf(NameParser.ForFile(f)) == e.Key).ToList();
            files = files.Where(f => NameParser.Classify(f).Kind == FileKind.Episode).ToList();
            files.Sort(Players.Natural);
        }
        if (files.Count == 0) { Say("No episode files found for " + e.DisplayTitle + " (is the drive plugged in?)"); return; }

        // first episode after the last finished one; everything watched (or nothing) -> from the start
        int start = 0;
        if (e.Watched > 0)
        {
            var eps = files.Select(f => NameParser.ForFile(f).Episode).ToList();
            start = eps.Any(x => x == null)
                ? (e.Watched >= files.Count ? 0 : e.Watched)                      // names without numbers: count position
                : eps.FindIndex(x => x > e.Watched);
            if (start < 0) start = 0;
        }
        PlayFiles(e, files.Skip(start).ToList());
    }

    void PlayFiles(Entry e, List<string> list)
    {
        if (list.Count == 0) return;
        string? err = Players.Launch(_s, list);
        if (err != null) { Say(err); return; }
        e.LastPlayed = DateTime.Now; e.LastFile = list[0];
        Persist();
        Say($"Playing {e.DisplayTitle} from \"{System.IO.Path.GetFileName(list[0])}\" in {Players.Describe(_s)}.");
    }

    // ---- tracker actions (MyAnimeList): episode up / down, score, complete. Saved at once, uploaded as soon as you are online.
    void TrackChanged(Entry e)
    {
        e.NeedsSync = true; Persist(); Render(); UpdateSyncButton();
        if (_s.AutoSyncOnline) _ = SyncAsync(false);
    }

    void StepEpisode(Entry e, int d)
    {
        int max = e.ShownTotal > 0 ? e.ShownTotal : Math.Max(e.Episodes, e.Watched + 1);
        int n = Math.Clamp(e.Watched + d, 0, max);
        if (n == e.Watched) return;
        if (n < e.Watched) e.LowerOk = true;
        e.Watched = n;
        TrackChanged(e);
    }

    /// <summary>Scoring window: ten stars you can hover and click, the number and the MyAnimeList word for it (Masterpiece ... Appalling), keys 0-9 / Enter / Esc.</summary>
    void SetScore(Entry e)
    {
        var w = NewDialog("Your score", 470);
        int value = e.Score;
        string[] names = { "No score", "Appalling", "Horrible", "Very bad", "Bad", "Average", "Fine", "Good", "Very good", "Great", "Masterpiece" };
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        panel.Children.Add(new TextBlock { Text = e.DisplayTitle, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        var big = new TextBlock { FontSize = 52, FontWeight = FontWeights.Bold, Foreground = _accent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        var label = new TextBlock { FontSize = 15, Foreground = CText, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(big); panel.Children.Add(label);
        var stars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var marks = new TextBlock[11];
        void Paint(int hover)
        {
            int v = hover > 0 ? hover : value;
            for (int i = 1; i <= 10; i++) { marks[i].Text = i <= v ? "★" : "☆"; marks[i].Foreground = i <= v ? Br("#F5A524") : CText; }
            big.Text = v > 0 ? v.ToString() : "–";
            label.Text = names[v];
        }
        for (int i = 1; i <= 10; i++)
        {
            int n = i;
            var tb = new TextBlock { Text = "☆", FontSize = 30, Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand };
            marks[n] = tb;
            tb.MouseEnter += (_, _) => Paint(n);
            tb.MouseLeave += (_, _) => Paint(0);
            tb.MouseLeftButtonUp += (_, _) => { value = n; Paint(0); };
            stars.Children.Add(tb);
        }
        panel.Children.Add(stars);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
        var clear = new Button { Content = "No score", Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(26, 6, 26, 6) };
        clear.Click += (_, _) => { value = 0; Paint(0); };
        save.Click += (_, _) =>
        {
            w.DialogResult = true;
            if (value != e.Score) { e.Score = value; e.ScoreDirty = true; TrackChanged(e); }
        };
        buttons.Children.Add(clear); buttons.Children.Add(cancel); buttons.Children.Add(save);
        panel.Children.Add(buttons);
        w.PreviewKeyDown += (_, a) =>
        {
            if (a.Key >= Key.D0 && a.Key <= Key.D9) { value = a.Key == Key.D0 ? 10 : a.Key - Key.D0; Paint(0); a.Handled = true; }     // 1-9 = that score, 0 = 10
            else if (a.Key == Key.Back || a.Key == Key.Delete) { value = 0; Paint(0); a.Handled = true; }
        };
        w.Content = panel;
        Paint(0);
        w.ShowDialog();
    }

    void MarkCompleted(Entry e)
    {
        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        if (total > 0) e.Watched = Math.Max(e.Watched, total);
        e.ForceStatus = "completed";
        TrackChanged(e);
    }

    string? Prompt(string title, string def)
    {
        var tb = new TextBox { Text = def, Margin = new Thickness(10) };
        var ok = new Button { Content = "OK", Margin = new Thickness(10), IsDefault = true };
        var w = new Window { Title = title, Width = 400, SizeToContent = SizeToContent.Height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = CSurface, Foreground = CText };
        var sp = new StackPanel(); sp.Children.Add(tb); sp.Children.Add(ok); w.Content = sp;
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true ? tb.Text.Trim() : null;
    }
}
