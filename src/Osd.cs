using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The on-screen indicator: rounded, icon-led, just above the taskbar on
    /// whichever monitor has the active window.
    ///
    /// It is a *layered* window drawn from a 32-bit ARGB bitmap, not a normal
    /// window with a clipped region. A region is hard-edged - it can only
    /// include or exclude whole pixels - which is what left the rounded corners
    /// looking chewed. Per-pixel alpha composites the curve smoothly against
    /// whatever is behind it.
    ///
    /// WS_EX_NOACTIVATE means it never takes focus; WS_EX_TRANSPARENT means
    /// clicks pass straight through.
    /// </summary>
    public static class Osd
    {
        public enum Sym { None, Mic, MicOff }

        const float Radius = 17f;

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst,
            ref SIZE size, IntPtr hdcSrc, ref POINT src, int key,
            ref BLENDFUNCTION blend, int flags);

        class OsdForm : Form
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TRANSPARENT = 0x20;
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_LAYERED = 0x80000;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT
                                | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            public OsdForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
            }

            /// <summary>Push an ARGB bitmap to the window, alpha and all.</summary>
            public void Blit(Bitmap bmp) { Blit(bmp, 255); }

            /// <summary>Blend the whole window at `alpha` - used for the fade.</summary>
            public void Blit(Bitmap bmp, byte alpha)
            {
                IntPtr screen = GetDC(IntPtr.Zero);
                IntPtr mem = CreateCompatibleDC(screen);
                IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
                try
                {
                    hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                    old = SelectObject(mem, hBmp);

                    SIZE size = new SIZE();
                    size.cx = bmp.Width; size.cy = bmp.Height;
                    POINT src = new POINT();
                    POINT dst = new POINT();
                    dst.X = Left; dst.Y = Top;

                    BLENDFUNCTION blend = new BLENDFUNCTION();
                    blend.BlendOp = 0;                 // AC_SRC_OVER
                    blend.SourceConstantAlpha = alpha;
                    blend.AlphaFormat = 1;             // AC_SRC_ALPHA

                    UpdateLayeredWindow(Handle, screen, ref dst, ref size,
                                        mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
                }
                finally
                {
                    if (old != IntPtr.Zero) SelectObject(mem, old);
                    if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                    DeleteDC(mem);
                    ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        static OsdForm _form;
        static Timer _timer;
        static Timer _anim;
        static Bitmap _frame;
        static int _alpha, _targetAlpha;
        static int _restY;
        static bool _pendingHide;

        // what the pill currently shows, so a new message can morph into it
        static string _msg = "";
        static Sym _glyph;
        static Color _accent;
        static float _w, _targetW;    // animated width
        static int _h = 54;

        static Bitmap Render(string message, Sym glyph, Color accent, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;   // keeps alpha
                g.Clear(Color.Transparent);

                RectangleF r = new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f);
                using (GraphicsPath path = Theme.Rounded(r, Radius))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(246, Theme.Card)))
                        g.FillPath(b, path);
                    using (Pen p = new Pen(Color.FromArgb(150, Theme.Border), 1f))
                        g.DrawPath(p, path);
                }

                float x = 22f;
                if (glyph != Sym.None)
                {
                    string ch = glyph == Sym.MicOff ? "" : "";
                    Font icon = IconFont(19f);
                    if (icon != null)
                        using (icon)
                        using (SolidBrush b = new SolidBrush(accent))
                        using (StringFormat fmt = new StringFormat())
                        {
                            fmt.Alignment = StringAlignment.Center;
                            fmt.LineAlignment = StringAlignment.Center;
                            g.DrawString(ch, icon, b, x + 13f, h / 2f, fmt);
                        }
                    x += 40f;
                }

                using (Font f = Theme.Font(10.5f, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(Theme.Text))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString(message, f, b, new RectangleF(x, 0, w - x - 20f, h), fmt);
                }
            }
            return bmp;
        }

        static Font IconFont(float px)
        {
            try
            {
                Font f = new Font("Segoe MDL2 Assets", px, GraphicsUnit.Pixel);
                if (string.Equals(f.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                    return f;
                f.Dispose();
            }
            catch { }
            return null;
        }

        public static void Show(string message, Sym glyph, Color accent, bool persist = false)
        {
            if (!Mapping.ShowIndicator) return;

            _msg = message;
            _glyph = glyph;
            _accent = accent;
            _targetW = Measure(message, glyph);

            bool fresh = _form == null;
            if (fresh)
            {
                _form = new OsdForm();
                _w = _targetW;
                _alpha = 0;
                _form.Size = new Size((int)_w, _h);
                Place();
                _form.Top = _restY + 8;        // rise into place
                _form.Show();
            }
            else
            {
                // Already on screen: keep the same window and morph it, rather
                // than tearing it down and building another - that swap is what
                // made switching look like a flicker.
                _alpha = Math.Max(_alpha - 90, 70);
            }

            _targetAlpha = 255;
            _pendingHide = false;
            Redraw();
            StartAnim();

            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (!persist)
            {
                _timer = new Timer();
                _timer.Interval = 1500;
                _timer.Tick += delegate { FadeOut(); };
                _timer.Start();
            }
        }

        static float Measure(string message, Sym glyph)
        {
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (Font f = Theme.Font(10.5f, FontStyle.Regular))
                return 44 + (float)Math.Ceiling(g.MeasureString(message, f).Width)
                          + (glyph == Sym.None ? 0 : 40);
        }

        static void Place()
        {
            Screen scr = Screen.FromHandle(GetForegroundWindow());
            Rectangle wa = scr.WorkingArea;      // stops at the taskbar
            _restY = wa.Bottom - _h - 16;
            _form.Left = wa.Left + (wa.Width - (int)_w) / 2;
        }

        /// <summary>Re-render at the current width and push it to the window.</summary>
        static void Redraw()
        {
            if (_form == null) return;
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            _frame = Render(_msg, _glyph, _accent, Math.Max(40, (int)_w), _h);
            _form.Size = new Size((int)_w, _h);
            Place();
            _form.Blit(_frame, (byte)_alpha);
        }

        /// <summary>Fade and settle, rather than appearing and vanishing.</summary>
        static void StartAnim()
        {
            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 8;             // ~120fps: no visible stepping
                _anim.Tick += delegate { Step(); };
            }
            _anim.Start();
        }

        static void Step()
        {
            if (_form == null || _frame == null) { if (_anim != null) _anim.Stop(); return; }

            // Ease out: most of the distance is covered in the first frames, so
            // it feels immediate but still settles rather than snapping.
            int remaining = _targetAlpha - _alpha;
            int step = (int)Math.Round(remaining * 0.34);
            if (step == 0) step = remaining > 0 ? 1 : -1;
            _alpha += step;
            if (_alpha > 255) _alpha = 255;
            if (_alpha < 0) _alpha = 0;
            if (Math.Abs(_targetAlpha - _alpha) < 4) _alpha = _targetAlpha;

            // Width eases toward the new message, so a longer or shorter label
            // grows the pill instead of replacing it.
            float dw = _targetW - _w;
            if (Math.Abs(dw) > 0.5f) _w += dw * 0.34f; else _w = _targetW;

            int offset = (int)Math.Round(8.0 * (255 - _alpha) / 255.0);

            try
            {
                Redraw();
                _form.Top = _restY + offset;
                _form.Blit(_frame, (byte)_alpha);
            }
            catch { }

            if (_alpha == _targetAlpha && _w == _targetW)
            {
                _anim.Stop();
                if (_targetAlpha == 0 && _pendingHide) Hide();
            }
        }

        static void FadeOut()
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (_form == null) return;
            _targetAlpha = 0;
            _pendingHide = true;
            StartAnim();
        }

        public static void Hide()
        {
            if (_anim != null) { _anim.Stop(); }
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            if (_form != null)
            {
                try { _form.Close(); _form.Dispose(); }
                catch { }
                _form = null;
            }
        }
    }
}
