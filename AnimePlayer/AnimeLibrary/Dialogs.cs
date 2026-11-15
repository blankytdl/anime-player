using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeLibrary;

public partial class MainWindow
{
    static TextBlock Lbl(string t) => new() { Text = t, Margin = new Thickness(0, 8, 0, 2), FontWeight = FontWeights.SemiBold };

    static StackPanel PathRow(TextBox box, Action browse)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        box.Width = 360;
        var b = new Button { Content = "Browse...", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        b.Click += (_, _) => browse();
        row.Children.Add(box); row.Children.Add(b);
        return row;
    }


    // ------------------------------------------------------------------ library folders
    void ShowFolders()
    {
        var w = new Window { Background = CSurface, Foreground = CText, Title = "Library folders", Width = 780, Height = 460, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Margin = new Thickness(18) };

        void Rebuild()
        {
            list.Children.Clear();
            list.Children.Add(new TextBlock { Text = "Library folders", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            list.Children.Add(new TextBlock { Text = "Every video inside is scanned (sub-folders too); titles come from the file names. Choose where titles are looked up for each folder.", Foreground = CMuted, TextWrapping = TextWrapping.Wrap });
            if (_s.Folders.Count == 0) list.Children.Add(new TextBlock { Text = "No folders yet.", Foreground = CMuted, Margin = new Thickness(0, 12, 0, 0) });
            foreach (var f in _s.Folders.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                var rm = new Button { Content = "Remove", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
                rm.Click += (_, _) => { _s.Folders.Remove(f); Persist(); Rebuild(); };

                var ext = new ComboBox { Width = 150, Margin = new Thickness(8, 0, 0, 0), DisplayMemberPath = "Name" };
                foreach (var m in Extensions.Installed()) ext.Items.Add(m);
                ext.SelectedItem = ext.Items.OfType<ExtensionManifest>().FirstOrDefault(m => m.Id == f.ExtensionId);
                ext.Visibility = f.Source == Source.Extension ? Visibility.Visible : Visibility.Collapsed;
                ext.SelectionChanged += (_, _) => { f.ExtensionId = (ext.SelectedItem as ExtensionManifest)?.Id; Persist(); };

                var src = new ComboBox { Width = 130, Margin = new Thickness(8, 0, 0, 0) };
                foreach (var n in Enum.GetNames<Source>()) src.Items.Add(n);
                src.SelectedIndex = (int)f.Source;
                src.SelectionChanged += (_, _) =>
                {
                    if (src.SelectedIndex < 0) return;
                    f.Source = (Source)src.SelectedIndex;
                    ext.Visibility = f.Source == Source.Extension ? Visibility.Visible : Visibility.Collapsed;
                    Persist();
                };

                DockPanel.SetDock(rm, Dock.Right); DockPanel.SetDock(ext, Dock.Right); DockPanel.SetDock(src, Dock.Right);
                row.Children.Add(rm); row.Children.Add(ext); row.Children.Add(src);
                row.Children.Add(new TextBlock { Text = f.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f.Path });
                list.Children.Add(row);
            }

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var add = new Button { Content = "＋ Add folder", Padding = new Thickness(14, 5, 14, 5) };
            add.Click += (_, _) => { AddFolder(); Rebuild(); };
            var scan = new Button { Content = "⟳ Scan now", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
            scan.Click += async (_, _) => { w.Close(); try { await ScanAsync(); } catch (Exception ex) { Say("Scan failed: " + ex.Message); } };
            bar.Children.Add(add); bar.Children.Add(scan);
            list.Children.Add(bar);
        }

        Rebuild();
        w.Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.ShowDialog();
    }

    // ------------------------------------------------------------------ settings
    void ShowSettings()
    {
        var w = new Window { Background = CBg, Foreground = CText, Title = "Settings", Width = 660, Height = 800, MinWidth = 520, MinHeight = 420, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });

        StackPanel Card(string title, string? note = null)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
            if (note != null) body.Children.Add(Note(note));
            root.Children.Add(new Border { Background = CSurface, BorderBrush = CLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = body });
            return body;
        }
        Slider Sl(StackPanel p, string label, double min, double max, double val, Func<double, string> fmt)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var lt = new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center };
            var vt = new TextBlock { Foreground = CMuted, Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var sl = new Slider { Minimum = min, Maximum = max, Value = val, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            sl.ValueChanged += (_, _) => vt.Text = fmt(sl.Value);
            vt.Text = fmt(val);
            DockPanel.SetDock(lt, Dock.Left); DockPanel.SetDock(vt, Dock.Right);
            row.Children.Add(lt); row.Children.Add(vt); row.Children.Add(sl);
            p.Children.Add(row);
            return sl;
        }
        CheckBox Cb(StackPanel p, string text, bool on) { var c = new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 7, 0, 0) }; p.Children.Add(c); return c; }
        ComboBox Combo(StackPanel p, string label, IEnumerable<string> items, int index, double width = 200)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var lt = new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(lt, Dock.Left);
            var cb = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (string i in items) cb.Items.Add(i);
            cb.SelectedIndex = Math.Max(0, index);
            row.Children.Add(lt); row.Children.Add(cb); p.Children.Add(row);
            return cb;
        }

