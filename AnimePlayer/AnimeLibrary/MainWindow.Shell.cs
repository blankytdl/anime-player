using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace AnimeLibrary;

/// <summary>Round 5 shell: sidebar (Now Playing, Library, Unmatched, Updates, Folders, Extensions, Settings, Open player, MyAnimeList profile card,
/// hide button), status tabs with counts (Watching / Plan to Watch / Completed / All), filter row, lazy poster grid.
/// Inspired by the layout of the TORA reference screenshot, not a copy: no text or symbol is ever drawn ON a poster - everything sits under it.</summary>
public partial class MainWindow
{
    enum LibTab { Watching, NotStarted, Completed, All }

    static readonly (LibTab Tab, string Label)[] TabDefs =
    {
        (LibTab.Watching, "Watching"), (LibTab.NotStarted, "Plan to Watch"), (LibTab.Completed, "Completed"), (LibTab.All, "All")
    };

    readonly Dictionary<LibTab, (Border Box, TextBlock Label, TextBlock Count)> _tabUi = new();
    UIElement _tabsPill = null!;
    Panel _filterRow = null!;
    readonly TextBlock _profileTitle = new(), _profileSub = new();
    Border _profileCard = null!;
    bool _malBusy, _tabInit;
    Func<bool>? _lazy;          // appends the next batch of cards, returns true while more are left
    int _builtCards, _keepCards; // cards built by this render / by the previous one (a live update rebuilds at least as many, so the scroll position stays)

    LibTab CurrentTab => (LibTab)Math.Clamp(_s.StatusTab, 0, 3);

    bool InTab(Entry e, LibTab t) => t switch
    {
        LibTab.Watching => e.Watched > 0 && !Completed(e),
        LibTab.NotStarted => e.Watched == 0,
        LibTab.Completed => Completed(e),
        _ => true
    };

