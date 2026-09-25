using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The version and whether a newer one is out, with the one action that
    /// matters about it; then what Keycap is for, the way out when the keyboard
    /// seems stuck, and where to look for more.
    /// </summary>
    public class AboutPage : Page
    {
        ScrollPage _scroll;
        RowCard _updateCard;
        FlatButton _updateButton;
        TextLine _whatHead, _whatText, _stuckHead, _stuckText, _linksHead;
        Card _whatCard;
        FlatButton _repo, _log;

        const int MaxColumn = 760;

        public AboutPage()
        {
            Title = "About";
            Subtitle = "Keycap " + Updater.Pretty(Updater.Current) + "  ·  MIT licensed";

            _scroll = new ScrollPage();
            Controls.Add(_scroll);
            Panel content = _scroll.Content;

            _updateButton = new FlatButton();
            _updateButton.Height = 32;
            _updateButton.Font = Theme.Font(9f, FontStyle.Regular);
            _updateButton.Backdrop = Theme.Card;
            _updateButton.Click += delegate { UpdateClicked(); };

            _updateCard = new RowCard();
            _updateCard.Fill = Theme.Card;
            _updateCard.Page = Theme.Back;
            _updateCard.Add("", "", _updateButton);
            content.Controls.Add(_updateCard);

            _whatHead = Section("What it does", Theme.Back);
            content.Controls.Add(_whatHead);

            _whatCard = new Card();
            _whatCard.Fill = Theme.Card;
            _whatCard.Page = Theme.Back;
            content.Controls.Add(_whatCard);

            _whatText = Hint("Keycap makes an Apple Magic Keyboard behave on Windows: Command acts as Ctrl, "
                           + "Option as the Windows key, the keys that type the wrong character are fixed per "
                           + "layout, and the function row does what is printed on it. It lives in the tray - "
                           + "closing the window keeps the remaps running.", Theme.Card);
            _whatCard.Controls.Add(_whatText);

            _stuckHead = Section("If the keyboard feels stuck", Theme.Back);
            content.Controls.Add(_stuckHead);

            _stuckText = Hint("Press both Shift keys together. That releases every modifier and turns remapping "
                            + "off; turn it back on from the Keyboard page or the tray.", Theme.Back);
            content.Controls.Add(_stuckText);

            _linksHead = Section("Links", Theme.Back);
            content.Controls.Add(_linksHead);

            _repo = NewLink("github.com/" + Updater.Repo);
            _repo.Click += delegate
            {
                try { Process.Start("https://github.com/" + Updater.Repo); }
                catch { }
            };

            _log = NewLink("Open the error log");
            _log.Click += delegate
            {
                string path = Path.Combine(Program.Dir, "error.log");
                if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\"");
                else Say("No errors logged", Theme.Dim);
            };

            DrawUpdate();
        }

        FlatButton NewLink(string text)
        {
            FlatButton b = new FlatButton();
            b.Ghost = true;
            b.Text = text;
            b.Height = 26;
            b.BackColor = Theme.Back;
            b.Backdrop = Theme.Back;
            b.Width = b.PreferredWidth();
            _scroll.Content.Controls.Add(b);
            return b;
        }

        /// <summary>The window calls this when an update check finishes.</summary>
        public override void Reload()
        {
            DrawUpdate();
        }

        /// <summary>Put the updater's state into the update row and its button.</summary>
        void DrawUpdate()
        {
            string head, sub;
            if (Updater.Checking)
            {
                head = "Looking for a new version...";
                sub = "";
                _updateButton.Text = "Check for updates";
                _updateButton.Primary = false;
                _updateButton.Enabled = false;
            }
            else if (Updater.Available)
            {
                head = "Keycap " + Updater.PrettyTag(Updater.LatestTag) + " is available.";
                sub = "Installing takes a few seconds and Keycap restarts itself. Your mappings are kept.";
                _updateButton.Text = "Install now";
                _updateButton.Primary = true;
                _updateButton.Enabled = true;
            }
            else if (Updater.LastError.Length > 0)
            {
                head = "Could not check for updates.";
                sub = "Keycap could not reach the update server. It will try again the next time it starts.";
                _updateButton.Text = "Try again";
                _updateButton.Primary = false;
                _updateButton.Enabled = true;
            }
            else
            {
                head = "Keycap is up to date.";
                sub = "You are running version " + Updater.Pretty(Updater.Current)
                    + ". Keycap checks again each time it starts.";
                _updateButton.Text = "Check for updates";
                _updateButton.Primary = false;
                _updateButton.Enabled = true;
            }
            _updateButton.Width = _updateButton.PreferredWidth();
            _updateButton.Invalidate();
            _updateCard.SetText(0, head, sub);
            Arrange();
        }

        void UpdateClicked()
        {
            if (!Updater.Available)
            {
                Updater.CheckAsync();
                DrawUpdate();
                return;
            }

            _updateButton.Enabled = false;
            _updateCard.SetText(0, "Downloading Keycap " + Updater.PrettyTag(Updater.LatestTag) + "...",
                "Keycap will close and come back on the new version.");
            Refresh();
            string err = Updater.Install();
            if (err.Length > 0)
            {
                _updateButton.Enabled = true;
                _updateCard.SetText(0, "The update could not be installed.", err);
            }
            else MainForm.Quit();
        }

        public override void Arrange()
        {
            if (_log == null) return;
            _scroll.SetBounds(Inset, ContentTop, Width - Inset, Height - ContentTop);
            int col = Math.Max(1, Math.Min(_scroll.Width - Inset, MaxColumn));
            int y = 0;

            _updateCard.Location = new Point(0, y);
            y += _updateCard.Arrange(col);

            y = PlaceSection(_whatHead, y, col);
            _whatText.SetBounds(18, 14, Math.Max(1, col - 36), 1);
            _whatText.Height = _whatText.Measure();
            _whatCard.SetBounds(0, y, col, _whatText.Height + 28);
            y += _whatCard.Height;

            y = PlaceSection(_stuckHead, y, col);
            _stuckText.SetBounds(0, y, col, 1);
            _stuckText.Height = _stuckText.Measure();
            y += _stuckText.Height;

            y = PlaceSection(_linksHead, y, col);
            _repo.SetBounds(0, y, _repo.PreferredWidth(), 26);
            y += 26 + 4;
            _log.SetBounds(0, y, _log.PreferredWidth(), 26);
            y += 26;

            _scroll.Content.Height = y + 24;
            _scroll.Sync();
        }

        /// <summary>A section heading under what came before; returns where its content starts.</summary>
        static int PlaceSection(TextLine head, int y, int col)
        {
            y += 22;
            head.SetBounds(0, y, col, 18);
            return y + 18 + 8;
        }
    }
}
