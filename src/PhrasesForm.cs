using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// Phrases: a trigger that types a block of text. The trigger is either a
    /// key combination or a word that expands as you type it.
    ///
    /// A combination is captured by pressing it rather than picked from a list
    /// of key names - you press what you will actually press, and what you see
    /// is what the hook will match. A word is captured the same way: type it.
    /// </summary>
    public class PhrasesForm : Form
    {
        PhraseList _list;
        FlatButton _new, _save, _delete;
        Label _listHead, _comboHead, _textHead, _hint, _counter;
        ComboCapture _capture;
        TextBox _text;
        Mapping.Phrase _editing;

        const int Pad = 18;
        const int Gap = 12;
        const int ListW = 236;

        public PhrasesForm()
        {
            Text = "Phrases";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 430);
            MinimumSize = new Size(720, 400);
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            _listHead = Head("SAVED PHRASES");
            _list = new PhraseList();
            _list.SelectionChanged += delegate { Edit(_list.Selected); };
            Controls.Add(_list);

            _new = Btn("New phrase", true);
            _new.Click += delegate { _list.Selected = null; _list.Invalidate(); Edit(null); _capture.Begin(); };

            _comboHead = Head("TRIGGER");
            _capture = new ComboCapture();
            _capture.Font = Theme.Mono(10.5f, FontStyle.Bold);
            Controls.Add(_capture);

            _hint = new Label();
            _hint.Text = "Click the box, then press a combination - or type a word such as "
                       + "mymail, which expands wherever you type it.";
            _hint.Font = Theme.Font(8.25f, FontStyle.Regular);
            _hint.ForeColor = Theme.Dimmer;
            _hint.BackColor = Theme.Back;
            _hint.AutoSize = false;
            Controls.Add(_hint);

            _textHead = Head("TEXT TO TYPE");
            _text = new TextBox();
            _text.Multiline = true;
            // No scrollbar: the only one Win32 offers here is the old grey
            // one, which cannot be themed and looks pasted onto a dark panel.
            // The text wraps and the wheel still scrolls it.
            _text.ScrollBars = ScrollBars.None;
            _text.WordWrap = true;
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = Theme.Card;
            _text.ForeColor = Theme.Text;
            _text.Font = Theme.Font(10f, FontStyle.Regular);
            _text.TextChanged += delegate { UpdateCounter(); };
            Controls.Add(_text);

            _counter = new Label();
            _counter.Font = Theme.Mono(8f, FontStyle.Regular);
            _counter.ForeColor = Theme.Dimmer;
            _counter.BackColor = Theme.Back;
            _counter.AutoSize = false;
            _counter.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_counter);

            _delete = Btn("Delete", false);
            _delete.Click += delegate
            {
                if (_editing == null) return;
                Mapping.RemovePhrase(_editing);
                Edit(null);
                Reload();
            };

            _save = Btn("Save phrase", true);
            _save.Click += delegate { SaveCurrent(); };

            Resize += delegate { Place(); };
            Reload();
            Edit(null);
            Place();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        FlatButton Btn(string text, bool primary)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Primary = primary;
            b.Backdrop = Theme.Back;
            b.Font = Theme.Font(9f, FontStyle.Regular);
            b.Height = 34;
            b.Width = primary ? 118 : 92;
            Controls.Add(b);
            return b;
        }

        Label Head(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Font(7.75f, FontStyle.Bold);
            l.ForeColor = Theme.Dimmer;
            l.BackColor = Theme.Back;
            l.AutoSize = false;
            l.Height = 16;
            Controls.Add(l);
            return l;
        }

        void Place()
        {
            int bottom = ClientSize.Height - Pad - 34;

            // left column: the saved list, with New pinned under it
            _listHead.SetBounds(Pad, Pad, ListW, 16);
            _list.SetBounds(Pad, Pad + 22, ListW, bottom - Pad - 22 - Gap);
            _new.SetBounds(Pad, bottom, ListW, 34);

            // right column: the editor
            int x = Pad + ListW + Gap * 2;
            int w = ClientSize.Width - x - Pad;

            const int CapW = 230;
            const int TriggerH = 46;    // room for the hint's third line

            _comboHead.SetBounds(x, Pad, w, 16);
            _capture.SetBounds(x, Pad + 22, CapW, 42);
            _hint.SetBounds(x + CapW + Gap, Pad + 20, w - CapW - Gap, TriggerH);

            int ty = Pad + 22 + TriggerH + Gap + 6;
            _textHead.SetBounds(x, ty, w - 90, 16);
            _counter.SetBounds(x + w - 90, ty, 90, 16);

            int th = bottom - (ty + 22) - Gap;
            // TextBox has no padding of its own, so inset it inside its panel
            _text.SetBounds(x + 10, ty + 22 + 8, w - 20, Math.Max(60, th - 16));

            _save.SetBounds(ClientSize.Width - Pad - _save.Width, bottom, _save.Width, 34);
            _delete.SetBounds(_save.Left - Gap - _delete.Width, bottom, _delete.Width, 34);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // A card behind the text area, so the borderless TextBox reads as a
            // field rather than text floating on the window.
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle t = _text.Bounds;
            RectangleF r = new RectangleF(t.Left - 10, t.Top - 8, t.Width + 20, t.Height + 16);
            using (GraphicsPath p = Theme.Rounded(r, 8f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border, 1f)) g.DrawPath(pen, p);
            }
        }

        void Reload()
        {
            _list.Items = Mapping.Phrases;
            _list.Invalidate();
        }

        void UpdateCounter()
        {
            int n = _text.Text.Length;
            _counter.Text = n == 0 ? "" : n + (n == 1 ? " char" : " chars");
        }

        void Edit(Mapping.Phrase p)
        {
            _editing = p;
            if (p == null)
            {
                _capture.Set(0, 0, "");
                _text.Text = "";
                _delete.Enabled = false;
            }
            else
            {
                _capture.Set(p.Mods, p.Vk, p.Word);
                _text.Text = p.Text;
                _delete.Enabled = true;
            }
            UpdateCounter();
            Invalidate();
        }

        void SaveCurrent()
        {
            string word = _capture.Word;
            bool isWord = word.Length > 0;

            if (isWord && word.Length < 2)
            {
                MessageBox.Show(this,
                    "A trigger word needs at least two letters, or it would fire constantly.",
                    "Keycap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _capture.Begin();
                return;
            }
            if (!isWord && (_capture.Vk == 0 || _capture.Mods == 0))
            {
                MessageBox.Show(this,
                    "Set a trigger first. Either press a combination - Command, Option or "
                    + "Shift plus a key - or just type a word such as mymail.",
                    "Keycap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _capture.Begin();
                return;
            }
            if (_text.Text.Length == 0)
            {
                MessageBox.Show(this, "Type the text this should write.",
                    "Keycap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _text.Focus();
                return;
            }

            // Moving an entry onto a different trigger replaces it.
            if (_editing != null &&
                (_editing.IsWord != isWord
                 || (isWord && !string.Equals(_editing.Word, word, StringComparison.OrdinalIgnoreCase))
                 || (!isWord && (_editing.Mods != _capture.Mods || _editing.Vk != _capture.Vk))))
                Mapping.RemovePhrase(_editing);

            if (isWord) Mapping.AddWord(word, _text.Text);
            else Mapping.AddPhrase(_capture.Mods, _capture.Vk, _text.Text);

            Reload();
            _list.Selected = isWord
                ? Mapping.FindWord(word)
                : Mapping.FindPhrase(_capture.Mods, _capture.Vk);
            Edit(_list.Selected);
        }
    }

    /// <summary>
    /// Press the combination instead of describing it - or type a word, and
    /// the phrase expands as soon as that word is typed anywhere.
    /// </summary>
    public class ComboCapture : Control
    {
        public int Mods, Vk;
        public string Word = "";
        bool _capturing, _hover;

        public ComboCapture()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                   | ControlStyles.Selectable, true);
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public void Set(int mods, int vk, string word)
        {
            Mods = mods; Vk = vk; Word = word ?? "";
            _capturing = false;
            Invalidate();
        }

        public void Begin()
        {
            Focus();
            Mods = 0; Vk = 0; Word = "";
            _capturing = true;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Begin(); base.OnMouseDown(e); }
        protected override void OnLostFocus(EventArgs e) { _capturing = false; Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys k) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_capturing) return;

            int vk = (int)e.KeyCode;
            // Ignore the modifiers themselves - we want the key they go with.
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C ||
                vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5)
                return;

            // Command reaches us as Ctrl and Option as Win, because the hook has
            // already remapped them - which is exactly what the hook will match.
            bool cmd = e.Control;
            bool opt = (GetKeyState(0x5B) & 0x8000) != 0;
            bool held = cmd || opt || e.Alt;

            e.Handled = true;
            e.SuppressKeyPress = true;

            // Plain letters and digits spell out a trigger word instead: type
            // "mymail" and the phrase replaces it wherever you type it.
            if (!held && ((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39)))
            {
                if (Word.Length < 24) Word += char.ToLowerInvariant((char)vk);
                Mods = 0; Vk = 0;
                Invalidate();
                return;                      // stay in capture, the word goes on
            }
            if (!held && vk == 0x08)         // Backspace edits the word
            {
                if (Word.Length > 0) Word = Word.Substring(0, Word.Length - 1);
                Invalidate();
                return;
            }
            if (!held && (vk == 0x0D || vk == 0x09) && Word.Length > 0)
            {
                _capturing = false;          // Enter or Tab finishes the word
                Invalidate();
                return;
            }
            if (vk == 0x1B)                  // Escape starts over
            {
                Word = ""; Mods = 0; Vk = 0;
                Invalidate();
                return;
            }

            int mods = 0;
            if (cmd) mods |= 1;
            if (opt) mods |= 2;
            if (e.Shift) mods |= 4;

            Word = "";
            Mods = mods;
            Vk = vk;
            _capturing = false;
            Invalidate();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, 9f))
            {
                using (SolidBrush b = new SolidBrush(_capturing ? Theme.KeyModFill
                                                                : (_hover ? Theme.CardHi : Theme.Card)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(_capturing ? Theme.Accent : Theme.Border,
                                         _capturing ? 1.6f : 1f))
                    g.DrawPath(pen, p);
            }

            bool empty = Vk == 0 && Word.Length == 0;
            string text = empty
                ? (_capturing ? "press keys, or type a word" : "click to set")
                : Describe();

            TextRenderer.DrawText(g, text, Font, ClientRectangle,
                _capturing ? Theme.Accent : (empty ? Theme.Dimmer : Theme.Text),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix);
        }

        string Describe()
        {
            if (Word.Length > 0) return Word;
            string s = "";
            if ((Mods & 1) != 0) s += "Cmd + ";
            if ((Mods & 2) != 0) s += "Opt + ";
            if ((Mods & 4) != 0) s += "Shift + ";
            return s + Mapping.Phrase.KeyName(Vk);
        }
    }

    /// <summary>The saved phrases: combination over a preview of the text.</summary>
    public class PhraseList : Control
    {
        public List<Mapping.Phrase> Items = new List<Mapping.Phrase>();
        public Mapping.Phrase Selected;
        public event EventHandler SelectionChanged;

        const int RowH = 56;
        int _hover = -1;
        int _scroll;                 // first visible row

        public PhraseList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        int VisibleRows { get { return Math.Max(1, Height / RowH); } }
        int MaxScroll { get { return Math.Max(0, Items.Count - VisibleRows); } }

        int RowAt(int y)
        {
            int i = _scroll + y / RowH;
            return i >= 0 && i < Items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = RowAt(e.Y);
            if (i != _hover) { _hover = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>Past a handful of phrases the list has to scroll.</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int was = _scroll;
            _scroll -= Math.Sign(e.Delta);
            if (_scroll > MaxScroll) _scroll = MaxScroll;
            if (_scroll < 0) _scroll = 0;
            if (_scroll != was) { _hover = RowAt(e.Y); Invalidate(); }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = RowAt(e.Y);
            if (i >= 0)
            {
                Selected = Items[i];
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (Items.Count == 0)
            {
                using (Font f = Theme.Font(9f, FontStyle.Regular))
                    TextRenderer.DrawText(g, "Nothing saved yet.\n\nPress New phrase, hold a combination, and type what it should write.", f,
                        new Rectangle(2, 6, Width - 8, 90), Theme.Dimmer,
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            if (_scroll > MaxScroll) _scroll = MaxScroll;

            using (Font fc = Theme.Mono(9.5f, FontStyle.Bold))
            using (Font ft = Theme.Font(8.5f, FontStyle.Regular))
            using (Font fk = Theme.Font(7f, FontStyle.Bold))
            {
                for (int i = _scroll; i < Items.Count; i++)
                {
                    int y = (i - _scroll) * RowH;
                    if (y + RowH > Height + 4) break;
                    Mapping.Phrase p = Items[i];
                    bool sel = p == Selected;

                    // The row insets from the column edge, so the highlight
                    // reads as a card rather than a band across the panel.
                    RectangleF r = new RectangleF(1.5f, y + 3.5f, Width - 4f, RowH - 9f);
                    if (sel || i == _hover)
                        using (GraphicsPath path = Theme.Rounded(r, 10f))
                        {
                            using (SolidBrush b = new SolidBrush(sel ? Theme.KeyModFill : Theme.Card))
                                g.FillPath(b, path);
                            using (Pen pen = new Pen(sel ? Theme.AccentDark : Theme.Border, 1f))
                                g.DrawPath(pen, path);
                        }

                    // What kind of trigger it is, so the two sorts are
                    // distinguishable at a glance.
                    string kind = p.IsWord ? "TYPED" : "KEYS";
                    Size ks = TextRenderer.MeasureText(kind, fk);
                    TextRenderer.DrawText(g, kind, fk,
                        new Rectangle(Width - 14 - ks.Width, y + 13, ks.Width, 14),
                        Theme.Dimmer, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

                    TextRenderer.DrawText(g, p.Combo, fc,
                        new Rectangle(14, y + 11, Width - 34 - ks.Width, 18),
                        sel ? Theme.Accent : Theme.Text,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    TextRenderer.DrawText(g, p.Preview, ft,
                        new Rectangle(14, y + 31, Width - 28, 16), Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }

                // Say so when the list runs past the bottom.
                int hidden = Items.Count - _scroll - VisibleRows;
                if (hidden > 0)
                    TextRenderer.DrawText(g, "+" + hidden + " more - scroll", ft,
                        new Rectangle(14, Height - 16, Width - 28, 16), Theme.Dimmer,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        }
    }
}