    // ------------------------------------------------------------------ sidebar
    UIElement BuildSidebar()
    {
        var dock = new DockPanel();

        _railLogo.Width = 38; _railLogo.Height = 38; _railLogo.CornerRadius = new CornerRadius(10);
        _railLogo.Margin = new Thickness(0);
        _railLogo.Background = _accent; _railLogo.Child = Icons.Make("Books", Brushes.White, 22);
        var brandText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock { Text = "Anime Library", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold });
        brandText.Children.Add(new TextBlock { Text = "Library & tracker", Foreground = CMuted, FontSize = 11 });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 14, 16) };
        brand.Children.Add(_railLogo); brand.Children.Add(brandText);
        DockPanel.SetDock(brand, Dock.Top); dock.Children.Add(brand);

        _profileCard = BuildProfileCard();
        DockPanel.SetDock(_profileCard, Dock.Bottom); dock.Children.Add(_profileCard);

        var nav = new StackPanel();
        nav.Children.Add(RailItem(View.Library, "Books", "Library"));
        nav.Children.Add(RailItem(View.Unmatched, "Warning", "Unmatched"));
        nav.Children.Add(RailItem(View.Pending, "Updates", "Updates"));
        nav.Children.Add(new Border { Height = 1, Background = CLine, Margin = new Thickness(18, 10, 18, 10) });
        nav.Children.Add(RailAction("Folder", "Folders", ShowFolders));
        nav.Children.Add(RailAction("Extensions", "Extensions", ShowExtensions));
        nav.Children.Add(RailAction("Settings", "Settings", ShowSettings));
        nav.Children.Add(RailAction("Launch", "Open player", OpenPlayer));
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav });
        return dock;
    }

    void OpenPlayer()
    {
        string? err = Players.Open(_s);
        Say(err ?? "Player started.");
    }

    void ToggleSidebar()
    {
        _s.SidebarHidden = !_s.SidebarHidden;
        ApplySidebar();
        Persist();
    }

    void ApplySidebar() => _rail.Visibility = _s.SidebarHidden ? Visibility.Collapsed : Visibility.Visible;

    // ------------------------------------------------------------------ MyAnimeList profile card (bottom of the sidebar)
    Border BuildProfileCard()
    {
        _profileChip.Width = 38; _profileChip.Height = 38; _profileChip.CornerRadius = new CornerRadius(19);
        _profileChip.Margin = new Thickness(0); _profileChip.Visibility = Visibility.Visible;
        _profileChip.HorizontalAlignment = HorizontalAlignment.Left; _profileChip.VerticalAlignment = VerticalAlignment.Center;
        _profileChip.Background = Br("#2A2A2A");
        _profileTitle.Foreground = Brushes.White; _profileTitle.FontSize = 13; _profileTitle.FontWeight = FontWeights.SemiBold; _profileTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _profileSub.Foreground = CMuted; _profileSub.FontSize = 11; _profileSub.TextTrimming = TextTrimming.CharacterEllipsis;
        var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_profileTitle); texts.Children.Add(_profileSub);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_profileChip, 0); Grid.SetColumn(texts, 1);
        g.Children.Add(_profileChip); g.Children.Add(texts);
        var card = new Border
        {
            Child = g, Background = CCard, BorderBrush = CLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10), Margin = new Thickness(12, 8, 12, 14), Cursor = Cursors.Hand
        };
        card.MouseEnter += (_, _) => card.Background = CHover;
        card.MouseLeave += (_, _) => card.Background = CCard;
        card.MouseLeftButtonUp += (_, _) => ShowProfileMenu(card);
        UpdateProfileCard();
        return card;
    }

    void UpdateProfileCard()
    {
        if (_mal.NeedsLogin)
        {
            _profileTitle.Text = "Login expired";
            _profileSub.Text = "Click to log in again"; _profileSub.Foreground = Br("#F5A524");
            _profileChip.Child = Icons.Make("Warning", Br("#F5A524"), 20);
            ((FrameworkElement)_profileChip.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((FrameworkElement)_profileChip.Child).VerticalAlignment = VerticalAlignment.Center;
        }
        else if (_mal.LoggedIn)
        {
            string name = _profileName ?? _s.MalUser ?? "MyAnimeList";
            _profileTitle.Text = name;
            _profileSub.Text = "MyAnimeList · connected"; _profileSub.Foreground = CMuted;
            if (_profileChip.Child is not Image)
                _profileChip.Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            _profileTitle.Text = "Connect MyAnimeList";
            _profileSub.Text = "Not logged in"; _profileSub.Foreground = CMuted;
            var ic = Icons.Make("Person", CMuted, 22);
            ic.HorizontalAlignment = HorizontalAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
            _profileChip.Child = ic;
        }
    }

    void ShowProfileMenu(FrameworkElement target)
    {
        var m = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        MenuItem Mi(string h, Action act) { var x = new MenuItem { Header = h }; x.Click += (_, _) => { try { act(); } catch (Exception ex) { Say("Error: " + ex.Message); } }; return x; }
        if (_mal.LoggedIn && !_mal.NeedsLogin)
        {
            string? name = _profileName ?? _s.MalUser;
            if (!string.IsNullOrEmpty(name)) m.Items.Add(Mi("Open my MyAnimeList profile", () => OpenUrl("https://myanimelist.net/profile/" + Uri.EscapeDataString(name))));
            m.Items.Add(Mi("Test connection", () => _ = TestMalAsync()));
            m.Items.Add(Mi("Log in again...", () => _ = MalLoginAsync()));
            m.Items.Add(new Separator());
            m.Items.Add(Mi("Log out of MyAnimeList", MalLogout));
        }
        else m.Items.Add(Mi("Log in to MyAnimeList...", () => _ = MalLoginAsync()));
        m.Items.Add(Mi("MyAnimeList settings (Client ID)...", ShowSettings));
        m.IsOpen = true;
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    async Task TestMalAsync()
    {
        Say("Testing the MyAnimeList connection...");
        Say(await _mal.TestAsync());
        UpdateProfileCard();
    }

    async Task MalLoginAsync()
    {
        if (_malBusy) { Say("A MyAnimeList login is already waiting in your browser."); return; }
        _malBusy = true;
        try
        {
            Say("Opening MyAnimeList in your browser - log in and press Allow...");
            bool ok = await _mal.LoginAsync();
            if (ok)
            {
                _profileName = null; _profileChip.Child = null;
                await LoadProfileAsync();
                Say("MyAnimeList connected" + (_profileName != null ? " as " + _profileName : "") + ".");
                Persist();
                await RefreshNetAsync();
                if (Pending > 0) await AutoSyncAsync();
            }
            else Say("MAL login failed: " + (string.IsNullOrEmpty(_mal.LastError) ? "cancelled." : _mal.LastError));
        }
        catch (Exception ex) { Say("MAL login failed: " + ex.Message); }
        finally { _malBusy = false; UpdateProfileCard(); }
    }

    void MalLogout()
    {
        _mal.Logout();
        _profileName = null; _profileChip.Child = null;
        Persist(); UpdateProfileCard();
        Say("Logged out of MyAnimeList. Your watched progress stays saved here and uploads after the next login.");
    }

    /// <summary>The paste-the-address fallback of the login (when the local redirect listener cannot start). A proper dialog with the instructions
    /// inside it - the first version put the whole text into the window title and showed an unlabeled box.</summary>
    string? AskPaste(string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = CText, Margin = new Thickness(14, 14, 14, 10) };
        var tb = new TextBox { Margin = new Thickness(14, 0, 14, 10), MinHeight = 34 };
        var ok = new Button { Content = "OK", Margin = new Thickness(14, 0, 14, 14), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        var w = new Window { Title = "MyAnimeList login", Width = 540, SizeToContent = SizeToContent.Height, Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = CSurface, Foreground = CText };
        var sp = new StackPanel(); sp.Children.Add(text); sp.Children.Add(tb); sp.Children.Add(ok); w.Content = sp;
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => tb.Focus();
        return w.ShowDialog() == true ? tb.Text.Trim() : null;
    }

    // ------------------------------------------------------------------ top area: tabs | actions, then search / sort / views
    UIElement BuildTabs()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var pill = new Border { Background = CCard, BorderBrush = CLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(3), Child = host, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (tab, label) in TabDefs)
        {
            var lbl = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var cnt = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var cntBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), Child = cnt };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(lbl); row.Children.Add(cntBox);
            var box = new Border { Child = row, CornerRadius = new CornerRadius(9), Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var t = tab;
            box.MouseLeftButtonUp += (_, _) =>
            {
                if (CurrentTab == t) return;
                _s.StatusTab = (int)t; Persist(); _animate = true; Render();
            };
            _tabUi[tab] = (box, lbl, cnt);
            host.Children.Add(box);
        }
        return pill;
    }

    void UpdateTabs()
    {
        foreach (var kv in _tabUi)
        {
            bool on = kv.Key == CurrentTab;
            kv.Value.Count.Text = _entries.Count(x => InTab(x, kv.Key)).ToString();
            kv.Value.Box.Background = on ? _accent : Brushes.Transparent;
            kv.Value.Label.Foreground = on ? Brushes.White : CText;
            kv.Value.Count.Foreground = on ? Brushes.White : CMuted;
            if (kv.Value.Count.Parent is Border cb) cb.Background = on ? Br("#33FFFFFF") : Br("#1AFFFFFF");
        }
    }

    UIElement BuildTopArea()
    {
        var top = new StackPanel { Margin = new Thickness(28, 14, 28, 6) };

        // row A: tabs (Library) or the page title | actions
        _tabsPill = BuildTabs();
        var rowA = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Chip("＋ Folder", AddFolder));
        actions.Children.Add(Chip("＋ Custom", AddCustom));
        actions.Children.Add(Chip("⟳ Scan folders", () => _ = ScanAsync()));
        actions.Children.Add(ChipAsync(_syncText, async () => await SyncAsync(true), true));
        DockPanel.SetDock(actions, Dock.Right);
        rowA.Children.Add(actions);
        var leftHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _title.Margin = new Thickness(0);
        leftHost.Children.Add(_tabsPill); leftHost.Children.Add(_title);
        rowA.Children.Add(leftHost);
        top.Children.Add(rowA);

        // row B: search, sort, shuffle, scan, view modes
        var rowB = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        _filterRow = rowB;
        var left = new StackPanel { Orientation = Orientation.Horizontal };

        _search.Width = double.NaN; _search.Padding = new Thickness(34, 7, 10, 7); _search.ToolTip = "Filter by title (Ctrl+K)";
        var searchHost = new Grid { Width = 300 };
        searchHost.Children.Add(_search);
        var mag = Icons.Make("Search", CMuted, 16);
        mag.HorizontalAlignment = HorizontalAlignment.Left; mag.VerticalAlignment = VerticalAlignment.Center; mag.Margin = new Thickness(11, 0, 0, 0);
        searchHost.Children.Add(mag);
        left.Children.Add(searchHost);

        _sortBox = new ComboBox { Width = 170, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (string n in SortNames) _sortBox.Items.Add(n);
        _sortBox.SelectedIndex = Math.Clamp((int)_s.Sort, 0, SortNames.Length - 1);
        _sortBox.SelectionChanged += (_, _) =>
        {
            if (_sortBox.SelectedIndex < 0 || (int)_s.Sort == _sortBox.SelectedIndex) return;
            _s.Sort = (SortMode)_sortBox.SelectedIndex; Persist(); _animate = true; Render();
        };
        _sortBox.ToolTip = "Sort order";
        left.Children.Add(_sortBox);

        left.Children.Add(IconButton("Shuffle", "Open a random title from this list", PickRandom));
        left.Children.Add(IconButton("Refresh", "Scan the folders now", () => _ = ScanAsync()));

        var views = new StackPanel { Orientation = Orientation.Horizontal };
        views.Children.Add(ViewButton("Grid", LibView.Posters, "Posters"));
        views.Children.Add(ViewButton("List", LibView.List, "Detailed list"));
        views.Children.Add(ViewButton("Compact", LibView.Compact, "Compact list"));
        var viewPill = new Border { Background = CCard, BorderBrush = CLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Margin = new Thickness(10, 0, 0, 0), Child = views };
        left.Children.Add(viewPill);

        DockPanel.SetDock(left, Dock.Left);
        rowB.Children.Add(left);
        top.Children.Add(rowB);
        return top;
    }

    Border IconButton(string icon, string tip, Action act)
    {
        var b = new Border
        {
            Width = 36, Height = 34, CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = tip,
            Background = CCard, BorderBrush = CLine, BorderThickness = new Thickness(1), Child = Icons.Make(icon, CText, 17)
        };
        b.MouseEnter += (_, _) => b.Background = CHover;
        b.MouseLeave += (_, _) => b.Background = CCard;
        b.MouseLeftButtonUp += (_, _) => { try { act(); } catch (Exception ex) { Say("Error: " + ex.Message); } };
        return b;
    }

    /// <summary>Shows the tabs (Library) or a plain page title, and the search / sort / view row, for the page that is about to be drawn.</summary>
    void SetChrome(bool tabs, bool filters, string? title)
    {
        _tabsPill.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        _title.Visibility = tabs ? Visibility.Collapsed : Visibility.Visible;
        if (title != null) _title.Text = title;
        _filterRow.Visibility = filters ? Visibility.Visible : Visibility.Collapsed;
    }

    Border SideToggleButton()
    {
        var b = new Border { Width = 40, Height = 38, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Hide / show the sidebar (Ctrl+B)", Child = Icons.Make("Panel", CText, 18) };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => b.Background = CHover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => ToggleSidebar();
        return b;
    }

    // ------------------------------------------------------------------ lists
    IEnumerable<Entry> SearchFiltered()
    {
        string q = _search.Text.Trim();
        return _entries.Where(e => q.Length == 0
            || e.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (e.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    void PickRandom()
    {
        List<Entry> pool = _view switch
        {
            View.Unmatched => SearchFiltered().Where(e => !e.Matched && !e.Custom).ToList(),
            View.Pending => SearchFiltered().Where(e => e.NeedsSync).ToList(),
            _ => SearchFiltered().Where(e => InTab(e, CurrentTab)).ToList()
        };
        if (pool.Count == 0) { Say("Nothing to pick from in this list."); return; }
        OpenDetail(pool[Random.Shared.Next(pool.Count)]);
    }

    const int CardBatch = 60;

    /// <summary>One grid cell: a series, or a franchise folder that holds several series.</summary>
    sealed record Coll(string Key, string Name, List<Entry> Members);

    void AddCards(IList<Entry> list) => AddCards(list.Cast<object>().ToList());

    /// <summary>A poster grid that builds its cards in batches while you scroll (a few thousand series would otherwise freeze the window).</summary>
    void AddCards(IList<object> list)
    {
        _shown += list.Count;
        var wrap = new WrapPanel();
        _page.Children.Add(wrap);
        int next = 0;
        bool More(int n)
        {
            int start = next, end = Math.Min(list.Count, next + n);
            for (; next < end; next++) wrap.Children.Add(list[next] is Coll c ? MakeCollectionCard(c, _s.TileSize) : MakeCard((Entry)list[next], _s.TileSize));
            _builtCards += end - start;
            return next < list.Count;
        }
        if (More(Math.Max(CardBatch * 2, _keepCards))) _lazy = () => { _anim = false; return More(CardBatch); };
    }

    // ------------------------------------------------------------------ franchise folders
    /// <summary>"Library folder | first folder below it" for a series that lives inside a sub folder (Monogatari/Bakemonogatari -> "...|Monogatari"); null for loose series.</summary>
    string? CollectionKey(Entry e)
    {
        if (!string.IsNullOrEmpty(e.Group)) return "group|" + e.Group;   // a folder the user made (Group into a folder...) beats the disk layout
        if (string.IsNullOrEmpty(e.FolderPath)) return null;
        foreach (var f in _s.Folders)
        {
            string root = f.Path.TrimEnd('\\', '/');
            if (root.Length == 0 || !Scanner.IsUnder(e.FolderPath, root) || e.FolderPath.TrimEnd('\\', '/').Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = e.FolderPath[root.Length..].TrimStart('\\', '/');
            string first = rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return first.Length == 0 ? null : root + "|" + first;
        }
        return null;
    }

    static string CollectionName(string key)
    {
        string n = key[(key.LastIndexOf('|') + 1)..].Replace('_', ' ').Replace('.', ' ').Trim();
        return n.Length == 0 ? "Folder" : n;
    }

    /// <summary>Series that share a folder (2 or more in the list) become ONE card with the picture of the first series inside; a click opens the folder.</summary>
    List<object> GroupCollections(List<Entry> list)
    {
        var groups = list.Where(e => !e.Custom).GroupBy(CollectionKey).Where(g => g.Key != null && g.Count() >= 2).ToDictionary(g => g.Key!, g => g.ToList());
        var items = new List<object>(); var seen = new HashSet<string>();
        foreach (var e in list)
        {
            string? k = e.Custom ? null : CollectionKey(e);
            if (k != null && groups.TryGetValue(k, out var members)) { if (seen.Add(k)) items.Add(new Coll(k, CollectionName(k), members)); }
            else items.Add(e);
        }
        return items;
    }

    void RenderCollection()
    {
        var members = _entries.Where(e => !e.Custom && CollectionKey(e) == _collection).OrderBy(e => e.FolderPath, Scanner.NaturalComparer.I).ThenBy(e => e.DisplayTitle, StringComparer.OrdinalIgnoreCase).ToList();
        if (members.Count == 0) { _collection = null; Render(); return; }
        var back = Chip("←  Library", () => { _collection = null; _animate = true; Render(); });
        back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(0, 4, 0, 12);
        _page.Children.Add(back);
        _page.Children.Add(Header(CollectionName(_collection!), members.Count, "Series in this folder - click one to see its episodes. (Esc goes back)"));
        AddCards(members);
    }

    UIElement MakeCollectionCard(Coll c, int w)
    {
        double h = w * 1.5;
        var ordered = c.Members.OrderBy(m => m.FolderPath, Scanner.NaturalComparer.I).ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase).ToList();
        var first = ordered.FirstOrDefault(m => !string.IsNullOrEmpty(m.CoverFile) && File.Exists(m.CoverFile)) ?? ordered[0];     // cover of the first series inside
        var c0 = AccentColor;
        var ringBrush = new SolidColorBrush(Color.FromArgb(0, c0.R, c0.G, c0.B));
        var scale = new ScaleTransform(1, 1);
        var ring = new Border
        {
            CornerRadius = new CornerRadius(_s.CardRadius + 3), BorderThickness = new Thickness(2), BorderBrush = ringBrush, Padding = new Thickness(2),
            Child = Poster(first, w, h, false), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale, Margin = new Thickness(0, 12, 0, 0)
        };
        // two sheets behind the poster = "this is a folder of several series"
        var stack = new Grid { Width = w + 8 };
        stack.Children.Add(new Border { Height = h, Width = w - 28, Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(_s.CardRadius), Background = Br("#2B2B2B"), Opacity = 0.7 });
        stack.Children.Add(new Border { Height = h, Width = w - 14, Margin = new Thickness(0, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(_s.CardRadius), Background = Br("#383838"), Opacity = 0.85 });
        stack.Children.Add(ring);

        var sp = new StackPanel { Width = w + 8 };
        sp.Children.Add(stack);

        int total = c.Members.Sum(m => m.ShownTotal > 0 ? m.ShownTotal : m.Episodes), watched = c.Members.Sum(m => Math.Min(m.Watched, m.ShownTotal > 0 ? m.ShownTotal : m.Episodes));
        if (_s.ShowProgress && total > 0 && watched > 0)
            sp.Children.Add(new ProgressBar { Height = 4, Maximum = total, Value = watched, Foreground = c.Members.All(Completed) ? Br("#1FA971") : _accent, Background = Br("#26FFFFFF"), BorderThickness = new Thickness(0), Margin = new Thickness(4, 6, 4, 0) });
        sp.Children.Add(new TextBlock { Text = c.Name, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 7, 4, 0), ToolTip = c.Name });
        sp.Children.Add(new TextBlock { Text = c.Members.Count + " series  ·  " + watched + " / " + total + " watched", Foreground = CMuted, FontSize = 11.5, Margin = new Thickness(4, 4, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (_s.ShowIndicators)
        {
            var chips = new WrapPanel { Margin = new Thickness(4, 6, 4, 0) };
            chips.Children.Add(MiniChip("Folder", "#336152DF", "#A99BFF", "Several series live in this folder"));
            if (c.Members.All(Completed)) chips.Children.Add(MiniChip("Completed", "#331FA971", "#4ADE80", "Everything in this folder is watched"));
            else if (c.Members.Any(m => m.Watched > 0)) chips.Children.Add(MiniChip("Watching", "#2622C55E", "#86EFAC", "Started"));
            sp.Children.Add(chips);
        }

        var slide = new TranslateTransform(0, 0);
        var card = new Border { Margin = new Thickness(0, 0, 14, 20), Child = sp, Cursor = Cursors.Hand, Background = Brushes.Transparent, RenderTransform = slide };
        card.MouseEnter += (_, _) => Hover(scale, ringBrush, true);
        card.MouseLeave += (_, _) => Hover(scale, ringBrush, false);
        card.MouseLeftButtonUp += (_, _) => { _collection = c.Key; _animate = true; Render(); };
        Reveal(card, slide, _cardIndex++);
        return card;
    }

    void HookLazyScroll()
    {
        _scroll.ScrollChanged += (_, a) =>
        {
            if (_lazy == null) return;
            if (a.VerticalChange == 0 && a.ExtentHeightChange == 0 && a.ViewportHeightChange == 0) return;
            if (_scroll.VerticalOffset + _scroll.ViewportHeight > _scroll.ExtentHeight - 700 && !_lazy()) _lazy = null;
        };
    }

    /// <summary>On a big window the first batch may not fill the screen: keep adding until there is something to scroll.</summary>
    void FillViewport()
    {
        for (int i = 0; i < 40 && _lazy != null; i++)
        {
            _scroll.UpdateLayout();
            if (_scroll.ExtentHeight > _scroll.ViewportHeight + 500) break;
            if (!_lazy()) _lazy = null;
        }
    }
}
