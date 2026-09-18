using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Keycap
{
    public class MainForm : Form
    {
        KeyboardView _board;
        BatteryPill _battery;
        FlatButton _reload, _start, _stop, _open, _phrases;
        FlatButton _update, _settings;
        Label _title, _toast;
        Card _detailCard, _refCard, _refCard2, _refCard3;
        Label _dTitle, _dWhy;
        ChainView _dChain;
        Legend _legend;
        DropChip _target;
        CapPreview _preview;
        Label _editLabel;
        FlatButton _apply;
        PairList _shortcuts, _mods, _scans;
        DropChip _layoutPick;
        Card _headerCard;
        MarkBox _mark;
        System.Collections.Generic.List<InputLayout> _layouts;
        Label _hShortcuts, _hMods, _hScans;
        Timer _poll;
        bool _userPickedLayout;
        NotifyIcon _tray;
        System.Collections.Generic.List<string[]> _choices =
            new System.Collections.Generic.List<string[]>();   // label, value
        string _editKind = "";                                  // scan | mod
        bool _exiting;

        public MainForm()
        {
            Text = "Keycap";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScroll = true;
            ClientSize = new Size(1080, 780);
            MinimumSize = new Size(720, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            try { Icon = new Icon(System.IO.Path.Combine(AppDir(), "keycap.ico")); } catch { }

            BuildChrome();
            BuildBoard();
            BuildDetail();
            BuildReference();

            Resize += delegate { DoLayoutAll(); };

            _poll = new Timer();
            _poll.Interval = 20000;
            _poll.Tick += delegate { RefreshAll(false); };
            _poll.Start();

            BuildTray();
            Mapping.Load();
            Updater.CheckAsync();
            Hook.Start();          // the app IS the remapper now
            Hook.Changed += delegate { UpdateTray(); };

            DoLayoutAll();
            RefreshAll(true);
        }

        /// <summary>
        /// Closing the window keeps the remaps running - this is a background
        /// app. Only Exit from the tray actually stops it.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            Hook.Stop();       // never leave a hook behind
            Osd.Hide();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            base.OnFormClosing(e);
        }

        void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            menu.BackColor = Theme.Card;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;

            ToolStripMenuItem open = new ToolStripMenuItem("Open Keycap");
            open.Click += delegate { ShowWindow(); };
            ToolStripMenuItem toggle = new ToolStripMenuItem("Remapping");
            toggle.Click += delegate
            {
                if (Hook.Running) Hook.Stop(); else { Mapping.Load(); Hook.Start(); }
                UpdateTray();
            };
            ToolStripMenuItem quit = new ToolStripMenuItem("Exit");
            quit.Click += delegate { _exiting = true; Close(); Application.Exit(); };

            menu.Items.Add(open);
            menu.Items.Add(toggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);
            menu.Opening += delegate { toggle.Checked = Hook.Running; };

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowWindow(); };
            UpdateTray();
        }

        void ShowUpdate()
        {
            if (_update == null) return;
            _update.Visible = Updater.Available;
            _update.Text = "Update to " + Updater.LatestTag;
            _update.Width = TextRenderer.MeasureText(_update.Text, _update.Font).Width + 28;
            DoLayoutAll();
        }

        void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void UpdateTray()
        {
            if (_tray == null) return;
            _tray.Text = Hook.Running ? "Keycap - remapping on" : "Keycap - remapping off";
            RefreshAll(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DoLayoutAll();
        }

        static string AppDir()
        {
            return System.IO.Path.GetDirectoryName(Application.ExecutablePath);
        }

        // ---- chrome ---------------------------------------------------------

        void BuildChrome()
        {
            _headerCard = new Card();
            _headerCard.Fill = Theme.Back;
            _headerCard.Outline = false;
            Controls.Add(_headerCard);

            _mark = new MarkBox();
            _headerCard.Controls.Add(_mark);

            _title = new Label();
            _title.Text = "Keycap";
            _title.Font = Theme.Font(16f, FontStyle.Bold);
            _title.ForeColor = Theme.Text;
            _title.BackColor = Theme.Back;
            _title.AutoSize = true;
            _headerCard.Controls.Add(_title);



            _battery = new BatteryPill();
            _battery.Font = Theme.Font(8.75f, FontStyle.Regular);
            _battery.Width = 210;
            _battery.Height = 32;
            _battery.Flat = true;
            _battery.BackColor = Theme.Back;
            _headerCard.Controls.Add(_battery);

            _settings = MakeButton("Settings", false);
            _settings.Click += delegate
            {
                using (SettingsForm f = new SettingsForm())
                {
                    f.Owner = this;
                    f.ShowDialog(this);
                }
                RefreshAll(false);
            };

            _update = MakeButton("Update", true);
            _update.Visible = false;
            _update.Click += delegate
            {
                Toast("downloading " + Updater.LatestTag + "...", Theme.Dim);
                Application.DoEvents();
                string err = Updater.Install();
                if (err.Length > 0) { Toast(err, Theme.Bad); return; }
                _exiting = true;
                Close();
                Application.Exit();
            };

            Updater.Checked += delegate
            {
                // Raised from a background thread.
                try { BeginInvoke((MethodInvoker)delegate { ShowUpdate(); }); }
                catch { }
            };

            _reload = MakeButton("Reload", true);
            _reload.Click += delegate { Control("reload"); };
            _start = MakeButton("Start", false);
            _start.Click += delegate { Control("start"); };
            _stop = MakeButton("Stop", false);
            _stop.Click += delegate { Control("stop"); };
            _phrases = MakeButton("Phrases", false);
            _phrases.Click += delegate
            {
                using (PhrasesForm f = new PhrasesForm())
                {
                    f.Owner = this;
                    f.ShowDialog(this);
                }
                RefreshAll(false);
            };

            _open = MakeButton("Open config", false);
            _open.Click += delegate
            {
                try { Process.Start("notepad.exe", "\"" + Mapping.File_ + "\""); }
                catch (Exception ex) { Toast(ex.Message, Theme.Bad); }
            };

            _layoutPick = new DropChip();
            _layoutPick.Font = Theme.Font(8.75f, FontStyle.Regular);
            _layoutPick.Visible = false;   // the spacebar is the selector now
            _headerCard.Controls.Add(_layoutPick);
            _layouts = InputLang.Installed();
            if (_layouts.Count == 0)
            {
                // No layout list from Windows - fall back to the legend sets.
                foreach (Layout l in Layouts.All())
                {
                    InputLayout il = new InputLayout();
                    il.Name = l.Name; il.Board = l;
                    _layouts.Add(il);
                }
            }
            foreach (InputLayout il in _layouts) _layoutPick.Items.Add(il.ToString());
            _layoutPick.Width = _layoutPick.PreferredWidth();

            int active = InputLang.CurrentLangId();
            int start = 0;
            for (int i = 0; i < _layouts.Count; i++)
                if (_layouts[i].LangId == active) { start = i; break; }
            _layoutPick.SetIndexQuiet(start);

            _layoutPick.SelectionChanged += delegate
            {
                if (_board == null || _layoutPick.SelectedIndex < 0) return;
                _userPickedLayout = true;
                InputLayout il = _layouts[_layoutPick.SelectedIndex];
                Mapping.ViewLang = il.LangId;
                _board.SetLayout(il.Board);
                _board.SpaceLabel = il.Name;
                SelectFirstMapped();
                if (il.Hkl != IntPtr.Zero)
                {
                    // Success is visible in the spacebar itself, so only a
                    // failure is worth saying anything about.
                    if (!InputLang.Activate(il))
                        Toast("could not switch Windows to " + il.Name, Theme.Bad);
                }
            };

            _toast = new Label();
            _toast.Font = Theme.Mono(8.25f, FontStyle.Regular);
            _toast.ForeColor = Theme.Dim;
            _toast.BackColor = Theme.Back;
            _toast.AutoSize = false;
            _toast.TextAlign = ContentAlignment.MiddleLeft;
            _toast.Text = "";
            _headerCard.Controls.Add(_toast);
        }

        FlatButton MakeButton(string text, bool primary)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Primary = primary;
            b.Font = Theme.Font(9f, FontStyle.Regular);
            b.Width = primary ? 92 : 98;
            b.Height = 32;
            b.Backdrop = Theme.Back;
            _headerCard.Controls.Add(b);
            return b;
        }

        void BuildBoard()
        {
            _legend = new Legend();
            Controls.Add(_legend);

            Card holder = new Card();
            holder.Name = "boardCard";
            holder.Fill = Theme.Card;
            holder.Outline = true;
            holder.Radius = 12;
            Controls.Add(holder);

            _board = new KeyboardView();
            _board.BackColor = Theme.Card;
            _board.KeyPicked += delegate(object s, Cap c) { ShowKey(c); };
            _board.SpaceClicked += delegate(object s, Cap c)
            {
                _layoutPick.ShowMenu(_board,
                    new Point((int)c.Bounds.X + 12, (int)c.Bounds.Bottom + 2));
            };
            Mapping.ViewLang = _layouts[_layoutPick.SelectedIndex].LangId;
            _board.SetLayout(_layouts[_layoutPick.SelectedIndex].Board);
            _board.SpaceLabel = _layouts[_layoutPick.SelectedIndex].Name;
            holder.Controls.Add(_board);
        }

        void BuildDetail()
        {
            _detailCard = new Card();
            _detailCard.Fill = Theme.Card;
            _detailCard.Outline = true;
            _detailCard.Radius = 12;
            Controls.Add(_detailCard);

            _dTitle = new Label();
            _dTitle.Font = Theme.Font(12f, FontStyle.Bold);
            _dTitle.ForeColor = Theme.Text;
            _dTitle.BackColor = Theme.Card;
            _dTitle.AutoSize = false;
            _detailCard.Controls.Add(_dTitle);

            _dChain = new ChainView();
            _dChain.BackColor = Theme.Card;
            _detailCard.Controls.Add(_dChain);

            _dWhy = new Label();
            _dWhy.Font = Theme.Font(9f, FontStyle.Regular);
            _dWhy.ForeColor = Theme.Dim;
            _dWhy.BackColor = Theme.Card;
            _dWhy.AutoSize = false;
            _detailCard.Controls.Add(_dWhy);

            _preview = new CapPreview();
            _preview.BackColor = Theme.Card;
            _detailCard.Controls.Add(_preview);

            _editLabel = new Label();
            _editLabel.Text = "BEHAVIOUR";
            _editLabel.Font = Theme.Font(7.75f, FontStyle.Bold);
            _editLabel.ForeColor = Theme.Dimmer;
            _editLabel.BackColor = Theme.Card;
            _editLabel.AutoSize = false;
            _editLabel.Visible = false;
            _detailCard.Controls.Add(_editLabel);

            _target = new DropChip();
            _target.Font = Theme.Font(9f, FontStyle.Regular);
            _target.BackColor = Theme.Card;
            _target.Visible = false;
            _detailCard.Controls.Add(_target);

            _apply = new FlatButton();
            _apply.Text = "Apply";
            _apply.Primary = true;
            _apply.Backdrop = Theme.Card;
            _apply.Font = Theme.Font(9f, FontStyle.Regular);
            _apply.Width = 96;
            _apply.Visible = false;
            _apply.Click += delegate { ApplyScancode(); };
            _detailCard.Controls.Add(_apply);
        }

        void BuildReference()
        {

            _refCard = new Card();
            _refCard.Fill = Theme.Card;
            _refCard.Outline = true;
            _refCard.Radius = 12;
            Controls.Add(_refCard);

            _refCard2 = SubCard();
            _refCard3 = SubCard();

            _hShortcuts = SectionLabel(_refCard, "SHORTCUTS");
            _hMods = SectionLabel(_refCard2, "MODIFIERS");
            _hScans = SectionLabel(_refCard3, "SCANCODE MAP");

            _shortcuts = MakeList(_refCard, 120);
            _mods = MakeList(_refCard2, 110);
            _scans = MakeList(_refCard3, 130);
        }

        Card SubCard()
        {
            Card c = new Card();
            c.Fill = Theme.Card;
            c.Outline = true;
            c.Radius = 12;
            Controls.Add(c);
            return c;
        }

        Label SectionLabel(Control parent, string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Font(7.75f, FontStyle.Bold);
            l.ForeColor = Theme.Dimmer;
            l.BackColor = Theme.Card;
            l.AutoSize = false;
            l.Height = 18;
            parent.Controls.Add(l);
            return l;
        }

        PairList MakeList(Control parent, int keyWidth)
        {
            PairList p = new PairList();
            p.KeyWidth = keyWidth;
            p.BackColor = Theme.Card;
            parent.Controls.Add(p);
            return p;
        }

        // ---- layout ---------------------------------------------------------

        const int Pad = 14;
        const int Gap = 9;
        const int CardPad = 0;   // flat page: content aligns to the outer margin

        /// <summary>Height the keyboard wants at this width, keeping the caps
        /// close to square instead of stretching to fill the card.</summary>
        int BoardHeight(int cardWidth)
        {
            float pitch = (cardWidth - 8f + 4f) / 15f;   // board pads 4, gap 4
            float keyH = pitch * 0.78f;
            if (keyH < 36f) keyH = 36f;
            if (keyH > 58f) keyH = 58f;
            return (int)Math.Round(keyH * 6 + 5 * 4 + 8);
        }

        /// <summary>
        /// Fixed, deliberately. Sizing this to its content made every section
        /// below jump whenever a different key was selected.
        /// </summary>
        int DetailHeight(int cardWidth)
        {
            return 118;
        }

        int RefHeight()
        {
            int rows = 0;
            rows = Math.Max(rows, _shortcuts.Items.Count);
            rows = Math.Max(rows, _mods.Items.Count);
            rows = Math.Max(rows, _scans.Items.Count);
            if (rows < 3) rows = 3;
            if (rows > 12) rows = 12;   // the rest is reported as "+N more"
            return 12 + 16 + 6 + rows * 20 + 14;
        }

        void DoLayoutAll()
        {
            if (_board == null || _refCard == null) return;

            int w = ClientSize.Width - Pad * 2;

            // --- header ------------------------------------------------------
            const int HeaderH = 56;
            _headerCard.SetBounds(Pad, Pad, w, HeaderH);

            const int MarkSize = 32;
            int markY = 12;
            _mark.SetBounds(CardPad, markY, MarkSize, MarkSize);
            _title.Location = new Point(CardPad + MarkSize + 12,
                                        markY + (MarkSize - _title.Height) / 2);

            int by = 9;
            _reload.Location = new Point(w - CardPad - _reload.Width, by);
            _start.Location = new Point(_reload.Left - 8 - _start.Width, by);
            _stop.Location = new Point(_start.Left - 8 - _stop.Width, by);
            _open.Location = new Point(_stop.Left - 8 - _open.Width, by);
            _phrases.Location = new Point(_open.Left - 8 - _phrases.Width, by);
            _settings.Location = new Point(_phrases.Left - 8 - _settings.Width, by);
            if (_update.Visible)
                _update.Location = new Point(_settings.Left - 10 - _update.Width, by);

            // The layout picker lives on the spacebar now, so the toast simply
              // continues the title row.
            int chipY = markY + (MarkSize - 28) / 2;
            _battery.SetBounds(_title.Right + 20,
                               markY + (MarkSize - _battery.Height) / 2,
                               210, _battery.Height);
            _layoutPick.SetBounds(CardPad, chipY, _layoutPick.PreferredWidth(), 28);
            int toastX = _battery.Right + 18;
            _toast.SetBounds(toastX, chipY,
                             Math.Max(80, _open.Left - 16 - toastX), 28);

            int top = Pad + HeaderH + Gap;
            _legend.SetBounds(Pad + 2, top, w - 4, 20);
            top += 26;

            // --- sizes from content -----------------------------------------
            int boardNat = BoardHeight(w);
            int detailH = DetailHeight(w);
            int refH = RefHeight();

            int needed = top + boardNat + Gap + 6 + detailH + Gap + 8 + refH + Pad;
            // The window is free to be any size: a floor just large enough for
            // the board to stay legible, and anything shorter scrolls.
            Size min = new Size(720, 420);
            if (MinimumSize != min) MinimumSize = min;
            Size want = new Size(0, needed);
            if (AutoScrollMinSize != want) AutoScrollMinSize = want;

            // Any height beyond what the content needs goes to the board, the
            // only panel that reads better larger.
            int boardH = boardNat + Math.Max(0, ClientSize.Height - needed);
            if (boardH < 180) boardH = 180;

            // --- board -------------------------------------------------------
            Card boardCard = (Card)Controls["boardCard"];
            boardCard.SetBounds(Pad, top, w, boardH);
            _board.SetBounds(1, 1, boardCard.Width - 2, boardCard.Height - 2);

            // --- detail ------------------------------------------------------
            int dTop = top + boardH + Gap + 6;
            _detailCard.SetBounds(Pad, dTop, w, detailH);

            int editW = _target.Visible ? _target.PreferredWidth() + _apply.Width + 10 : 0;
            int textL = CardPad + 10 + 132 + 20;
            int textR = editW > 0 ? w - CardPad - editW - 24 : w - CardPad;

            _preview.SetBounds(CardPad + 10, 12, 132, Math.Max(64, detailH - 24));
            _dTitle.SetBounds(textL, 12, Math.Max(120, textR - textL), 26);
            _dChain.SetBounds(textL, detailH - 34, w - CardPad - textL - 8, 28);

            _dWhy.SetBounds(textL, 44, Math.Max(160, textR - textL), 34);

            if (_target.Visible)
            {
                int tw = _target.PreferredWidth();
                int gx = w - CardPad - _apply.Width - 10 - tw;
                _editLabel.SetBounds(gx, 8, tw, 14);
                _target.SetBounds(gx, 26, tw, 30);
                _apply.SetBounds(gx + tw + 10, 26, _apply.Width, 30);
            }

            // --- reference ---------------------------------------------------

            int rTop = dTop + detailH + Gap + 8;
            int colGap = 12;
            int colW = (w - colGap * 2) / 3;
            _refCard.SetBounds(Pad, rTop, colW, refH);
            _refCard2.SetBounds(Pad + colW + colGap, rTop, colW, refH);
            _refCard3.SetBounds(Pad + (colW + colGap) * 2, rTop, w - (colW + colGap) * 2, refH);

            int inset = 14;
            int listH = refH - 30 - 12;
            _hShortcuts.SetBounds(inset, 12, colW - inset * 2, 16);
            _hMods.SetBounds(inset, 12, colW - inset * 2, 16);
            _hScans.SetBounds(inset, 12, colW - inset * 2, 16);
            _shortcuts.SetBounds(inset, 34, colW - inset * 2, listH);
            _mods.SetBounds(inset, 34, colW - inset * 2, listH);
            _scans.SetBounds(inset, 34, _refCard3.Width - inset * 2, listH);
        }

        // ---- data -----------------------------------------------------------

        void Toast(string msg, Color c)
        {
            _toast.Text = msg;
            _toast.ForeColor = c;
        }

        void RefreshAll(bool selectDefault)
        {
            Mapping.Load();
            _board.Sync();

            _start.Enabled = !Hook.Running;
            _stop.Enabled = Hook.Running;


            RefreshBattery();
            FollowSystemLayout();
            FillLists();

            if (selectDefault) SelectFirstMapped();
            else if (_board.Selected != null) ShowKey(_board.Selected);
        }

        void SelectFirstMapped()
        {
            foreach (Cap c in _board.Keys)
                if (c.Kind == KeyKind.Layout) { _board.Selected = c; ShowKey(c); return; }
            foreach (Cap c in _board.Keys)
                if (c.Kind != KeyKind.Plain) { _board.Selected = c; ShowKey(c); return; }
        }

        /// <summary>Track the Windows layout until the user picks one here.</summary>
        void FollowSystemLayout()
        {
            if (_userPickedLayout || _layoutPick == null) return;
            int active = InputLang.CurrentLangId();
            for (int i = 0; i < _layouts.Count; i++)
            {
                if (_layouts[i].LangId != active) continue;
                if (i == _layoutPick.SelectedIndex) return;
                _layoutPick.SetIndexQuiet(i);
                Mapping.ViewLang = _layouts[i].LangId;
                _board.SetLayout(_layouts[i].Board);
                _board.SpaceLabel = _layouts[i].Name;
                SelectFirstMapped();
                return;
            }
        }

        void RefreshBattery()
        {
            try
            {
                List<BatteryInfo> b = Hid.Scan();
                if (b.Count > 0) _battery.Set(b[0].Name, b[0].Percent);
                else _battery.Set("", -1);
            }
            catch { _battery.Set("", -1); }
        }

        void FillLists()
        {
            _shortcuts.Items.Clear();
            foreach (string[] row in Shortcuts.All)
                _shortcuts.Items.Add(row);
            foreach (Mapping.Phrase ph in Mapping.Phrases)
                _shortcuts.Items.Add(new string[] { ph.Combo, "types: " + ph.Preview });
            _shortcuts.Invalidate();

            _mods.Items.Clear();
            foreach (Mapping.Mod m in Mapping.Mods)
                _mods.Items.Add(new string[] { m.Label, "acts as " + m.Act });
            foreach (string[] row in Shortcuts.FRow)
                _mods.Items.Add(row);
            _mods.Invalidate();

            _scans.Items.Clear();
            foreach (Mapping.Rule r in Mapping.Scans)
            {
                if (!Mapping.InScope(r.Lang)) continue;
                _scans.Items.Add(r.Text != null
                    ? new string[] { "SC" + r.From + " -> text", r.Text + "  " + r.ShiftText }
                    : new string[] { "SC" + r.From + " -> SC" + r.To, Mapping.TargetLabel(r.To) });
            }
            _scans.Invalidate();
            DoLayoutAll();
        }

        void ShowKey(Cap c)
        {
            _board.Selected = c;
            _board.Invalidate();

            Mapping.Rule hit = c.Sc == null ? null : Mapping.FindScan(c.Sc);
            Mapping.Rule scan = (hit != null && hit.Text == null) ? hit : null;
            Mapping.Rule send = (hit != null && hit.Text != null) ? hit : null;
            Mapping.Mod mod = c.Mod == null ? null : Mapping.FindMod(c.Mod);
            string med = c.FKey == null ? null : Shortcuts.MediaFor(c.FKey);

            string act = "not remapped", why = c.Why == null ? "" : c.Why;
            string name = string.IsNullOrEmpty(c.Label) ? c.Pos : c.Label;
            List<string> steps = new List<string>();
            steps.Add("key: " + name);

            if (c.Dead)
            {
                act = "nothing";
                steps.Add("never reaches Windows");
            }
            else if (scan != null)
            {
                act = "types " + Mapping.TargetLabel(scan.To);
                steps.Add("Windows sees SC" + scan.From);
                steps.Add("SC" + scan.To);
            }
            else if (send != null)
            {
                act = "sends " + send.Text + " directly";
                steps.Add("Windows sees SC" + send.From);
                steps.Add(send.Text);
            }
            else if (mod != null)
            {
                act = mod.Act;
                steps.Add("reports as " + mod.Src);
                steps.Add(mod.Act);
                if (why.Length == 0)
                    why = "Physical " + mod.Label + " reports as " + mod.Src
                        + ", which Keycap remaps to " + mod.Dst + ".";
            }
            else if (med != null)
            {
                act = med;
                steps.Add(med);
                if (why.Length == 0)
                    why = "Windows cannot read Apple's Fn layer, so the F-row drives media "
                        + "directly, matching the icons printed on the keys. Hold Option for the real "
                        + c.FKey + ".";
            }
            else
            {
                steps.Add("passed through");
            }

            _dChain.Set(steps.ToArray());

            _dTitle.Text = name + "  →  " + act;
            _dWhy.Text = why;

            Color tint = Theme.KeyPlain, edge = Theme.KeyEdge;
            if (mod != null) { tint = Theme.KeyModFill; edge = Theme.AccentDark; }
            else if (hit != null) { tint = Theme.KeyLayFill; edge = ControlPaint.Dark(Theme.Warn, 0.18f); }
            else if (med != null) { tint = Theme.KeyMedFill; edge = ControlPaint.Dark(Theme.Good, 0.18f); }
            _preview.Set(string.IsNullOrEmpty(c.Label) ? c.Pos : c.Label, c.Top, tint, edge);

            BuildEditor(c, hit, mod);
            DoLayoutAll();
        }

        /// <summary>
        /// Offer what this key can be changed to. Character keys choose which
        /// pt-PT key they should behave as; modifiers choose what they act as.
        /// </summary>
        void BuildEditor(Cap c, Mapping.Rule rule, Mapping.Mod mod)
        {
            _choices.Clear();
            _editKind = "";
            int pick = 0;

            if (c.Mod != null)
            {
                _editKind = "mod";
                _choices.Add(new string[] { "leave this key alone", "" });
                _choices.Add(new string[] { "act as Ctrl", "LCtrl" });
                _choices.Add(new string[] { "act as the Windows key", "LWin" });
                _choices.Add(new string[] { "act as Alt", "LAlt" });
                string now = mod == null ? "" : mod.Dst;
                for (int i = 0; i < _choices.Count; i++)
                    if (_choices[i][1] == now) pick = i;
            }
            else if (c.Sc != null)
            {
                _editKind = "scan";
                _choices.Add(new string[] { "leave this key alone", "" });
                for (int i = 0; i < Mapping.Targets.Length; i++)
                    _choices.Add(new string[] {
                        "type  " + Mapping.Targets[i][1],
                        Mapping.Targets[i][0] });
                string now = (rule != null && rule.To != null) ? rule.To : "";
                for (int i = 0; i < _choices.Count; i++)
                    if (_choices[i][1] == now) pick = i;
            }

            bool any = _choices.Count > 0;
            _target.Visible = any;
            _apply.Visible = any;
            _editLabel.Visible = any;
            if (!any) { DoLayoutAll(); return; }

            _target.Items.Clear();
            foreach (string[] ch in _choices) _target.Items.Add(ch[0]);
            _target.SetIndexQuiet(pick);
            _target.Width = _target.PreferredWidth();
        }

        void ApplyScancode()
        {
            Cap c = _board.Selected;
            if (c == null || _target.SelectedIndex < 0 ||
                _target.SelectedIndex >= _choices.Count) return;

            string value = _choices[_target.SelectedIndex][1];

            if (_editKind == "mod")
            {
                Mapping.SetMod(c.Mod, value == "" ? null : value);
                Toast(value == ""
                        ? c.Label + " left alone"
                        : c.Label + " will " + _choices[_target.SelectedIndex][0], Theme.Good);
            }
            else if (_editKind == "scan")
            {
                Mapping.SetScan(c.Sc, value == "" ? null : value, Mapping.ViewLang);
                Toast(value == ""
                        ? c.Label + " left alone"
                        : c.Label + " will type " + Mapping.TargetLabel(value), Theme.Good);
            }
            else return;

            // The hook reads the mapping on every keypress, so this is live.
            RefreshAll(false);
        }


        void Control(string action)
        {
            if (action == "stop")
            {
                Hook.Stop();
                Toast("remapping off", Theme.Dim);
            }
            else
            {
                Mapping.Load();
                Hook.Start();
                Toast(Hook.Running ? "remapping on" : "could not install the keyboard hook",
                      Hook.Running ? Theme.Good : Theme.Bad);
            }
            RefreshAll(false);
        }
    }
}
