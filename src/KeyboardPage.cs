using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The board, and an inspector for whichever key is selected on it: what
    /// the key does, why, the path it takes, and what it can be changed to. A
    /// change takes effect the moment it is picked - there is no Apply.
    /// </summary>
    public class KeyboardPage : Page
    {
        KeyboardView _board;
        Card _boardCard, _detailCard;
        Legend _legend;
        ToggleChip _remap;

        CapPreview _preview;
        TextLine _dTitle, _dWhy, _editLabel;
        ChainView _dChain;
        DropChip _target;

        DropChip _layoutPick;          // never shown: the spacebar opens its menu
        List<InputLayout> _layouts;
        bool _userPickedLayout;

        List<string[]> _choices = new List<string[]>();   // label, value
        string _editKind = "";                             // scan | mod
        /// <summary>
        /// Whether the selected key can be changed. Kept here rather than read
        /// back from _target.Visible, which is false while the page is hidden.
        /// </summary>
        bool _editable;

        const int Gap = 12;
        const int DetailH = 140;

        public KeyboardPage()
        {
            Title = "Keyboard";
            Subtitle = "Click a highlighted key to see what it does and change it. "
                     + "Click the spacebar to switch layout.";

            _remap = new ToggleChip();
            _remap.Text = "Remapping";
            _remap.Font = Theme.Font(9.75f, FontStyle.Regular);
            _remap.BackColor = Theme.Back;
            _remap.Height = 32;
            _remap.Width = _remap.PreferredWidth();
            _remap.SetQuiet(Hook.Running);
            _remap.CheckedChanged += delegate { SetRemapping(_remap.Checked); };
            Controls.Add(_remap);
            HeaderRight = _remap.Width + 24;

            ToolTip tip = Theme.Tip();
            tip.SetToolTip(_remap, "Pressing both Shift keys together also turns remapping off.");

            _legend = new Legend();
            Controls.Add(_legend);

            BuildLayouts();
            BuildBoard();
            BuildDetail();
        }

        void BuildLayouts()
        {
            _layoutPick = new DropChip();
            _layoutPick.Font = Theme.Font(9f, FontStyle.Regular);
            _layoutPick.Visible = false;
            Controls.Add(_layoutPick);

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
                ShowLayout(il);
                if (il.Hkl != IntPtr.Zero)
                {
                    // Success is visible in the spacebar itself, so only a
                    // failure is worth saying anything about.
                    if (!InputLang.Activate(il))
                        Say("Could not switch Windows to " + il.Name, Theme.Bad);
                }
            };
        }

        void BuildBoard()
        {
            _boardCard = new Card();
            _boardCard.Fill = Theme.Card;
            _boardCard.Radius = 12;
            Controls.Add(_boardCard);

            _board = new KeyboardView();
            _board.BackColor = Theme.Card;
            _board.KeyPicked += delegate(object s, Cap c) { ShowKey(c); };
            _board.SpaceClicked += delegate(object s, Cap c)
            {
                _layoutPick.ShowMenu(_board,
                    new Point((int)c.Bounds.X + 12, (int)c.Bounds.Bottom + 4));
            };
            _boardCard.Controls.Add(_board);

            InputLayout il = _layouts[_layoutPick.SelectedIndex];
            Mapping.ViewLang = il.LangId;
            _board.SetLayout(il.Board);
            _board.SpaceLabel = il.Name;
        }

        void BuildDetail()
        {
            _detailCard = new Card();
            _detailCard.Fill = Theme.Card;
            _detailCard.Radius = 12;
            Controls.Add(_detailCard);

            _preview = new CapPreview();
            _preview.BackColor = Theme.Card;
            _detailCard.Controls.Add(_preview);

            _dTitle = new TextLine();
            _dTitle.Font = Theme.Semi(12f);
            _dTitle.BackColor = Theme.Card;
            _dTitle.Height = 26;
            _detailCard.Controls.Add(_dTitle);

            _dWhy = Hint("", Theme.Card);
            _dWhy.Font = Theme.Font(9f, FontStyle.Regular);
            _detailCard.Controls.Add(_dWhy);

            _dChain = new ChainView();
            _dChain.BackColor = Theme.Card;
            _detailCard.Controls.Add(_dChain);

            _editLabel = Section("What it does", Theme.Card);
            _editLabel.Visible = false;
            _detailCard.Controls.Add(_editLabel);

            _target = new DropChip();
            _target.Font = Theme.Font(9f, FontStyle.Regular);
            _target.BackColor = Theme.Card;
            _target.Visible = false;
            _target.SelectionChanged += delegate { ApplyChoice(); };
            _detailCard.Controls.Add(_target);
        }

        // ---- layout ---------------------------------------------------------

        /// <summary>
        /// Height the keyboard wants at this width, keeping the caps close to
        /// square instead of stretching them to fill whatever room is left.
        /// </summary>
        static int BoardHeight(int boardWidth)
        {
            float pitch = (boardWidth + 5f) / 15f;
            float keyH = pitch * 0.8f;
            if (keyH < 36f) keyH = 36f;
            if (keyH > 64f) keyH = 64f;
            return (int)Math.Round(keyH * 6 + 5 * 5 + 12);
        }

        /// <summary>The narrowest the page can be before key legends stop fitting.</summary>
        public const int MinContentWidth = 860;

        public override void Arrange()
        {
            if (_board == null) return;
            int w = ContentWidth;

            _remap.Location = new Point(Width - Inset - _remap.Width, 8);

            int top = ContentTop;
            _legend.SetBounds(Inset + 2, top, w - 4, 20);
            top += 30;

            // The board takes its natural height, or what is left once the
            // inspector has its room - whichever is smaller. Extra height stays
            // at the bottom rather than stretching the keys tall.
            const int PadX = 16, PadY = 10;
            int natural = BoardHeight(w - PadX * 2) + PadY * 2;
            int room = Height - top - Gap - DetailH - 24;
            int boardH = Math.Max(200, Math.Min(natural, room));

            _boardCard.SetBounds(Inset, top, w, boardH);
            _board.SetBounds(PadX, PadY, w - PadX * 2, boardH - PadY * 2);
            _board.Invalidate();

            _detailCard.SetBounds(Inset, top + boardH + Gap, w, DetailH);
            ArrangeDetail();
        }

        void ArrangeDetail()
        {
            int w = _detailCard.Width;
            const int Pad = 18, Cap = DetailH - Pad * 2;

            int editW = _editable ? Math.Max(200, Math.Min(280, _target.PreferredWidth())) : 0;
            int textL = Pad + Cap + 22;
            int textR = editW > 0 ? w - Pad - editW - 28 : w - Pad;

            _preview.SetBounds(Pad, Pad, Cap, Cap);
            _dTitle.SetBounds(textL, Pad - 2, Math.Max(120, textR - textL), 26);
            _dWhy.SetBounds(textL, Pad + 28, Math.Max(160, textR - textL), 40);
            _dChain.SetBounds(textL, DetailH - Pad - 28, w - Pad - textL, 28);

            if (_editable)
            {
                int gx = w - Pad - editW;
                _editLabel.SetBounds(gx, Pad - 2, editW, 18);
                _target.SetBounds(gx, Pad + 20, editW, 32);
            }
        }

        // ---- remapping ------------------------------------------------------

        void SetRemapping(bool on)
        {
            if (on)
            {
                Mapping.Load();
                Hook.Start();
                if (!Hook.Running) Say("Could not install the keyboard hook", Theme.Bad);
                else Say("Remapping on", Theme.Good);
            }
            else
            {
                Hook.Stop();
                Say("Remapping off - the keyboard is back to plain Windows", Theme.Dim);
            }
            _remap.SetQuiet(Hook.Running);
        }

        // ---- data -----------------------------------------------------------

        public override void Reload()
        {
            _remap.SetQuiet(Hook.Running);
            _board.Sync();
            FollowSystemLayout();
            if (_board.Selected != null) ShowKey(_board.Selected);
            else SelectFirstMapped();
        }

        void ShowLayout(InputLayout il)
        {
            Mapping.ViewLang = il.LangId;
            _board.SetLayout(il.Board);
            _board.SpaceLabel = il.Name;
            SelectFirstMapped();
        }

        public void SelectFirstMapped()
        {
            foreach (Cap c in _board.Keys)
                if (c.Kind == KeyKind.Layout) { ShowKey(c); return; }
            foreach (Cap c in _board.Keys)
                if (c.Kind != KeyKind.Plain) { ShowKey(c); return; }
        }

        /// <summary>Track the Windows layout until the user picks one here.</summary>
        void FollowSystemLayout()
        {
            if (_userPickedLayout) return;
            int active = InputLang.CurrentLangId();
            for (int i = 0; i < _layouts.Count; i++)
            {
                if (_layouts[i].LangId != active) continue;
                if (i == _layoutPick.SelectedIndex) return;
                _layoutPick.SetIndexQuiet(i);
                ShowLayout(_layouts[i]);
                return;
            }
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
                if (why.Length == 0) why = "Keycap leaves this key exactly as Windows reads it.";
            }

            _dChain.Set(steps.ToArray());
            _dTitle.Text = name + "  →  " + act;
            _dWhy.Text = why;

            Color tint = Theme.KeyPlain, edge = Theme.KeyEdge;
            if (mod != null) { tint = Theme.KeyModFill; edge = Theme.AccentDark; }
            else if (hit != null) { tint = Theme.KeyLayFill; edge = ControlPaint.Dark(Theme.Warn, 0.3f); }
            else if (med != null) { tint = Theme.KeyMedFill; edge = ControlPaint.Dark(Theme.Good, 0.3f); }
            _preview.Set(name, c.Top, tint, edge);

            BuildEditor(c, hit, mod);
            ArrangeDetail();
        }

        /// <summary>
        /// Offer what this key can be changed to. Character keys choose which
        /// key they should behave as; modifiers choose what they act as.
        /// </summary>
        void BuildEditor(Cap c, Mapping.Rule rule, Mapping.Mod mod)
        {
            _choices.Clear();
            _editKind = "";
            int pick = 0;

            if (c.Mod != null)
            {
                _editKind = "mod";
                _choices.Add(new string[] { "Leave this key alone", "" });
                _choices.Add(new string[] { "Act as Ctrl", "LCtrl" });
                _choices.Add(new string[] { "Act as the Windows key", "LWin" });
                _choices.Add(new string[] { "Act as Alt", "LAlt" });
                string now = mod == null ? "" : mod.Dst;
                for (int i = 0; i < _choices.Count; i++)
                    if (_choices[i][1] == now) pick = i;
            }
            else if (c.Sc != null)
            {
                _editKind = "scan";
                _choices.Add(new string[] { "Leave this key alone", "" });
                for (int i = 0; i < Mapping.Targets.Length; i++)
                    _choices.Add(new string[] {
                        "Type  " + Mapping.Targets[i][1],
                        Mapping.Targets[i][0] });
                string now = (rule != null && rule.To != null) ? rule.To : "";
                for (int i = 0; i < _choices.Count; i++)
                    if (_choices[i][1] == now) pick = i;
            }

            bool any = _choices.Count > 0;
            _editable = any;
            _target.Visible = any;
            _editLabel.Visible = any;
            if (!any) return;

            _target.Items.Clear();
            foreach (string[] ch in _choices) _target.Items.Add(ch[0]);
            _target.SetIndexQuiet(pick);
        }

        void ApplyChoice()
        {
            Cap c = _board.Selected;
            if (c == null || _target.SelectedIndex < 0 ||
                _target.SelectedIndex >= _choices.Count) return;

            string value = _choices[_target.SelectedIndex][1];
            string label = string.IsNullOrEmpty(c.Label) ? c.Pos : c.Label;

            if (_editKind == "mod")
            {
                Mapping.SetMod(c.Mod, value == "" ? null : value);
                Say(value == ""
                        ? label + " is left alone"
                        : label + " will " + _choices[_target.SelectedIndex][0].ToLowerInvariant(), Theme.Good);
            }
            else if (_editKind == "scan")
            {
                Mapping.SetScan(c.Sc, value == "" ? null : value, Mapping.ViewLang);
                Say(value == ""
                        ? label + " is left alone"
                        : label + " will type " + Mapping.TargetLabel(value), Theme.Good);
            }
            else return;

            // The hook reads the mapping on every keypress, so this is live.
            Changed();
        }
    }
}
