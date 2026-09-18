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

        public SettingsForm()
        {
            Text = "Settings";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f, FontStyle.Regular);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(560, 340);
            MinimumSize = new Size(520, 340);
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

            Row("Start with Windows",
                "Keycap runs from your Startup folder, so the remaps are live from login.",
                StartupEnabled(),
                delegate (bool on) { SetStartup(on); });

            Section("UPDATES");

            Label repo = new Label();
            repo.Text = string.IsNullOrEmpty(Updater.Repo)
                ? "No release repository configured yet."
                : "Checking " + Updater.Repo + " - you are on v" + Updater.Current;
            repo.Font = Theme.Font(8.75f, FontStyle.Regular);
            repo.ForeColor = Theme.Dim;
            repo.BackColor = Theme.Back;
            repo.AutoSize = false;
            repo.SetBounds(Pad, _y, ClientSize.Width - Pad * 2, 20);
            Controls.Add(repo);
            _y += 28;

            FlatButton close = new FlatButton();
            close.Text = "Done";
            close.Primary = true;
            close.Backdrop = Theme.Back;
            close.Font = Theme.Font(9f, FontStyle.Regular);
            close.SetBounds(ClientSize.Width - Pad - 100, ClientSize.Height - Pad - 34, 100, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);
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
            l.SetBounds(Pad, _y, ClientSize.Width - Pad * 2, 16);
            Controls.Add(l);
            _y += 24;
        }

        delegate void Setter(bool on);

        void Row(string title, string detail, bool value, Setter apply)
        {
            ToggleChip t = new ToggleChip();
            t.Text = "";
            t.Font = Theme.Font(9f, FontStyle.Regular);
            t.BackColor = Theme.Back;
            t.Checked = value;
            t.SetBounds(ClientSize.Width - Pad - 44, _y + 2, 44, 24);
            t.CheckedChanged += delegate { apply(t.Checked); };
            Controls.Add(t);

            Label head = new Label();
            head.Text = title;
            head.Font = Theme.Font(9.75f, FontStyle.Regular);
            head.ForeColor = Theme.Text;
            head.BackColor = Theme.Back;
            head.AutoSize = false;
            head.SetBounds(Pad, _y, ClientSize.Width - Pad * 2 - 64, 20);
            Controls.Add(head);

            Label sub = new Label();
            sub.Text = detail;
            sub.Font = Theme.Font(8.25f, FontStyle.Regular);
            sub.ForeColor = Theme.Dimmer;
            sub.BackColor = Theme.Back;
            sub.AutoSize = false;
            sub.SetBounds(Pad, _y + 20, ClientSize.Width - Pad * 2 - 64, 32);
            Controls.Add(sub);

            _y += 62;
        }

        static string StartupLink()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Keycap.lnk");
        }

        static bool StartupEnabled() { return System.IO.File.Exists(StartupLink()); }

        static void SetStartup(bool on)
        {
            try
            {
                string link = StartupLink();
                if (!on)
                {
                    if (System.IO.File.Exists(link)) System.IO.File.Delete(link);
                    return;
                }
                string exe = Application.ExecutablePath;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { exe });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { System.IO.Path.GetDirectoryName(exe) });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty,
                    null, sc, new object[] { exe + ",0" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                    null, sc, new object[] { });
            }
            catch { }
        }
    }
}
