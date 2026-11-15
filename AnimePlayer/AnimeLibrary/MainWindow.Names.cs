using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeLibrary;

/// <summary>Smart names: fix name mismatches, merge variants into one series and keep series OFFLINE (no cover / tracker look-ups).</summary>
public partial class MainWindow
{
    Window NewDialog(string title, double width)
    {
        return new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = CSurface, Foreground = CText, ShowInTaskbar = false
        };
    }

    static List<string> MatchKeysOf(Entry e) => new[] { e.Key }.Concat(e.Aliases).Distinct().ToList();

    TextBlock Note(string t) => new() { Text = t, Foreground = CMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };

    // ------------------------------------------------------------------ smart name
    void ShowSmartName(Entry e)
    {
        var w = NewDialog("Smart name", 560);
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = "Smart name", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        sp.Children.Add(Note("Files whose names produce this series are always filed under the name below - even after a rescan - no matter how the file names are spelled."));

        sp.Children.Add(Lbl("Series name (shown in the library)"));
        var name = new TextBox { Text = e.DisplayTitle };
        sp.Children.Add(name);

        sp.Children.Add(Lbl("Search online as (optional)"));
        var search = new TextBox { Text = e.SearchName ?? "" };
        sp.Children.Add(search);
        sp.Children.Add(Note("Use this when the file names are wrong or abbreviated, e.g. the file says \"Juukishi\" but the site knows \"Juuki Kishi\"."));

        var local = new CheckBox { Content = "Local only - never look this series up online (no cover search, no tracker matching, nothing is sent)", IsChecked = e.LocalOnly, Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(local);
        sp.Children.Add(Note("Good for home videos, concerts or anything the sites do not know. The series stays in the library with a cover cut from its first video."));

        var keys = MatchKeysOf(e);
        sp.Children.Add(Lbl("Applies to titles"));
        sp.Children.Add(new TextBlock { Text = string.Join("   ", keys), Foreground = CMuted, FontSize = 11, TextWrapping = TextWrapping.Wrap });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(22, 6, 22, 6) };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        sp.Children.Add(buttons);
        w.Content = sp;

        save.Click += (_, _) =>
        {
            string n = name.Text.Trim().Length > 0 ? name.Text.Trim() : e.DisplayTitle;
            bool wasLocal = e.LocalOnly;
            string newSearch = search.Text.Trim();
            bool searchChanged = !string.Equals(e.SearchName ?? "", newSearch, StringComparison.Ordinal);
            Scanner.SetRule(_s, keys, n, e.Key, local.IsChecked == true);
            e.SmartName = n;
            e.LocalOnly = local.IsChecked == true;
            e.SearchName = newSearch.Length > 0 ? newSearch : null;
            if (e.LocalOnly) e.Matched = true;
            else if (searchChanged || wasLocal) { e.Matched = false; e.MatchTries = 0; }
            w.DialogResult = true;
            Persist(); _imgCache.Clear(); Render(); UpdateSyncButton();
            Say(e.LocalOnly ? $"\"{n}\" is local only - it is never looked up online." : $"Smart name saved: \"{n}\".");
            if (!e.LocalOnly && !e.Matched) _ = SyncAsync(true);
        };
        w.ShowDialog();
    }

    void ToggleLocalOnly(Entry e)
    {
        bool on = !e.LocalOnly;
        Scanner.SetRule(_s, MatchKeysOf(e), e.SmartName ?? e.DisplayTitle, e.Key, on);
        e.SmartName ??= e.DisplayTitle;
        e.LocalOnly = on;
        if (on) e.Matched = true; else { e.Matched = false; e.MatchTries = 0; }
        Persist(); Render(); UpdateSyncButton();
        Say(on ? $"\"{e.DisplayTitle}\" is local only now." : $"\"{e.DisplayTitle}\" may be looked up online again.");
        if (!on) _ = SyncAsync(true);
    }

    // ------------------------------------------------------------------ group into a folder
    /// <summary>Series that are not grouped by the disk layout (loose folders, or the wrong folder) can be put into a folder card of your own.</summary>
    void ShowGroup(Entry e)
    {
        var w = NewDialog("Group series into a folder", 600);
        w.SizeToContent = SizeToContent.Manual; w.Height = 600; w.ResizeMode = ResizeMode.CanResize; w.MinHeight = 400;
        var root = new DockPanel { Margin = new Thickness(20) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Group series into a folder", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        head.Children.Add(Note("Tick the series that belong together (for example every Monogatari season) and name the folder. In the library they show as ONE folder card, wherever they are stored on disk. Leave the name empty to take the ticked series out of their group again."));
        head.Children.Add(Lbl("Folder name"));
        var name = new ComboBox { IsEditable = true, Text = e.Group ?? "" };
        foreach (string g in _entries.Where(x => !string.IsNullOrEmpty(x.Group)).Select(x => x.Group!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) name.Items.Add(g);
        head.Children.Add(name);
        head.Children.Add(Lbl("Series"));
        var filter = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(filter);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(22, 6, 22, 6) };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, Entry Item)>();
        foreach (var x in _entries.Where(x => !x.Custom && x.Kind == Kind.Anime).OrderBy(x => x.DisplayTitle, StringComparer.OrdinalIgnoreCase))
        {
            bool pre = x == e || (!string.IsNullOrEmpty(e.Group) && string.Equals(x.Group, e.Group, StringComparison.OrdinalIgnoreCase));
            var cb = new CheckBox { Content = x.DisplayTitle + (string.IsNullOrEmpty(x.Group) ? "" : "   [" + x.Group + "]"), IsChecked = pre, Margin = new Thickness(0, 2, 0, 2), Foreground = CText };
            boxes.Add((cb, x)); list.Children.Add(cb);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        filter.TextChanged += (_, _) =>
        {
            string q = filter.Text.Trim();
            foreach (var (b, x) in boxes) b.Visibility = q.Length == 0 || x.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        };
        w.Content = root;

        save.Click += (_, _) =>
        {
            string g = name.Text.Trim();
            int n = 0;
            foreach (var (b, x) in boxes) if (b.IsChecked == true) { x.Group = g.Length > 0 ? g : null; n++; }
            w.DialogResult = true;
            _collection = null; _detail = null; _animate = true;
            Persist(); Render();
            Say(g.Length > 0 ? $"{n} series grouped in \"{g}\"." : $"{n} series taken out of their group.");
        };
        w.ShowDialog();
    }

    // ------------------------------------------------------------------ merge
    void ShowMerge(Entry e)
    {
        var w = NewDialog("Merge into another series", 600);
        w.SizeToContent = SizeToContent.Manual; w.Height = 560; w.ResizeMode = ResizeMode.CanResize; w.MinHeight = 380;
        var sp = new DockPanel { Margin = new Thickness(20) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Merge into another series", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        head.Children.Add(Note($"\"{e.DisplayTitle}\" ({e.Episodes} episodes) becomes part of the series you pick - its files, folders and progress move over, and later scans keep them together."));
        var filter = new TextBox { Margin = new Thickness(0, 4, 0, 8) };
        head.Children.Add(filter);
        DockPanel.SetDock(head, Dock.Top);
        sp.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var merge = new Button { Content = "Merge", IsDefault = true, Padding = new Thickness(22, 6, 22, 6), IsEnabled = false };
        buttons.Children.Add(cancel); buttons.Children.Add(merge);
        DockPanel.SetDock(buttons, Dock.Bottom);
        sp.Children.Add(buttons);

        var list = new ListBox { Background = CCard, BorderBrush = CLine, Foreground = CText, BorderThickness = new Thickness(1) };
        sp.Children.Add(list);
        void Fill()
        {
            string q = filter.Text.Trim();
            list.Items.Clear();
            foreach (var o in _entries.Where(x => x != e && !x.Custom && x.Kind == Kind.Anime && (q.Length == 0 || x.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase))).OrderBy(x => x.DisplayTitle, StringComparer.OrdinalIgnoreCase))
                list.Items.Add(new ListBoxItem { Content = $"{o.DisplayTitle}   ({o.Episodes} ep)", Tag = o, Padding = new Thickness(8, 5, 8, 5) });
        }
        filter.TextChanged += (_, _) => Fill();
        list.SelectionChanged += (_, _) => merge.IsEnabled = list.SelectedItem != null;
        Fill();

        merge.Click += (_, _) =>
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is not Entry target) return;
            Scanner.SetRule(_s, MatchKeysOf(e), target.SmartName ?? target.DisplayTitle, target.Key, target.LocalOnly);
            Scanner.MergeEntries(_entries, target, e);
            if (_detail == e) _detail = null;
            w.DialogResult = true;
            Persist(); _imgCache.Clear(); Render(); UpdateSyncButton();
            Say($"Merged into \"{target.DisplayTitle}\".");
        };
        w.Content = sp;
        w.ShowDialog();
    }
}
