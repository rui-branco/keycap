using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
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
        TextLine _hKeyboard, _hBright, _hDisplays, _hSystem, _hFile;
        RowCard _keyboard, _bright, _displays, _system, _file;
        ToggleChip _indicator, _brightKeys, _softDim, _startup;
        DropChip _step, _which;

        /// <summary>What "Each press" offers, in the order it lists them.</summary>
        static readonly int[] Steps = new int[] { 5, 10, 20 };

        /// <summary>Each display's slider, by device.</summary>
        readonly Dictionary<string, LevelSlider> _sliders = new Dictionary<string, LevelSlider>();

        /// <summary>The displays the rows on the Displays card were built for.</summary>
        string _shown;

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
            _keyboard.Add("Show the on-screen indicator",
                "A small panel above the taskbar when F5 mutes or unmutes the microphone, "
                + "or F1 and F2 change the brightness. "
                + "It never takes focus, and clicks pass straight through it.", _indicator);

            _hBright = Section("Brightness", Theme.Back);
            c.Controls.Add(_hBright);
            _bright = NewCard(c);
            _brightKeys = Switch(Mapping.BrightnessKeys, delegate(bool on)
            {
                Mapping.BrightnessKeys = on;
                Mapping.Save();
                Changed();          // the board and the shortcut list redraw F1 and F2
            });
            _bright.Add("F1 and F2 set the display brightness",
                "Keycap asks the monitor itself over DDC/CI, the way its own buttons do - "
                + "Windows has no brightness key for an external screen. "
                + "Hold Option for the real F1 and F2. Off, they are plain F1 and F2.", _brightKeys);

            _softDim = Switch(Mapping.SoftDim, delegate(bool on)
            {
                Mapping.SoftDim = on;
                Mapping.Save();
                if (!on) Dimmer.Clear();
                Brightness.Scan();      // every display is sorted again, veils and all
            });
            _bright.Add("Dim displays that cannot be controlled",
                "A monitor that does not answer DDC/CI gets a dark veil over its picture instead, "
                + "so F1 and F2 still work on it. It can only go down from the monitor's own "
                + "brightness, a full-screen game may draw over it, and screenshots show the real "
                + "picture.", _softDim);

            _step = Chip("5%", "10%", "20%");
            _which = Chip("Under the mouse", "All together");
            // One width for both, so the two chips line up down the card.
            int chipW = Math.Max(_step.PreferredWidth(), _which.PreferredWidth());
            _step.Width = chipW;
            _which.Width = chipW;
            _step.SetIndexQuiet(NearestStep(Mapping.BrightnessStep));
            _which.SetIndexQuiet(Mapping.BrightnessAll ? 1 : 0);
            _step.SelectionChanged += delegate
            {
                Mapping.BrightnessStep = Steps[_step.SelectedIndex];
                Mapping.Save();
            };
            _which.SelectionChanged += delegate
            {
                Mapping.BrightnessAll = _which.SelectedIndex == 1;
                Mapping.Save();
            };
            _bright.Add("Each press",
                "How far one press of F1 or F2 moves the brightness. Holding the key keeps going.", _step);
            _bright.Add("Which display",
                "Under the mouse is the display the pointer is on - or, if that one cannot be "
                + "controlled at all, the ones that can. All together sets every display to the "
                + "same level.", _which);

            _hDisplays = Section("Displays", Theme.Back);
            c.Controls.Add(_hDisplays);
            _displays = NewCard(c);

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

            Brightness.Changed += delegate { SyncDisplays(); };
            SyncDisplays();
        }

        /// <summary>
        /// The Displays card, from what Brightness last read. The rows are only
        /// rebuilt when the displays themselves change; otherwise each slider is
        /// moved to its new level in place, which is how F1 and F2 move them
        /// live. A slider being dragged is left alone - the echo of its own
        /// writes coming back would otherwise make it jitter under the pointer.
        /// </summary>
        void SyncDisplays()
        {
            bool scanned = Brightness.Scanned;
            List<Brightness.Display> all = Brightness.Displays();

            StringBuilder sig = new StringBuilder(scanned ? "1" : "0");
            foreach (Brightness.Display d in all)
                sig.Append('|').Append(d.Device).Append(d.Supported ? '+' : '-')
                   .Append(d.Soft ? '~' : '.').Append(d.Name);
            string signature = sig.ToString();

            if (signature == _shown)
            {
                foreach (Brightness.Display d in all)
                {
                    LevelSlider s;
                    if (_sliders.TryGetValue(d.Device, out s) && !s.Dragging) s.SetQuiet(d.Percent);
                }
                return;
            }
            _shown = signature;
            _displays.Clear();
            _sliders.Clear();

            if (!scanned)
                _displays.Add("Looking for displays...",
                    "Each one is asked for its brightness over DDC/CI.", null);
            else
            {
                FlatButton again = Button("Check again");
                again.Backdrop = Theme.Card;
                again.Click += delegate { Brightness.Scan(); };
                _displays.Add("Connected displays", "Read from each monitor when this page opens.", again);

                foreach (Brightness.Display d in all)
                {
                    LevelSlider s = null;
                    if (d.Supported || d.Soft)
                    {
                        s = new LevelSlider();
                        s.SetQuiet(d.Percent);
                        string device = d.Device;
                        s.ValueChanged += delegate { Brightness.Set(device, s.Value); };
                        _sliders[device] = s;
                    }
                    _displays.Add(d.Name + (d.Primary ? "  \u00B7  main" : ""),
                        d.Size.Width + " \u00D7 " + d.Size.Height + (d.Supported
                            ? ", over DDC/CI"
                            : d.Soft
                            ? ", dimmed by Keycap - it does not answer DDC/CI, so 100% is the "
                              + "monitor's own brightness"
                            : ". It does not answer brightness control - if its menu has a DDC/CI "
                              + "setting, turn that on and check again."),
                        s);
                }
            }
            Arrange();
        }

        /// <summary>The step on offer nearest to this one - a hand-edited file can hold any.</summary>
        static int NearestStep(int value)
        {
            int best = 0;
            for (int i = 1; i < Steps.Length; i++)
                if (Math.Abs(Steps[i] - value) < Math.Abs(Steps[best] - value)) best = i;
            return best;
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

        DropChip Chip(params string[] items)
        {
            DropChip d = new DropChip();
            d.Font = Theme.Font(9f, FontStyle.Regular);
            d.BackColor = Theme.Card;
            d.Height = 32;
            d.Items.AddRange(items);
            return d;
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
            _brightKeys.SetQuiet(Mapping.BrightnessKeys);
            _softDim.SetQuiet(Mapping.SoftDim);
            _step.SetIndexQuiet(NearestStep(Mapping.BrightnessStep));
            _which.SetIndexQuiet(Mapping.BrightnessAll ? 1 : 0);
            _startup.SetQuiet(Startup.Enabled());
        }

        /// <summary>Read every display again, so the list and the levels are current.</summary>
        public override void Entered()
        {
            Brightness.Scan();
        }

        public override void Arrange()
        {
            if (_scroll == null) return;
            _scroll.SetBounds(Inset, ContentTop, Width - Inset, Height - ContentTop);
            int w = Math.Min(_scroll.Width - Inset, 860);

            int y = 4;
            foreach (object[] pair in new object[][] {
                         new object[] { _hKeyboard, _keyboard },
                         new object[] { _hBright, _bright },
                         new object[] { _hDisplays, _displays },
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
