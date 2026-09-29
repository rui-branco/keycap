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
    /// the monitor it is given - else whichever monitor has the active window.
    /// It shows either a message or a level - a bar and a percentage, as
    /// Windows shows volume and brightness.
    ///
    /// It is a *layered* window drawn from a 32-bit ARGB bitmap, not a normal
    /// window with a clipped region. A region is hard-edged - it can only
    /// include or exclude whole pixels - which is what left the rounded corners
    /// looking chewed. Per-pixel alpha composites the curve smoothly against
    /// whatever is behind it.
    ///
    /// WS_EX_NOACTIVATE means it never takes focus; WS_EX_TRANSPARENT means
    /// clicks pass straight through.
    ///
    /// The app as a whole is DPI-unaware, so Windows would bitmap-stretch this
    /// window on a scaled display and the text would arrive soft. Only this
    /// window opts in to per-monitor awareness (see EnterDpi), and every
    /// measurement below is in unscaled units multiplied by the monitor's own
    /// scale at draw time - so the glyph and the label are rendered at real
    /// device pixels instead of being blown up from 96 DPI.
    /// </summary>
    public static class Osd
    {
        public enum Sym { None, Mic, MicOff, Sun }

        // Metrics in 96-DPI units; multiplied by the monitor scale to draw.
        // Half the height: the ends are full semicircles, a capsule like the
        // system's own indicators. Theme.Rounded clamps the diameter to the
        // rect's height, so it stays a true semicircle on Edge()'s one pixel
        // shorter rect.
        const float Radius = BaseH / 2f;
        const float PadLeft = 22f;      // pill edge to icon box
        const float IconAdvance = 40f;  // icon box to text
        const float PadRight = 20f;
        const float IconPx = 19f;
        const float TextPt = 10.5f;
        const float SlackW = 44f;       // padding the measured text sits in
        const int BaseH = 54;
        const int BottomGap = 16;
        const int RiseY = 8;            // how far it rises while fading in
        const float CrossDy = 7f;       // how far the two layers pass each other
        const float BarW = 150f;        // a level's bar
        const float BarH = 4f;
        const float BarGap = 14f;       // bar to percentage

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

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
        }

        const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);

        // -4 is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.
        static readonly IntPtr PerMonitorV2 = new IntPtr(-4);
        static bool _dpiApi = true;     // false once we learn this build lacks it
        static bool _dpiQuery = true;

        /// <summary>
        /// Borrow per-monitor DPI awareness for the current call. The window is
        /// created and updated inside one of these, so Windows hands us real
        /// device pixels and stops scaling the result behind our back. Pre-1607
        /// builds have no such export; there we simply stay unaware, which is
        /// what the whole app did before. Dimmer borrows it too, for its veils.
        /// </summary>
        internal static IntPtr EnterDpi()
        {
            if (!_dpiApi) return IntPtr.Zero;
            try { return SetThreadDpiAwarenessContext(PerMonitorV2); }
            catch { _dpiApi = false; return IntPtr.Zero; }
        }

        internal static void LeaveDpi(IntPtr prev)
        {
            if (prev == IntPtr.Zero) return;
            try { SetThreadDpiAwarenessContext(prev); }
            catch { }
        }

        /// <summary>The monitor the active window is on, in true pixels.</summary>
        static IntPtr ActiveMonitor()
        {
            return MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
        }

        static Rectangle WorkArea(IntPtr mon)
        {
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                return Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top,
                                          mi.rcWork.Right, mi.rcWork.Bottom);
            return Screen.PrimaryScreen.WorkingArea;
        }

        /// <summary>How many device pixels one layout unit is worth here.</summary>
        static float ScaleFor(IntPtr mon)
        {
            if (_dpiQuery && mon != IntPtr.Zero)
            {
                try
                {
                    uint dx, dy;
                    if (GetDpiForMonitor(mon, 0 /* MDT_EFFECTIVE_DPI */, out dx, out dy) == 0 && dx > 0)
                        return dx / 96f;
                }
                catch { _dpiQuery = false; }
            }
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int dpi = GetDeviceCaps(dc, 88 /* LOGPIXELSX */);
                ReleaseDC(IntPtr.Zero, dc);
                if (dpi > 0) return dpi / 96f;
            }
            catch { }
            return 1f;
        }

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
                // We size this window in device pixels ourselves; letting
                // WinForms autoscale it on top would undo that.
                AutoScaleMode = AutoScaleMode.None;
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
        static int _h = BaseH;
        static float _scale = 1f;     // device pixels per layout unit
        static IntPtr _shownOn;       // the monitor the pill is on

        // A level rather than a message: the value it is going to, and where
        // the bar has got to on its way there.
        static bool _isLevel;
        static int _level;
        static float _shownLevel;

        // The persistent message - "Microphone muted" - that a Flash passes
        // over and comes back to. Null when there is none.
        static string _stickyMsg;
        static Sym _stickyGlyph;
        static Color _stickyAccent;

        // The two content layers and how far the cross-fade between them has
        // got. Switching used to dim the whole pill and bring it back, which
        // read as a blink; now the pill holds still and only its contents
        // change hands.
        static Bitmap _outgoing, _incoming;
        static float _cross = 1f;     // 1 = the incoming layer owns the pill

        /// <summary>
        /// Draw the pill. With <paramref name="backdrop"/> false only the icon
        /// and the text are drawn, on transparency - that layer is what one
        /// message cross-fades into another, leaving the pill itself steady.
        /// With <paramref name="bar"/> at zero or above it draws a level instead
        /// of the message.
        /// </summary>
        static Bitmap Render(string message, Sym glyph, Color accent, int w, int h, bool backdrop, float s,
                             float bar, int value)
        {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Grid-fitted rather than plain antialiasing: the stems land on
                // whole pixels, so the label reads crisp instead of smeared.
                // ClearType is out - it would not leave us usable alpha.
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                if (backdrop)
                {
                    // The same pixel grid Compose clips with, where pixel n runs
                    // from n to n + 1 - so Edge() is the centre of the outermost
                    // pixel on all four sides, and the border lands in it whole.
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using (GraphicsPath path = Theme.Rounded(Edge(w, h), Radius * s))
                    {
                        // Solid rather than the old 96% - subpixel text needs
                        // an opaque surface underneath to blend against, and
                        // four percent of the wallpaper was never the point.
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, Theme.Card)))
                            g.FillPath(b, path);
                        // A hairline, one device pixel at every scale, as Windows
                        // 11 draws its flyouts. Opaque and a step lighter than
                        // the fill: it is what separates the pill from a dark
                        // window behind it.
                        using (Pen p = new Pen(Theme.Hover, 1f))
                            g.DrawPath(p, path);
                    }
                    return bmp;
                }

                Content(g, message, glyph, accent, w, h, s, false, bar, value);
            }
            return bmp;
        }

        /// <summary>
        /// The pill's outline: the centre line of a one-device-pixel stroke
        /// whose outer edge is exactly the bitmap's edge on all four sides.
        /// Theme.Rounded draws precisely the rectangle it is given, and the old
        /// one stopped half a pixel short on the right and bottom - those edges
        /// came out soft and borderless while the left and top were crisp. The
        /// fill, the border and the clip all come from here, so they cannot
        /// disagree again about where the pill ends.
        /// </summary>
        static RectangleF Edge(int w, int h)
        {
            return new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);
        }

        /// <summary>
        /// Icon and label, painted straight onto whatever surface is given.
        /// At rest that is the composed frame itself: the glyph edges are
        /// then antialiased once, against the pill. Going by way of a
        /// transparent layer blends those same edges a second time, which
        /// is what left the label looking soft when nothing was moving.
        ///
        /// With <paramref name="bar"/> at zero or above, the icon is followed
        /// by a level instead of the message: a bar filled to that point, and
        /// <paramref name="value"/> as a percentage after it.
        /// </summary>
        static void Content(Graphics g, string message, Sym glyph, Color accent,
                            int w, int h, float s, bool crisp, float bar, int value)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float x = PadLeft * s;
            if (glyph != Sym.None)
            {
                string ch = glyph == Sym.MicOff ? "\uF781" : glyph == Sym.Sun ? "\uE706" : "\uE720";
                Font icon = IconFont(IconPx * s);
                if (icon != null)
                    using (icon)
                    using (SolidBrush b = new SolidBrush(accent))
                    using (StringFormat fmt = StringFormat.GenericTypographic)   // a new instance each time
                    using (GraphicsPath gp = new GraphicsPath())
                    {
                        // Centred on the glyph's own ink rather than the font's
                        // line box, which sits the MDL2 glyphs a couple of pixels
                        // high; and placed at the centre of the left round end -
                        // h/2 on both axes, since Edge() is half a pixel in and
                        // one pixel short - so the icon is concentric with the
                        // capsule's curve.
                        gp.AddString(ch, icon.FontFamily, (int)icon.Style, icon.Size, PointF.Empty, fmt);
                        RectangleF ink = gp.GetBounds();
                        if (ink.Width > 0 && ink.Height > 0)
                        {
                            float c = h / 2f;
                            using (Matrix m = new Matrix())
                            {
                                m.Translate(c - (ink.X + ink.Width / 2f), c - (ink.Y + ink.Height / 2f));
                                gp.Transform(m);
                            }
                            g.FillPath(b, gp);
                        }
                    }
                x += IconAdvance * s;
            }

            if (bar >= 0f)
            {
                float bw = BarW * s, bh = BarH * s;
                RectangleF track = new RectangleF(x, h / 2f - bh / 2f, bw, bh);
                using (GraphicsPath p = Theme.Rounded(track, bh / 2f))
                using (SolidBrush b = new SolidBrush(Theme.BorderHi))
                    g.FillPath(b, p);
                float filled = bw * Math.Min(100f, bar) / 100f;
                if (filled > 0.5f)
                    using (GraphicsPath p = Theme.Rounded(
                               new RectangleF(track.X, track.Y, filled, bh), bh / 2f))
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillPath(b, p);

                // The number is right-aligned in its box, so the bar holds
                // still however many digits it has.
                float nx = x + bw + BarGap * s;
                string number = value + "%";
                using (Font f = Theme.Font(TextPt * s, FontStyle.Regular))
                {
                    if (crisp)
                        TextRenderer.DrawText(g, number, f,
                            new Rectangle((int)Math.Round(nx), 0,
                                          (int)Math.Round(w - nx - PadRight * s), h),
                            Theme.Text, NumberFlags);
                    else using (SolidBrush b = new SolidBrush(Theme.Text))
                    using (StringFormat fmt = new StringFormat())
                    {
                        fmt.Alignment = StringAlignment.Far;
                        fmt.LineAlignment = StringAlignment.Center;
                        fmt.FormatFlags |= StringFormatFlags.NoWrap;
                        g.DrawString(number, f, b,
                            new RectangleF(nx, 0, w - nx - PadRight * s, h), fmt);
                    }
                }
                return;
            }

            using (Font f = Theme.Font(TextPt * s, FontStyle.Regular))
            {
                if (crisp)
                {
                    // Subpixel text, the way every other label in Windows is
                    // drawn. It only works over something opaque - which the
                    // pill is - and it is the whole difference between a label
                    // that looks rendered and one that looks photographed.
                    TextRenderer.DrawText(g, message, f,
                        new Rectangle((int)Math.Round(x), 0,
                                      (int)Math.Round(w - x - PadRight * s), h),
                        Theme.Text, TextFlags);
                }
                else using (SolidBrush b = new SolidBrush(Theme.Text))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.LineAlignment = StringAlignment.Center;
                    fmt.FormatFlags |= StringFormatFlags.NoWrap;
                    g.DrawString(message, f, b,
                        new RectangleF(x, 0, w - x - PadRight * s, h), fmt);
                }
            }
        }

        const TextFormatFlags TextFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine;

        const TextFormatFlags NumberFlags =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine;

        /// <summary>
        /// GDI leaves the alpha byte at zero wherever it drew, which on a
        /// layered window would punch the label straight out of the pill. The
        /// pill itself already knows the right answer for every pixel - opaque
        /// inside, feathered along the curve - so copy its alpha channel back
        /// over the finished frame. Doing it from the shape rather than from a
        /// guessed inset means a longer label can never poke a hole in itself.
        /// </summary>
        static void RestoreAlpha(Bitmap frame, Bitmap shape)
        {
            Rectangle r = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData fd = frame.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData sd = shape.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = r.Width * 4;
                byte[] fr = new byte[rowBytes];
                byte[] sr = new byte[rowBytes];
                for (int y = 0; y < r.Height; y++)
                {
                    IntPtr fp = new IntPtr(fd.Scan0.ToInt64() + (long)y * fd.Stride);
                    IntPtr sp = new IntPtr(sd.Scan0.ToInt64() + (long)y * sd.Stride);
                    Marshal.Copy(fp, fr, 0, rowBytes);
                    Marshal.Copy(sp, sr, 0, rowBytes);
                    for (int i = 3; i < rowBytes; i += 4) fr[i] = sr[i];
                    Marshal.Copy(fr, 0, fp, rowBytes);
                }
            }
            finally { frame.UnlockBits(fd); shape.UnlockBits(sd); }
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

            // A persistent message is remembered so a Flash can return to it;
            // any other message replaces it for good.
            if (persist)
            {
                _stickyMsg = message;
                _stickyGlyph = glyph;
                _stickyAccent = accent;
            }
            else _stickyMsg = null;

            IntPtr ctx = EnterDpi();
            try { ShowCore(message, glyph, accent, persist, -1, IntPtr.Zero); }
            finally { LeaveDpi(ctx); }
        }

        /// <summary>
        /// A passing message that leaves a persistent one standing. It shows for
        /// the usual moment and then, instead of fading out, the pill goes back
        /// to the persistent message it passed over - a brightness step must not
        /// be what quietly hides that the microphone is still muted. With no
        /// persistent message it behaves exactly like Show. It goes on
        /// <paramref name="monitor"/>, or with IntPtr.Zero on the active
        /// window's monitor, as Show does.
        /// </summary>
        public static void Flash(string message, Sym glyph, Color accent, IntPtr monitor)
        {
            if (!Mapping.ShowIndicator) return;

            IntPtr ctx = EnterDpi();
            try { ShowCore(message, glyph, accent, false, -1, monitor); }
            finally { LeaveDpi(ctx); }
        }

        /// <summary>
        /// A level rather than a sentence, the way Windows shows volume and
        /// brightness: the icon, a bar and the percentage. A new value while one
        /// is showing moves the bar instead of replacing the pill, so a held key
        /// reads as one control sliding rather than a run of messages each
        /// cross-fading into the next. Like Flash, it passes over a persistent
        /// message and goes back to it, and it goes on <paramref name="monitor"/>
        /// - IntPtr.Zero for the active window's.
        /// </summary>
        public static void Level(Sym glyph, int percent, Color accent, IntPtr monitor)
        {
            if (!Mapping.ShowIndicator) return;

            IntPtr ctx = EnterDpi();
            try { ShowCore(null, glyph, accent, false, Math.Max(0, Math.Min(100, percent)), monitor); }
            finally { LeaveDpi(ctx); }
        }

        /// <summary>
        /// Put a message - or, with <paramref name="level"/> at zero or above, a
        /// level - on the pill, on <paramref name="monitor"/> or, given
        /// IntPtr.Zero, on the active window's. -1 is a message.
        /// </summary>
        static void ShowCore(string message, Sym glyph, Color accent, bool persist, int level, IntPtr monitor)
        {
            // A pill that has to move to another display leaves and arrives
            // fresh there - fading and rising in - rather than jumping across
            // in the middle of an animation.
            IntPtr where = monitor != IntPtr.Zero ? monitor : ActiveMonitor();
            if (_form != null && where != _shownOn) HideCore();
            _shownOn = where;

            bool fresh = _form == null;

            // Pick up the density of the monitor the pill is on, so moving
            // between a laptop panel and an external screen redraws at that
            // screen's own resolution rather than being stretched to it. It is
            // the monitor it was put on, not wherever focus is now, so the pill
            // does not wander to another screen mid-animation when focus moves.
            float scale = ScaleFor(_shownOn);
            bool rescaled = Math.Abs(scale - _scale) > 0.001f;
            _scale = scale;
            _h = (int)Math.Round(BaseH * _scale);

            // One level following another only moves the bar. Treating it as a
            // new message - the cross-fade and the width change - is what made
            // a held key glitch: each step started a fade before the last had
            // finished, and the label jumped. The level's layer is still
            // refreshed, so a later hand-over - back to the persistent message,
            // or a fade still in progress - never shows a stale value.
            bool asLevel = level >= 0;
            bool sameLevel = !fresh && !rescaled && asLevel && _isLevel && glyph == _glyph;
            bool changed = !sameLevel && (fresh || rescaled || asLevel != _isLevel || glyph != _glyph
                                          || (!asLevel && message != _msg));

            _isLevel = asLevel;
            _msg = asLevel ? "" : message;
            _glyph = glyph;
            _accent = accent;
            if (asLevel)
            {
                // A pill that is just appearing, or turning from a sentence
                // into a level, starts with the bar already there; only one
                // level following another slides.
                if (!sameLevel) _shownLevel = level;
                _level = level;
            }
            _targetW = asLevel ? MeasureLevel(_scale) : Measure(message, glyph, _scale);

            if (changed)
            {
                // The layer on screen steps aside for the new one instead of
                // being replaced between two frames.
                if (_outgoing != null) _outgoing.Dispose();
                // A layer drawn at the old density cannot cross-fade into one
                // drawn at the new one - it would be resampled. Start clean.
                if (rescaled && !fresh && _incoming != null)
                {
                    _incoming.Dispose();
                    _incoming = null;
                }
                _outgoing = fresh || rescaled ? null : _incoming;
                _incoming = Render(message, glyph, accent, (int)_targetW, _h, false, _scale,
                                   asLevel ? level : -1f, level);
                _cross = fresh || rescaled ? 1f : 0f;
            }
            else if (sameLevel)
            {
                // The same layer at the new value - no fade starts, and one
                // already under way carries on with the right number in it.
                if (_incoming != null) _incoming.Dispose();
                _incoming = Render(null, glyph, accent, (int)_targetW, _h, false, _scale, level, level);
            }

            if (fresh)
            {
                _form = new OsdForm();
                _w = _targetW;
                _alpha = 0;
                _form.Size = new Size((int)_w, _h);
                Place();
                _form.Top = _restY + (int)Math.Round(RiseY * _scale);   // rise into place
                _form.Show();
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
                _timer.Tick += delegate
                {
                    // After a Flash, back to the persistent message rather than away.
                    if (_stickyMsg == null) { FadeOut(); return; }
                    IntPtr ctx = EnterDpi();
                    try { ShowCore(_stickyMsg, _stickyGlyph, _stickyAccent, true, -1, IntPtr.Zero); }
                    finally { LeaveDpi(ctx); }
                };
                _timer.Start();
            }
        }

        static float Measure(string message, Sym glyph, float s)
        {
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (Font f = Theme.Font(TextPt * s, FontStyle.Regular))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                // The pill has to fit whichever of the two text paths is wider,
                // or a message would clip the moment the crisp one took over.
                float gdiPlus = g.MeasureString(message, f).Width;
                float gdi = TextRenderer.MeasureText(g, message, f,
                                new Size(int.MaxValue, int.MaxValue), TextFlags).Width;
                return SlackW * s
                     + (float)Math.Ceiling(Math.Max(gdiPlus, gdi))
                     + (glyph == Sym.None ? 0 : IconAdvance * s);
            }
        }

        /// <summary>
        /// A level's width. It is the same for every value - room is kept for
        /// "100%" - so the pill never resizes while the level changes.
        /// </summary>
        static float MeasureLevel(float s)
        {
            return SlackW * s + IconAdvance * s + BarW * s + BarGap * s + NumberWidth(s);
        }

        /// <summary>The widest number, "100%", on whichever text path is wider - as Measure does.</summary>
        static float NumberWidth(float s)
        {
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            using (Font f = Theme.Font(TextPt * s, FontStyle.Regular))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float gdiPlus = g.MeasureString("100%", f).Width;
                float gdi = TextRenderer.MeasureText(g, "100%", f,
                                new Size(int.MaxValue, int.MaxValue), NumberFlags).Width;
                return (float)Math.Ceiling(Math.Max(gdiPlus, gdi));
            }
        }

        static void Place()
        {
            Rectangle wa = WorkArea(_shownOn);   // stops at the taskbar; the pill's own monitor, see ShowCore
            _restY = wa.Bottom - _h - (int)Math.Round(BottomGap * _scale);
            _form.Left = wa.Left + (wa.Width - (int)_w) / 2;
        }

        /// <summary>Re-render at the current width and push it to the window.</summary>
        static void Redraw()
        {
            if (_form == null) return;
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            _frame = Compose(Math.Max((int)Math.Round(40 * _scale), (int)_w), _h);
            _form.Size = new Size((int)_w, _h);
            Place();
            _form.Blit(_frame, (byte)_alpha);
        }

        /// <summary>
        /// One frame: the steady pill, then whatever mix of the two content
        /// layers the cross-fade is currently at. They pass each other
        /// vertically - the old one lifting out, the new one arriving from
        /// below - which reads as a change rather than a flash.
        /// </summary>
        static Bitmap Compose(int w, int h)
        {
            bool settled = _cross >= 1f && _outgoing == null;
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f);
            Bitmap back = Render("", Sym.None, _accent, w, h, true, _scale, -1f, 0);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // The content layers are already at the right size, so copy
                // them pixel for pixel - the default bilinear path would put a
                // half-pixel of softness on text that is otherwise sharp.
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.Transparent);

                g.DrawImageUnscaled(back, 0, 0);

                // Clip to the pill so a layer wider than the animating window
                // cannot spill past the rounded edge.
                using (GraphicsPath path = Theme.Rounded(Edge(w, h), Radius * _scale))
                {
                    g.SetClip(path);
                    if (settled)
                    {
                        // Nothing is crossing over, so skip the pre-rendered
                        // layer and draw the real thing here, subpixel and all.
                        Content(g, _msg, _glyph, _accent, w, h, _scale, true,
                                _isLevel ? _shownLevel : -1f, _level);
                    }
                    else
                    {
                        float dy = CrossDy * _scale;
                        Layer(g, _outgoing, 1f - _cross, -dy * _cross);
                        Layer(g, _incoming, _cross, dy * (1f - _cross));
                    }
                    g.ResetClip();
                }
            }
            // Only the crisp path goes through GDI, so only it needs the
            // alpha channel put back.
            if (settled) RestoreAlpha(bmp, back);
            back.Dispose();
            return bmp;
        }

        static void Layer(Graphics g, Bitmap b, float alpha, float dy)
        {
            if (b == null || alpha <= 0.004f) return;
            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = alpha > 1f ? 1f : alpha;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                ia.SetWrapMode(WrapMode.TileFlipXY);   // no bleed at the edges
                g.DrawImage(b, new Rectangle(0, (int)Math.Round(dy), b.Width, b.Height),
                            0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
            }
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
            IntPtr ctx = EnterDpi();
            try { StepCore(); }
            finally { LeaveDpi(ctx); }
        }

        static void StepCore()
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

            // The hand-over between the two content layers, on the same ease.
            if (_cross < 1f)
            {
                _cross += (1f - _cross) * 0.26f + 0.015f;
                if (_cross > 0.995f) _cross = 1f;
            }

            // A level's bar slides to its new value on the same ease.
            if (_isLevel)
            {
                float dl = _level - _shownLevel;
                if (Math.Abs(dl) > 0.3f) _shownLevel += dl * 0.34f;
                else _shownLevel = _level;
            }

            int offset = (int)Math.Round(RiseY * _scale * (255 - _alpha) / 255.0);

            try
            {
                Redraw();
                _form.Top = _restY + offset;
                _form.Blit(_frame, (byte)_alpha);
            }
            catch { }

            if (_alpha == _targetAlpha && _w == _targetW && _cross >= 1f
                && (!_isLevel || _shownLevel == _level))
            {
                _anim.Stop();
                if (_targetAlpha == 0 && _pendingHide) { Hide(); return; }
                if (_outgoing != null)
                {
                    // Let go of the layer that just finished handing over, then
                    // paint one more frame: with nothing crossing any more the
                    // label goes straight onto the pill, and the frame it rests
                    // on is the sharp one rather than the composited one.
                    _outgoing.Dispose();
                    _outgoing = null;
                    try { Redraw(); }
                    catch { }
                }
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

        /// <summary>
        /// Back to the top of the topmost windows. A dimming veil made while the
        /// pill is up lands above it, and a pill under a veil would be dimmed
        /// along with the screen it is reporting on.
        /// </summary>
        internal static void Raise()
        {
            if (_form == null) return;
            // HWND_TOPMOST; SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE.
            SetWindowPos(_form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }

        public static void Hide()
        {
            _stickyMsg = null;
            IntPtr ctx = EnterDpi();
            try { HideCore(); }
            finally { LeaveDpi(ctx); }
        }

        static void HideCore()
        {
            if (_anim != null) { _anim.Stop(); }
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            if (_outgoing != null) { _outgoing.Dispose(); _outgoing = null; }
            if (_incoming != null) { _incoming.Dispose(); _incoming = null; }
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
