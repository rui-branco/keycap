using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Keycap
{
    /// <summary>One keyboard layout Windows currently has installed.</summary>
    public class InputLayout
    {
        public IntPtr Hkl;
        public int LangId;          // low word of the HKL
        public string Klid;         // eight hex digits, e.g. "00000816"
        public string Name;         // "Portuguese (Portugal)"
        public Layout Board;        // the legends Keycap draws for it

        public override string ToString()
        {
            return Name + "  (" + (Board == null ? "?" : Board.Form.ToString()) + ")";
        }
    }

    /// <summary>
    /// Reads the keyboard layouts installed in Windows and switches between
    /// them, so Keycap follows the system rather than keeping its own idea of
    /// which language the board is.
    /// </summary>
    public static class InputLang
    {
        [DllImport("user32.dll")]
        static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] list);
        [DllImport("user32.dll")]
        static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

        const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        const uint KLF_ACTIVATE = 0x00000001;
        const uint KLF_SETFORPROCESS = 0x00000100;
        const uint INPUTLANGCHANGE_SYSCHARSET = 0x0001;

        static string FriendlyName(int langId)
        {
            switch (langId)
            {
                case 0x0816: return "Portuguese (Portugal)";
                case 0x0416: return "Portuguese (Brazil)";
                case 0x0C0A: return "Spanish (Spain)";
                case 0x040A: return "Spanish";
                case 0x040C: return "French (France)";
                case 0x080C: return "French (Belgium)";
                case 0x0407: return "German (Germany)";
                case 0x0807: return "German (Switzerland)";
                case 0x0809: return "English (UK)";
                case 0x0409: return "English (US)";
                case 0x0410: return "Italian";
                case 0x0413: return "Dutch";
                case 0x041D: return "Swedish";
                case 0x0406: return "Danish";
                case 0x0414: return "Norwegian";
                case 0x040B: return "Finnish";
            }
            try
            {
                return new System.Globalization.CultureInfo(langId).DisplayName;
            }
            catch { return "Layout " + langId.ToString("X4"); }
        }

        /// <summary>Which of Keycap's legend sets fits this Windows layout.</summary>
        static Layout BoardFor(int langId, List<Layout> boards)
        {
            string want;
            switch (langId)
            {
                case 0x0816: case 0x0416: want = "Portuguese"; break;
                case 0x0C0A: case 0x040A: want = "Spanish"; break;
                case 0x040C: case 0x080C: want = "French"; break;
                case 0x0407: case 0x0807: want = "German"; break;
                case 0x0809: want = "British"; break;
                default: want = "US"; break;
            }
            foreach (Layout l in boards) if (l.Name == want) return l;
            return boards[boards.Count - 1];
        }

        public static List<InputLayout> Installed()
        {
            List<Layout> boards = Layouts.All();
            List<InputLayout> result = new List<InputLayout>();

            int n = GetKeyboardLayoutList(0, null);
            if (n <= 0) return result;
            IntPtr[] list = new IntPtr[n];
            GetKeyboardLayoutList(n, list);

            List<int> seen = new List<int>();
            foreach (IntPtr hkl in list)
            {
                int lang = (int)((long)hkl & 0xFFFF);
                if (seen.Contains(lang)) continue;
                seen.Add(lang);

                InputLayout il = new InputLayout();
                il.Hkl = hkl;
                il.LangId = lang;
                il.Klid = (((long)hkl >> 16) & 0xFFFF) == 0
                    ? lang.ToString("X8")
                    : (((long)hkl >> 16) & 0xFFFF).ToString("X4") + lang.ToString("X4");
                il.Name = FriendlyName(lang);
                il.Board = BoardFor(lang, boards);
                result.Add(il);
            }
            return result;
        }

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        static int _lastSeen;

        /// <summary>
        /// The layout in use right now. GetKeyboardLayout(0) would report our
        /// own thread's layout, which for a freshly started process is the
        /// system default rather than whatever the user is actually typing in -
        /// so ask the foreground window's thread instead.
        /// </summary>
        public static int CurrentLangId()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint pid;
                uint tid = GetWindowThreadProcessId(fg, out pid);
                // Our own window reports the process default, not what the user
                // is typing in, so remember the last value from another app.
                if (pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                {
                    IntPtr hkl = GetKeyboardLayout(tid);
                    if (hkl != IntPtr.Zero)
                    {
                        _lastSeen = (int)((long)hkl & 0xFFFF);
                        return _lastSeen;
                    }
                }
            }
            if (_lastSeen != 0) return _lastSeen;
            return (int)((long)GetKeyboardLayout(0) & 0xFFFF);
        }

        /// <summary>
        /// Switch Windows to this layout. The request goes to the window that
        /// had focus, which is what the taskbar indicator follows; the process
        /// copy is set too so Keycap's own fields agree.
        /// </summary>
        public static bool Activate(InputLayout il)
        {
            if (il == null) return false;
            try
            {
                IntPtr hkl = LoadKeyboardLayout(il.Klid, KLF_ACTIVATE);
                if (hkl == IntPtr.Zero) hkl = il.Hkl;

                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                    PostMessage(fg, WM_INPUTLANGCHANGEREQUEST,
                                new IntPtr(INPUTLANGCHANGE_SYSCHARSET), hkl);

                ActivateKeyboardLayout(hkl, KLF_SETFORPROCESS);
                return true;
            }
            catch { return false; }
        }
    }
}
