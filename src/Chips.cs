using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The app mark, drawn rather than loaded: GDI+ cannot decode our
    /// PNG-compressed .ico through Icon.ToBitmap.
    /// </summary>
    public class MarkBox : Drawn
    {
        public MarkBox()
        {
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            float s = Math.Min(Width, Height);
            Draw(e.Graphics, new RectangleF((Width - s) / 2f, (Height - s) / 2f, s, s));
        }

        /// <summary>The mark inside a square - the rail header draws it this way.</summary>
        public static void Draw(Graphics g, RectangleF box)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float s = Math.Min(box.Width, box.Height);
            RectangleF r = new RectangleF(box.X + 0.5f, box.Y + 0.5f, s - 1f, s - 1f);
            using (GraphicsPath p = Theme.Rounded(r, s * 0.25f))
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
                    float target = s * 0.6f;
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
            g.SmoothingMode = old;
        }
    }

    /// <summary>
    /// The selected key, drawn as an actual keycap. Gives the inspector an
    /// anchor so it reads as "this key" rather than a floating line of text.
    /// </summary>
    public class CapPreview : Drawn
    {
        public string Label = "";
        public string TopLegend = "";
        public Color Tint = Theme.KeyPlain;
        public Color Edge = Theme.KeyEdge;

        public CapPreview()
        {
            BackColor = Theme.Card;
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

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 2.5f);
            // A darker lip under the cap, so it sits on the card like a key.
            using (GraphicsPath lip = Theme.Rounded(new RectangleF(r.X, r.Y + 2, r.Width, r.Height), 9f))
            using (SolidBrush b = new SolidBrush(Theme.Blend(Tint, Color.Black, 0.35f)))
                g.FillPath(b, lip);
            using (GraphicsPath p = Theme.Rounded(r, 9f))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                           ControlPaint.Light(Tint, 0.08f), Tint, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Edge, 1f)) g.DrawPath(pen, p);
            }

            if (!string.IsNullOrEmpty(TopLegend))
                using (Font ft = Theme.Mono(9f, FontStyle.Regular))
                    TextRenderer.DrawText(g, TopLegend, ft, new Rectangle(10, 8, Width - 18, 16),
                        Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            // Step down until the whole name fits - a key called "command"
            // must never render as "c...".
            Rectangle box = new Rectangle(9, Height - 34, Width - 16, 26);
            float[] sizes = new float[] { 15f, 13f, 11f, 9f, 7.5f, 6f };
            foreach (float size in sizes)
            {
                using (Font fm = Theme.Mono(size, FontStyle.Bold))
                {
                    bool fits = TextRenderer.MeasureText(Label, fm).Width <= box.Width;
                    if (fits || size == sizes[sizes.Length - 1])
                    {
                        TextRenderer.DrawText(g, Label, fm, box, Theme.Text,
                            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// A switch, the Windows 11 way: an outlined track with a small knob when
    /// off, a filled accent track with a white knob when on. With Text it is
    /// the label and the switch together, and the whole width is clickable.
    /// </summary>
    public class ToggleChip : Drawn
    {
        public event EventHandler CheckedChanged;
        bool _checked, _hover, _down;

        public ToggleChip()
        {
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

        /// <summary>Set the state without raising CheckedChanged.</summary>
        public void SetQuiet(bool value)
        {
            if (value == _checked) return;
            _checked = value;
            Invalidate();
        }

        public int PreferredWidth()
        {
            if (string.IsNullOrEmpty(Text)) return 40;
            return TextRenderer.MeasureText(Text, Font).Width + 12 + 40;
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool click = _down && ClientRectangle.Contains(e.Location);
            _down = false;
            if (click) Checked = !Checked;
            else Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            const float tw = 40f, th = 20f;
            float tx = Width - tw - 0.5f, ty = (Height - th) / 2f;

            if (!string.IsNullOrEmpty(Text))
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(0, 0, (int)tx - 10, Height),
                    _checked || _hover ? Theme.Text : Theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            RectangleF track = new RectangleF(tx, ty, tw, th);
            using (GraphicsPath p = Theme.Rounded(track, th / 2f))
            {
                if (_checked)
                    using (SolidBrush b = new SolidBrush(_hover ? Theme.AccentHi : Theme.Accent))
                        g.FillPath(b, p);
                else
                {
                    using (SolidBrush b = new SolidBrush(_hover ? Theme.Hover : Theme.Field))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.Dim, 1f)) g.DrawPath(pen, p);
                }
            }

            // The knob grows under the pointer and stretches while pressed.
            float kd = _hover ? 14f : 12f;
            float kw = _down ? kd + 3f : kd;
            float kx = _checked ? tx + tw - (th - kd) / 2f - kw : tx + (th - kd) / 2f;
            RectangleF knob = new RectangleF(kx, ty + (th - kd) / 2f, kw, kd);
            using (GraphicsPath p = Theme.Rounded(knob, kd / 2f))
            using (SolidBrush b = new SolidBrush(_checked ? Color.White : Theme.Text))
                g.FillPath(b, p);
        }
    }

    /// <summary>The colour key for the board.</summary>
    public class Legend : Drawn
    {
        public Legend()
        {
            BackColor = Theme.Back;
            Height = 20;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            string[] items = new string[] { "Modifier remap", "Scancode fix", "Media and volume", "Untouched" };
            Color[] cols = new Color[] { Theme.Accent, Theme.Warn, Theme.Good, Theme.KeyEdge };

            int x = 0;
            using (Font f = Theme.Font(8.75f, FontStyle.Regular))
                for (int i = 0; i < items.Length; i++)
                {
                    using (SolidBrush b = new SolidBrush(cols[i]))
                        g.FillEllipse(b, x, Height / 2f - 4, 8, 8);
                    x += 14;
                    Size sz = TextRenderer.MeasureText(items[i], f);
                    TextRenderer.DrawText(g, items[i], f,
                        new Rectangle(x, 0, sz.Width + 4, Height), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += sz.Width + 20;
                }
        }
    }

    /// <summary>The key's path, drawn as pills rather than a run of text.</summary>
    public class ChainView : Drawn
    {
        static readonly string ArrowGlyph = char.ConvertFromUtf32(0x2192);
        public string[] Steps = new string[0];

        public ChainView()
        {
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
                    float w = sz.Width + 20;
                    RectangleF r = new RectangleF(x + 0.5f, 1.5f, w - 1, Height - 3);

                    using (GraphicsPath p2 = Theme.Rounded(r, 6f))
                    {
                        using (SolidBrush b = new SolidBrush(last ? Theme.AccentSoft : Theme.Field))
                            g.FillPath(b, p2);
                        using (Pen pen = new Pen(last ? Theme.AccentDark : Theme.Border, 1f))
                            g.DrawPath(pen, p2);
                    }
                    TextRenderer.DrawText(g, Steps[i], f,
                        new Rectangle((int)x, 0, (int)w, Height),
                        last ? Theme.AccentHi : Theme.Dim, Theme.Center);
                    x += w;

                    if (!last)
                    {
                        TextRenderer.DrawText(g, ArrowGlyph, fa,
                            new Rectangle((int)x, 0, 22, Height), Theme.Dimmer, Theme.Center);
                        x += 22;
                    }
                }
        }
    }

    /// <summary>A hairline separator.</summary>
    public class Rule : Drawn
    {
        public Rule()
        {
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

    /// <summary>
    /// Menus in the family style: a dark popover with a hairline edge, rounded
    /// by the desktop manager on Windows 11, and an accent pill under the item
    /// the pointer is on.
    /// </summary>
    public static class Menus
    {
        public static readonly Color Back = ColorTranslator.FromHtml("#232329");

        public static ContextMenuStrip Create(Font font)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Back;
            menu.ForeColor = Theme.Text;
            menu.Font = font;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Padding = new Padding(0, 4, 0, 4);
            menu.Opened += delegate { Round(menu); };
            return menu;
        }

        public static ToolStripMenuItem Item(string text, Color ink)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = ink;
            item.Padding = new Padding(8, 6, 16, 6);
            return item;
        }

        static void Round(ToolStripDropDown menu)
        {
            try
            {
                int round = 3;   // DWMWCP_ROUNDSMALL, what Windows uses for its own menus
                DwmSetWindowAttribute(menu.Handle, 33, ref round, 4);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Menus.Back);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Border, 1f))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (GraphicsPath p = Theme.Rounded(r, 5f))
            using (SolidBrush b = new SolidBrush(Theme.Accent))
                g.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled) e.TextColor = Color.White;
            else if (!e.Item.Enabled) e.TextColor = Theme.Dimmer;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(Theme.Border, 1f))
                e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
    }

    /// <summary>Dark menu colours, for whatever the renderer does not draw itself.</summary>
    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Menus.Back; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color MenuItemSelected { get { return Theme.Accent; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Accent; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Accent; } }
        public override Color ImageMarginGradientBegin { get { return Menus.Back; } }
        public override Color ImageMarginGradientMiddle { get { return Menus.Back; } }
        public override Color ImageMarginGradientEnd { get { return Menus.Back; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    /// <summary>
    /// A dropdown field. Windows' own ComboBox cannot be styled to match a
    /// dark window - it keeps a light border and a system arrow, and it clips
    /// its text - so this draws the closed state itself and uses a themed
    /// menu for the list.
    /// </summary>
    public class DropChip : Drawn
    {
        public List<string> Items = new List<string>();
        public event EventHandler SelectionChanged;

        int _index = -1;
        bool _hover;
        ContextMenuStrip _menu;
        int _closedAt;          // tick when the menu last closed

        public DropChip()
        {
            BackColor = Theme.Card;
            Height = 32;
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

        bool Open { get { return _menu != null && _menu.Visible; } }

        /// <summary>Width that fits the longest entry, so nothing is clipped.</summary>
        public int PreferredWidth()
        {
            int w = 0;
            foreach (string s in Items)
                w = Math.Max(w, TextRenderer.MeasureText(s, Font).Width);
            return w + 12 + 34;     // text + padding + chevron
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ShowMenu(this, new Point(0, Height + 4));
        }

        /// <summary>
        /// A click on the chip while the list is open should shut it. The menu
        /// has already closed itself by then (click-away), so without this the
        /// same click immediately reopens it and it never appears to close.
        /// </summary>
        bool JustClosed()
        {
            if (Open) { _menu.Close(); return true; }
            return unchecked(Environment.TickCount - _closedAt) < 250;
        }

        /// <summary>Open the list anywhere - the spacebar uses this.</summary>
        public void ShowMenu(Control anchor, Point at)
        {
            if (Items.Count == 0) return;
            if (JustClosed()) return;

            if (_menu != null) _menu.Dispose();
            _menu = Menus.Create(Font);
            if (anchor == this) _menu.MinimumSize = new Size(Width, 0);

            for (int i = 0; i < Items.Count; i++)
            {
                ToolStripMenuItem item = Menus.Item(Items[i], i == _index ? Theme.AccentHi : Theme.Text);
                int captured = i;
                item.Click += delegate { SelectedIndex = captured; };
                _menu.Items.Add(item);
            }
            _menu.Closed += delegate { _closedAt = Environment.TickCount; Invalidate(); };
            _menu.Show(anchor, at);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, 6f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Field)) g.FillPath(b, p);
                Color edge = Open ? Theme.Accent : (_hover ? Theme.BorderHi : Theme.Border);
                using (Pen pen = new Pen(edge, 1f)) g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, SelectedText, Font,
                new Rectangle(11, 0, Width - 11 - 28, Height), Theme.Text, Theme.Left);

            // The family's chevron: a stroked V, not a filled triangle.
            float cx = Width - 16, cy = Height / 2f;
            using (Pen pen = new Pen(Theme.Dim, 1.4f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[] {
                    new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            }
        }
    }
}
