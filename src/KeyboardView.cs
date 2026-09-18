using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Keycap
{
    public enum KeyKind { Plain, Mod, Layout, Media, Dead }

    /// <summary>A key as drawn: its position, its legends for the chosen
    /// language, and what the script currently does with it.</summary>
    public class Cap
    {
        public PosKey Def;
        public string Label = "";
        public string Top = "";
        public RectangleF Bounds;
        public KeyKind Kind = KeyKind.Plain;

        public string Pos { get { return Def.Pos; } }
        public string Sc { get { return Def.Scan; } }
        public string Mod { get { return Def.Mod; } }
        public string FKey { get { return Def.FKey; } }
        public bool Dead { get { return Def.Dead; } }
        public string Why { get { return Def.Why; } }
        public Glyph Icon { get { return Def.Icon; } }
    }

    /// <summary>
    /// The board, drawn from a layout description rather than hard-coded keys.
    /// Rows sit on one shared 15-unit grid so the board is a true rectangle,
    /// the function row carries the real Mac pictograms, and the arrow cluster
    /// is the inverted T with half-height up and down.
    /// </summary>
    public class KeyboardView : Control
    {
        public List<Cap> Keys = new List<Cap>();
        public Cap Selected;
        public event EventHandler<Cap> KeyPicked;
        /// <summary>Raised when the spacebar - the layout selector - is clicked.</summary>
        public event EventHandler<Cap> SpaceClicked;
        /// <summary>Layout name drawn across the spacebar.</summary>
        public string SpaceLabel = "";

        Layout _layout;
        List<List<PosKey>> _rows;
        Cap _hover;

        const float Gap = 5f;
        const float TotalUnits = 15f;

        public KeyboardView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        public Layout CurrentLayout { get { return _layout; } }

        public void SetLayout(Layout l)
        {
            _layout = l;
            _rows = l.Form == Shape.ANSI ? Layouts.Ansi() : Layouts.Iso();

            Keys.Clear();
            foreach (List<PosKey> row in _rows)
                foreach (PosKey def in row)
                {
                    Cap c = new Cap();
                    c.Def = def;
                    c.Label = l.Primary(def.Pos);
                    c.Top = l.Shifted(def.Pos);
                    Keys.Add(c);
                }

            Selected = null;
            Sync();
        }

        /// <summary>Recolours every key from the parsed script.</summary>
        public void Sync()
        {
            foreach (Cap c in Keys)
            {
                if (c.Dead) { c.Kind = KeyKind.Dead; continue; }
                if (c.Mod != null && Mapping.FindMod(c.Mod) != null) { c.Kind = KeyKind.Mod; continue; }
                if (c.Sc != null && Mapping.FindScan(c.Sc) != null)
                { c.Kind = KeyKind.Layout; continue; }
                if (c.FKey != null) { c.Kind = KeyKind.Media; continue; }
                c.Kind = KeyKind.Plain;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cap hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                bool clickable = hit != null &&
                    (hit.Kind != KeyKind.Plain || hit.Pos == "SPCE" ||
                     hit.Sc != null || hit.Mod != null || hit.FKey != null);
                Cursor = clickable ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = null; Cursor = Cursors.Default; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Cap hit = HitTest(e.Location);
            if (hit != null && hit.Pos == "SPCE")
            {
                if (SpaceClicked != null) SpaceClicked(this, hit);
            }
            else if (hit != null && (hit.Kind != KeyKind.Plain ||
                     hit.Sc != null || hit.Mod != null || hit.FKey != null))
            {
                Selected = hit;
                Invalidate();
                if (KeyPicked != null) KeyPicked(this, hit);
            }
            base.OnMouseDown(e);
        }

        Cap HitTest(Point p)
        {
            foreach (Cap c in Keys) if (!c.Def.Hidden && c.Bounds.Contains(p)) return c;
            return null;
        }

        void LayoutKeys()
        {
            if (_rows == null) return;

            // One pixel on the right so the last key's border - and its
            // selection outline - is not clipped by the control edge.
            float padX = 0f, padY = 6f, edge = 1f;
            float avail = Width - padX - edge;
            int rowCount = _rows.Count;

            // Folding the gap into the pitch makes a row's width depend only on
            // its unit count, never on how many keys it is split into.
            float pitch = (avail + Gap) / TotalUnits;
            float h = (Height - padY * 2 - (rowCount - 1) * Gap) / rowCount;

            int i = 0;
            float y = padY;
            foreach (List<PosKey> row in _rows)
            {
                float x = padX;
                for (int k = 0; k < row.Count; k++)
                {
                    PosKey def = row[k];
                    Cap cap = Keys[i++];
                    float w = def.Units * pitch - Gap;

                    if (def.Half)
                    {
                        // Up sits on the top half, Down on the bottom half of the
                        // same slot - the inverted T of a real Mac keyboard.
                        float hh = (h - 2f) / 2f;
                        bool isUp = def.Pos == "UP";
                        cap.Bounds = new RectangleF(x, isUp ? y : y + hh + 2f, w, hh);
                        if (!isUp) x += def.Units * pitch;   // advance once per pair
                    }
                    else if (def.Tall)
                    {
                        // Reaches into the row below, so the ISO enter is one
                        // key rather than two stacked rectangles.
                        cap.Bounds = new RectangleF(x, y, w, h * 2 + Gap);
                        x += def.Units * pitch;
                    }
                    else
                    {
                        cap.Bounds = new RectangleF(x, y, w, h);
                        x += def.Units * pitch;
                    }
                }
                y += h + Gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            LayoutKeys();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (_rows == null) return;

            using (Font fMain = Theme.Mono(9.5f, FontStyle.Bold))
            using (Font fMid = Theme.Mono(8f, FontStyle.Bold))
            using (Font fSmall = Theme.Mono(6.75f, FontStyle.Bold))
            using (Font fTop = Theme.Mono(7.5f, FontStyle.Regular))
            {
                foreach (Cap c in Keys)
                {
                    if (c.Def.Hidden) continue;
                    Color fill = Theme.KeyPlain, edge = Theme.KeyEdge,
                          ink = Theme.Text, tagCol = Color.Empty;
                    switch (c.Kind)
                    {
                        case KeyKind.Mod:
                            fill = Theme.KeyModFill; edge = Theme.AccentDark; tagCol = Theme.Accent; break;
                        case KeyKind.Layout:
                            fill = Theme.KeyLayFill; edge = ControlPaint.Dark(Theme.Warn, 0.18f); tagCol = Theme.Warn; break;
                        case KeyKind.Media:
                            fill = Theme.KeyMedFill; edge = ControlPaint.Dark(Theme.Good, 0.18f); tagCol = Theme.Good; break;
                        case KeyKind.Dead:
                            fill = Theme.Back; edge = Theme.Border; ink = Theme.Dimmer; break;
                    }
                    if (c == _hover && c.Kind != KeyKind.Plain)
                        fill = ControlPaint.Light(fill, 0.15f);

                    RectangleF r = c.Bounds;
                    using (GraphicsPath p = Theme.Rounded(r, 6f))
                    {
                        // A shallow top-to-bottom gradient plus a lit top edge
                        // gives the cap a surface without turning it into a
                        // heavy 3D button.
                        Color topCol = ControlPaint.Light(fill, 0.06f);
                        using (LinearGradientBrush b = new LinearGradientBrush(
                                   new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                                   topCol, fill, 90f))
                            g.FillPath(b, p);

                        using (Pen hi = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
                            g.DrawArc(hi, r.X + 1, r.Y + 1, 12, 12, 180, 90);

                        using (Pen pen = new Pen(c == Selected ? Theme.Text : edge,
                                                 c == Selected ? 1.6f : 1f))
                            g.DrawPath(pen, p);
                    }

                    bool hasLabel = !string.IsNullOrEmpty(c.Label);

                    if (c.Icon != Glyph.None)
                    {
                        // Function row: pictogram above, F-number below.
                        // Arrows and globe: pictogram centred on the cap.
                        if (hasLabel)
                        {
                            Icons.Draw(g, c.Icon, r.X + r.Width / 2, r.Y + r.Height * 0.34f,
                                       Math.Min(r.Width, r.Height) * 0.52f, Theme.Dim);
                        }
                        else
                        {
                            Icons.Draw(g, c.Icon, r.X + r.Width / 2, r.Y + r.Height / 2,
                                       Math.Min(r.Width, r.Height) * 0.62f, ink);
                        }
                    }
                    else if (!string.IsNullOrEmpty(c.Top))
                    {
                        TextRenderer.DrawText(g, c.Top, fTop,
                            new Rectangle((int)r.X + 6, (int)r.Y + 4, (int)r.Width - 10, 12),
                            Theme.Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    }

                    if (hasLabel)
                    {
                        bool underIcon = c.Icon != Glyph.None;
                        float labelBottom = c.Def.Tall
                            ? r.Y + (r.Height - Gap) / 2f - 6      // sit in the upper row
                            : r.Y + r.Height - 20;
                        Rectangle textBox = new Rectangle(
                            (int)r.X + 6,
                            (int)(underIcon ? r.Y + r.Height * 0.60f : labelBottom),
                            (int)r.Width - 10, 16);

                        Font use = fMain;
                        if (TextRenderer.MeasureText(c.Label, use).Width > textBox.Width) use = fMid;
                        if (TextRenderer.MeasureText(c.Label, use).Width > textBox.Width) use = fSmall;

                        TextRenderer.DrawText(g, c.Label, use, textBox, ink,
                            (underIcon ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left)
                            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    }

                    if (c.Pos == "SPCE" && SpaceLabel.Length > 0)
                    {
                        using (Font fSpace = Theme.Font(9f, FontStyle.Regular))
                        {
                            Size sz = TextRenderer.MeasureText(SpaceLabel, fSpace);
                            int tx = (int)(r.X + (r.Width - sz.Width - 14) / 2);
                            int ty = (int)(r.Y + (r.Height - sz.Height) / 2);
                            TextRenderer.DrawText(g, SpaceLabel, fSpace,
                                new Point(tx, ty),
                                c == _hover ? Theme.Text : Theme.Dim);
                            float ax = tx + sz.Width + 8, ay = r.Y + r.Height / 2f + 1;
                            using (SolidBrush b = new SolidBrush(c == _hover ? Theme.Text : Theme.Dim))
                                g.FillPolygon(b, new PointF[] {
                                    new PointF(ax - 3.5f, ay - 2f),
                                    new PointF(ax + 3.5f, ay - 2f),
                                    new PointF(ax, ay + 2.8f) });
                        }
                    }

                    if (tagCol != Color.Empty)
                        using (SolidBrush b = new SolidBrush(tagCol))
                            g.FillEllipse(b, r.Right - 10, r.Y + 6, 4.5f, 4.5f);
                }
            }
        }
    }
}
