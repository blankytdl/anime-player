using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace AnimeLibrary;

/// <summary>Window chrome (custom title bar + border), custom background, surfaces, sorting and view-mode controls.</summary>
public partial class MainWindow
{
    Border _rail = null!;
    DockPanel _footerPanel = null!;
    Grid _titleBar = null!;
    Border _frame = null!;
    Border _maxBtn = null!;
    readonly Image _bgImg = new() { IsHitTestVisible = false, Stretch = Stretch.UniformToFill };
    readonly Border _bgDim = new() { IsHitTestVisible = false };
    bool _hasBg;
    ComboBox _sortBox = null!;
    readonly Dictionary<LibView, Border> _viewBtns = new();
    readonly Border _railLogo = new();
    // MyAnimeList profile card (avatar + name as tooltip); stays hidden until a login exists
    readonly Border _profileChip = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 18, 0, 8), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed, ClipToBounds = true, Background = Brushes.DimGray };
    string? _profileName;

    static readonly string[] SortNames = { "Title A-Z", "Title Z-A", "Recently watched", "Recently added", "Progress", "Episodes on disk" };

    // ------------------------------------------------------------------ build
    void BuildUi()
    {
        _rail = new Border { Width = 240, Child = BuildSidebar(), BorderBrush = CLine, BorderThickness = new Thickness(0, 0, 1, 0) };
        ApplySidebar();
        var top = BuildTopArea();

        // ---- content ----
        _scroll.Content = _page;
        _scroll.Background = Brushes.Transparent;
        _footerPanel = new DockPanel();
        DockPanel.SetDock(_net, Dock.Right);
        _spinner.RenderTransform = _spin;
        DockPanel.SetDock(_spinner, Dock.Left);
        _footerPanel.Children.Add(_net); _footerPanel.Children.Add(_spinner); _footerPanel.Children.Add(_status);

        var content = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(_footerPanel, Dock.Bottom);
        content.Children.Add(top); content.Children.Add(_footerPanel); content.Children.Add(_scroll);

        var body = new DockPanel();
        DockPanel.SetDock(_rail, Dock.Left);
        body.Children.Add(_rail); body.Children.Add(content);

        // ---- custom frame: title bar + body, thin coloured border, optional background image ----
        _titleBar = BuildTitleBar();
        var layout = new DockPanel();
        DockPanel.SetDock(_titleBar, Dock.Top);
        layout.Children.Add(_titleBar); layout.Children.Add(body);
        _frame = new Border { Child = layout };

        var root = new Grid { Background = CBg };
        root.Children.Add(_bgImg); root.Children.Add(_bgDim); root.Children.Add(_frame);
        Content = root;
        HookLazyScroll();
        PreviewKeyDown += (_, a) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (a.Key == Key.B) { ToggleSidebar(); a.Handled = true; }
            else if (a.Key == Key.K || a.Key == Key.F) { _search.Focus(); _search.SelectAll(); a.Handled = true; }
        };

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 38, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
        });
        StateChanged += (_, _) => { UpdateMaxIcon(); ApplyFrame(); };
        ApplyAppearance();
        RefreshViewButtons();
    }

    Grid BuildTitleBar()
    {
        var g = new Grid { Height = 38 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(SideToggleButton());
        try { left.Children.Add(new Image { Width = 18, Height = 18, Source = BitmapFrame.Create(new Uri("pack://application:,,,/app.png")), Margin = new Thickness(8, 0, 10, 0) }); } catch { }
        left.Children.Add(new TextBlock { Text = "Anime Library", Foreground = CText, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(CaptionButton("Minimize", () => WindowState = WindowState.Minimized));
        _maxBtn = CaptionButton("Maximize", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        btns.Children.Add(_maxBtn);
        btns.Children.Add(CaptionButton("Close", Close, true));
        Grid.SetColumn(left, 0); Grid.SetColumn(btns, 2);
        g.Children.Add(left); g.Children.Add(btns);
        return g;
    }

    Border CaptionButton(string icon, Action click, bool danger = false)
    {
        var b = new Border { Width = 46, Height = 38, Background = Brushes.Transparent, Child = Icons.Make(icon, CText, 14) };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => b.Background = danger ? Br("#E81123") : CHover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    void UpdateMaxIcon() => _maxBtn.Child = Icons.Make(WindowState == WindowState.Maximized ? "Restore" : "Maximize", CText, 14);

    Border ViewButton(string icon, LibView v, string tip)
    {
        var b = new Border { Width = 32, Height = 30, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, ToolTip = tip, Child = Icons.Make(icon, CText, 16) };
        b.MouseEnter += (_, _) => { if (_s.View != v) b.Background = CHover; };
        b.MouseLeave += (_, _) => { if (_s.View != v) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => { _s.View = v; Persist(); RefreshViewButtons(); _animate = true; Render(); };
        _viewBtns[v] = b;
        return b;
    }

    void RefreshViewButtons()
    {
        var c = AccentColor;
        foreach (var kv in _viewBtns) kv.Value.Background = kv.Key == _s.View ? new SolidColorBrush(Color.FromArgb(80, c.R, c.G, c.B)) : Brushes.Transparent;
    }

    // ------------------------------------------------------------------ maximise inside the work area (no covering the taskbar)
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] class MONITORINFO { public int cbSize = Marshal.SizeOf(typeof(MONITORINFO)); public RECT rcMonitor, rcWork; public int dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024)   // WM_GETMINMAXINFO
        {
            try
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                IntPtr mon = MonitorFromWindow(hwnd, 2);
                if (mon != IntPtr.Zero)
                {
                    var mi = new MONITORINFO();
                    if (GetMonitorInfo(mon, mi))
                    {
                        mmi.ptMaxPosition.x = Math.Abs(mi.rcWork.left - mi.rcMonitor.left);
                        mmi.ptMaxPosition.y = Math.Abs(mi.rcWork.top - mi.rcMonitor.top);
                        mmi.ptMaxSize.x = Math.Abs(mi.rcWork.right - mi.rcWork.left);
                        mmi.ptMaxSize.y = Math.Abs(mi.rcWork.bottom - mi.rcWork.top);
                    }
                }
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------ appearance (theme, border, background) - applied live
    static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
    }

    void PushTheme()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        var a = new SolidColorBrush(AccentColor); a.Freeze();
        res["AccentBrush"] = a;
        if (_railLogo != null) { _railLogo.Background = _accent; _railLogo.Child = Icons.Make("Books", Brushes.White, 22); }
    }

    void ApplyFrame()
    {
        bool max = WindowState == WindowState.Maximized;
        _frame.BorderThickness = new Thickness(max ? 0 : _s.BorderSize);
        var c = string.IsNullOrWhiteSpace(_s.BorderColor) ? AccentColor : ParseColor(_s.BorderColor, AccentColor);
        _frame.BorderBrush = new SolidColorBrush(c);
    }

    /// <summary>Background picture (it was stored before but never drawn), darkening, blur, translucent side panels, frame border.</summary>
    void ApplyAppearance()
    {
        ApplyFrame();
        BitmapImage? bi = null;
        if (!string.IsNullOrWhiteSpace(_s.BackgroundImage) && File.Exists(_s.BackgroundImage)) bi = CoverTools.Load(_s.BackgroundImage, 2400);
        _hasBg = bi != null;
        _bgImg.Source = bi;
        _bgImg.Visibility = _hasBg ? Visibility.Visible : Visibility.Collapsed;
        _bgImg.Stretch = _s.BackgroundFit switch { "Fit" => Stretch.Uniform, "Stretch" => Stretch.Fill, _ => Stretch.UniformToFill };
        _bgImg.Effect = _s.BackgroundBlur > 0 ? new BlurEffect { Radius = _s.BackgroundBlur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality } : null;
        _bgImg.Margin = _s.BackgroundBlur > 0 ? new Thickness(-_s.BackgroundBlur) : new Thickness(0);   // hides the soft blurred edge
        byte dim = (byte)Math.Clamp(_s.BackgroundDim * 255 / 100, 0, 245);
        _bgDim.Background = new SolidColorBrush(Color.FromArgb(_hasBg ? dim : (byte)0, 7, 7, 7));

        // side panels: solid on the plain background, see-through glass over a picture
        var panel = _hasBg ? Br("#D80B0B0B") : CRail;
        _rail.Background = panel; _footerPanel.Background = panel;
        _titleBar.Background = _hasBg ? Br("#E6070707") : Br("#0A0A0A");
        RefreshViewButtons();
    }

    /// <summary>Settings changed: theme, frame, background, sort / view controls and the page, all live.</summary>
    void ApplyAll()
    {
        ApplyTheme(); ApplyAppearance();
        _sortBox.SelectedIndex = Math.Clamp((int)_s.Sort, 0, SortNames.Length - 1);
        _imgCache.Clear(); _animate = true; Render();
    }

    // ------------------------------------------------------------------ sorting
    static double Fraction(Entry e)
    {
        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        return total <= 0 ? 0 : Math.Min(1.0, (double)e.Watched / total);
    }

    IEnumerable<Entry> Sorted(IEnumerable<Entry> src)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        return _s.Sort switch
        {
            SortMode.TitleZA => src.OrderByDescending(e => e.DisplayTitle, cmp),
            SortMode.RecentlyWatched => src.OrderByDescending(e => e.LastPlayed ?? DateTime.MinValue).ThenBy(e => e.DisplayTitle, cmp),
            SortMode.RecentlyAdded => src.OrderByDescending(e => e.Added).ThenBy(e => e.DisplayTitle, cmp),
            SortMode.Progress => src.OrderByDescending(Fraction).ThenBy(e => e.DisplayTitle, cmp),
            SortMode.EpisodeCount => src.OrderByDescending(e => e.Episodes).ThenBy(e => e.DisplayTitle, cmp),
            _ => src.OrderBy(e => e.DisplayTitle, cmp)
        };
    }

    // ------------------------------------------------------------------ list / compact rows
    UIElement MakeRow(Entry e, bool compact)
    {
        double ph = compact ? 0 : 84, pw = ph * 2 / 3;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (!compact)
        {
            var thumb = Poster(e, pw, ph, false);
            thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            Grid.SetColumn(thumb, 0); grid.Children.Add(thumb);
        }
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = e.DisplayTitle, Foreground = Brushes.White, FontSize = compact ? 13 : 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string sub = e.Kind == Kind.Manga ? "Manga" : e.Episodes > 0 ? $"{e.Episodes} episodes on disk" : "no files found";
        if (e.Extras > 0) sub += $" + {e.Extras} OP/ED/music/extras";
        if (!e.Matched && !e.Custom) sub += "  •  unmatched";
        if (e.LocalOnly) sub += "  •  local only";
        if (e.NeedsSync) sub += "  •  waiting for upload";
        info.Children.Add(new TextBlock { Text = sub, Foreground = CMuted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(info, 1); grid.Children.Add(info);

        int total = e.ShownTotal > 0 ? e.ShownTotal : e.Episodes;
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (total > 0)
        {
            right.Children.Add(new ProgressBar { Width = compact ? 90 : 140, Height = 5, Maximum = total, Value = Math.Min(e.Watched, total), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(new TextBlock { Text = $"{e.Watched} / {total}", Foreground = CText, FontSize = 12, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        }
        var play = MakeChip(new TextBlock { Text = "▶ Play", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold }, true);
        play.MouseLeftButtonUp += (_, a) => { a.Handled = true; Play(e); };
        right.Children.Add(play);
        Grid.SetColumn(right, 2); grid.Children.Add(right);

        var row = new Border { Child = grid, Padding = new Thickness(compact ? 10 : 12, compact ? 7 : 10, 14, compact ? 7 : 10), CornerRadius = new CornerRadius(_s.CardRadius), Background = new SolidColorBrush(Color.FromArgb(_hasBg ? (byte)150 : (byte)255, 20, 20, 20)), Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => row.Background = CHover;
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(_hasBg ? (byte)150 : (byte)255, 20, 20, 20));
        row.MouseLeftButtonUp += (_, _) => OpenDetail(e);
        row.ContextMenu = BuildMenu(e);
        var slide = new TranslateTransform(); row.RenderTransform = slide;
        Reveal(row, slide, _cardIndex++, 10);
        return row;
    }
}
