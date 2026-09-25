using System;
using System.Drawing;

namespace Keycap
{
    /// <summary>
    /// A reference for everything the hook does beyond the remapped keys: the
    /// Mac shortcuts and phrase combinations, what the modifiers and function
    /// row act as, and the scancode fixes for the current layout. Read-only -
    /// each list is rebuilt from the mapping whenever it changes.
    /// </summary>
    public class ShortcutsPage : Page
    {
        ScrollPage _scroll;
        PairCard _mac, _mods, _scans;

        const int Gap = 12;

        public ShortcutsPage()
        {
            Title = "Shortcuts";
            Subtitle = "What the keyboard does beyond the remapped keys. All of it is live while remapping is on.";

            _scroll = new ScrollPage();
            Controls.Add(_scroll);

            _mac = NewCard("Mac shortcuts", 200);
            _mods = NewCard("Modifiers and function row", 150);
            _scans = NewCard("Scancode fixes", 170);

            Reload();
        }

        PairCard NewCard(string heading, int keyWidth)
        {
            PairCard c = new PairCard();
            c.Heading = heading;
            c.KeyWidth = keyWidth;
            c.Fill = Theme.Card;
            c.Page = Theme.Back;
            _scroll.Content.Controls.Add(c);
            return c;
        }

        public override void Reload()
        {
            _mac.Items.Clear();
            _mac.Items.AddRange(Shortcuts.All);
            foreach (Mapping.Phrase p in Mapping.Phrases)
                _mac.Items.Add(new string[] { p.Combo, "types: " + p.Preview });

            _mods.Items.Clear();
            foreach (Mapping.Mod m in Mapping.Mods)
                _mods.Items.Add(new string[] { m.Label, "acts as " + m.Act });
            _mods.Items.AddRange(Shortcuts.FRow);

            _scans.Items.Clear();
            foreach (Mapping.Rule r in Mapping.Scans)
            {
                if (!Mapping.InScope(r.Lang)) continue;
                if (r.Text != null)
                    _scans.Items.Add(new string[] { "SC" + r.From + " → text", r.Text + "  " + r.ShiftText });
                else
                    _scans.Items.Add(new string[] { "SC" + r.From + " → SC" + r.To, Mapping.TargetLabel(r.To) });
            }

            Arrange();
        }

        public override void Arrange()
        {
            if (_scroll == null) return;
            _scroll.SetBounds(Inset, ContentTop, Width - Inset, Height - ContentTop);
            int w = Math.Max(1, _scroll.Width - Inset);
            int bottom;

            if (w >= 900)
            {
                // Two columns: the long shortcut list on the left, the two
                // shorter lists stacked on the right.
                int left = (w - Gap) / 2, right = w - Gap - left;
                _mac.Location = new Point(0, 0);
                int lh = _mac.Arrange(left);
                _mods.Location = new Point(left + Gap, 0);
                int rh = _mods.Arrange(right);
                _scans.Location = new Point(left + Gap, rh + Gap);
                rh += Gap + _scans.Arrange(right);
                bottom = Math.Max(lh, rh);
            }
            else
            {
                int y = 0;
                _mac.Location = new Point(0, y);
                y += _mac.Arrange(w) + Gap;
                _mods.Location = new Point(0, y);
                y += _mods.Arrange(w) + Gap;
                _scans.Location = new Point(0, y);
                y += _scans.Arrange(w);
                bottom = y;
            }

            _scroll.Content.Height = bottom + 24;
            _scroll.Sync();
        }
    }
}
