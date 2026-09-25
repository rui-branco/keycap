using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// Settings. Each row says what it does in a sentence, rather than relying
    /// on a one-word label to carry the meaning, and every control writes as it
    /// is changed - there is no Save button to forget.
    /// </summary>
    public class SettingsPage : Page
    {
        ScrollPage _scroll;
        TextLine _hKeyboard, _hSystem, _hFile;
        RowCard _keyboard, _system, _file;
        ToggleChip _indicator, _startup;

        /// <summary>Set while a switch is being put back, so the correction
        /// does not read as another click and run the setter again.</summary>
        bool _syncing;

        public SettingsPage()
        {
            Title = "Settings";
            Subtitle = "Everything here saves the moment you change it.";

            _scroll = new ScrollPage();
            Controls.Add(_scroll);
            Panel c = _scroll.Content;

            _hKeyboard = Section("Keyboard", Theme.Back);
            c.Controls.Add(_hKeyboard);
            _keyboard = NewCard(c);
            _indicator = Switch(Mapping.ShowIndicator, delegate(bool on)
            {
                Mapping.ShowIndicator = on;
                Mapping.Save();
                if (!on) Osd.Hide();
            });
            _keyboard.Add("Show the microphone indicator",
                "A small panel above the taskbar when F5 mutes or unmutes the microphone. "
                + "It never takes focus, and clicks pass straight through it.", _indicator);

            _hSystem = Section("System", Theme.Back);
            c.Controls.Add(_hSystem);
            _system = NewCard(c);
            _startup = Switch(Startup.Enabled(), delegate(bool on)
            {
                string err = Startup.Set(on);
                if (err == null) return;
                // Put the switch back where the Startup folder says it is, so it
                // cannot sit there claiming something that did not happen.
                Quiet(_startup, Startup.Enabled());
                Say((on ? "Could not add Keycap to Startup: " : "Could not remove Keycap from Startup: ") + err,
                    Theme.Bad);
            });
            _system.Add("Start with Windows",
                "Keycap runs from your Startup folder, so the remaps are live from login. "
                + "It starts in the tray, without opening this window.", _startup);

            _hFile = Section("Mapping file", Theme.Back);
            c.Controls.Add(_hFile);
            _file = NewCard(c);

            FlatButton open = Button("Open");
            open.Click += delegate
            {
                try { Process.Start("notepad.exe", "\"" + Mapping.File_ + "\""); }
                catch (Exception ex) { Say(ex.Message, Theme.Bad); }
            };
            FlatButton reload = Button("Reload");
            reload.Click += delegate
            {
                Changed();          // re-reads the file and redraws every page
                Say("Mapping reloaded from disk", Theme.Good);
            };
            Panel pair = new Panel();
            pair.Size = new Size(open.Width + 8 + reload.Width, open.Height);
            reload.Location = new Point(0, 0);
            open.Location = new Point(reload.Width + 8, 0);
            pair.Controls.Add(reload);
            pair.Controls.Add(open);
            pair.BackColorChanged += delegate
            {
                open.Backdrop = pair.BackColor;
                reload.Backdrop = pair.BackColor;
            };
            _file.Add(System.IO.Path.GetFileName(Mapping.File_),
                "In " + Mapping.Dir + ". Plain text, one rule per line, so it can be read "
                + "and edited without Keycap. "
                + "Changes made by hand are picked up within 20 seconds, or at once with Reload.", pair);
        }

        RowCard NewCard(Panel parent)
        {
            RowCard r = new RowCard();
            r.Fill = Theme.Card;
            r.Page = Theme.Back;
            parent.Controls.Add(r);
            return r;
        }

        FlatButton Button(string text)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Font = Theme.Font(9f, FontStyle.Regular);
            b.Height = 32;
            b.Width = b.PreferredWidth();
            return b;
        }

        delegate void Setter(bool on);

        ToggleChip Switch(bool value, Setter apply)
        {
            ToggleChip t = new ToggleChip();
            t.Size = new Size(40, 24);
            t.SetQuiet(value);
            t.CheckedChanged += delegate { if (!_syncing) apply(t.Checked); };
            return t;
        }

        void Quiet(ToggleChip t, bool value)
        {
            _syncing = true;
            try { t.Checked = value; }
            finally { _syncing = false; }
        }

        public override void Reload()
        {
            _indicator.SetQuiet(Mapping.ShowIndicator);
            _startup.SetQuiet(Startup.Enabled());
        }

        public override void Arrange()
        {
            if (_scroll == null) return;
            _scroll.SetBounds(Inset, ContentTop, Width - Inset, Height - ContentTop);
            int w = Math.Min(_scroll.Width - Inset, 860);

            int y = 4;
            foreach (object[] pair in new object[][] {
                         new object[] { _hKeyboard, _keyboard },
                         new object[] { _hSystem, _system },
                         new object[] { _hFile, _file } })
            {
                TextLine head = (TextLine)pair[0];
                RowCard card = (RowCard)pair[1];
                head.SetBounds(2, y, w, 18);
                y += 26;
                card.Location = new Point(0, y);
                y += card.Arrange(w) + 22;
            }
            _scroll.Content.Height = y + 2;
            _scroll.Sync();
        }
    }

    /// <summary>The Startup-folder shortcut that launches Keycap at login.</summary>
    public static class Startup
    {
        static string Link()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Keycap.lnk");
        }

        public static bool Enabled() { return System.IO.File.Exists(Link()); }

        /// <summary>
        /// Writes or removes the Startup shortcut, and says what went wrong rather than
        /// nothing at all. The switch paints itself the moment it is clicked, so a failure
        /// swallowed here left it showing ON while Keycap would not in fact start with
        /// Windows - a lie that only unwound the next time Settings was opened.
        /// </summary>
        public static string Set(bool on)
        {
            try
            {
                string link = Link();
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
        public static void EnsureArgs()
        {
            try
            {
                string link = Link();
                if (!System.IO.File.Exists(link)) return;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                string args = (string)sc.GetType().InvokeMember("Arguments",
                    System.Reflection.BindingFlags.GetProperty, null, sc, new object[] { });
                if (args == null ||
                    args.IndexOf(Program.BackgroundArg, StringComparison.OrdinalIgnoreCase) < 0)
                    Set(true);
            }
            catch { }
        }
    }
}
