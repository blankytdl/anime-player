using System.Drawing;
using System.Windows.Forms;

namespace MpvNet.Windows.Anime;

/// <summary>Aero-snap for the borderless player window, drawn like Windows does it: while the window is dragged with the pointer on a screen
/// edge a translucent preview shows where it will land (left / right half, corner = quarter, top edge = whole work area), releasing there
/// puts the window in that place, and dragging it away restores the size it had before. Works without changing the window style (that broke
/// fullscreen). Never active in fullscreen / maximized.</summary>
public static class WindowSnap
{
    static Rectangle _before;
    static bool _resizing;   // the current size/move loop is a RESIZE (edge drag): never snap or restore then

    /// <summary>WM_SYSCOMMAND: SC_SIZE (0xF000) = resize loop, SC_MOVE (0xF010) = move loop.</summary>
    public static void NoteSysCommand(IntPtr wParam) { int c = (int)(wParam.ToInt64() & 0xFFF0); if (c == 0xF000) _resizing = true; else if (c == 0xF010) _resizing = false; }
    /// <summary>WM_SIZING only arrives while an edge / corner is dragged.</summary>
    public static void NoteResize() => _resizing = true;

    static bool _snapped;
    static SnapPreview? _preview;
    static System.Windows.Forms.Timer? _timer;

    public static void EnterMove(Form f)
    {
        try
        {
            if (_resizing) return;   // edge resize: keep what the user drags (and do not show the snap preview)
            if (AnimeOptions.WindowsSnap && _snapped && f.WindowState == FormWindowState.Normal)
            {
                var cur = Cursor.Position; var b = f.Bounds;
                double rx = b.Width > 0 ? Math.Clamp((double)(cur.X - b.X) / b.Width, 0.05, 0.95) : 0.5;
                f.Bounds = new Rectangle(cur.X - (int)(_before.Width * rx), b.Y, _before.Width, _before.Height);
                _snapped = false;
            }
            StartPreview(f);
        }
        catch { }
    }

    public static void ExitMove(Form f)
    {
        try
        {
            StopPreview();
            bool wasResize = _resizing;
            _resizing = false;       // the next loop starts as a move until Windows says otherwise
            if (wasResize) return;
            if (!AnimeOptions.WindowsSnap || f.WindowState != FormWindowState.Normal || Player.Fullscreen) return;
            if (TargetFor(Cursor.Position) is Rectangle r)
            {
                if (!_snapped) _before = f.Bounds;
                _snapped = true;
                f.Bounds = Compensate(f, r);
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RectL rect, int size);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RectL { public int L, T, R, B; }

    /// <summary>A window with a resize frame has invisible borders (the shadow area): grow the target by them, so the VISIBLE window fills the quarter / half exactly.</summary>
    static Rectangle Compensate(Form f, Rectangle r)
    {
        try
        {
            if (DwmGetWindowAttribute(f.Handle, 9 /*EXTENDED_FRAME_BOUNDS*/, out RectL v, 16) != 0) return r;
            Rectangle b = f.Bounds;
            int l = v.L - b.Left, t = v.T - b.Top, rr = b.Right - v.R, bb = b.Bottom - v.B;
            if (l < 0 || t < 0 || rr < 0 || bb < 0 || l > 40 || rr > 40 || bb > 40) return r;
            return new Rectangle(r.X - l, r.Y - t, r.Width + l + rr, r.Height + t + bb);
        }
        catch { return r; }
    }

    /// <summary>Where the window would land if it were released with the pointer at <paramref name="cur"/>.</summary>
    static Rectangle? TargetFor(Point cur)
    {
        var scr = Screen.FromPoint(cur);
        Rectangle wa = scr.WorkingArea, sb = scr.Bounds;
        const int edge = 4;   // pointer within 4 px of the screen edge
        bool left = cur.X <= sb.Left + edge, right = cur.X >= sb.Right - 1 - edge;
        bool top = cur.Y <= sb.Top + edge, bottom = cur.Y >= sb.Bottom - 1 - edge;
        if (!(left || right || top || bottom)) return null;
        int hw = wa.Width / 2, hh = wa.Height / 2;
        // Corner zone like Windows: on a side edge the outer part of the edge (not only the exact corner pixel) makes a QUARTER.
        // Before, a quarter needed the pointer within 4 px of BOTH edges at once - almost impossible - and anything else gave a half.
        int zone = Math.Clamp(Math.Min(wa.Width, wa.Height) / 5, 60, 220);
        if ((left || right) && !top && !bottom)
        {
            if (cur.Y <= wa.Top + zone) top = true;
            else if (cur.Y >= wa.Bottom - 1 - zone) bottom = true;
        }
        else if ((top || bottom) && !left && !right)
        {
            if (cur.X <= wa.Left + zone) left = true;
            else if (cur.X >= wa.Right - 1 - zone) right = true;
            else return top ? wa : null;     // top edge = whole work area; the bottom edge alone does nothing
        }
        int rw = wa.Width - hw, rh = wa.Height - hh;
        if (left && top) return new Rectangle(wa.Left, wa.Top, hw, hh);
        if (right && top) return new Rectangle(wa.Left + hw, wa.Top, rw, hh);
        if (left && bottom) return new Rectangle(wa.Left, wa.Top + hh, hw, rh);
        if (right && bottom) return new Rectangle(wa.Left + hw, wa.Top + hh, rw, rh);
        if (left) return new Rectangle(wa.Left, wa.Top, hw, wa.Height);
        if (right) return new Rectangle(wa.Left + hw, wa.Top, rw, wa.Height);
        return null;
    }

    static void StartPreview(Form f)
    {
        if (!AnimeOptions.WindowsSnap || Player.Fullscreen || f.WindowState != FormWindowState.Normal) return;
        _preview ??= new SnapPreview();
        _timer ??= new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    static void StopPreview()
    {
        _timer?.Stop();
        _preview?.Hide();
    }

    static void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (TargetFor(Cursor.Position) is Rectangle r) _preview?.ShowAt(r);
            else _preview?.Hide();
        }
        catch { }
    }

    /// <summary>The translucent rectangle; click-through, never takes focus.</summary>
    sealed class SnapPreview : Form
    {
        Rectangle _last = Rectangle.Empty;

        public SnapPreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = MpvNet.Windows.WinForms.DropOverlay.ReadAccent();
            Opacity = 0.30;
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

        public void ShowAt(Rectangle r)
        {
            if (r != _last)
            {
                _last = r;
                BackColor = MpvNet.Windows.WinForms.DropOverlay.ReadAccent();
                Bounds = new Rectangle(r.X + 6, r.Y + 6, Math.Max(10, r.Width - 12), Math.Max(10, r.Height - 12));
            }
            if (!Visible) Show();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(Color.White, 3);
            e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }
}
