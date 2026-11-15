
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MpvNet.Windows.WinForms;

// The progress bar on the taskbar button is drawn by Windows (green, yellow when paused) and cannot be recoloured.
// So it is drawn onto the window icon instead, in the theme / accent colour (the same one uosc and the drop zones use).
public static class TaskbarIcon
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

    static Icon? _base;
    static IntPtr _lastHandle = IntPtr.Zero;
    static long _lastKey = long.MinValue;

    // fraction < 0 = no bar
    public static void Apply(Form form, double fraction, bool paused)
    {
        try
        {
            _base ??= new Icon(form.Icon!, new Size(256, 256));
            Color accent = DropOverlay.ReadAccent();
            long key = (fraction < 0 ? -1 : (long)(fraction * 200)) * 1000 + (paused ? 1 : 0) * 500 + (accent.ToArgb() & 0xFF);
            if (key == _lastKey) return;
            _lastKey = key;

            using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(_base, new Rectangle(0, 0, 256, 256));

                if (fraction >= 0)
                {
                    int margin = 18, h = 40, y = 256 - h - 10, w = 256 - margin * 2;
                    using var track = RoundRect(margin, y, w, h, h / 2);
                    using var dark = new SolidBrush(Color.FromArgb(215, 12, 14, 18));
                    g.FillPath(dark, track);

                    int fill = Math.Max(h, (int)(w * Math.Clamp(fraction, 0, 1)));
                    Color c = paused ? Color.FromArgb(150, accent) : accent;
                    using var fp = RoundRect(margin, y, fill, h, h / 2);
                    using var fb = new SolidBrush(c);
                    g.FillPath(fb, fp);
                    using var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 3);
                    g.DrawPath(pen, track);
                }
            }

            IntPtr handle = bmp.GetHicon();
            var icon = Icon.FromHandle(handle);
            form.Icon = icon;
            if (_lastHandle != IntPtr.Zero) DestroyIcon(_lastHandle);
            _lastHandle = handle;
        }
        catch { /* the normal icon stays */ }
    }

    static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        var p = new GraphicsPath();
        int d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
