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
    public class PhrasesPage : Page
    {
        Card _listCard, _editor;
        PhraseList _list;
        FlatButton _new, _save, _delete;
        TextLine _comboHead, _textHead, _hint, _counter;
        ComboCapture _capture;
        TextBox _text;
        Mapping.Phrase _editing;

        const int Gap = 12;
        const int ListW = 270;
        const int Pad = 20;

        public PhrasesPage()
        {
            Title = "Phrases";
            Subtitle = "A key combination, or a word you type, that writes a block of text for you.";

            _new = Btn("New phrase", true, Theme.Back);
            _new.Click += delegate { _list.Selected = null; _list.Invalidate(); Edit(null); _capture.Begin(); };
            Controls.Add(_new);
            HeaderRight = _new.Width + 24;

            _listCard = new Card();
            _listCard.Fill = Theme.Card;
            _listCard.Radius = 12;
            Controls.Add(_listCard);

            _list = new PhraseList();
            _list.SelectionChanged += delegate { Edit(_list.Selected); };
            _listCard.Controls.Add(_list);

            _editor = new Card();
            _editor.Fill = Theme.Card;
            _editor.Radius = 12;
            _editor.Paint += PaintField;
            Controls.Add(_editor);

            _comboHead = Section("Trigger", Theme.Card);
            _editor.Controls.Add(_comboHead);

            _capture = new ComboCapture();
            _capture.Font = Theme.Mono(10.5f, FontStyle.Bold);
            _capture.BackColor = Theme.Card;
            _editor.Controls.Add(_capture);

            _hint = Hint("Click the box, then press a combination - or type a word such as "
                       + "mymail, which expands wherever you type it.", Theme.Card);
            _editor.Controls.Add(_hint);

            _textHead = Section("Text to type", Theme.Card);
            _editor.Controls.Add(_textHead);

            _counter = new TextLine();
            _counter.Font = Theme.Font(8.25f, FontStyle.Regular);
            _counter.Ink = Theme.Dimmer;
            _counter.BackColor = Theme.Card;
            _counter.AlignRight = true;
            _editor.Controls.Add(_counter);

            _text = new TextBox();
            _text.Multiline = true;
            // No scrollbar: the only one Win32 offers here is the old grey
            // one, which cannot be themed and looks pasted onto a dark panel.
            // The text wraps and the wheel still scrolls it.
            _text.ScrollBars = ScrollBars.None;
            _text.WordWrap = true;
            _text.AcceptsReturn = true;
            _text.BorderStyle = BorderStyle.None;
            _text.BackColor = Theme.Field;
            _text.ForeColor = Theme.Text;
            _text.Font = Theme.Font(10f, FontStyle.Regular);
            _text.TextChanged += delegate { UpdateCounter(); };
            _text.GotFocus += delegate { _editor.Invalidate(); };
            _text.LostFocus += delegate { _editor.Invalidate(); };
            _editor.Controls.Add(_text);

            _delete = Btn("Delete", false, Theme.Card);
            _delete.Click += delegate
            {
                if (_editing == null) return;
                string was = _editing.Combo;
                Mapping.RemovePhrase(_editing);
                Edit(null);
                Changed();
                Say(was + " deleted", Theme.Dim);
            };
            _editor.Controls.Add(_delete);

            _save = Btn("Save phrase", true, Theme.Card);
            _save.Click += delegate { SaveCurrent(); };
            _editor.Controls.Add(_save);

            _list.Items = Mapping.Phrases;
            Edit(null);
        }

        FlatButton Btn(string text, bool primary, Color backdrop)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Primary = primary;
            b.Backdrop = backdrop;
            b.Font = Theme.Font(9f, FontStyle.Regular);
            b.Height = 34;
            b.Width = Math.Max(96, b.PreferredWidth());
            return b;
        }

        public override void Arrange()
        {
            if (_editor == null) return;
            _new.Location = new Point(Width - Inset - _new.Width, 10);

            int top = ContentTop;
            int h = Height - top - 24;

            _listCard.SetBounds(Inset, top, ListW, h);
            _list.SetBounds(6, 6, ListW - 12, h - 12);

            int ex = Inset + ListW + Gap;
            int w = Width - Inset - ex;
            _editor.SetBounds(ex, top, w, h);

            const int CapW = 240;
            _comboHead.SetBounds(Pad, Pad - 2, w - Pad * 2, 18);
            _capture.SetBounds(Pad, Pad + 22, CapW, 42);
            _hint.SetBounds(Pad + CapW + 16, Pad + 24, Math.Max(80, w - Pad * 2 - CapW - 16), 40);

            int ty = Pad + 22 + 42 + 22;
            _textHead.SetBounds(Pad, ty, w - Pad * 2 - 90, 18);
            _counter.SetBounds(w - Pad - 90, ty, 90, 18);

            int bottom = h - Pad - 34;
            // TextBox has no padding of its own, so it is inset inside the
            // field that PaintField draws around it.
            _text.SetBounds(Pad + 12, ty + 26 + 10, w - Pad * 2 - 24, Math.Max(40, bottom - Gap - (ty + 26) - 20));

            _save.Location = new Point(w - Pad - _save.Width, bottom);
            _delete.Location = new Point(_save.Left - 8 - _delete.Width, bottom);
            _editor.Invalidate();
        }

        /// <summary>
        /// The field behind the text box, so the borderless TextBox reads as an
        /// input rather than text floating on the card. Accent edge when focused.
        /// </summary>
        void PaintField(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle t = _text.Bounds;
            RectangleF r = new RectangleF(t.Left - 12.5f, t.Top - 10.5f, t.Width + 24, t.Height + 20);
            using (GraphicsPath p = Theme.Rounded(r, 6f))
            {
                using (SolidBrush b = new SolidBrush(Theme.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(_text.Focused ? Theme.Accent : Theme.Border, 1f)) g.DrawPath(pen, p);
            }
        }

        /// <summary>
        /// Load() rebuilds every phrase object, so the one being edited is found
        /// again by its trigger. What is half-typed in the editor is left alone.
        /// </summary>
        public override void Reload()
        {
            _list.Items = Mapping.Phrases;
            if (_editing != null)
            {
                Mapping.Phrase again = _editing.IsWord
                    ? Mapping.FindWord(_editing.Word)
                    : Mapping.FindPhrase(_editing.Mods, _editing.Vk);
                _editing = again;
                _list.Selected = again;
                _delete.Enabled = again != null;
            }
            _list.Invalidate();
        }

        /// <summary>Open on the first phrase rather than an empty editor.</summary>
        public override void Entered()
        {
            if (_editing != null || _text.Text.Length > 0 || _capture.Word.Length > 0 || _capture.Vk != 0) return;
            if (Mapping.Phrases.Count == 0) return;
            _list.Selected = Mapping.Phrases[0];
            _list.Invalidate();
            Edit(_list.Selected);
        }

        void UpdateCounter()
        {
            int n = _text.Text.Length;
            _counter.Text = n == 0 ? "" : n + (n == 1 ? " character" : " characters");
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
        }

        void SaveCurrent()
        {
            string word = _capture.Word;
            bool isWord = word.Length > 0;

            if (isWord && word.Length < 2)
            {
                Say("A trigger word needs at least two letters, or it would fire constantly", Theme.Warn);
                _capture.Begin();
                return;
            }
            if (!isWord && (_capture.Vk == 0 || _capture.Mods == 0))
            {
                Say("Set a trigger first: press Command, Option or Shift with a key, or type a word", Theme.Warn);
                _capture.Begin();
                return;
            }
            if (_text.Text.Length == 0)
            {
                Say("Type the text this should write", Theme.Warn);
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

            Changed();
            _list.Selected = isWord
                ? Mapping.FindWord(word)
                : Mapping.FindPhrase(_capture.Mods, _capture.Vk);
            Edit(_list.Selected);
            _list.Invalidate();
            if (_list.Selected != null) Say(_list.Selected.Combo + " saved", Theme.Good);
        }
    }

    /// <summary>
    /// Press the combination instead of describing it - or type a word, and
    /// the phrase expands as soon as that word is typed anywhere.
    /// </summary>
    public class ComboCapture : Drawn
    {
        public int Mods, Vk;
        public string Word = "";
        bool _capturing, _hover;

        public ComboCapture()
        {
            SetStyle(ControlStyles.Selectable, true);
            BackColor = Theme.Card;
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

            // Shotwin's shortcut box: a field that turns blue while it listens.
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.Rounded(r, 6f))
            {
                using (SolidBrush b = new SolidBrush(_capturing ? Theme.AccentSoft : Theme.Field))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(_capturing ? Theme.Accent : (_hover ? Theme.BorderHi : Theme.Border), 1f))
                    g.DrawPath(pen, p);
            }

            bool empty = Vk == 0 && Word.Length == 0;
            string text = empty
                ? (_capturing ? "press keys, or type a word" : "click to set")
                : Describe();

            Font f = empty ? null : Font;
            using (Font fe = empty ? Theme.Font(9f, FontStyle.Regular) : null)
                TextRenderer.DrawText(g, text, f ?? fe, ClientRectangle,
                    _capturing ? Theme.AccentHi : (empty ? Theme.Dimmer : Theme.Text), Theme.Center);
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

    /// <summary>The saved phrases: the trigger over a preview of the text.</summary>
    public class PhraseList : Drawn
    {
        public List<Mapping.Phrase> Items = new List<Mapping.Phrase>();
        public Mapping.Phrase Selected;
        public event EventHandler SelectionChanged;

        const int RowH = 58;
        int _hover = -1;
        int _scroll;                 // first visible row

        public PhraseList()
        {
            BackColor = Theme.Card;
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
            if (i != _hover) { _hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
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
                    TextRenderer.DrawText(g, "Nothing saved yet.\n\nPress New phrase, set a trigger, and type what it should write.",
                        f, new Rectangle(12, 12, Width - 24, 110), Theme.Dimmer, Theme.Wrap);
                return;
            }

            if (_scroll > MaxScroll) _scroll = MaxScroll;

            using (Font fc = Theme.Mono(9.5f, FontStyle.Bold))
            using (Font ft = Theme.Font(8.5f, FontStyle.Regular))
            using (Font fk = Theme.Semi(7f))
            {
                for (int i = _scroll; i < Items.Count; i++)
                {
                    int y = (i - _scroll) * RowH;
                    if (y + RowH > Height + 4) break;
                    Mapping.Phrase p = Items[i];
                    bool sel = p == Selected;

                    RectangleF r = new RectangleF(0, y + 1, Width - 0.5f, RowH - 2);
                    if (sel || i == _hover)
                        using (GraphicsPath path = Theme.Rounded(r, 8f))
                        using (SolidBrush b = new SolidBrush(sel ? Theme.CardHi : Theme.Field))
                            g.FillPath(b, path);

                    // What kind of trigger it is, so the two sorts are
                    // distinguishable at a glance.
                    string kind = p.IsWord ? "TYPED" : "KEYS";
                    Size ks = TextRenderer.MeasureText(kind, fk);
                    TextRenderer.DrawText(g, kind, fk,
                        new Rectangle(Width - 14 - ks.Width, y + 13, ks.Width, 14),
                        Theme.Dimmer, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

                    TextRenderer.DrawText(g, p.Combo, fc,
                        new Rectangle(14, y + 11, Width - 34 - ks.Width, 18),
                        sel ? Theme.AccentHi : Theme.Text,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    TextRenderer.DrawText(g, p.Preview, ft,
                        new Rectangle(14, y + 32, Width - 28, 16), Theme.Dim,
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
