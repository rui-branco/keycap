using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The app mark, drawn rather than loaded: GDI+ cannot decode our
    /// PNG-compressed .ico through Icon.ToBitmap.
    /// </summary>
    public class MarkBox : Control
    {
        public MarkBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float s = Math.Min(Width, Height);
            RectangleF r = new RectangleF((Width - s) / 2f + 0.5f, (Height - s) / 2f + 0.5f,
                                          s - 1f, s - 1f);
            using (GraphicsPath p = Theme.Rounded(r, s * 0.23f))
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                       Color.FromArgb(255, 125, 211, 255),
                       Color.FromArgb(255, 44, 138, 214), 90f))
                g.FillPath(b, p);

            using (FontFamily fam = new FontFamily("Segoe UI Symbol"))
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddString("⌘", fam, 0, 100f, new PointF(0, 0),
                             StringFormat.GenericTypographic);
                RectangleF b2 = gp.GetBounds();
                if (b2.Width > 0 && b2.Height > 0)
                {
                    float target = s * 0.62f;
                    float scale = Math.Min(target / b2.Width, target / b2.Height);
                    using (Matrix m = new Matrix())
                    {
                        m.Translate(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                        m.Scale(scale, scale);
                        m.Translate(-(b2.X + b2.Width / 2f), -(b2.Y + b2.Height / 2f));
                        gp.Transform(m);
                    }
                    Color ink = Color.FromArgb(255, 16, 34, 51);
                    using (Pen pen2 = new Pen(ink, Math.Max(1f, s * 0.045f)))
                    {
                        pen2.LineJoin = LineJoin.Round;
                        g.DrawPath(pen2, gp);
                    }
                    using (SolidBrush b3 = new SolidBrush(ink)) g.FillPath(b3, gp);
                }
            }
        }
    }

    /// <summary>
    /// The selected key, drawn as an actual keycap. Gives the inspector an
    /// anchor so it reads as "this key" rather than a floating line of text.
    /// </summary>
    public class CapPreview : Control
    {
        public string Label = "";
        public string TopLegend = "";
        public Color Tint = Theme.KeyPlain;
        public Color Edge = Theme.KeyEdge;

        public CapPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        public void Set(string label, string top, Color tint, Color edge)
        {
            Label = label; TopLegend = top; Tint = tint; Edge = edge;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, 8f))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                           ControlPaint.Light(Tint, 0.06f), Tint, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Edge, 1f)) g.DrawPath(pen, p);
            }

            if (!string.IsNullOrEmpty(TopLegend))
                using (Font ft = Theme.Mono(8f, FontStyle.Regular))
                    TextRenderer.DrawText(g, TopLegend, ft, new Rectangle(8, 7, Width - 14, 14),
                        Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Step down until the whole name fits - a key called "command"
            // must never render as "c...".
            Rectangle box = new Rectangle(7, Height - 30, Width - 13, 24);
            float[] sizes = new float[] { 14f, 12f, 10f, 8.5f, 7f, 6f };
            foreach (float size in sizes)
            {
                using (Font fm = Theme.Mono(size, FontStyle.Bold))
                {
                    bool fits = TextRenderer.MeasureText(Label, fm).Width <= box.Width;
                    if (fits || size == sizes[sizes.Length - 1])
                    {
                        TextRenderer.DrawText(g, Label, fm, box, Theme.Text,
                            TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>A labelled switch: pill, sliding knob, accent when on.</summary>
    public class ToggleChip : Control
    {
        public event EventHandler CheckedChanged;
        bool _checked, _hover;

        public ToggleChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            Height = 26;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (value == _checked) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public int PreferredWidth()
        {
            return TextRenderer.MeasureText(Text, Font).Width + 34 + 22;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Checked = !Checked; base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            bool bare = string.IsNullOrEmpty(Text);

            // With a label it is a pill with a switch on the end. On its own it
            // is just the switch - wrapping a switch in another pill is what
            // made these look like a control inside a control.
            float tw = bare ? Math.Min(44f, Width) : 38f;
            float th = bare ? Math.Min(24f, Height) : 20f;
            float tx = bare ? (Width - tw) / 2f : Width - tw - 11f;
            float ty = (Height - th) / 2f;

            if (!bare)
            {
                RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using (GraphicsPath p = Theme.Rounded(r, Height / 2f))
                {
                    using (SolidBrush b = new SolidBrush(_hover ? Theme.CardHi : Theme.Card))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.Border, 1f))
                        g.DrawPath(pen, p);
                }

                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(13, 0, Width - 13 - (int)tw - 16, Height),
                    _checked ? Theme.Text : Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // One flat colour for the track. Filling it and then stroking it in
            // a second tone left a two-tone edge at the rounded ends, which is
            // what read as bleeding.
            Color track = _checked
                ? (_hover ? Theme.Accent : Theme.AccentDark)
                : (_hover ? Theme.CardHi : Theme.Border);
            using (GraphicsPath p = Theme.Rounded(new RectangleF(tx, ty, tw, th), th / 2f))
            using (SolidBrush b = new SolidBrush(track))
                g.FillPath(b, p);

            float kd = th - 6f;
            float kx = _checked ? tx + tw - kd - 3f : tx + 3f;
            using (SolidBrush b = new SolidBrush(_checked ? Color.White : Theme.Dim))
                g.FillEllipse(b, kx, ty + 3f, kd, kd);
        }
    }

    /// <summary>The colour key for the board.</summary>
    public class Legend : Control
    {
        public Legend()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Height = 20;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            string[][] items = new string[][] {
                new string[] { "Modifier remap", "" },
                new string[] { "Scancode fix", "" },
                new string[] { "Media / volume", "" },
                new string[] { "Untouched", "" },
            };
            Color[] cols = new Color[] { Theme.Accent, Theme.Warn, Theme.Good, Theme.KeyEdge };

            int x = 0;
            using (Font f = Theme.Font(8.75f, FontStyle.Regular))
                for (int i = 0; i < items.Length; i++)
                {
                    using (SolidBrush b = new SolidBrush(cols[i]))
                    using (GraphicsPath p2 = Theme.Rounded(new RectangleF(x, Height / 2f - 5, 10, 10), 3f))
                        g.FillPath(b, p2);
                    x += 16;
                    Size sz = TextRenderer.MeasureText(items[i][0], f);
                    TextRenderer.DrawText(g, items[i][0], f,
                        new Rectangle(x, 0, sz.Width + 4, Height), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += sz.Width + 22;
                }
        }
    }

    /// <summary>The key's path, drawn as pills rather than a run of text.</summary>
    public class ChainView : Control
    {
        static readonly string ArrowGlyph = char.ConvertFromUtf32(0x2192);
        public string[] Steps = new string[0];

        public ChainView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 28;
        }

        public void Set(params string[] steps) { Steps = steps; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            float x = 0;
            using (Font f = Theme.Mono(8.5f, FontStyle.Regular))
            using (Font fa = Theme.Font(10f, FontStyle.Regular))
                for (int i = 0; i < Steps.Length; i++)
                {
                    bool last = i == Steps.Length - 1;
                    Size sz = TextRenderer.MeasureText(Steps[i], f);
                    float w = sz.Width + 22;
                    RectangleF r = new RectangleF(x, 1, w, Height - 2);

                    using (GraphicsPath p2 = Theme.Rounded(r, (Height - 2) / 2f))
                    {
                        using (SolidBrush b = new SolidBrush(last ? Theme.KeyModFill : Theme.Back))
                            g.FillPath(b, p2);
                        using (Pen pen = new Pen(last ? Theme.AccentDark : Theme.Border, 1f))
                            g.DrawPath(pen, p2);
                    }
                    TextRenderer.DrawText(g, Steps[i], f,
                        new Rectangle((int)x, 0, (int)w, Height),
                        last ? Theme.Accent : Theme.Dim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix);
                    x += w;

                    if (!last)
                    {
                        TextRenderer.DrawText(g, ArrowGlyph, fa,
                            new Rectangle((int)x, 0, 22, Height), Theme.Dimmer,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPrefix);
                        x += 22;
                    }
                }
        }
    }

    /// <summary>A hairline separator - the flat replacement for a card edge.</summary>
    public class Rule : Control
    {
        public Rule()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Height = 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using (SolidBrush b = new SolidBrush(Theme.Border))
                e.Graphics.FillRectangle(b, 0, 0, Width, 1);
        }
    }

    /// <summary>Dark menu colours, so the dropdown matches the window.</summary>
    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.AccentDark; } }
        public override Color MenuItemSelected { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHi; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHi; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    /// <summary>
    /// A pill that opens a themed menu. Windows' own ComboBox cannot be styled
    /// to match a dark window - it keeps a light border and a system arrow, and
    /// it clips its text - so this draws the closed state itself and uses a
    /// ToolStripDropDown for the list.
    /// </summary>
    public class DropChip : Control
    {
        public List<string> Items = new List<string>();
        public event EventHandler SelectionChanged;

        int _index = -1;
        bool _hover;
        ContextMenuStrip _menu;
        int _closedAt;          // tick when the menu last closed

        public DropChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (value == _index || value < 0 || value >= Items.Count) return;
                _index = value;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Set the selection without raising SelectionChanged.</summary>
        public void SetIndexQuiet(int value)
        {
            if (value < 0 || value >= Items.Count) return;
            _index = value;
            Invalidate();
        }

        public string SelectedText
        {
            get { return _index >= 0 && _index < Items.Count ? Items[_index] : ""; }
        }

        /// <summary>Width that fits the longest entry, so nothing is clipped.</summary>
        public int PreferredWidth()
        {
            int w = 0;
            foreach (string s in Items)
                w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
            return w + 26 + 22;     // text + caret + padding
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ShowMenu(this, new Point(0, Height + 2));
        }

        /// <summary>
        /// A click on the chip while the list is open should shut it. The menu
        /// has already closed itself by then (click-away), so without this the
        /// same click immediately reopens it and it never appears to close.
        /// </summary>
        bool JustClosed()
        {
            if (_menu != null && _menu.Visible) { _menu.Close(); return true; }
            return unchecked(Environment.TickCount - _closedAt) < 250;
        }

        /// <summary>Open the list anywhere - the spacebar uses this.</summary>
        public void ShowMenu(Control anchor, Point at)
        {
            if (Items.Count == 0) return;
            if (JustClosed()) return;

            if (_menu != null) _menu.Dispose();
            _menu = new ContextMenuStrip();
            _menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            _menu.BackColor = Theme.Card;
            _menu.ForeColor = Theme.Text;
            _menu.Font = Font;
            _menu.ShowImageMargin = false;

            for (int i = 0; i < Items.Count; i++)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(Items[i]);
                item.ForeColor = i == _index ? Theme.Accent : Theme.Text;
                item.BackColor = Theme.Card;
                int captured = i;
                item.Click += delegate { SelectedIndex = captured; };
                _menu.Items.Add(item);
            }
            _menu.Closed += delegate { _closedAt = Environment.TickCount; };
            _menu.Show(anchor, at);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, Height / 2f))
            {
                using (SolidBrush b = new SolidBrush(_hover ? Theme.CardHi : Theme.Card))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(_hover ? Theme.Dim : Theme.Border, 1f))
                    g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, SelectedText, Font,
                new Rectangle(13, 0, Width - 13 - 24, Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // caret
            float cx = Width - 15, cy = Height / 2f + 1, s = 3.5f;
            using (SolidBrush b = new SolidBrush(Theme.Dim))
                g.FillPolygon(b, new PointF[] {
                    new PointF(cx - s, cy - s * 0.6f),
                    new PointF(cx + s, cy - s * 0.6f),
                    new PointF(cx, cy + s * 0.7f)
                });
        }
    }
}
