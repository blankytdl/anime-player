
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MpvNet.Windows.WinForms;

// Shown while files are dragged over the player: top strip = "Play later" (adds to the Queue), the rest = "Play now".
// Click-through (WS_EX_TRANSPARENT) so the drag keeps going to the main window underneath.
public sealed class DropOverlay : Form
{
    public const double LaterFraction = 0.2;
    bool _later;
    Color _accent = Color.FromArgb(0xfb, 0x8c, 0x00);

    public DropOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = 0.62;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000; // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
            return cp;
        }
    }

    public static bool IsLaterZone(Control c, int screenX, int screenY)
    {
        var p = c.PointToClient(new Point(screenX, screenY));
        return p.Y < c.ClientSize.Height * LaterFraction;
    }

    public void ShowOver(Form owner)
    {
        _accent = ReadAccent();
        Bounds = owner.RectangleToScreen(owner.ClientRectangle);
        if (!Visible) Show(owner);
        Invalidate();
    }

    public void SetZone(bool later)
    {
        if (later == _later) return;
        _later = later;
        Invalidate();
    }

    // accent / theme colour from uosc_theme.json (same file the player UI uses); amber when there is none
    public static Color ReadAccent()
    {
        try
        {
            string file = Path.Combine(Player.ConfigFolder, "uosc_theme.json");
            if (File.Exists(file))
            {
                string json = File.ReadAllText(file);
                var m = Regex.Match(json, "\"accent\"\\s*:\\s*\"([0-9a-fA-F]{6})\"");
                if (m.Success) return ColorTranslator.FromHtml("#" + m.Groups[1].Value);
                m = Regex.Match(json, "\"theme\"\\s*:\\s*\"(\\w+)\"");
                if (m.Success)
                {
                    string hex = m.Groups[1].Value switch
                    {
                        "ocean" => "3ea0ff", "violet" => "9b87ff", "emerald" => "22c58b", "sakura" => "ff7eb6",
                        "crimson" => "e5484d", "midnight" => "8ab4ff", "mono" => "f2f2f2", "light" => "5b4cd6", _ => "fb8c00",
                    };
                    return ColorTranslator.FromHtml("#" + hex);
                }
            }
        }
        catch { }
        return Color.FromArgb(0xfb, 0x8c, 0x00);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        int w = ClientSize.Width, h = ClientSize.Height;
        int topH = (int)(h * LaterFraction);
        var top = new Rectangle(0, 0, w - 1, topH);
        var rest = new Rectangle(0, topH + 6, w, h - topH - 6);
        float fs = Math.Max(12f, Math.Min(w, h) / 14f);

        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var font = new Font("Segoe UI", fs, FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);
        using var acc = new SolidBrush(_accent);

        // Play later: black box with a border
        using (var pen = new Pen(_later ? _accent : Color.White, _later ? 3 : 1))
            g.DrawRectangle(pen, top.X + 1, top.Y + 1, top.Width - 2, top.Height - 2);
        g.DrawString("Play later", font, _later ? acc : white, top, fmt);

        // Play now: the dimmed video with the text in the middle
        if (!_later)
        {
            using var pen = new Pen(_accent, 3);
            g.DrawRectangle(pen, 1, rest.Y + 1, w - 3, rest.Height - 3);
        }
        g.DrawString("Play now", font, _later ? white : acc, rest, fmt);
    }
}