        // ---------------------------------------------------------------- player
        var pl = Card("Player", "Used by Play. AnimePlayer is your mpv.net build and also reports what you watched back to this library.");
        var player = Combo(pl, "Player", new[] { "AnimePlayer (mpv.net build)", "mpv", "VLC", "MPC-HC", "Custom exe" }, (int)_s.Player, 260);
        var exe = new TextBox { Text = _s.PlayerExe };
        pl.Children.Add(PathRow(exe, () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe" };
            if (d.ShowDialog() == true) exe.Text = d.FileName;
        }));
        var found = new TextBlock { Foreground = CMuted, Margin = new Thickness(0, 4, 0, 0), FontSize = 12 };
        void ShowFound()
        {
            var tmp = new Settings { Player = (PlayerKind)Math.Max(0, player.SelectedIndex), PlayerExe = exe.Text };
            found.Text = Players.Find(tmp) is { } p ? "Found: " + p : "Not found - choose the exe above.";
        }
        player.SelectionChanged += (_, _) => ShowFound(); exe.TextChanged += (_, _) => ShowFound(); ShowFound();
        pl.Children.Add(found);

        // ---------------------------------------------------------------- tracking
        var tr = Card("Tracking");
        tr.Children.Add(Lbl("MyAnimeList Client ID (myanimelist.net/apiconfig, redirect URL http://localhost:8765/cb)"));
        var clientId = new TextBox { Text = _s.MalClientId };
        tr.Children.Add(clientId);
        tr.Children.Add(Lbl("Client Secret (only if your MAL app type is \"web\"; leave empty for type \"other\")"));
        var clientSecret = new TextBox { Text = _s.MalClientSecret };
        tr.Children.Add(clientSecret);
        tr.Children.Add(Lbl("Redirect URL (must match your MAL app exactly; empty = http://localhost:8765/cb)"));
        var redirectUrl = new TextBox { Text = _s.MalRedirect };
        tr.Children.Add(redirectUrl);
        var noRedirect = new CheckBox { Content = "Do not send a redirect URL (use the one registered on myanimelist.net/apiconfig) - tick this if MyAnimeList shows \"400 Bad Request\" after you press Allow", IsChecked = _s.MalNoRedirect, Margin = new Thickness(0, 8, 0, 0), Foreground = CText };
        noRedirect.Checked += (_, _) => { _s.MalNoRedirect = true; Persist(); };
        noRedirect.Unchecked += (_, _) => { _s.MalNoRedirect = false; Persist(); };
        tr.Children.Add(noRedirect);
        tr.Children.Add(Note("The redirect URL of your app on MyAnimeList must be exactly http://localhost:8765/cb (no trailing slash, http not https) unless you typed another one above. App type: other."));
        // login state + the three things that were missing: log in here, test the connection, log out
        var malState = new TextBlock { Foreground = CMuted, Margin = new Thickness(0, 10, 0, 0), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void ShowMalState() => malState.Text = _mal.NeedsLogin ? "Login expired - press Log in to connect again."
            : _mal.LoggedIn ? "Logged in" + (string.IsNullOrEmpty(_s.MalUser) ? "." : " as " + _s.MalUser + ".") : "Not logged in.";
        ShowMalState();
        tr.Children.Add(malState);
        void ApplyMalFields() { _s.MalClientId = clientId.Text.Trim(); _s.MalClientSecret = clientSecret.Text.Trim(); _s.MalRedirect = redirectUrl.Text.Trim(); Persist(); }
        Button MalBtn(string text, Func<Task> act)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
            b.Click += async (_, _) => { try { ApplyMalFields(); await act(); } catch (Exception ex) { malState.Text = "Error: " + ex.Message; } };
            return b;
        }
        var malRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
        malRow.Children.Add(MalBtn("Log in", async () => { malState.Text = "Waiting for the browser..."; await MalLoginAsync(); ShowMalState(); if (!string.IsNullOrEmpty(_mal.LastError) && !_mal.LoggedIn) malState.Text = _mal.LastError; }));
        malRow.Children.Add(MalBtn("Test connection", async () => { malState.Text = "Testing..."; malState.Text = await _mal.TestAsync(); UpdateProfileCard(); }));
        malRow.Children.Add(MalBtn("Log out", () => { MalLogout(); ShowMalState(); return Task.CompletedTask; }));
        tr.Children.Add(malRow);
        var auto = Cb(tr, "Upload watched episodes automatically when the internet is back", _s.AutoSyncOnline);
        tr.Children.Add(Lbl("Player watch log (written by AnimePlayer, read offline)"));
        var log = new TextBox { Text = _s.WatchLog };
        tr.Children.Add(PathRow(log, () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Log|*.log;*.txt|All|*.*" };
            if (d.ShowDialog() == true) log.Text = d.FileName;
        }));

        // ---------------------------------------------------------------- covers
        var cv = Card("Covers", "Every series gets a picture: AniList, MyAnimeList, Kitsu - and as a last resort a frame cut from its first episode (needs ffmpeg.exe next to this app).");
        var dl = Cb(cv, "Download covers into the anime folders (cover.jpg)", _s.DownloadCovers);
        var ico = Cb(cv, "Use the cover as the folder icon in Explorer", _s.SetFolderIcon);

        // ---------------------------------------------------------------- appearance
        var ap = Card("Appearance");
        var theme = Combo(ap, "Colour theme", Themes.Keys, Themes.Keys.ToList().IndexOf(Themes.ContainsKey(_s.Theme) ? _s.Theme : "Seanime"), 160);
        var accentRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var accentLbl = new TextBlock { Text = "Accent colour", Width = 170, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(accentLbl, Dock.Left);
        var accent = new TextBox { Text = _s.Accent, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        accentRow.Children.Add(accentLbl); accentRow.Children.Add(accent);
        ap.Children.Add(accentRow);
        var swatches = new WrapPanel { Margin = new Thickness(170, 8, 0, 4) };
        foreach (string hex in Themes.Values.Where(v => v.Length > 0).Concat(new[] { "#9B6BFF", "#00B8D9", "#FF8A3D", "#A3E635", "#D4D4D8" }))
        {
            var sw = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Br(hex), Margin = new Thickness(0, 0, 8, 6), Cursor = System.Windows.Input.Cursors.Hand, BorderBrush = Br("#44FFFFFF"), BorderThickness = new Thickness(1), ToolTip = hex };
            string h = hex;
            sw.MouseLeftButtonUp += (_, _) => { accent.Text = h; theme.SelectedItem = Themes.FirstOrDefault(kv => kv.Value == h).Key ?? "Custom"; };
            swatches.Children.Add(sw);
        }
        ap.Children.Add(swatches);
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is string t && Themes[t].Length > 0) accent.Text = Themes[t]; };
        var surface = Combo(ap, "Surface (needs a restart)", Surfaces.Keys, Surfaces.Keys.ToList().IndexOf(Surfaces.ContainsKey(_s.Surface) ? _s.Surface : "Dark"), 160);
        var tile = Sl(ap, "Poster size", 120, 300, _s.TileSize, v => v + " px");
        var radius = Sl(ap, "Corner roundness", 0, 24, _s.CardRadius, v => v + " px");
        var showTitles = Cb(ap, "Show titles under posters", _s.ShowTitles);
        var indicators = Cb(ap, "Show indicators under posters (Completed / Airing / Ended / Movie / Watched)", _s.ShowIndicators);
        var prog = Cb(ap, "Show watch progress ring", _s.ShowProgress);
        var anim = Cb(ap, "Animations (fade / slide-in / hover)", _s.Animations);

        // ---------------------------------------------------------------- window
        var wn = Card("Window frame", "The window has its own title bar and a thin border. Drag the title bar to move, drag any edge to resize, snap to screen edges with Win+Arrows.");
        var border = Sl(wn, "Border thickness", 0, 6, _s.BorderSize, v => v == 0 ? "off" : v + " px");
        wn.Children.Add(Lbl("Border colour (empty = accent colour, e.g. #FF8A3D)"));
        var bcol = new TextBox { Text = _s.BorderColor, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        wn.Children.Add(bcol);

        // ---------------------------------------------------------------- background
        var bgc = Card("Background", "A picture behind the whole library. Darkening keeps text readable; blur softens busy pictures.");
        var bg = new TextBox { Text = _s.BackgroundImage };
        var bgRow = PathRow(bg, () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" };
            if (d.ShowDialog() == true) bg.Text = d.FileName;
        });
        var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        clear.Click += (_, _) => bg.Text = "";
        bgRow.Children.Add(clear);
        bgc.Children.Add(bgRow);
        var dim = Sl(bgc, "Darkening", 0, 95, _s.BackgroundDim, v => v + " %");
        var blur = Sl(bgc, "Blur", 0, 40, _s.BackgroundBlur, v => v == 0 ? "off" : v + " px");
        var fit = Combo(bgc, "Picture fit", new[] { "Fill", "Fit", "Stretch" }, Math.Max(0, new[] { "Fill", "Fit", "Stretch" }.ToList().IndexOf(_s.BackgroundFit)), 140);

        // ---------------------------------------------------------------- layout
        var ly = Card("Library layout");
        var sort = Combo(ly, "Sort by", SortNames, (int)_s.Sort, 200);
        var view = Combo(ly, "Library view", new[] { "Posters", "Detailed list", "Compact list" }, (int)_s.View, 200);
        var dview = Combo(ly, "Series page view", new[] { "Thumbnails", "Detailed list", "Compact list" }, (int)_s.DetailMode, 200);

        // ---------------------------------------------------------------- smart names + extensions
        var sn = Card("Smart names", "Rules created with right-click > Smart name / Merge / Local only. Remove a rule here to undo it.");
        var rules = new Button { Content = $"Manage smart names ({_s.NameRules.Count})…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 6, 14, 6) };
        rules.Click += (_, _) => ShowRules();
        sn.Children.Add(rules);
        var mk = Card("Extensions");
        mk.Children.Add(Lbl("Marketplace address (JSON list)"));
        var market = new TextBox { Text = _s.MarketplaceUrl };
        mk.Children.Add(market);

        // ---------------------------------------------------------------- apply
        bool Commit()
        {
            string a = accent.Text.Trim();
            try { _ = new BrushConverter().ConvertFromString(a); }
            catch { MessageBox.Show(w, "The accent colour is not valid (use e.g. #6152DF).", "Settings"); return false; }
            string bc = bcol.Text.Trim();
            if (bc.Length > 0) { try { _ = new BrushConverter().ConvertFromString(bc); } catch { MessageBox.Show(w, "The border colour is not valid.", "Settings"); return false; } }

            _s.Player = (PlayerKind)Math.Max(0, player.SelectedIndex);
            _s.PlayerExe = exe.Text.Trim();
            _s.MalClientId = clientId.Text.Trim(); _s.MalClientSecret = clientSecret.Text.Trim(); _s.MalRedirect = redirectUrl.Text.Trim();
            _s.AutoSyncOnline = auto.IsChecked == true;
            _s.DownloadCovers = dl.IsChecked == true;
            _s.SetFolderIcon = ico.IsChecked == true;
            _s.Theme = theme.SelectedItem as string ?? "Seanime";
            _s.Accent = a;
            bool surfaceChanged = surface.SelectedItem as string != _s.Surface;
            _s.Surface = surface.SelectedItem as string ?? "Dark";
            _s.TileSize = (int)tile.Value; _s.CardRadius = (int)radius.Value;
            _s.ShowTitles = showTitles.IsChecked == true; _s.ShowIndicators = indicators.IsChecked == true; _s.ShowProgress = prog.IsChecked == true;
            _s.Animations = anim.IsChecked == true;
            _s.BorderSize = (int)border.Value; _s.BorderColor = bc;
            _s.BackgroundImage = bg.Text.Trim(); _s.BackgroundDim = (int)dim.Value; _s.BackgroundBlur = (int)blur.Value;
            _s.BackgroundFit = fit.SelectedItem as string ?? "Fill";
            _s.Sort = (SortMode)Math.Max(0, sort.SelectedIndex);
            _s.View = (LibView)Math.Max(0, view.SelectedIndex);
            _s.DetailMode = (DetailView)Math.Max(0, dview.SelectedIndex);
            _s.MarketplaceUrl = market.Text.Trim();
            if (!string.Equals(_s.WatchLog, log.Text.Trim(), StringComparison.OrdinalIgnoreCase)) { _s.WatchLog = log.Text.Trim(); _s.WatchLogOffset = 0; }
            Persist(); ApplyAll();
            Say(surfaceChanged ? "Settings applied. The new surface colours appear after you restart the library." : "Settings applied.");
            return true;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 8) };
        var apply = new Button { Content = "Apply", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(22, 6, 22, 6) };
        apply.Click += (_, _) => Commit();
        var ok = new Button { Content = "Save & close", IsDefault = true, Padding = new Thickness(22, 6, 22, 6) };
        ok.Click += (_, _) => { if (Commit()) w.DialogResult = true; };
        buttons.Children.Add(apply); buttons.Children.Add(ok);
        root.Children.Add(buttons);
        w.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.ShowDialog();
    }

    void ShowRules()
    {
        var w = NewDialog("Smart names", 620);
        w.SizeToContent = SizeToContent.Manual; w.Height = 520; w.ResizeMode = ResizeMode.CanResize; w.MinHeight = 320;
        var body = new StackPanel { Margin = new Thickness(18) };
        void Rebuild()
        {
            body.Children.Clear();
            body.Children.Add(new TextBlock { Text = "Smart names", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            body.Children.Add(Note("Files matching a rule are filed under the chosen name. Removing a rule restores the automatic name after the next scan."));
            if (_s.NameRules.Count == 0) body.Children.Add(new TextBlock { Text = "No rules yet.", Foreground = CMuted, Margin = new Thickness(0, 12, 0, 0) });
            foreach (var r in _s.NameRules.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var rm = new Button { Content = "Remove", Padding = new Thickness(10, 3, 10, 3) };
                DockPanel.SetDock(rm, Dock.Right);
                var rr = r;
                rm.Click += (_, _) => { _s.NameRules.Remove(rr); Persist(); Rebuild(); _ = ScanAsync(); };
                row.Children.Add(rm);
                row.Children.Add(new TextBlock { Text = $"{r.Match}  →  {r.Name}" + (r.LocalOnly ? "   [local only]" : ""), Foreground = CText, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                body.Children.Add(row);
            }
        }
        Rebuild();
        w.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.ShowDialog();
    }

    // ------------------------------------------------------------------ custom source (non-AniList anime / manga)
    void AddCustom()
    {
        var w = new Window { Background = CSurface, Foreground = CText, Title = "Add custom entry", Width = 540, SizeToContent = SizeToContent.Height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var title = new TextBox { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        var kind = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        kind.Items.Add("Anime (videos)"); kind.Items.Add("Manga (images / cbz)"); kind.SelectedIndex = 0;
        var folder = new TextBox();
        var cover = new TextBox();
        var total = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Text = "For series that are not on AniList / MyAnimeList (or manga). Never matched online.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(Lbl("Title")); sp.Children.Add(title);
        sp.Children.Add(Lbl("Type")); sp.Children.Add(kind);
        sp.Children.Add(Lbl("Folder"));
        sp.Children.Add(PathRow(folder, () => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog() == true) folder.Text = d.FolderName; }));
        sp.Children.Add(Lbl("Cover image (optional)"));
        sp.Children.Add(PathRow(cover, () => { var d = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp" }; if (d.ShowDialog() == true) cover.Text = d.FileName; }));
        sp.Children.Add(Lbl("Total episodes / chapters (optional)")); sp.Children.Add(total);
        var ok = new Button { Content = "Add", IsDefault = true, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(24, 6, 24, 6), HorizontalAlignment = HorizontalAlignment.Left };
        ok.Click += (_, _) =>
        {
            string t = title.Text.Trim();
            if (t.Length == 0 || !Directory.Exists(folder.Text.Trim())) { MessageBox.Show(w, "Enter a title and choose an existing folder.", "Custom entry"); return; }
            var e = new Entry
            {
                Key = "custom:" + NameParser.KeyOf(t), Title = t, RawName = t, FolderPath = folder.Text.Trim(), Custom = true, Matched = true,
                Kind = kind.SelectedIndex == 1 ? Kind.Manga : Kind.Anime
            };
            if (_entries.Any(x => x.Key == e.Key)) { MessageBox.Show(w, "A custom entry with that title exists already.", "Custom entry"); return; }
            if (int.TryParse(total.Text, out int n) && n > 0) e.TotalEpisodes = n;
            if (e.Kind == Kind.Anime)
                e.Episodes = Directory.EnumerateFiles(e.FolderPath, "*", SearchOption.AllDirectories).Count(Scanner.IsVideo);
            if (File.Exists(cover.Text.Trim())) CoverTools.Assign(e, cover.Text.Trim());
            else if (File.Exists(System.IO.Path.Combine(e.FolderPath, "cover.jpg"))) e.CoverFile = System.IO.Path.Combine(e.FolderPath, "cover.jpg");
            _entries.Add(e);
            if (e.CoverFile != null && _s.SetFolderIcon) e.IconSet = CoverTools.SetFolderIcon(e.FolderPath, e.CoverFile);
            Persist(); Render(); UpdateSyncButton();
            w.DialogResult = true;
        };
        sp.Children.Add(ok);
        w.Content = sp;
        w.ShowDialog();
    }

    // ------------------------------------------------------------------ extension marketplace
    void ShowExtensions()
    {
        var w = new Window { Background = CSurface, Foreground = CText, Title = "Extensions", Width = 640, Height = 640, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Margin = new Thickness(14) };
        var info = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        List<ExtensionManifest>? market = null;

        void Rebuild()
        {
            list.Children.Clear();
            list.Children.Add(new TextBlock { Text = "Installed", FontSize = 16, FontWeight = FontWeights.Bold });
            var installed = Extensions.Installed();
            if (installed.Count == 0) list.Children.Add(new TextBlock { Text = "Nothing installed yet.", Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
            foreach (var m in installed)
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var rm = new Button { Content = "Remove", Padding = new Thickness(8, 2, 8, 2) };
                rm.Click += (_, _) => { Extensions.Remove(m.Id); _s.DisabledExtensions.Remove(m.Id); Persist(); Rebuild(); };
                DockPanel.SetDock(rm, Dock.Right);
                var en = new CheckBox { Content = $"{m.Name}  v{m.Version}", IsChecked = !_s.DisabledExtensions.Contains(m.Id), VerticalAlignment = VerticalAlignment.Center };
                en.Click += (_, _) => { if (en.IsChecked == true) _s.DisabledExtensions.Remove(m.Id); else if (!_s.DisabledExtensions.Contains(m.Id)) _s.DisabledExtensions.Add(m.Id); Persist(); };
                row.Children.Add(rm); row.Children.Add(en);
                list.Children.Add(row);
                if (m.Description.Length > 0) list.Children.Add(new TextBlock { Text = m.Description, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 0, 0) });
            }

            var head = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
            var refresh = new Button { Content = "Refresh", Padding = new Thickness(8, 2, 8, 2) };
            refresh.Click += async (_, _) => await Load();
            DockPanel.SetDock(refresh, Dock.Right);
            head.Children.Add(refresh);
            head.Children.Add(new TextBlock { Text = "Marketplace", FontSize = 16, FontWeight = FontWeights.Bold });
            list.Children.Add(head);
            if (string.IsNullOrWhiteSpace(_s.MarketplaceUrl))
                list.Children.Add(new TextBlock { Text = "Set the marketplace address in Settings, or install an extension from a file / link below.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            if (market != null)
                foreach (var m in market)
                {
                    var have = installed.FirstOrDefault(x => x.Id == m.Id);
                    string label = have == null ? "Install" : Extensions.IsNewer(m.Version, have.Version) ? "Update" : "Installed";
                    var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var b = new Button { Content = label, Padding = new Thickness(8, 2, 8, 2), IsEnabled = label != "Installed" };
                    b.Click += async (_, _) =>
                    {
                        try { await Extensions.Install(m); info.Text = m.Name + " installed."; Rebuild(); }
                        catch (Exception ex) { info.Text = "Install failed: " + ex.Message; }
                    };
                    DockPanel.SetDock(b, Dock.Right);
                    row.Children.Add(b);
                    row.Children.Add(new TextBlock { Text = $"{m.Name}  v{m.Version}" + (m.Author.Length > 0 ? "  by " + m.Author : ""), VerticalAlignment = VerticalAlignment.Center });
                    list.Children.Add(row);
                    if (m.Description.Length > 0) list.Children.Add(new TextBlock { Text = m.Description, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
                }

            var ins = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var url = new TextBox { Width = 380 };
            var go = new Button { Content = "Install from link", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            go.Click += async (_, _) =>
            {
                try { var m = await Extensions.Install(new ExtensionManifest { ManifestUrl = url.Text.Trim(), Id = "x" }); info.Text = m.Name + " installed."; Rebuild(); }
                catch (Exception ex) { info.Text = "Install failed: " + ex.Message; }
            };
            var file = new Button { Content = "From file...", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            file.Click += (_, _) =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Extension|*.json" };
                if (d.ShowDialog() != true) return;
                try
                {
                    var m = System.Text.Json.JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(d.FileName), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (m == null || m.Id.Length == 0 || m.SearchUrl.Length == 0 || !m.SearchUrl.StartsWith("https://") || !m.SearchUrl.Contains("{query}")) throw new InvalidDataException("Needs id and an https searchUrl containing {query}.");
                    Extensions.Save(m); info.Text = m.Name + " installed."; Rebuild();
                }
                catch (Exception ex) { info.Text = "Install failed: " + ex.Message; }
            };
            ins.Children.Add(url); ins.Children.Add(go); ins.Children.Add(file);
            list.Children.Add(ins);
            list.Children.Add(info);
        }

        async Task Load()
        {
            if (string.IsNullOrWhiteSpace(_s.MarketplaceUrl)) { info.Text = "No marketplace address set (Settings)."; return; }
            try { info.Text = "Loading..."; market = await Extensions.FetchMarketplace(_s.MarketplaceUrl); info.Text = market.Count + " extension(s) in the marketplace."; Rebuild(); }
            catch (Exception ex) { info.Text = "Could not load the marketplace (offline?): " + ex.Message; }
        }

        Rebuild();
        w.Content = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.Loaded += async (_, _) => await Load();
        w.ShowDialog();
    }
}
