using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Keycap
{
    public static class Theme
    {
        // Neutrals carry a slight blue bias rather than being pure grey, so
        // they sit under the accent instead of fighting it. Three ink levels
        // only - primary, secondary, tertiary - applied consistently.
        public static readonly Color Back = ColorTranslator.FromHtml("#15171B");
        public static readonly Color Card = ColorTranslator.FromHtml("#1D2026");
        public static readonly Color CardHi = ColorTranslator.FromHtml("#262A32");
        public static readonly Color Text = ColorTranslator.FromHtml("#E9ECF1");
        public static readonly Color Dim = ColorTranslator.FromHtml("#98A1AE");
        public static readonly Color Dimmer = ColorTranslator.FromHtml("#646C79");
        public static readonly Color Accent = ColorTranslator.FromHtml("#6EA8FE");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#3E7BD6");
        public static readonly Color Border = ColorTranslator.FromHtml("#2C313A");
        public static readonly Color Good = ColorTranslator.FromHtml("#5BC98C");
        public static readonly Color Warn = ColorTranslator.FromHtml("#DDA544");
        public static readonly Color Bad = ColorTranslator.FromHtml("#E4705F");
        public static readonly Color OnAccent = ColorTranslator.FromHtml("#0E1724");

        // Keycaps: one base, three tints. Each tint is the same lightness step
        // from the base, so no colour reads heavier than the others.
        public static readonly Color KeyTop = ColorTranslator.FromHtml("#272B33");
        public static readonly Color KeyBottom = ColorTranslator.FromHtml("#20242B");
        public static readonly Color KeyEdge = ColorTranslator.FromHtml("#343A44");
        public static readonly Color KeyPlain = ColorTranslator.FromHtml("#272B33");
        public static readonly Color KeyModFill = ColorTranslator.FromHtml("#1E2A3D");
        public static readonly Color KeyLayFill = ColorTranslator.FromHtml("#31291B");
        public static readonly Color KeyMedFill = ColorTranslator.FromHtml("#1A2C23");

        public static Font Font(float size, FontStyle style)
        {
            string[] prefs = new string[] { "Segoe UI Variable Display", "Segoe UI" };
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
            return new System.Drawing.Font(FontFamily.GenericSansSerif, size, style);
        }

        public static Font Mono(float size, FontStyle style)
        {
            string[] prefs = new string[] { "Cascadia Mono", "Consolas" };
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
            return new System.Drawing.Font(FontFamily.GenericMonospace, size, style);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Ask the desktop manager for a dark caption. Without this a dark
        /// window still gets a white title bar, which is the single thing that
        /// makes a themed app look unfinished.
        /// </summary>
        public static void DarkTitleBar(System.Windows.Forms.Form form)
        {
            try
            {
                int on = 1;
                // 20 on current builds, 19 on early Windows 10 - try both.
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
            }
            catch { }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
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
            BackColor = Theme.Back;
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
        }
    }

    public class FlatButton : Button
    {
        public bool Primary = false;
        public int Radius = 8;
        public Color Backdrop = Theme.Back;
        bool _hover;

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

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Backdrop);

            Color fill, fg;
            if (!Enabled)
            {
                fill = Theme.Card;
                fg = Theme.Dimmer;
            }
            else if (Primary)
            {
                fill = _hover ? Theme.Accent : Theme.AccentDark;
                fg = _hover ? Theme.OnAccent : Theme.Text;
            }
            else
            {
                fill = _hover ? Theme.CardHi : Theme.Card;
                fg = Theme.Text;
            }

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (!Primary)
                    using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Pill showing a coloured dot and a short status string.</summary>
    public class Pill : Control
    {
        public Color Dot = Theme.Dim;
        public string Label = "";

        public Pill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Height = 28;
        }

        public void Set(string label, Color dot)
        {
            Label = label;
            Dot = dot;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, Height / 2f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
            }
            using (SolidBrush b = new SolidBrush(Dot))
                g.FillEllipse(b, 11, Height / 2f - 3.5f, 7, 7);
            TextRenderer.DrawText(g, Label, Font,
                new Rectangle(24, 0, Width - 30, Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Battery meter: a cell outline, a fill, and the percentage.</summary>
    public class BatteryPill : Control
    {
        public int Percent = -1;
        public string DeviceName = "";
        /// <summary>Draw the meter alone, with no pill behind it.</summary>
        public bool Flat = false;

        public BatteryPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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

            float x = Flat ? 0 : 12, h = 13, y = (Height - h) / 2f, w = 26;
            Color fill = Theme.Good;
            if (Percent >= 0 && Percent <= 10) fill = Theme.Bad;
            else if (Percent >= 0 && Percent <= 25) fill = Theme.Warn;

            using (Pen pen = new Pen(Percent < 0 ? Theme.Dimmer : Theme.Dim, 1.4f))
            using (GraphicsPath p = Theme.Rounded(new RectangleF(x, y, w, h), 3f))
                g.DrawPath(pen, p);
            using (SolidBrush b = new SolidBrush(Percent < 0 ? Theme.Dimmer : Theme.Dim))
                g.FillRectangle(b, x + w + 1.5f, y + 4, 2.5f, h - 8);

            if (Percent >= 0)
            {
                float inner = (w - 4) * (Percent / 100f);
                if (inner < 2) inner = 2;
                using (SolidBrush b = new SolidBrush(fill))
                using (GraphicsPath p = Theme.Rounded(new RectangleF(x + 2, y + 2, inner, h - 4), 1.5f))
                    g.FillPath(b, p);
            }

            string text = Percent < 0
                ? "no battery data"
                : Percent.ToString() + "%   " + DeviceName;
            TextRenderer.DrawText(g, text, Font,
                new Rectangle((int)(x + w + 10), 0, Width - (int)(x + w) - 14, Height),
                Percent < 0 ? Theme.Dim : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Two-column key/value list, owner drawn.</summary>
    public class PairList : Control
    {
        public System.Collections.Generic.List<string[]> Items =
            new System.Collections.Generic.List<string[]>();
        public int KeyWidth = 150;

        const int RowH = 20;
        int _scroll;          // first visible row

        int VisibleRows { get { return Math.Max(1, (Height - 4) / RowH); } }
        int MaxScroll { get { return Math.Max(0, Items.Count - VisibleRows); } }

        public void ResetScroll() { _scroll = 0; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Items.Count <= VisibleRows) return;
            int step = e.Delta > 0 ? -1 : 1;
            int next = Math.Min(MaxScroll, Math.Max(0, _scroll + step * 2));
            if (next == _scroll) return;
            _scroll = next;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (Items.Count > VisibleRows) Focus();   // so the wheel reaches us
        }

        public PairList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int y = 2;
            int first = Math.Min(_scroll, MaxScroll);
            int shown = 0;
            using (Font fm = Theme.Mono(8.25f, FontStyle.Regular))
            using (Font fb = Theme.Font(8.75f, FontStyle.Regular))
            {
                for (int i = first; i < Items.Count; i++)
                {
                    if (y + RowH > Height) break;
                    string[] row = Items[i];
                    shown++;
                    TextRenderer.DrawText(g, row[0], fm, new Rectangle(0, y, KeyWidth, 18),
                        Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, row[1], fb,
                        new Rectangle(KeyWidth + 8, y, Width - KeyWidth - 10, 18),
                        Theme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    y += RowH;
                }

            }

            // Slim scroll indicator, only while there is more to see.
            if (Items.Count > VisibleRows)
            {
                float track = Height - 4;
                float thumb = Math.Max(24f, track * VisibleRows / Items.Count);
                float pos = MaxScroll == 0 ? 0 : (track - thumb) * first / MaxScroll;
                using (SolidBrush b = new SolidBrush(Theme.Border))
                    g.FillRectangle(b, Width - 3, 2 + pos, 3, thumb);
            }
        }
    }
}
