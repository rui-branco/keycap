using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// The remapper. A low-level keyboard hook does everything the AutoHotkey
    /// script used to do, in-process: modifier swaps, per-layout scancode
    /// fixes, the Mac shortcut set and the function row.
    ///
    /// Two rules keep it honest:
    ///   - anything we inject is tagged in dwExtraInfo so the hook ignores it
    ///     and cannot feed itself;
    ///   - when a shortcut fires we first lift whatever modifier we are
    ///     currently holding down on the OS's behalf, then restore it, so the
    ///     app underneath never sees a stray Ctrl or Win.
    /// </summary>
    public static class Hook
    {
        // ---- win32 ---------------------------------------------------------
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        const uint MAPVK_VSC_TO_VK_EX = 3;

        // Our own marker, so injected keys are never re-processed.
        static readonly UIntPtr Mark = (UIntPtr)0x4B435031;   // "KCP1"

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);

        // ---- virtual keys we care about -------------------------------------
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        const int VK_LCONTROL = 0xA2, VK_CONTROL = 0x11;
        const int VK_LSHIFT = 0xA0, VK_SHIFT = 0x10;
        const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        const int VK_HOME = 0x24, VK_END = 0x23;
        const int VK_BACK = 0x08, VK_DELETE = 0x2E, VK_TAB = 0x09, VK_SPACE = 0x20;
        const int VK_F3 = 0x72, VK_F4 = 0x73, VK_F5 = 0x74;
        const int VK_F7 = 0x76, VK_F12 = 0x7B;
        const int VK_Q = 0x51, VK_M = 0x4D, VK_S = 0x53;
        const int VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1;
        const int VK_MEDIA_PLAY = 0xB3;
        const int VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;

        // ---- state ----------------------------------------------------------
        static IntPtr _hook = IntPtr.Zero;
        static HookProc _proc;          // kept alive; a collected delegate crashes the hook
        static bool _cmdDown;           // physical Command  (reports as LWin)
        static bool _optDown;           // physical Option   (reports as LAlt)
        static bool _altTabbing;        // Option+Tab switcher is open
        static bool _winHeld;           // we are holding Win on Option's behalf
        static bool _optUsed;           // a key was pressed while Option was held

        public static bool Running { get { return _hook != IntPtr.Zero; } }
        public static event EventHandler Changed;

        static Timer _watchdog;

        public static void Start()
        {
            if (_hook != IntPtr.Zero) return;
            StartWorker();
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                                     GetModuleHandle(null), 0);
            _lastEvent = Environment.TickCount;
            _rearmed = Environment.TickCount;

            // A key-up can be missed entirely - a focus change during a press is
            // enough - and then nothing arrives to trigger Reconcile. This sweeps
            // for that case while the keyboard is idle, and checks the hook is
            // still installed at all.
            if (_watchdog == null)
            {
                _watchdog = new Timer();
                _watchdog.Interval = 700;
                _watchdog.Tick += delegate { Reconcile(); CheckAlive(); };
            }
            _watchdog.Start();

            Raise();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        static int _rearmed;

        /// <summary>
        /// Put the hook back when Windows has quietly taken it away.
        ///
        /// It does that to any callback that overruns the low-level hook
        /// timeout, and it tells nobody: the handle stays valid-looking, no
        /// error is raised, and the keyboard simply stops being remapped. The
        /// tell is the system seeing input that the hook did not.
        /// </summary>
        static void CheckAlive()
        {
            if (_hook == IntPtr.Zero) return;
            if (_cmdDown || _optDown || _winHeld || _altTabbing) return;  // mid-chord
            if (unchecked(Environment.TickCount - _rearmed) < 3000) return;

            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return;

            // Mouse movement also counts as input here, so this is evidence
            // rather than proof - but re-arming costs nothing either way.
            bool silent = unchecked((int)li.dwTime - _lastEvent) > 1500;
            bool stale = unchecked(Environment.TickCount - _rearmed) > 30000;
            if (!silent && !stale) return;

            UnhookWindowsHookEx(_hook);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _rearmed = Environment.TickCount;
            _lastEvent = Environment.TickCount;
        }

        /// <summary>Let go of everything and stop remapping.</summary>
        public static void Panic()
        {
            ReleaseAll();
            Stop();
            Osd.Show("Remapping off - both Shifts pressed", Osd.Sym.MicOff, Theme.Warn);
        }

        /// <summary>Drop every key we might be holding on the OS's behalf.</summary>
        public static void ReleaseAll()
        {
            if (_altTabbing) { Key(VK_LMENU, false); _altTabbing = false; }
            if (_winHeld) { Key(VK_LWIN, false); _winHeld = false; }
            if (_cmdDown) { Key(VK_LCONTROL, false); _cmdDown = false; }
            _optDown = false;
            _optUsed = false;
        }

        public static void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            // Never leave a modifier stuck down.
            if (_cmdDown) { Key(VK_LCONTROL, false); _cmdDown = false; }
            if (_altTabbing) { Key(VK_LMENU, false); _altTabbing = false; }
            if (_winHeld) { Key(VK_LWIN, false); _winHeld = false; }
            _optDown = false;
            Raise();
        }

        static void Raise()
        {
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        // ---- sending --------------------------------------------------------

        static void Send(INPUT[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Navigation keys live on the E0 page. Without KEYEVENTF_EXTENDEDKEY
        /// an injected VK_LEFT goes out as scancode 0x4B with no E0 prefix -
        /// which is numpad 4, not the arrow. Plain arrows still move, but
        /// Shift+arrow then behaves like the numpad and never selects.
        /// </summary>
        static bool IsExtended(int vk)
        {
            switch (vk)
            {
                case VK_LEFT: case VK_RIGHT: case VK_UP: case VK_DOWN:
                case VK_HOME: case VK_END: case VK_DELETE:
                case 0x21: case 0x22:          // PageUp / PageDown
                case 0x2D:                     // Insert
                case VK_LWIN: case VK_RWIN:
                case 0xA3:                     // RControl
                case VK_RMENU:
                    return true;
            }
            return false;
        }

        static INPUT Vk(int vk, bool down)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wVk = (ushort)vk;
            i.u.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
            i.u.ki.dwFlags = (down ? 0 : KEYEVENTF_KEYUP)
                           | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            i.u.ki.dwExtraInfo = Mark;
            return i;
        }

        static void Key(int vk, bool down) { Send(new INPUT[] { Vk(vk, down) }); }

        static void Tap(int vk, params int[] mods)
        {
            List<INPUT> seq = new List<INPUT>();
            foreach (int m in mods) if (m != 0) seq.Add(Vk(m, true));
            seq.Add(Vk(vk, true));
            seq.Add(Vk(vk, false));
            for (int i = mods.Length - 1; i >= 0; i--)
                if (mods[i] != 0) seq.Add(Vk(mods[i], false));
            Send(seq.ToArray());
        }

        /// <summary>Send one scancode, letting the layout engine decide the
        /// character - this is what keeps dead keys and AltGr working.</summary>
        static void Scan(int sc, bool down)
        {
            INPUT i = new INPUT();
            i.type = 1;
            bool ext = sc > 0xFF;
            i.u.ki.wScan = (ushort)(sc & 0xFF);
            i.u.ki.wVk = (ushort)MapVirtualKey((uint)(sc & 0xFF), MAPVK_VSC_TO_VK_EX);
            i.u.ki.dwFlags = KEYEVENTF_SCANCODE
                           | (down ? 0 : KEYEVENTF_KEYUP)
                           | (ext ? KEYEVENTF_EXTENDEDKEY : 0);
            i.u.ki.dwExtraInfo = Mark;
            Send(new INPUT[] { i });
        }

        static void Text(string s)
        {
            List<INPUT> seq = new List<INPUT>();
            foreach (char c in s)
            {
                INPUT d = new INPUT();
                d.type = 1;
                d.u.ki.wScan = c;
                d.u.ki.dwFlags = KEYEVENTF_UNICODE;
                d.u.ki.dwExtraInfo = Mark;
                seq.Add(d);

                INPUT u = d;
                u.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
                seq.Add(u);
            }
            Send(seq.ToArray());
        }

        /// <summary>
        /// Run an action with our injected modifiers lifted, then restore them.
        /// Without this, Command+Left would arrive as Ctrl+Home because Command
        /// is holding Ctrl down on the OS side.
        ///
        /// The work is handed to a worker thread rather than done here. A
        /// low-level hook callback has a few hundred milliseconds to return -
        /// Windows silently unhooks one that overruns, with no error and no
        /// notification, and the app then sits there doing nothing. Typing a
        /// phrase from inside the callback was more than enough to trip it.
        /// </summary>
        static void Bare(Action act)
        {
            bool cmd = _cmdDown, win = _winHeld;   // read on the hook thread
            Post(delegate
            {
                if (cmd) Key(VK_LCONTROL, false);
                if (win) Key(VK_LWIN, false);
                act();
                if (win) Key(VK_LWIN, true);
                if (cmd) Key(VK_LCONTROL, true);
            });
        }

        // ---- the injection thread -------------------------------------------
        // One thread, one queue, so injections still happen in the order the
        // keystrokes arrived.
        static readonly Queue<Action> _work = new Queue<Action>();
        static System.Threading.Thread _worker;

        static void Post(Action act)
        {
            lock (_work)
            {
                _work.Enqueue(act);
                System.Threading.Monitor.Pulse(_work);
            }
        }

        static void StartWorker()
        {
            if (_worker != null) return;
            _worker = new System.Threading.Thread(delegate ()
            {
                for (; ; )
                {
                    Action a;
                    lock (_work)
                    {
                        while (_work.Count == 0) System.Threading.Monitor.Wait(_work);
                        a = _work.Dequeue();
                    }
                    try { a(); }
                    catch { }        // one bad phrase must not take the thread down
                }
            });
            _worker.IsBackground = true;
            _worker.Name = "keycap-inject";
            _worker.Start();
        }

        static bool Shift
        {
            get { return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0; }
        }

        // ---- the hook -------------------------------------------------------

        static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);

            KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                lParam, typeof(KBDLLHOOKSTRUCT));

            if (k.dwExtraInfo == Mark)
                return CallNextHookEx(_hook, code, wParam, lParam);

            int msg = (int)wParam;
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool handled = false;

            try { handled = Handle(k, down); }
            catch { handled = false; }

            if (handled) return (IntPtr)1;
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        /// <summary>What a physical modifier is configured to act as.</summary>
        static string DstFor(string src, string fallback)
        {
            Mapping.Mod m = Mapping.FindMod(src);
            return m == null ? fallback : m.Dst;
        }

        static int VkOf(string name)
        {
            switch (name)
            {
                case "LCtrl": return VK_LCONTROL;
                case "RCtrl": return 0xA3;
                case "LWin": return VK_LWIN;
                case "RWin": return VK_RWIN;
                case "LAlt": return VK_LMENU;
                case "RAlt": return VK_RMENU;
            }
            return 0;
        }

        static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        /// <summary>The tail of what is being typed, for abbreviations.</summary>
        static readonly System.Text.StringBuilder _typed = new System.Text.StringBuilder();

        /// <summary>
        /// Follow the letters going past and report a trigger word the moment
        /// one is completed.
        ///
        /// Only letters and digits are followed. That is all a trigger needs,
        /// and it avoids asking Windows what character a key produces -
        /// ToUnicode would advance the layout's dead-key state, so asking
        /// would break typing an accent.
        /// </summary>
        static Mapping.Phrase Track(int vk)
        {
            if (vk == VK_BACK)
            {
                if (_typed.Length > 0) _typed.Length--;
                return null;
            }

            bool typed = (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39);
            if (!typed) { _typed.Length = 0; return null; }   // space, punctuation, arrows...

            _typed.Append(char.ToLowerInvariant((char)vk));
            if (_typed.Length > 40) _typed.Remove(0, _typed.Length - 40);

            Mapping.Phrase p = Mapping.FindWordAt(_typed.ToString());
            if (p != null) _typed.Length = 0;
            return p;
        }

        static int _lastEvent;          // tick of the last real key event

        /// <summary>
        /// Recover from a key-up we never saw - a focus change during a press is
        /// enough - which used to leave Ctrl or Win held forever and make the
        /// keyboard look dead.
        ///
        /// This cannot ask the hardware: we *swallow* Command and Option, so
        /// Windows never records them and GetAsyncKeyState reports them as up
        /// even while they are held. So it goes by idle time instead - if we are
        /// holding something and no key has arrived for a few seconds, the press
        /// is over and we let go.
        /// </summary>
        static void Reconcile()
        {
            if (!_cmdDown && !_optDown && !_winHeld && !_altTabbing) return;
            if (unchecked(Environment.TickCount - _lastEvent) < 4000) return;
            ReleaseAll();
        }

        static bool Handle(KBDLLHOOKSTRUCT k, bool down)
        {
            _lastEvent = Environment.TickCount;

            int vk = (int)k.vkCode;
            int sc = (int)k.scanCode;

            // ---- modifiers: Command -> Ctrl, Option -> Windows -------------
            if (vk == VK_LWIN)
            {
                string dst = DstFor("LWin", "LCtrl");
                if (dst == "LWin") return false;          // left as the Windows key
                _cmdDown = down && dst == "LCtrl";
                int target = VkOf(dst);
                if (target == 0) return false;
                Key(target, down);
                return true;
            }
            if (vk == VK_LMENU)
            {
                string optDst = DstFor("LAlt", "LWin");
                if (optDst == "LAlt") return false;       // left as Alt
                if (optDst != "LWin")
                {
                    int t = VkOf(optDst);
                    if (t == 0) return false;
                    _optDown = false;
                    Key(t, down);
                    return true;
                }
                _optDown = down;
                if (down)
                {
                    _optUsed = false;
                }
                else
                {
                    if (_altTabbing)
                    {
                        Key(VK_LMENU, false);   // releasing Option commits the switcher
                        _altTabbing = false;
                    }
                    else if (_winHeld)
                    {
                        Key(VK_LWIN, false);
                        _winHeld = false;
                    }
                    else if (!_optUsed)
                    {
                        // Option on its own is a Windows tap: opens Start.
                        Key(VK_LWIN, true);
                        Key(VK_LWIN, false);
                    }
                }
                // Win is held lazily, only once a key actually needs it - a
                // Win press released with nothing in between counts as a tap
                // and would open Start in the middle of Option+Tab.
                return true;
            }
            // right Command stays the Windows key; Control stays Control;
            // right Option stays AltGr - all pass through untouched.

            if (!down) return ScanRemap(k, false);

            // ---- panic release ------------------------------------------------
            // Holding both Shift keys drops everything we are holding and turns
            // remapping off, so a wedged keyboard is always recoverable from the
            // keyboard itself.
            if (down && (vk == VK_LSHIFT || vk == 0xA1) && Down(VK_LSHIFT) && Down(0xA1))
            {
                Panic();
                return false;
            }

            // ---- user phrases ------------------------------------------------
            // Checked before the built-in shortcuts so a phrase can claim a
            // combination the defaults would otherwise swallow.
            int pmods = (_cmdDown ? 1 : 0) | (_optDown ? 2 : 0) | (Shift ? 4 : 0);
            if (pmods != 0)
            {
                Mapping.Phrase ph = Mapping.FindPhrase(pmods, vk);
                if (ph != null)
                {
                    string body = ph.Text;
                    Bare(delegate { Text(body); });
                    _optUsed = true;
                    return true;
                }
            }

            // ---- typed abbreviations ------------------------------------------
            // A phrase can also be triggered by typing a word. Command and
            // Option mean the keystroke is a shortcut rather than typing, so
            // they break the word instead of extending it.
            if ((pmods & 3) != 0) _typed.Length = 0;
            else if (Mapping.AnyWords)
            {
                Mapping.Phrase word = Track(vk);
                if (word != null)
                {
                    // The trigger's last letter is swallowed, so only the
                    // letters already on screen have to be rubbed out.
                    int back = word.Word.Length - 1;
                    string body = word.Text;
                    Bare(delegate
                    {
                        for (int i = 0; i < back; i++) Tap(VK_BACK);
                        Text(body);
                    });
                    return true;
                }
            }

            // ---- Command shortcuts ------------------------------------------
            if (_cmdDown)
            {
                switch (vk)
                {
                    case VK_LEFT: Bare(delegate { Tap(VK_HOME); }); return true;
                    case VK_RIGHT: Bare(delegate { Tap(VK_END); }); return true;
                    case VK_UP: Bare(delegate { TapCtrl(VK_HOME); }); return true;
                    case VK_DOWN: Bare(delegate { TapCtrl(VK_END); }); return true;
                    case VK_BACK: Bare(delegate { Tap(VK_HOME, VK_LSHIFT); Tap(VK_BACK); }); return true;
                    case VK_Q: Bare(delegate { Tap(0x73, 0xA4); }); return true;      // Alt+F4
                    case VK_M: Bare(delegate { Tap(VK_DOWN, VK_LWIN); }); return true;
                    case VK_SPACE: Bare(delegate { Tap(VK_S, VK_LWIN); }); return true;
                }
                return false;   // everything else is a plain Ctrl shortcut
            }

            // ---- Option combos ----------------------------------------------
            if (_optDown)
            {
                _optUsed = true;
                // Option+Tab is the app switcher. Alt has to stay held while
                // Tab is tapped, or the list closes after one step - so swap
                // the Windows key we are holding for Alt and keep it down
                // until Option is released.
                if (vk == VK_TAB)
                {
                    if (!_altTabbing)
                    {
                        if (_winHeld) { Key(VK_LWIN, false); _winHeld = false; }
                        Key(VK_LMENU, true);
                        _altTabbing = true;
                    }
                    Tap(VK_TAB);
                    return true;
                }

                switch (vk)
                {
                    case VK_LEFT: Bare(delegate { TapCtrl(VK_LEFT); }); return true;
                    case VK_RIGHT: Bare(delegate { TapCtrl(VK_RIGHT); }); return true;
                    case VK_BACK: Bare(delegate { TapCtrl(VK_BACK); }); return true;
                }
                if (vk >= VK_F3 && vk <= VK_F12)
                {
                    int f = vk;
                    Bare(delegate { Tap(f); });      // the real function key
                    return true;
                }

                // Everything else is a Windows shortcut: Option+E, Option+L...
                if (!_winHeld) { Key(VK_LWIN, true); _winHeld = true; }
                return false;
            }

            // ---- function row ------------------------------------------------
            switch (vk)
            {
                case VK_F3: Bare(delegate { Tap(VK_TAB, VK_LWIN); }); return true;
                case VK_F4: Bare(delegate { Tap(VK_S, VK_LWIN); }); return true;   // the magnifier
                case VK_F5: Audio.ToggleMic(); return true;
                case VK_F7: Tap(VK_MEDIA_PREV); return true;
                case 0x77: Tap(VK_MEDIA_PLAY); return true;     // F8
                case 0x78: Tap(VK_MEDIA_NEXT); return true;     // F9
                case 0x79: Tap(VK_VOLUME_MUTE); return true;    // F10
                case 0x7A: Tap(VK_VOLUME_DOWN); return true;    // F11
                case 0x7B: Tap(VK_VOLUME_UP); return true;      // F12
            }

            return ScanRemap(k, true);
        }

        /// <summary>Ctrl+key. Shift is left alone: if the user is holding it,
        /// the injected key already inherits it.</summary>
        static void TapCtrl(int vk) { Tap(vk, VK_CONTROL); }

        // ---- per-layout scancode fixes --------------------------------------

        static bool ScanRemap(KBDLLHOOKSTRUCT k, bool down)
        {
            string sc = ((int)k.scanCode).ToString("X3");
            Mapping.Rule r = Mapping.FindScan(sc, InputLang.CurrentLangId());
            if (r == null) return false;

            if (r.Text != null)
            {
                if (down) Text(Shift ? r.ShiftText : r.Text);
                return true;
            }

            Scan(Convert.ToInt32(r.To, 16), down);
            return true;
        }
    }
}
