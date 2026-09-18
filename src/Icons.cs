using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Keycap
{
    /// <summary>
    /// The pictograms printed on a Mac keyboard's function row, drawn as
    /// vectors so they scale with the keycap instead of depending on a font
    /// that may not carry the glyph.
    ///
    /// Every icon draws inside a square of side `s` centred on (cx, cy).
    /// </summary>
    public static class Icons
    {
        public static void Draw(Graphics g, Glyph icon, float cx, float cy, float s, Color c)
        {
            if (icon == Glyph.None) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(c, Math.Max(1f, s * 0.085f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                switch (icon)
                {
                    // Windows' own icon font, so the function row matches the
                    // on-screen indicator and the rest of the system.
                    case Glyph.BrightDown: SystemGlyph(g, "\uE706", cx, cy, s * 0.82f, c); break;
                    case Glyph.BrightUp:   SystemGlyph(g, "\uE706", cx, cy, s * 1.04f, c); break;
                    case Glyph.Mission:    SystemGlyph(g, "\uE8A9", cx, cy, s, c); break;
                    case Glyph.Spotlight:  SystemGlyph(g, "\uE721", cx, cy, s, c); break;
                    case Glyph.Dictate:    SystemGlyph(g, "\uE720", cx, cy, s, c); break;
                    case Glyph.DoNotDisturb: SystemGlyph(g, "\uE708", cx, cy, s, c); break;
                    case Glyph.Prev:       SystemGlyph(g, "\uE892", cx, cy, s, c); break;
                    case Glyph.Play:       SystemGlyph(g, "\uE768", cx, cy, s, c); break;
                    case Glyph.Next:       SystemGlyph(g, "\uE893", cx, cy, s, c); break;
                    case Glyph.Mute:       SystemGlyph(g, "\uE74F", cx, cy, s, c); break;
                    case Glyph.VolDown:    SystemGlyph(g, "\uE993", cx, cy, s, c); break;
                    case Glyph.VolUp:      SystemGlyph(g, "\uE767", cx, cy, s, c); break;
                    case Glyph.Globe:      SystemGlyph(g, "\uE774", cx, cy, s, c); break;

                    // Arrows stay hand-drawn: they need to read at half height
                    // in the cluster, where a font glyph is too heavy.
                    case Glyph.ArrowLeft:  Arrow(g, pen, cx, cy, s, 180); break;
                    case Glyph.ArrowRight: Arrow(g, pen, cx, cy, s, 0); break;
                    case Glyph.ArrowUp:    Arrow(g, pen, cx, cy, s, 270); break;
                    case Glyph.ArrowDown:  Arrow(g, pen, cx, cy, s, 90); break;
                }
            }
            g.SmoothingMode = old;
        }

        static void Sun(Graphics g, Pen pen, SolidBrush br, float cx, float cy, float s, float rayLen)
        {
            float r = s * 0.20f;
            g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
            float inner = s * 0.30f, outer = s * (0.30f + rayLen * 0.55f);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.DrawLine(pen,
                    cx + (float)Math.Cos(a) * inner, cy + (float)Math.Sin(a) * inner,
                    cx + (float)Math.Cos(a) * outer, cy + (float)Math.Sin(a) * outer);
            }
        }

        static void Mission(Graphics g, SolidBrush br, float cx, float cy, float s)
        {
            float w = s * 0.40f, h = s * 0.26f, gap = s * 0.09f;
            g.FillRectangle(br, cx - w - gap / 2, cy - h - gap / 2, w, h);
            g.FillRectangle(br, cx + gap / 2, cy - h - gap / 2, w, h);
            g.FillRectangle(br, cx - w - gap / 2, cy + gap / 2, w, h);
            g.FillRectangle(br, cx + gap / 2, cy + gap / 2, w, h);
        }

        static void Spotlight(Graphics g, Pen pen, float cx, float cy, float s)
        {
            float r = s * 0.28f;
            float ox = -s * 0.06f, oy = -s * 0.06f;
            g.DrawEllipse(pen, cx + ox - r, cy + oy - r, r * 2, r * 2);
            g.DrawLine(pen, cx + ox + r * 0.72f, cy + oy + r * 0.72f,
                            cx + s * 0.34f, cy + s * 0.34f);
        }

        /// <summary>
        /// Draw one of Windows' own icon-font glyphs. The microphone uses this
        /// so the key and the on-screen indicator show the same mark.
        /// </summary>
        static void SystemGlyph(Graphics g, string ch, float cx, float cy, float s, Color c)
        {
            Font f = null;
            try
            {
                f = new Font("Segoe MDL2 Assets", s * 0.92f, GraphicsUnit.Pixel);
                if (!string.Equals(f.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                {
                    f.Dispose();
                    f = null;
                }
            }
            catch { f = null; }

            if (f == null) return;
            using (f)
            using (StringFormat fmt = new StringFormat())
            using (SolidBrush b = new SolidBrush(c))
            {
                fmt.Alignment = StringAlignment.Center;
                fmt.LineAlignment = StringAlignment.Center;
                TextRenderingHint old = g.TextRenderingHint;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(ch, f, b, cx, cy, fmt);
                g.TextRenderingHint = old;
            }
        }

        static void Mic(Graphics g, Pen pen, SolidBrush br, float cx, float cy, float s)
        {
            float w = s * 0.22f, h = s * 0.42f;
            using (GraphicsPath p = Theme.Rounded(
                new RectangleF(cx - w / 2, cy - s * 0.40f, w, h), w / 2))
                g.FillPath(br, p);
            float r = s * 0.28f;
            g.DrawArc(pen, cx - r, cy - s * 0.18f, r * 2, r * 2, 20, 140);
            g.DrawLine(pen, cx, cy + s * 0.22f, cx, cy + s * 0.38f);
        }

        static void Moon(Graphics g, SolidBrush br, float cx, float cy, float s)
        {
            float r = s * 0.36f;
            using (GraphicsPath outer = new GraphicsPath())
            using (GraphicsPath inner = new GraphicsPath())
            {
                outer.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                inner.AddEllipse(cx - r + r * 0.52f, cy - r - r * 0.18f, r * 2, r * 2);
                using (Region reg = new Region(outer))
                {
                    reg.Exclude(inner);
                    g.FillRegion(br, reg);
                }
            }
        }

        static void Transport(Graphics g, SolidBrush br, float cx, float cy, float s,
                              int dir, bool bar)
        {
            float w = s * 0.24f, h = s * 0.46f;
            float x = cx - w * 0.55f * dir;
            Tri(g, br, x, cy, w, h, dir);
            Tri(g, br, x + w * 1.05f * dir, cy, w, h, dir);
            if (bar)
            {
                float bx = cx + (w * 1.75f) * dir;
                g.FillRectangle(br, bx - s * 0.04f, cy - h / 2, s * 0.08f, h);
            }
        }

        static void Tri(Graphics g, SolidBrush br, float x, float cy,
                        float w, float h, int dir)
        {
            PointF[] pts = new PointF[] {
                new PointF(x - w / 2 * dir, cy - h / 2),
                new PointF(x - w / 2 * dir, cy + h / 2),
                new PointF(x + w / 2 * dir, cy)
            };
            g.FillPolygon(br, pts);
        }

        static void PlayPause(Graphics g, SolidBrush br, float cx, float cy, float s)
        {
            float h = s * 0.46f, w = s * 0.26f;
            PointF[] pts = new PointF[] {
                new PointF(cx - s * 0.34f, cy - h / 2),
                new PointF(cx - s * 0.34f, cy + h / 2),
                new PointF(cx - s * 0.34f + w, cy)
            };
            g.FillPolygon(br, pts);
            float bw = s * 0.09f;
            g.FillRectangle(br, cx + s * 0.08f, cy - h / 2, bw, h);
            g.FillRectangle(br, cx + s * 0.08f + bw * 1.8f, cy - h / 2, bw, h);
        }

        static void Speaker(Graphics g, Pen pen, SolidBrush br, float cx, float cy,
                            float s, int waves)
        {
            float bx = cx - s * 0.30f;
            float bh = s * 0.22f;
            g.FillRectangle(br, bx, cy - bh / 2, s * 0.14f, bh);
            PointF[] cone = new PointF[] {
                new PointF(bx + s * 0.14f, cy - bh / 2),
                new PointF(bx + s * 0.34f, cy - s * 0.26f),
                new PointF(bx + s * 0.34f, cy + s * 0.26f),
                new PointF(bx + s * 0.14f, cy + bh / 2)
            };
            g.FillPolygon(br, cone);

            if (waves == 0)
            {
                float k = s * 0.13f, mx = cx + s * 0.20f;
                g.DrawLine(pen, mx - k, cy - k, mx + k, cy + k);
                g.DrawLine(pen, mx - k, cy + k, mx + k, cy - k);
            }
            else
            {
                for (int i = 0; i < waves; i++)
                {
                    float r = s * (0.13f + i * 0.13f);
                    g.DrawArc(pen, cx + s * 0.04f - r, cy - r, r * 2, r * 2, -55, 110);
                }
            }
        }

        static void Globe(Graphics g, Pen pen, float cx, float cy, float s)
        {
            float r = s * 0.34f;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawLine(pen, cx - r, cy, cx + r, cy);
            g.DrawArc(pen, cx - r * 0.5f, cy - r, r, r * 2, 0, 360);
        }

        static void Arrow(Graphics g, Pen pen, float cx, float cy, float s, float deg)
        {
            float a = s * 0.26f;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(deg);
            g.DrawLine(pen, -a * 0.8f, 0, a * 0.8f, 0);
            g.DrawLine(pen, a * 0.8f, 0, a * 0.15f, -a * 0.62f);
            g.DrawLine(pen, a * 0.8f, 0, a * 0.15f, a * 0.62f);
            g.Restore(st);
        }
    }
}
