using System;
using System.Drawing;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// Settings. Each row says what it does in a sentence, rather than relying
    /// on a one-word label to carry the meaning.
    /// </summary>
    public class SettingsForm : Form
    {
        const int Pad = 22;
        int _y;

        Label _upHead, _upSub;
        FlatButton _act;

        public SettingsForm()
        {
            Text = "Settings";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(560, 404);
            MinimumSize = new Size(520, 404);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            _y = Pad;

            Section("KEYBOARD");

            Row("Show the microphone indicator",
                "A small panel above the taskbar when F5 mutes or unmutes the microphone. "
                + "It never takes focus and clicks pass through it.",
                Mapping.ShowIndicator,
                delegate (bool on)
                {
                    Mapping.ShowIndicator = on;
                    Mapping.Save();
                    if (!on) Osd.Hide();
                });

            Row("Remap while Keycap is running",
                "Turn this off to hand the keyboard back to Windows without closing the app. "
                + "Pressing both Shift keys together does the same thing.",
                Hook.Running,
                delegate (bool on)
                {
                    if (on) { Mapping.Load(); Hook.Start(); }
                    else Hook.Stop();
                });

            ToggleChip startup = null;
            startup = Row("Start with Windows",
                "Keycap runs from your Startup folder, so the remaps are live from login - "
                + "it starts in the tray, without opening the window.",
                StartupEnabled(),
                delegate (bool on)
                {
                    string err = SetStartup(on);
                    if (err == null) return;

                    // Put the switch back where the Startup folder says it should be, so
                    // it cannot sit there claiming something that did not happen.
                    _syncing = true;
                    try { startup.Checked = StartupEnabled(); }
                    finally { _syncing = false; }

                    MessageBox.Show(this,
                        (on ? "Could not add Keycap to your Startup folder.\r\n\r\n"
                            : "Could not remove Keycap from your Startup folder.\r\n\r\n") + err,
                        "Keycap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });

            Section("UPDATES");

            _upHead = new Label();
            _upHead.Font = Theme.Font(9.75f, FontStyle.Regular);
            _upHead.ForeColor = Theme.Text;
            _upHead.BackColor = Theme.Back;
            _upHead.AutoSize = false;
            _upHead.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _upHead.SetBounds(Pad, _y, ClientSize.Width - Pad * 2 - 160, 20);
            Controls.Add(_upHead);

            _upSub = new Label();
            _upSub.Font = Theme.Font(8.25f, FontStyle.Regular);
            _upSub.ForeColor = Theme.Dimmer;
            _upSub.BackColor = Theme.Back;
            _upSub.AutoSize = false;
            _upSub.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _upSub.SetBounds(Pad, _y + 20, ClientSize.Width - Pad * 2 - 160, 40);
            Controls.Add(_upSub);

            _act = new FlatButton();
            _act.Font = Theme.Font(9f, FontStyle.Regular);
            _act.Backdrop = Theme.Back;
            _act.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _act.SetBounds(ClientSize.Width - Pad - 142, _y - 2, 142, 32);
            _act.Click += delegate { Act(); };
            Controls.Add(_act);

            _y += 64;

            DrawUpdate();
            Updater.Checked += OnChecked;

            FlatButton close = new FlatButton();
            close.Text = "Done";
            close.Primary = true;
            close.Backdrop = Theme.Back;
            close.Font = Theme.Font(9f, FontStyle.Regular);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.SetBounds(ClientSize.Width - Pad - 100, ClientSize.Height - Pad - 34, 100, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Label about = new Label();
            about.Text = "Keycap " + Updater.Pretty(Updater.Current) + "  -  MIT licensed";
            about.Font = Theme.Font(7.75f, FontStyle.Regular);
            about.ForeColor = Theme.Dimmer;
            about.BackColor = Theme.Back;
            about.AutoSize = false;
            about.TextAlign = ContentAlignment.MiddleLeft;
            about.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            about.SetBounds(Pad, ClientSize.Height - Pad - 30, 300, 26);
            Controls.Add(about);
        }

        /// <summary>
        /// The whole update section is one label pair plus one button, so the
        /// state is written in a single place rather than spread over handlers.
        /// </summary>
        void DrawUpdate()
        {
            if (Updater.Checking)
            {
                _upHead.Text = "Looking for a new version...";
                _upSub.Text = "";
                _act.Text = "Check for updates";
                _act.Primary = false;
                _act.Enabled = false;
            }
            else if (Updater.Available)
            {
                _upHead.Text = "Keycap " + Updater.PrettyTag(Updater.LatestTag) + " is available.";
                _upSub.Text = "Installing takes a few seconds and Keycap restarts itself. "
                            + "Your mappings are kept.";
                _act.Text = "Install now";
                _act.Primary = true;
                _act.Enabled = true;
            }
            else if (Updater.LastError.Length > 0)
            {
                _upHead.Text = "Could not check for updates.";
                _upSub.Text = "Keycap could not reach the update server. It will try again "
                            + "the next time it starts.";
                _act.Text = "Try again";
                _act.Primary = false;
                _act.Enabled = true;
            }
            else
            {
                _upHead.Text = "Keycap is up to date.";
                _upSub.Text = "You are running version " + Updater.Pretty(Updater.Current)
                            + ". Keycap checks again each time it starts.";
                _act.Text = "Check for updates";
                _act.Primary = false;
                _act.Enabled = true;
            }
            _act.Invalidate();
        }

        void Act()
        {
            if (!Updater.Available) { Updater.CheckAsync(); DrawUpdate(); return; }

            _act.Enabled = false;
            _upHead.Text = "Downloading Keycap " + Updater.PrettyTag(Updater.LatestTag) + "...";
            _upSub.Text = "Keycap will close and come back on the new version.";
            Refresh();

            string err = Updater.Install();
            if (err.Length > 0)
            {
                _act.Enabled = true;
                _upHead.Text = "The update could not be installed.";
                _upSub.Text = err;
                return;
            }
            MainForm.Quit();   // the handover script waits for this process to go
        }

        /// <summary>Raised on the checking thread.</summary>
        void OnChecked(object sender, EventArgs e)
        {
            try { BeginInvoke((MethodInvoker)delegate { DrawUpdate(); }); }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Updater.Checked -= OnChecked;   // the event is static, the form is not
            base.OnFormClosed(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        void Section(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Font(7.75f, FontStyle.Bold);
            l.ForeColor = Theme.Dimmer;
            l.BackColor = Theme.Back;
            l.AutoSize = false;
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            l.SetBounds(Pad, _y, ClientSize.Width - Pad * 2, 16);
            Controls.Add(l);
            _y += 24;
        }

        delegate void Setter(bool on);

        /// <summary>Set while a row is putting its own switch back, so the correction
        /// does not read as another click and run the setter again.</summary>
        bool _syncing;

        ToggleChip Row(string title, string detail, bool value, Setter apply)
        {
            ToggleChip t = new ToggleChip();
            t.Text = "";
            t.Font = Theme.Font(9f, FontStyle.Regular);
            t.BackColor = Theme.Back;
            t.Checked = value;
            t.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            t.SetBounds(ClientSize.Width - Pad - 44, _y + 2, 44, 24);
            t.CheckedChanged += delegate { if (!_syncing) apply(t.Checked); };
            Controls.Add(t);

            Label head = new Label();
            head.Text = title;
            head.Font = Theme.Font(9.75f, FontStyle.Regular);
            head.ForeColor = Theme.Text;
            head.BackColor = Theme.Back;
            head.AutoSize = false;
            head.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            head.SetBounds(Pad, _y, ClientSize.Width - Pad * 2 - 64, 20);
            Controls.Add(head);

            Label sub = new Label();
            sub.Text = detail;
            sub.Font = Theme.Font(8.25f, FontStyle.Regular);
            sub.ForeColor = Theme.Dimmer;
            sub.BackColor = Theme.Back;
            sub.AutoSize = false;
            sub.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            sub.SetBounds(Pad, _y + 20, ClientSize.Width - Pad * 2 - 64, 32);
            Controls.Add(sub);

            _y += 62;
            return t;
        }

        static string StartupLink()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Keycap.lnk");
        }

        static bool StartupEnabled() { return System.IO.File.Exists(StartupLink()); }

        /// <summary>
        /// Writes or removes the Startup shortcut, and says what went wrong rather than
        /// nothing at all. The switch paints itself the moment it is clicked, so a failure
        /// swallowed here left it showing ON while Keycap would not in fact start with
        /// Windows - a lie that only unwound the next time Settings was opened.
        /// </summary>
        static string SetStartup(bool on)
        {
            try
            {
                string link = StartupLink();
                if (!on)
                {
                    if (System.IO.File.Exists(link)) System.IO.File.Delete(link);
                    return null;
                }
                string exe = Application.ExecutablePath;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { exe });
                st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { Program.BackgroundArg });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { System.IO.Path.GetDirectoryName(exe) });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { exe + ",0" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                    null, sc, new object[] { });
                return null;
            }
            catch (Exception ex)
            {
                // Unwrapped: everything here goes through InvokeMember, which hands back
                // whatever the shortcut object threw wrapped in a TargetInvocationException.
                // The outer message is always the same sentence about an invocation target,
                // so on its own it says nothing about what actually failed.
                Exception cause = ex;
                while (cause.InnerException != null) cause = cause.InnerException;

                Program.LogError(cause);
                return cause.Message;
            }
        }

        /// <summary>Shortcuts written by older builds carry no flag, so without
        /// this they would still pop the window open at login.</summary>
        public static void EnsureStartupArgs()
        {
            try
            {
                string link = StartupLink();
                if (!System.IO.File.Exists(link)) return;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                string args = (string)sc.GetType().InvokeMember("Arguments",
                    System.Reflection.BindingFlags.GetProperty, null, sc, new object[] { });
                if (args == null ||
                    args.IndexOf(Program.BackgroundArg, StringComparison.OrdinalIgnoreCase) < 0)
                    SetStartup(true);
            }
            catch { }
        }
    }
}
