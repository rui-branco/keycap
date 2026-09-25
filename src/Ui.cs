using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Keycap
{
    public static class Theme
    {
        // The same surface ladder as Shotwin and Playbar, darkest to lightest,
        // so the apps read as one family: rail, page, raised card, field, chip.
        // Three ink levels only - primary, secondary, tertiary.
        public static readonly Color Rail = ColorTranslator.FromHtml("#161619");
        public static readonly Color Back = ColorTranslator.FromHtml("#1B1B1F");
        public static readonly Color Card = ColorTranslator.FromHtml("#212126");
        public static readonly Color Field = ColorTranslator.FromHtml("#26262B");
        public static readonly Color CardHi = ColorTranslator.FromHtml("#2E2E35");
        public static readonly Color Hover = ColorTranslator.FromHtml("#3A3A44");
        public static readonly Color NavOn = ColorTranslator.FromHtml("#2E2E38");
        public static readonly Color NavHover = ColorTranslator.FromHtml("#222228");
        public static readonly Color Border = ColorTranslator.FromHtml("#31313A");
        public static readonly Color BorderHi = ColorTranslator.FromHtml("#4A4A56");
        public static readonly Color Text = ColorTranslator.FromHtml("#E7E7EA");
        public static readonly Color Dim = ColorTranslator.FromHtml("#8A8A93");
        public static readonly Color Dimmer = ColorTranslator.FromHtml("#62626C");
        public static readonly Color Accent = ColorTranslator.FromHtml("#3D8BFD");
        public static readonly Color AccentHi = ColorTranslator.FromHtml("#5AA0FF");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#2F66C0");
        public static readonly Color AccentSoft = ColorTranslator.FromHtml("#1E3255");
        public static readonly Color OnAccent = Color.White;
        public static readonly Color Good = ColorTranslator.FromHtml("#4CC38A");
        public static readonly Color Warn = ColorTranslator.FromHtml("#FF9F0A");
        public static readonly Color Bad = ColorTranslator.FromHtml("#FF6B5E");
        public static readonly Color CloseRed = ColorTranslator.FromHtml("#E81123");

        // Keycaps: one base, three tints. Each tint is the same lightness step
        // from the base, so no colour reads heavier than the others.
        public static readonly Color KeyEdge = ColorTranslator.FromHtml("#3A3A42");
        public static readonly Color KeyPlain = ColorTranslator.FromHtml("#2B2B31");
        public static readonly Color KeyModFill = ColorTranslator.FromHtml("#1C2A42");
        public static readonly Color KeyLayFill = ColorTranslator.FromHtml("#33291A");
        public static readonly Color KeyMedFill = ColorTranslator.FromHtml("#1A2E25");

        /// <summary>Body text. The Variable "Text" cut is drawn for these sizes.</summary>
        public static Font Font(float size, FontStyle style)
        {
            return Pick(new string[] { "Segoe UI Variable Text", "Segoe UI" },
                        size, style, FontFamily.GenericSansSerif);
        }

        /// <summary>
        /// Semibold, the weight every heading in the family uses. GDI has no
        /// semibold style bit, so it is its own family name.
        /// </summary>
        public static Font Semi(float size)
        {
            return Pick(new string[] { "Segoe UI Variable Text Semibold", "Segoe UI Semibold" },
                        size, FontStyle.Regular, FontFamily.GenericSansSerif);
        }

        public static Font Mono(float size, FontStyle style)
        {
            return Pick(new string[] { "Cascadia Mono", "Consolas" },
                        size, style, FontFamily.GenericMonospace);
        }

        /// <summary>
        /// Windows' own icon font - the glyphs its title bars are drawn with.
        /// Segoe Fluent Icons on 11, the same code points in MDL2 on 10.
        /// </summary>
        public static Font Icons(float size)
        {
            return Pick(new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" },
                        size, FontStyle.Regular, FontFamily.GenericSansSerif);
        }

        static Font Pick(string[] prefs, float size, FontStyle style, FontFamily fallback)
        {
            foreach (string name in prefs)
            {
                try
                {
                    Font f = new Font(name, size, style);
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new System.Drawing.Font(fallback, size, style);
        }

        /// <summary>Mix two colours; t = 0 is a, t = 1 is b.</summary>
        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>
        /// Ask the desktop manager for a dark frame, rounded corners and a
        /// border that matches the page. Without this a dark window still gets
        /// a white title bar and a pale outline, which is the single thing that
        /// makes a themed app look unfinished.
        /// </summary>
        public static void DarkTitleBar(Form form)
        {
            try
            {
                int on = 1;
                // 20 on current builds, 19 on early Windows 10 - try both.
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref on, 4);

                // Windows 11 only; older builds reject these and nothing changes.
                int round = 2;                                  // DWMWCP_ROUND
                DwmSetWindowAttribute(form.Handle, 33, ref round, 4);
                int border = ColorTranslator.ToWin32(Border);   // COLORREF
                DwmSetWindowAttribute(form.Handle, 34, ref border, 4);
            }
            catch { }
        }

        /// <summary>
        /// Dark scrollbars and edit chrome for a native control. The only way to
        /// theme a Win32 scrollbar at all; a no-op before Windows 10 1809.
        /// </summary>
        public static void DarkControl(Control c)
        {
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch { }
        }

        /// <summary>
        /// A tooltip that matches the window. The stock one is a pale yellow
        /// box with dark text - invisible against everything else here.
        /// </summary>
        public static ToolTip Tip()
        {
            ToolTip t = new ToolTip();
            t.OwnerDraw = true;
            Font f = Font(8.75f, FontStyle.Regular);
            t.Popup += delegate(object s, PopupEventArgs e)
            {
                Size sz = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl), f);
                e.ToolTipSize = new Size(sz.Width + 20, sz.Height + 12);
            };
            t.Draw += delegate(object s, DrawToolTipEventArgs e)
            {
                e.Graphics.Clear(ColorTranslator.FromHtml("#2A2A31"));
                using (Pen pen = new Pen(ColorTranslator.FromHtml("#3C3C47")))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, f, e.Bounds, Text, Center);
            };
            return t;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top
            | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
    }

    /// <summary>Base for every owner-drawn control: double buffered, no flicker.</summary>
    public class Drawn : Control
    {
        public Drawn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>Rounded surface used to group controls.</summary>
    public class Card : Panel
    {
        public int Radius = 10;
        public bool Outline = false;

        Color _fill = Theme.Card;

        /// <summary>
        /// The card's surface. Setting it also sets BackColor, so controls
        /// placed on the card clear to the card rather than to the page - that
        /// mismatch is what made the background bleed through their corners.
        /// </summary>
        public Color Fill
        {
            get { return _fill; }
            set { _fill = value; BackColor = value; Invalidate(); }
        }

        /// <summary>What sits behind this card - shows at the rounded corners.</summary>
        public Color Page = Theme.Back;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = _fill;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Clear to the page, so the rounded corners blend with what is
            // behind the card rather than showing a square of card colour.
            g.Clear(Page);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(Fill)) g.FillPath(b, p);
                if (Outline)
                    using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
            }
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// Settings rows grouped on one card and divided by hairlines: what the
    /// setting is, what it does in a sentence under it, and its control at the
    /// right. A one-word label is never left to carry the meaning alone.
    /// </summary>
    public class RowCard : Card
    {
        class Row { public string Title, Detail; public Control Action; public int Top, Height, TextW; }
        readonly System.Collections.Generic.List<Row> _rows = new System.Collections.Generic.List<Row>();
        const int PadX = 18, PadY = 13;
        readonly Font _title = Theme.Font(9.75f, FontStyle.Regular);
        readonly Font _detail = Theme.Font(8.75f, FontStyle.Regular);

        /// <summary>Add a row. The action is optional, and keeps the size it has.</summary>
        public void Add(string title, string detail, Control action)
        {
            Row r = new Row();
            r.Title = title;
            r.Detail = detail ?? "";
            r.Action = action;
            if (action != null)
            {
                action.BackColor = Fill;
                Controls.Add(action);
            }
            _rows.Add(r);
        }

        /// <summary>Change a row's text after the fact - a status that moves on.</summary>
        public void SetText(int row, string title, string detail)
        {
            _rows[row].Title = title;
            _rows[row].Detail = detail ?? "";
            if (Width > 0) Arrange(Width);
            Invalidate();
        }

        /// <summary>Lay the rows out at this width, and return the height the card needs.</summary>
        public int Arrange(int width)
        {
            Width = width;
            int y = 0;
            foreach (Row r in _rows)
            {
                // Not Action.Visible: that reads false while the page is hidden,
                // which laid the text out under the button.
                int aw = r.Action != null ? r.Action.Width : 0;
                r.TextW = Math.Max(80, width - PadX * 2 - (aw > 0 ? aw + 28 : 0));
                int th = TextRenderer.MeasureText("Ag", _title).Height;
                int dh = r.Detail.Length == 0 ? 0 : TextRenderer.MeasureText(r.Detail, _detail,
                    new Size(r.TextW, 10000), Theme.Wrap).Height;
                int h = PadY * 2 + th + (dh > 0 ? 2 + dh : 0);
                if (r.Action != null) h = Math.Max(h, r.Action.Height + PadY * 2);
                r.Top = y;
                r.Height = h;
                if (r.Action != null)
                    r.Action.Location = new Point(width - PadX - r.Action.Width, y + (h - r.Action.Height) / 2);
                y += h;
            }
            Height = y;
            Invalidate();
            return y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int th = TextRenderer.MeasureText("Ag", _title).Height;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (i > 0)
                    using (SolidBrush b = new SolidBrush(Theme.Border))
                        g.FillRectangle(b, PadX, r.Top, Width - PadX * 2, 1);

                int dh = r.Detail.Length == 0 ? 0 : TextRenderer.MeasureText(r.Detail, _detail,
                    new Size(r.TextW, 10000), Theme.Wrap).Height;
                int block = th + (dh > 0 ? 2 + dh : 0);
                int y = r.Top + (r.Height - block) / 2;
                TextRenderer.DrawText(g, r.Title, _title, new Rectangle(PadX, y, r.TextW, th),
                    Theme.Text, Theme.Left);
                if (dh > 0)
                    TextRenderer.DrawText(g, r.Detail, _detail, new Rectangle(PadX, y + th + 2, r.TextW, dh),
                        Theme.Dim, Theme.Wrap);
            }
        }
    }

    /// <summary>
    /// Reference rows on a card: a key or combination drawn as a keycap, and
    /// what it does beside it. The heading sits inside the card, on top.
    /// </summary>
    public class PairCard : Card
    {
        public readonly System.Collections.Generic.List<string[]> Items =
            new System.Collections.Generic.List<string[]>();
        public string Heading = "";
        /// <summary>Where the description column starts.</summary>
        public int KeyWidth = 170;

        const int PadX = 18, PadTop = 14, RowH = 31;
        readonly Font _key = Theme.Mono(8.25f, FontStyle.Regular);
        readonly Font _text = Theme.Font(9f, FontStyle.Regular);
        readonly Font _head = Theme.Semi(8f);

        int HeadH { get { return Heading.Length > 0 ? 28 : 0; } }

        /// <summary>Size the card for its rows at this width, and return its height.</summary>
        public int Arrange(int width)
        {
            Width = width;
            Height = PadTop + HeadH + Math.Max(1, Items.Count) * RowH + 10;
            Invalidate();
            return Height;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (HeadH > 0)
                TextRenderer.DrawText(g, Heading.ToUpperInvariant(), _head,
                    new Rectangle(PadX, PadTop - 2, Width - PadX * 2, 20), Theme.Dim, Theme.Left);

            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, "Nothing here yet.", _text,
                    new Rectangle(PadX, PadTop + HeadH, Width - PadX * 2, RowH), Theme.Dimmer, Theme.Left);
                return;
            }

            int y = PadTop + HeadH;
            foreach (string[] row in Items)
            {
                int kw = Math.Min(KeyWidth - 12, TextRenderer.MeasureText(row[0], _key).Width + 14);
                RectangleF chip = new RectangleF(PadX + 0.5f, y + 4.5f, kw, RowH - 10);
                using (GraphicsPath p = Theme.Rounded(chip, 5f))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Field)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, row[0], _key, Rectangle.Round(chip), Theme.Text,
                    Theme.Center | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row[1], _text,
                    new Rectangle(PadX + KeyWidth, y, Width - PadX * 2 - KeyWidth, RowH), Theme.Dim, Theme.Left);
                y += RowH;
            }
        }
    }

    /// <summary>
    /// The family's buttons. A chip by default - a flat, borderless fill -
    /// Primary for the one action a view is for, Ghost for a text link.
    /// </summary>
    public class FlatButton : Button
    {
        public bool Primary = false;
        public bool Ghost = false;
        public int Radius = 7;
        public Color Backdrop = Theme.Back;
        /// <summary>A shortcut drawn in its own translucent key, after the label.</summary>
        public string Hint = "";
        bool _hover, _down;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            Height = 34;
        }

        /// <summary>Width that fits the label with the family's padding.</summary>
        public int PreferredWidth()
        {
            // A link is its text alone, so it lines up with the text above it.
            if (Ghost) return TextRenderer.MeasureText(Text, Font).Width + 4;
            int w = TextRenderer.MeasureText(Text, Font).Width + 30;
            if (Hint.Length > 0)
                using (Font f = Theme.Semi(Font.Size * 0.86f))
                    w += TextRenderer.MeasureText(Hint, f).Width + 16;
            return Math.Max(Ghost ? 0 : 72, w);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override bool ShowFocusCues { get { return false; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Backdrop);

            if (Ghost)
            {
                Color ink = !Enabled ? Theme.Dimmer : (_hover ? Theme.AccentHi : Theme.Accent);
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, Theme.Left);
                return;
            }

            Color fill, fg;
            if (Primary)
            {
                fill = _down ? Theme.AccentDark : (_hover ? Theme.AccentHi : Theme.Accent);
                fg = Theme.OnAccent;
            }
            else
            {
                fill = _down ? Theme.Border : (_hover ? Theme.Hover : Theme.CardHi);
                fg = Theme.Text;
            }
            if (!Enabled)
            {
                // The family greys a disabled control by fading it into what is
                // behind it, not by swapping in a separate colour.
                fill = Theme.Blend(Backdrop, fill, 0.45f);
                fg = Theme.Blend(Backdrop, fg, 0.4f);
            }

            RectangleF r = new RectangleF(0, 0, Width - 0.5f, Height - 0.5f);
            using (GraphicsPath p = Theme.Rounded(r, Radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            if (Hint.Length == 0)
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, Theme.Center);
                return;
            }

            // Label and key centred together as one group.
            using (Font fk = Theme.Semi(Font.Size * 0.86f))
            {
                Size ts = TextRenderer.MeasureText(Text, Font);
                Size ks = TextRenderer.MeasureText(Hint, fk);
                int kw = ks.Width + 8, gap = 8;
                int x = (Width - ts.Width - gap - kw) / 2;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, ts.Width, Height), fg, Theme.Center);
                RectangleF kr = new RectangleF(x + ts.Width + gap, (Height - 20) / 2f, kw, 20);
                using (GraphicsPath p = Theme.Rounded(kr, 4f))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Enabled ? 0x2B : 0x14, 255, 255, 255)))
                    g.FillPath(b, p);
                TextRenderer.DrawText(g, Hint, fk, Rectangle.Round(kr),
                    Color.FromArgb(Enabled ? 0xD8 : 0x60, fg), Theme.Center);
            }
        }
    }

    /// <summary>Battery meter: a cell outline, a fill, and the percentage.</summary>
    public class BatteryPill : Drawn
    {
        public int Percent = -1;
        public string DeviceName = "";
        /// <summary>Draw the meter alone, with no pill behind it.</summary>
        public bool Flat = false;
        /// <summary>Percentage only - for a header too narrow for the name.</summary>
        public bool Compact = false;

        /// <summary>Width this needs to show its text without clipping.</summary>
        public int PreferredWidth()
        {
            int meter = Flat ? 0 : 12;
            return meter + 26 + 10 + TextRenderer.MeasureText(Caption, Font).Width + 14;
        }

        string Caption
        {
            get
            {
                if (Percent < 0) return Compact ? "--" : "no battery data";
                if (Compact) return Percent.ToString() + "%";
                return Percent.ToString() + "%   " + DeviceName;
            }
        }

        public BatteryPill()
        {
            BackColor = Theme.Back;
            Height = 28;
        }

        public void Set(string name, int percent)
        {
            DeviceName = name;
            Percent = percent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (!Flat)
            {
                RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (GraphicsPath p = Theme.Rounded(r, Height / 2f))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
                }
            }

            float x = Flat ? 1 : 12, h = 12, y = (Height - h) / 2f, w = 23;
            Color fill = Theme.Good;
            if (Percent >= 0 && Percent <= 10) fill = Theme.Bad;
            else if (Percent >= 0 && Percent <= 25) fill = Theme.Warn;

            using (Pen pen = new Pen(Percent < 0 ? Theme.Dimmer : Theme.Dim, 1.3f))
            using (GraphicsPath p = Theme.Rounded(new RectangleF(x, y, w, h), 3f))
                g.DrawPath(pen, p);
            using (SolidBrush b = new SolidBrush(Percent < 0 ? Theme.Dimmer : Theme.Dim))
                g.FillRectangle(b, x + w + 1.5f, y + 3.5f, 2f, h - 7);

            if (Percent >= 0)
            {
                float inner = (w - 4) * (Percent / 100f);
                if (inner < 2) inner = 2;
                using (SolidBrush b = new SolidBrush(fill))
                using (GraphicsPath p = Theme.Rounded(new RectangleF(x + 2, y + 2, inner, h - 4), 1.5f))
                    g.FillPath(b, p);
            }

            TextRenderer.DrawText(g, Caption, Font,
                new Rectangle((int)(x + w + 10), 0, Width - (int)(x + w) - 10, Height),
                Percent < 0 ? Theme.Dim : Theme.Text, Theme.Left);
        }
    }

    /// <summary>
    /// A label that draws itself, so it takes its background from the parent
    /// instead of painting a rectangle of its own colour.
    /// </summary>
    public class TextLine : Drawn
    {
        public Color Ink = Theme.Text;
        public bool Wrap = false;
        public bool AlignRight = false;

        public TextLine()
        {
            BackColor = Theme.Back;
            Height = 18;
        }

        /// <summary>Height the text needs at the current width.</summary>
        public int Measure()
        {
            if (!Wrap) return TextRenderer.MeasureText(Text, Font).Height;
            return TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, Width), 10000),
                Theme.Wrap).Height;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextFormatFlags flags = Wrap ? Theme.Wrap : Theme.Left;
            if (AlignRight) flags = (flags & ~TextFormatFlags.Left) | TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Ink, flags);
        }
    }

    /// <summary>
    /// A vertically scrolling area with the family's thin overlay bar: no
    /// arrows, no track, a 6px thumb riding the edge, and only while there is
    /// more to see. Children go in Content, whose height the owner sets.
    /// </summary>
    public class ScrollPage : Panel
    {
        public readonly Panel Content;
        readonly ScrollThumb _thumb;
        int _offset;

        public ScrollPage()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Back;

            Content = new BufferedPanel();
            Content.BackColor = Theme.Back;
            Controls.Add(Content);

            _thumb = new ScrollThumb(this);
            Controls.Add(_thumb);
            _thumb.BringToFront();
        }

        public int Offset { get { return _offset; } }
        public int MaxOffset { get { return Math.Max(0, Content.Height - Height); } }

        public void ScrollTo(int y)
        {
            int next = Math.Max(0, Math.Min(MaxOffset, y));
            _offset = next;
            Content.Top = -next;
            _thumb.Visible = MaxOffset > 0;
            _thumb.Invalidate();
        }

        /// <summary>Call after changing Content's height.</summary>
        public void Sync()
        {
            Content.Width = Width;
            ScrollTo(_offset);
            _thumb.SetBounds(Width - 10, 0, 10, Height);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Sync();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Three lines at a time, the way the system scrolls a list.
            ScrollTo(_offset - Math.Sign(e.Delta) * 60);
            // Wheel messages land on whatever is under the pointer - usually a
            // row deep inside. What a child leaves unhandled travels up to its
            // parent, which is how it arrives here.
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                       | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        class ScrollThumb : Drawn
        {
            readonly ScrollPage _owner;
            bool _hover, _drag;
            int _grabY, _grabOffset;

            public ScrollThumb(ScrollPage owner)
            {
                _owner = owner;
                BackColor = Theme.Back;
                Visible = false;
            }

            RectangleF Thumb()
            {
                float track = Height - 8;
                int content = Math.Max(1, _owner.Content.Height);
                float h = Math.Max(32f, track * _owner.Height / content);
                float y = 4 + (_owner.MaxOffset == 0 ? 0 : (track - h) * _owner.Offset / _owner.MaxOffset);
                return new RectangleF(Width - 8, y, 6, h);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                RectangleF t = Thumb();
                if (e.Y >= t.Top && e.Y <= t.Bottom)
                {
                    _drag = true; _grabY = e.Y; _grabOffset = _owner.Offset;
                    Capture = true;
                }
                else
                {
                    // A click on the empty track pages towards it.
                    _owner.ScrollTo(_owner.Offset + (e.Y < t.Top ? -1 : 1) * _owner.Height * 9 / 10);
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_drag)
                {
                    RectangleF t = Thumb();
                    float track = Height - 8 - t.Height;
                    if (track > 0)
                        _owner.ScrollTo(_grabOffset + (int)((e.Y - _grabY) * _owner.MaxOffset / track));
                }
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _drag = false; Capture = false; Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(BackColor);
                if (_owner.MaxOffset == 0) return;
                using (GraphicsPath p = Theme.Rounded(Thumb(), 3f))
                using (SolidBrush b = new SolidBrush(_hover || _drag
                           ? ColorTranslator.FromHtml("#5C5C69")
                           : ColorTranslator.FromHtml("#43434E")))
                    g.FillPath(b, p);
            }
        }
    }

    /// <summary>
    /// A confirmation that floats over the bottom of a page for a few seconds,
    /// rather than holding a row of its own that leaves a gap when it goes.
    /// </summary>
    public class Toast : Drawn
    {
        public Color Ink = Theme.Dim;
        readonly Timer _timer = new Timer();

        public Toast()
        {
            BackColor = Theme.Back;
            Font = Theme.Font(8.75f, FontStyle.Regular);
            Visible = false;
            Height = 32;
            _timer.Tick += delegate { _timer.Stop(); Visible = false; };
        }

        public void Say(string text, Color ink)
        {
            Text = text;
            Ink = ink;
            Width = TextRenderer.MeasureText(text, Font).Width + 34;
            if (Parent != null)
                Left = CenterLeft + (Parent.ClientSize.Width - CenterLeft - Width) / 2;
            Visible = true;
            BringToFront();
            Invalidate();
            _timer.Stop();
            _timer.Interval = Math.Max(2600, 900 + text.Length * 45);
            _timer.Start();
        }

        /// <summary>Where the area it centres in starts - past the rail, in the window.</summary>
        public int CenterLeft;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, 6f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
            }
            float d = 6;
            using (SolidBrush b = new SolidBrush(Ink))
                g.FillEllipse(b, 13, (Height - d) / 2f, d, d);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(25, 0, Width - 31, Height),
                Ink == Theme.Dim ? Theme.Dim : Theme.Text, Theme.Left);
        }
    }
}
