using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The window: a rail on the left, one page at a time on the right, the
    /// same shell as Shotwin. Every page stays alive while hidden. Closing the
    /// window only hides it - the remaps keep running from the tray.
    /// </summary>
    public class MainForm : ChromeForm
    {
        Rail _rail;
        NavItem _navKeyboard, _navPhrases, _navShortcuts, _navSettings, _navAbout;
        KeyboardPage _keyboard;
        PhrasesPage _phrases;
        ShortcutsPage _shortcuts;
        SettingsPage _settings;
        AboutPage _about;
        readonly List<Page> _pages = new List<Page>();
        Page _current;

        BatteryPill _battery;
        TextLine _version;
        FlatButton _update;
        // Tracked here, not read back from Visible, which is false for every
        // control while the window itself is hidden in the tray.
        bool _showBattery, _showUpdate;
        Toast _toast;

        Timer _poll;
        NotifyIcon _tray;
        ToolStripMenuItem _trayToggle;
        bool _exiting;
        bool _startHidden;
        static MainForm _instance;

        /// <summary>
        /// A real exit, as opposed to closing the window - which only hides it.
        /// The updater needs this: its handover script waits for the process.
        /// </summary>
        public static void Quit()
        {
            if (_instance != null)
            {
                _instance._exiting = true;
                try { _instance.Close(); }
                catch { }
            }
            Application.Exit();
        }

        public MainForm() : this(false) { }

        public MainForm(bool background)
        {
            _startHidden = background;
            _instance = this;
            Text = "Keycap";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = new Icon(System.IO.Path.Combine(AppDir(), "keycap.ico")); } catch { }

            // Window sizes include the invisible resize frame: 8px each side
            // and 8 along the bottom, none on top now the caption is gone.
            int minW = Rail.W + Page.Inset * 2 + KeyboardPage.MinContentWidth + 16;
            MinimumSize = new Size(minW, 660);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(minW, Math.Min(1260, work.Width - 40)),
                            Math.Max(660, Math.Min(830, work.Height - 40)));

            // Loaded before the pages are built: the board colours itself from
            // the mapping as it is created.
            Mapping.Load();

            BuildRail();
            BuildPages();
            BuildTray();

            _poll = new Timer();
            _poll.Interval = 20000;
            _poll.Tick += delegate { RefreshAll(); };
            _poll.Start();

            Startup.EnsureArgs();
            Updater.Checked += delegate
            {
                // Raised from a background thread.
                try { BeginInvoke((MethodInvoker)delegate { ShowUpdate(); _about.Reload(); }); }
                catch { }
            };
            Updater.CheckAsync();

            // Subscribed before the hook starts, not after: Start() raises Changed on
            // its way out, and attaching one line later meant that first notification
            // had no listener. The tooltip then read "remapping off" for the whole
            // session while the app was in fact remapping, until something else
            // happened to toggle it.
            // Every way of turning remapping off ends in Hook.Raise(), so this is the one
            // place that has to take the indicator down with it. A persistent pill - the
            // mic-muted one - is only cleared by F5, and F5 only exists while the hook is
            // installed, so turning remapping off used to strand it on screen for good.
            Hook.Changed += delegate
            {
                if (InvokeRequired)
                {
                    try { BeginInvoke((MethodInvoker)OnHookChanged); } catch { }
                    return;
                }
                OnHookChanged();
            };
            Hook.Start();          // the app IS the remapper now

            ShowPage(_keyboard);
            LayoutShell();
            RefreshAll();
            _keyboard.SelectFirstMapped();
        }

        void OnHookChanged()
        {
            UpdateTray();
            if (!Hook.Running) Osd.Hide();
            foreach (Page p in _pages) p.Reload();
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

        // ---- rail -----------------------------------------------------------

        void BuildRail()
        {
            _rail = new Rail();
            Controls.Add(_rail);

            _navKeyboard = _rail.Add("Keyboard", NavGlyph.Keyboard);
            _navPhrases = _rail.Add("Phrases", NavGlyph.Phrases);
            _navShortcuts = _rail.Add("Shortcuts", NavGlyph.Shortcuts);
            _navSettings = _rail.Add("Settings", NavGlyph.Settings);
            _navAbout = _rail.Add("About", NavGlyph.About);
            _rail.Navigate += delegate(object s, NavItem n) { ShowPage(PageFor(n)); };

            _battery = new BatteryPill();
            _battery.Flat = true;
            _battery.Font = Theme.Font(8.5f, FontStyle.Regular);
            _battery.BackColor = Theme.Rail;
            _battery.Height = 22;
            _battery.Visible = false;
            _rail.Controls.Add(_battery);

            // The version sits here quietly, and is the same spot an update
            // offers itself from: a found build adds a button under it rather
            // than interrupting anything.
            _version = new TextLine();
            _version.Font = Theme.Font(8.25f, FontStyle.Regular);
            _version.Ink = Theme.Dimmer;
            _version.BackColor = Theme.Rail;
            _version.Text = "Keycap " + Updater.Pretty(Updater.Current);
            _rail.Controls.Add(_version);

            _update = new FlatButton();
            _update.Font = Theme.Font(8.25f, FontStyle.Regular);
            _update.Backdrop = Theme.Rail;
            _update.Height = 28;
            _update.Visible = false;
            _update.Click += delegate { InstallUpdate(); };
            _rail.Controls.Add(_update);

            _toast = new Toast();
            Controls.Add(_toast);
        }

        void BuildPages()
        {
            _keyboard = new KeyboardPage();
            _phrases = new PhrasesPage();
            _shortcuts = new ShortcutsPage();
            _settings = new SettingsPage();
            _about = new AboutPage();
            _pages.AddRange(new Page[] { _keyboard, _phrases, _shortcuts, _settings, _about });

            foreach (Page p in _pages)
            {
                p.Visible = false;
                p.Say = Toast;
                p.Changed = RefreshAll;
                Controls.Add(p);
            }
        }

        Page PageFor(NavItem n)
        {
            if (n == _navPhrases) return _phrases;
            if (n == _navShortcuts) return _shortcuts;
            if (n == _navSettings) return _settings;
            if (n == _navAbout) return _about;
            return _keyboard;
        }

        NavItem NavFor(Page p)
        {
            if (p == _phrases) return _navPhrases;
            if (p == _shortcuts) return _navShortcuts;
            if (p == _settings) return _navSettings;
            if (p == _about) return _navAbout;
            return _navKeyboard;
        }

        /// <summary>Bring a page forward. Public so the tray can open a page directly.</summary>
        public void ShowPage(Page page)
        {
            if (page == null) return;
            NavItem n = NavFor(page);
            if (!n.Selected) { _rail.Select(n); return; }   // Select comes back here

            if (_current == page) return;
            Page was = _current;
            _current = page;
            page.Visible = true;
            page.BringToFront();
            if (was != null) was.Visible = false;
            _toast.BringToFront();
            page.Entered();
        }

        /// <summary>The page names, in rail order, for Ctrl+1..5.</summary>
        public void ShowPage(int index)
        {
            if (index >= 0 && index < _pages.Count) ShowPage(_pages[index]);
        }

        // ---- layout ---------------------------------------------------------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutShell();
        }

        void LayoutShell()
        {
            if (_rail == null || _pages.Count == 0) return;
            int w = ClientSize.Width, h = ClientSize.Height;

            _rail.SetBounds(0, 0, Rail.W, h);
            foreach (Page p in _pages)
                p.SetBounds(Rail.W, CaptionHeight, w - Rail.W, h - CaptionHeight);

            // Rail footer, packed up from the bottom edge.
            int y = h - 16;
            if (_showUpdate)
            {
                _update.Width = _update.PreferredWidth();
                y -= _update.Height;
                _update.Location = new Point(16, y);
                y -= 8;
            }
            y -= 18;
            _version.SetBounds(17, y, Rail.W - 32, 18);
            if (_showBattery)
            {
                y -= 26;
                _battery.SetBounds(16, y, Rail.W - 28, 22);
            }

            _toast.CenterLeft = Rail.W;
            _toast.Top = h - _toast.Height - 22;
            if (_toast.Visible) _toast.Left = Rail.W + (w - Rail.W - _toast.Width) / 2;
        }

        // ---- data -----------------------------------------------------------

        void Toast(string msg, Color c)
        {
            _toast.Say(msg, c);
        }

        /// <summary>
        /// Re-read the mapping and redraw every page from it. Runs on a timer
        /// too, so an edit made to the file by hand shows up on its own.
        /// </summary>
        void RefreshAll()
        {
            Mapping.Load();
            RefreshBattery();
            foreach (Page p in _pages) p.Reload();

            int n = Mapping.Phrases.Count;
            string badge = n == 0 ? "" : n.ToString();
            if (_navPhrases.Badge != badge) { _navPhrases.Badge = badge; _navPhrases.Invalidate(); }
            UpdateTray();
        }

        void RefreshBattery()
        {
            int pct = -1;
            string name = "";
            try
            {
                List<BatteryInfo> b = Hid.Scan();
                if (b.Count > 0) { name = b[0].Name; pct = b[0].Percent; }
            }
            catch { }

            // Nothing is better than a line saying there is nothing.
            bool show = pct >= 0;
            _battery.Set(name, pct);
            if (_showBattery != show)
            {
                _showBattery = show;
                _battery.Visible = show;
                LayoutShell();
            }
        }

        void ShowUpdate()
        {
            bool show = Updater.Available;
            _update.Text = "Update to " + Updater.PrettyTag(Updater.LatestTag);
            _showUpdate = show;
            _update.Visible = show;
            LayoutShell();
        }

        void InstallUpdate()
        {
            Toast("Downloading " + Updater.PrettyTag(Updater.LatestTag) + "...", Theme.Dim);
            _update.Enabled = false;
            Application.DoEvents();
            string err = Updater.Install();
            if (err.Length > 0)
            {
                _update.Enabled = true;
                Toast(err, Theme.Bad);
                return;
            }
            Quit();   // the handover script waits for this process to go
        }

        // ---- keyboard -------------------------------------------------------

        /// <summary>
        /// Ctrl+1 to Ctrl+5 walk the rail - Command+1 on the Magic Keyboard, the
        /// way a Mac app switches its tabs - and Ctrl+W puts the window away.
        /// A box recording a phrase's trigger keeps every key for itself.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!(ActiveControl is ComboCapture))
            {
                Keys key = keyData & Keys.KeyCode;
                if ((keyData & Keys.Modifiers) == Keys.Control)
                {
                    if (key >= Keys.D1 && key <= Keys.D5) { ShowPage(key - Keys.D1); return true; }
                    if (key == Keys.W) { Close(); return true; }
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- tray -----------------------------------------------------------

        void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon; } catch { }
            _tray.Visible = true;

            ContextMenuStrip menu = Menus.Create(Theme.Font(9f, FontStyle.Regular));

            ToolStripMenuItem open = Menus.Item("Open Keycap", Theme.Text);
            open.Font = Theme.Semi(9f);
            open.Click += delegate { ShowWindow(); };

            // Says what clicking it will do, rather than a tick whose meaning
            // has to be worked out.
            _trayToggle = Menus.Item("Pause remapping", Theme.Text);
            _trayToggle.Click += delegate
            {
                if (Hook.Running) Hook.Stop(); else { Mapping.Load(); Hook.Start(); }
                UpdateTray();
            };

            ToolStripMenuItem phrases = Menus.Item("Phrases", Theme.Text);
            phrases.Click += delegate { ShowWindow(); ShowPage(_phrases); };
            ToolStripMenuItem settings = Menus.Item("Settings", Theme.Text);
            settings.Click += delegate { ShowWindow(); ShowPage(_settings); };

            ToolStripMenuItem quit = Menus.Item("Exit", Theme.Text);
            quit.Click += delegate { _exiting = true; Close(); Application.Exit(); };

            menu.Items.Add(open);
            menu.Items.Add(_trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(phrases);
            menu.Items.Add(settings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);
            menu.Opening += delegate { UpdateTray(); };

            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowWindow();
            };
            UpdateTray();
        }

        void UpdateTray()
        {
            if (_tray == null) return;
            _tray.Text = Hook.Running ? "Keycap - remapping on" : "Keycap - remapping off";
            if (_trayToggle != null)
                _trayToggle.Text = Hook.Running ? "Pause remapping" : "Resume remapping";
        }

        void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            try { SetForegroundWindow(Handle); } catch { }
        }

        /// <summary>
        /// Application.Run() shows the form; started from the Startup shortcut
        /// that first show is suppressed, so the app boots into the tray.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden)
            {
                _startHidden = false;
                // The handle must exist anyway: the tray menu, the hook and the
                // second-launch broadcast all need a window to talk to.
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        /// <summary>
        /// A second launch broadcasts WmShow rather than starting its own
        /// process, so answer it by surfacing the window we already have.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (Program.WmShow != 0 && m.Msg == Program.WmShow)
            {
                ShowWindow();
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        static string AppDir()
        {
            return System.IO.Path.GetDirectoryName(Application.ExecutablePath);
        }
    }
}
