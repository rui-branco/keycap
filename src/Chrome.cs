using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// A window with no system title bar that keeps everything Windows gives a
    /// normal one: snap, drag to move, double-click to maximise, the resize
    /// border, the shadow and the rounded corners. The same shell Shotwin and
    /// Playbar draw with WindowChrome, done here by hand.
    ///
    /// The trick is WM_NCCALCSIZE. Windows is left to size the side and bottom
    /// frames - on Windows 10 and 11 those are the invisible resize borders in
    /// the shadow - and only the caption is taken back, so the client runs to
    /// the top edge. The top resize band and the drag band are then answered
    /// in WM_NCHITTEST.
    /// </summary>
    public class ChromeForm : Form
    {
        /// <summary>Height of the drag band along the top, caption buttons included.</summary>
        public const int CaptionHeight = 42;
        const int TopResize = 5;

        protected readonly CaptionButton MinButton, MaxButton, CloseButton;

        public ChromeForm()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            MinButton = new CaptionButton("");
            MinButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            MaxButton = new CaptionButton("");
            MaxButton.Click += delegate
            {
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            CloseButton = new CaptionButton("");
            CloseButton.IsClose = true;
            CloseButton.Click += delegate { Close(); };

            Controls.Add(MinButton);
            Controls.Add(MaxButton);
            Controls.Add(CloseButton);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
            // WM_NCCALCSIZE only comes round when the frame changes, so ask for
            // one now - otherwise the system caption stays until the first resize.
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int x = ClientSize.Width;
            foreach (CaptionButton b in new CaptionButton[] { CloseButton, MaxButton, MinButton })
            {
                x -= CaptionButton.W;
                b.SetBounds(x, 0, CaptionButton.W, CaptionHeight);
                b.BringToFront();
            }
            string glyph = WindowState == FormWindowState.Maximized ? "" : "";
            if (MaxButton.Glyph != glyph) { MaxButton.Glyph = glyph; MaxButton.Invalidate(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // The first rectangle of NCCALCSIZE_PARAMS is the proposed window
                // rect going in and the client rect coming out.
                RECT before = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                base.WndProc(ref m);
                RECT after = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                // Maximised, the window hangs past the screen by the frame's
                // thickness on every side. The sides and bottom are frame and fall
                // off harmlessly; the top is now client, so pull it back in.
                after.Top = before.Top + (IsZoomed(Handle) ? FrameThickness() : 0);
                Marshal.StructureToPtr(after, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result != HTCLIENT) return;

                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point(unchecked((short)lp), unchecked((short)(lp >> 16))));
                if (WindowState == FormWindowState.Normal && p.Y < TopResize)
                {
                    if (p.X < TopResize * 3) m.Result = (IntPtr)HTTOPLEFT;
                    else if (p.X >= ClientSize.Width - TopResize * 3) m.Result = (IntPtr)HTTOPRIGHT;
                    else m.Result = (IntPtr)HTTOP;
                }
                else if (p.Y < CaptionHeight)
                    m.Result = (IntPtr)HTCAPTION;
                return;
            }

            base.WndProc(ref m);
        }

        static int FrameThickness()
        {
            return GetSystemMetrics(SM_CYSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        }

        public const int WM_NCHITTEST = 0x84;
        public const int HTTRANSPARENT = -1;
        const int WM_NCCALCSIZE = 0x83;
        const int HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        const int SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4,
                   SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    /// <summary>
    /// Minimise, maximise and close, drawn with the glyphs Windows uses in its
    /// own title bars. Close turns red under the pointer, as it does there.
    /// </summary>
    public class CaptionButton : Drawn
    {
        public const int W = 46;
        public string Glyph;
        public bool IsClose;
        bool _hover, _down;

        public CaptionButton(string glyph)
        {
            Glyph = glyph;
            BackColor = Theme.Back;
            Font = Theme.Icons(7.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        /// <summary>
        /// The top few pixels belong to the window's resize edge, the way they
        /// do above Windows' own caption buttons.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ChromeForm.WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point(unchecked((short)lp), unchecked((short)(lp >> 16))));
                Form f = FindForm();
                if (p.Y < 4 && f != null && f.WindowState == FormWindowState.Normal)
                {
                    m.Result = (IntPtr)ChromeForm.HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = BackColor, ink = Theme.Text;
            if (IsClose && (_hover || _down))
            {
                back = _down ? ColorTranslator.FromHtml("#B80F1F") : Theme.CloseRed;
                ink = Color.White;
            }
            else if (_down) back = Theme.Blend(BackColor, Color.White, 0.08f);
            else if (_hover) back = Theme.Blend(BackColor, Color.White, 0.13f);
            g.Clear(back);
            TextRenderer.DrawText(g, Glyph, Font, ClientRectangle, ink, Theme.Center);
        }
    }

    /// <summary>
    /// One view of the main window: a title, a line saying what it is for, and
    /// its content under them. Every page stays alive while hidden, so what is
    /// half-typed on one survives a visit to another.
    /// </summary>
    public class Page : Panel
    {
        /// <summary>Left and right margin, the same on every page.</summary>
        public const int Inset = 28;

        public string Title = "";
        public string Subtitle = "";
        /// <summary>Room kept clear at the right of the header for a control placed there.</summary>
        public int HeaderRight;
        /// <summary>The window's floating confirmation, set by the window.</summary>
        public Action<string, Color> Say = delegate { };
        /// <summary>
        /// Raised after this page changed the mapping, so every other page -
        /// and the tray - redraws from it. Set by the window.
        /// </summary>
        public Action Changed = delegate { };

        public Page()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        /// <summary>Where content starts, under the title and subtitle.</summary>
        public int ContentTop { get { return Subtitle.Length > 0 ? 68 : 46; } }

        /// <summary>Width between the margins.</summary>
        public int ContentWidth { get { return Width - Inset * 2; } }

        /// <summary>The mapping, the hook or the updater changed: redraw from them.</summary>
        public virtual void Reload() { }

        /// <summary>Lay the page out for its current size.</summary>
        public virtual void Arrange() { }

        /// <summary>The page has just been navigated to.</summary>
        public virtual void Entered() { }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0) Arrange();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int w = Width - Inset * 2 - HeaderRight;
            using (Font ft = Theme.Semi(14.25f))
                TextRenderer.DrawText(g, Title, ft, new Rectangle(Inset - 1, 2, w, 32), Theme.Text, Theme.Left);
            if (Subtitle.Length > 0)
                using (Font fs = Theme.Font(9f, FontStyle.Regular))
                    TextRenderer.DrawText(g, Subtitle, fs, new Rectangle(Inset, 34, w, 20), Theme.Dim, Theme.Left);
            base.OnPaint(e);
        }

        /// <summary>A section heading: small, semibold, secondary ink, upper case.</summary>
        public static TextLine Section(string text, Color back)
        {
            TextLine l = new TextLine();
            l.Text = text.ToUpperInvariant();
            l.Font = Theme.Semi(8f);
            l.Ink = Theme.Dim;
            l.BackColor = back;
            l.Height = 18;
            return l;
        }

        /// <summary>A wrapping line of secondary text.</summary>
        public static TextLine Hint(string text, Color back)
        {
            TextLine l = new TextLine();
            l.Text = text;
            l.Font = Theme.Font(8.75f, FontStyle.Regular);
            l.Ink = Theme.Dim;
            l.BackColor = back;
            l.Wrap = true;
            return l;
        }
    }

    public enum NavGlyph { Keyboard, Phrases, Shortcuts, Settings, About }

    /// <summary>
    /// Left-rail navigation: the app mark and name in the drag band, one item
    /// per page, and a footer the window fills in.
    /// </summary>
    public class Rail : Panel
    {
        public const int W = 196;
        public readonly List<NavItem> Items = new List<NavItem>();
        public event EventHandler<NavItem> Navigate;

        public Rail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Rail;
        }

        public NavItem Add(string text, NavGlyph glyph)
        {
            NavItem n = new NavItem();
            n.Text = text;
            n.Glyph = glyph;
            n.Font = Theme.Font(9.75f, FontStyle.Regular);
            n.SetBounds(8, 58 + Items.Count * 40, W - 16, 38);
            n.Click += delegate { Select(n); };
            Items.Add(n);
            Controls.Add(n);
            return n;
        }

        public void Select(NavItem n)
        {
            foreach (NavItem i in Items)
            {
                bool on = i == n;
                if (i.Selected != on) { i.Selected = on; i.Invalidate(); }
            }
            if (Navigate != null) Navigate(this, n);
        }

        /// <summary>The header is part of the window's drag band.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ChromeForm.WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point(unchecked((short)lp), unchecked((short)(lp >> 16))));
                if (p.Y < ChromeForm.CaptionHeight)
                {
                    m.Result = (IntPtr)ChromeForm.HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            MarkBox.Draw(g, new RectangleF(16, 7, 28, 28));
            using (Font f = Theme.Semi(10.9f))
                TextRenderer.DrawText(g, "Keycap", f, new Rectangle(54, 0, W - 60, ChromeForm.CaptionHeight),
                    Theme.Text, Theme.Left);
        }
    }

    public class NavItem : Drawn
    {
        public NavGlyph Glyph;
        public bool Selected;
        /// <summary>A count or a dot after the label - the phrase count, say.</summary>
        public string Badge = "";
        bool _hover;

        public NavItem()
        {
            BackColor = Theme.Rail;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (Selected || _hover)
                using (GraphicsPath p = Theme.Rounded(new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), 7f))
                using (SolidBrush b = new SolidBrush(Selected ? Theme.NavOn : Theme.NavHover))
                    g.FillPath(b, p);

            Color ink = Selected || _hover ? Theme.Text : Theme.Dim;
            NavIcons.Draw(g, Glyph, new RectangleF(12, (Height - 16) / 2f, 16, 16), ink);

            using (Font f = Selected ? Theme.Semi(Font.Size) : null)
                TextRenderer.DrawText(g, Text, f ?? Font, new Rectangle(39, 0, Width - 70, Height), ink, Theme.Left);

            if (Badge.Length > 0)
                using (Font fb = Theme.Font(8f, FontStyle.Regular))
                    TextRenderer.DrawText(g, Badge, fb, new Rectangle(Width - 40, 0, 30, Height),
                        Theme.Dimmer, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// Rail icons, drawn as 1.5px outlines on a 24-unit grid - the same weight
    /// and grid as Shotwin's - rather than taken from an icon font.
    /// </summary>
    public static class NavIcons
    {
        public static void Draw(Graphics g, NavGlyph glyph, RectangleF box, Color c)
        {
            List<PointF> dots = new List<PointF>();
            using (GraphicsPath p = new GraphicsPath())
            {
                switch (glyph)
                {
                    case NavGlyph.Keyboard:
                        using (GraphicsPath body = Theme.Rounded(new RectangleF(3f, 6.5f, 18f, 11f), 2f))
                            p.AddPath(body, false);
                        p.StartFigure(); p.AddLine(8.5f, 14f, 15.5f, 14f);
                        dots.AddRange(new PointF[] {
                            new PointF(7f, 10.3f), new PointF(10.3f, 10.3f),
                            new PointF(13.7f, 10.3f), new PointF(17f, 10.3f) });
                        break;

                    case NavGlyph.Phrases:
                        // A speech bubble with two lines of text in it.
                        p.AddArc(4f, 4.5f, 5f, 5f, 180, 90);
                        p.AddArc(15f, 4.5f, 5f, 5f, 270, 90);
                        p.AddArc(15f, 11.5f, 5f, 5f, 0, 90);
                        p.AddLine(12.5f, 16.5f, 8f, 20f);
                        p.AddLine(8f, 20f, 8f, 16.5f);
                        p.AddArc(4f, 11.5f, 5f, 5f, 90, 90);
                        p.CloseFigure();
                        p.StartFigure(); p.AddLine(8f, 9f, 16f, 9f);
                        p.StartFigure(); p.AddLine(8f, 12.3f, 13f, 12.3f);
                        break;

                    case NavGlyph.Shortcuts:
                        // The command key: a square whose sides run out into loops.
                        const float a = 9.2f, b = 14.8f, r = 2.6f;
                        p.AddLine(a, a - r, a, b + r); p.StartFigure();
                        p.AddLine(b, a - r, b, b + r); p.StartFigure();
                        p.AddLine(a - r, a, b + r, a); p.StartFigure();
                        p.AddLine(a - r, b, b + r, b); p.StartFigure();
                        p.AddArc(a - 2 * r, a - 2 * r, 2 * r, 2 * r, 0, -270); p.StartFigure();
                        p.AddArc(b, a - 2 * r, 2 * r, 2 * r, 180, 270); p.StartFigure();
                        p.AddArc(b, b, 2 * r, 2 * r, 270, 270); p.StartFigure();
                        p.AddArc(a - 2 * r, b, 2 * r, 2 * r, 0, 270);
                        break;

                    case NavGlyph.Settings:
                        Gear(p);
                        p.StartFigure(); p.AddEllipse(9f, 9f, 6f, 6f);
                        break;

                    case NavGlyph.About:
                        p.AddEllipse(3.5f, 3.5f, 17f, 17f);
                        p.StartFigure(); p.AddLine(12f, 11f, 12f, 16.5f);
                        dots.Add(new PointF(12f, 7.9f));
                        break;
                }

                float s = box.Width / 24f;
                using (Matrix m = new Matrix())
                {
                    m.Translate(box.X, box.Y);
                    m.Scale(s, s);
                    p.Transform(m);
                    PointF[] pts = dots.ToArray();
                    if (pts.Length > 0) m.TransformPoints(pts);
                    dots = new List<PointF>(pts);
                }

                using (Pen pen = new Pen(c, 1.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, p);
                }
                using (SolidBrush br = new SolidBrush(c))
                    foreach (PointF d in dots)
                        g.FillEllipse(br, d.X - 1.1f, d.Y - 1.1f, 2.2f, 2.2f);
            }
        }

        /// <summary>Eight teeth around a ring, as one closed outline.</summary>
        static void Gear(GraphicsPath p)
        {
            const float cx = 12f, cy = 12f, inner = 6.6f, outer = 9f;
            List<PointF> pts = new List<PointF>();
            for (int k = 0; k < 8; k++)
            {
                double at = k * Math.PI / 4;
                double[] offs = { -0.36, -0.2, 0.2, 0.36 };
                float[] rad = { inner, outer, outer, inner };
                for (int i = 0; i < 4; i++)
                    pts.Add(new PointF(cx + (float)(Math.Cos(at + offs[i]) * rad[i]),
                                       cy + (float)(Math.Sin(at + offs[i]) * rad[i])));
            }
            p.AddPolygon(pts.ToArray());
        }
    }
}
