using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnimeLibrary;

/// <summary>Vector icons on a 24x24 grid (replace the emoji glyphs, which looked different on every PC).</summary>
public static class Icons
{
    static readonly Dictionary<string, string> Data = new()
    {
        ["Play"] = "M7,4 L20,12 L7,20 Z",
        ["Library"] = "M3,3 H10.5 V10.5 H3 Z M13.5,3 H21 V10.5 H13.5 Z M3,13.5 H10.5 V21 H3 Z M13.5,13.5 H21 V21 H13.5 Z",
        ["Warning"] = "F0 M12,2.5 L22.5,20.5 H1.5 Z M10.9,9 H13.1 V14.5 H10.9 Z M10.9,16 H13.1 V18.2 H10.9 Z",
        ["Updates"] = "M8,3 L3,9 H6.5 V19 H9.5 V9 H13 Z M16,21 L21,15 H17.5 V5 H14.5 V15 H11 Z",
        ["Folder"] = "M2,5 H9 L11.2,7.5 H22 V19.5 H2 Z",
        ["Extensions"] = "M4,4 H10 V6 A2,2 0 1 1 14,6 V4 H20 V10 H18 A2,2 0 1 0 18,14 H20 V20 H14 V18 A2,2 0 1 0 10,18 V20 H4 Z",
        ["Settings"] = "F0 M10.06,3.93 L10.70,3.80 L10.89,1.16 L13.11,1.16 L13.30,3.80 L13.94,3.93 L16.34,4.92 L16.88,5.29 L18.88,3.55 L20.45,5.12 L18.71,7.12 L19.08,7.66 L20.07,10.06 L20.20,10.70 L22.84,10.89 L22.84,13.11 L20.20,13.30 L20.07,13.94 L19.08,16.34 L18.71,16.88 L20.45,18.88 L18.88,20.45 L16.88,18.71 L16.34,19.08 L13.94,20.07 L13.30,20.20 L13.11,22.84 L10.89,22.84 L10.70,20.20 L10.06,20.07 L7.66,19.08 L7.12,18.71 L5.12,20.45 L3.55,18.88 L5.29,16.88 L4.92,16.34 L3.93,13.94 L3.80,13.30 L1.16,13.11 L1.16,10.89 L3.80,10.70 L3.93,10.06 L4.92,7.66 L5.29,7.12 L3.55,5.12 L5.12,3.55 L7.12,5.29 L7.66,4.92 Z M15.50,12 A3.5,3.5 0 1 0 8.50,12 A3.5,3.5 0 1 0 15.50,12 Z",
        ["Search"] = "F0 M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 Z M10,5 A5,5 0 1 1 10,15 A5,5 0 1 1 10,5 Z M15,16.4 L16.4,15 L21,19.6 L19.6,21 Z",
        ["Grid"] = "M3,3 H9 V9 H3 Z M10.5,3 H16.5 V9 H10.5 Z M18,3 H21 V9 H18 Z M3,10.5 H9 V16.5 H3 Z M10.5,10.5 H16.5 V16.5 H10.5 Z M18,10.5 H21 V16.5 H18 Z M3,18 H9 V21 H3 Z M10.5,18 H16.5 V21 H10.5 Z M18,18 H21 V21 H18 Z",
        ["List"] = "M3,4.5 H5.5 V7 H3 Z M8,4.5 H21 V7 H8 Z M3,10.75 H5.5 V13.25 H3 Z M8,10.75 H21 V13.25 H8 Z M3,17 H5.5 V19.5 H3 Z M8,17 H21 V19.5 H8 Z",
        ["Compact"] = "M3,4 H21 V6 H3 Z M3,8 H21 V10 H3 Z M3,12 H21 V14 H3 Z M3,16 H21 V18 H3 Z M3,20 H21 V22 H3 Z",
        ["Tiles"] = "M3,3 H21 V12 H3 Z M3,13.5 H12 V21 H3 Z M13.5,13.5 H21 V21 H13.5 Z",
        ["Back"] = "M15,4 L7,12 L15,20 L16.5,18.5 L10,12 L16.5,5.5 Z",
        ["Music"] = "M9,3 V15.2 A3.2,3.2 0 1 0 11,18 V8 L19,6 V13.2 A3.2,3.2 0 1 0 21,16 V3 Z",
        ["Film"] = "M3,4 H21 V20 H3 Z M5,6 V8 H7 V6 Z M5,10 V12 H7 V10 Z M5,14 V16 H7 V14 Z M17,6 V8 H19 V6 Z M17,10 V12 H19 V10 Z M17,14 V16 H19 V14 Z",
        ["PlayRound"] = "M8,5 L19,12 L8,19 Z",
        ["Close"] = "M5,6.4 L6.4,5 L12,10.6 L17.6,5 L19,6.4 L13.4,12 L19,17.6 L17.6,19 L12,13.4 L6.4,19 L5,17.6 L10.6,12 Z",
        ["Minimize"] = "M5,17 H19 V19 H5 Z",
        ["Maximize"] = "F0 M5,5 H19 V19 H5 Z M7,7 V17 H17 V7 Z",
        ["Restore"] = "F0 M8,3 H21 V16 H18 V19 H5 V6 H8 Z M10,5 V6 H18 V14 H19 V5 Z M7,8 V17 H16 V8 Z",
        ["Check"] = "M9,16.2 L4.8,12 L3.4,13.4 L9,19 L21,7 L19.6,5.6 Z",
        ["Star"] = "M12,2 L14.9,8.6 L22,9.3 L16.6,14 L18.2,21 L12,17.3 L5.8,21 L7.4,14 L2,9.3 L9.1,8.6 Z",
        ["Sort"] = "M3,6 H13 V8 H3 Z M3,11 H10 V13 H3 Z M3,16 H7 V18 H3 Z M17,4 L21,9 H18 V20 H16 V9 H13 Z",
        // round 5: a real library icon (books on a shelf) instead of the play triangle / 4-square grid
        ["Plus"] = "M10.5,4 H13.5 V10.5 H20 V13.5 H13.5 V20 H10.5 V13.5 H4 V10.5 H10.5 Z",
        ["Minus"] = "M4,10.5 H20 V13.5 H4 Z",
        ["Check"] = "M9.5,16.2 L5.3,12 L3.9,13.4 L9.5,19 L20.1,8.4 L18.7,7 Z",
        ["Books"] = "M3.5,3 H8 V19 H3.5 Z M9.5,3 H14 V19 H9.5 Z M15.3,4.6 L19.6,3.4 L23.2,17.6 L18.9,18.8 Z M2,20.5 H22.5 V22.5 H2 Z",
        ["NowPlaying"] = "M4,10 H7 V20 H4 Z M10.5,4 H13.5 V20 H10.5 Z M17,8 H20 V20 H17 Z",
        ["Panel"] = "F0 M3,4 H21 V20 H3 Z M5,6 V18 H9 V6 Z M11,6 V18 H19 V6 Z",
        ["Shuffle"] = "M3,6 H7.5 L16,17 H18 V14 L22.5,18.5 L18,23 V20 H15 L6.5,8.5 H3 Z M3,16 H6.5 L8.8,13.2 L10.4,15 L8,18 H3 Z M15,8.5 L13.4,6.7 L15,5 H18 V2 L22.5,6.5 L18,11 V8 Z",
        ["Refresh"] = "M12,5 V2 L8,6 L12,10 V7 A5,5 0 1 1 7,12 H5 A7,7 0 1 0 12,5 Z",
        ["Person"] = "M12,12 A4,4 0 1 0 12,4 A4,4 0 1 0 12,12 Z M4,21 C4,16.5 8,14 12,14 C16,14 20,16.5 20,21 Z",
        ["Launch"] = "F0 M2,4 H22 V17 H2 Z M4,6 V15 H20 V6 Z M9,8 L15,10.5 L9,13 Z M8,19 H16 V21 H8 Z"
    };

    static readonly Dictionary<string, Geometry> Cache = new();

    public static Geometry Geo(string name)
    {
        if (Cache.TryGetValue(name, out var g)) return g;
        g = Geometry.Parse(Data.TryGetValue(name, out var d) ? d : Data["Play"]);
        g.Freeze();
        return Cache[name] = g;
    }

    /// <summary>An icon of the given pixel size and colour.</summary>
    public static FrameworkElement Make(string name, Brush fill, double size = 20)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geo(name), Fill = fill });
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
