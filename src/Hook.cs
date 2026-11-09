using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

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
    ///
    /// The hook has a thread to itself. Windows calls a low-level hook on the
    /// thread that installed it, and holds every keystroke - in every app -
    /// until that call comes back. Installed from the window's thread, each
    /// key waited for whatever the window was busy with: a repaint, the
    /// indicator animating, the keyboard's battery being read over Bluetooth.
    /// That last one froze typing for a few hundred milliseconds every twenty
    /// seconds, and past 300 ms Windows gives up on the hook and lets the key
    /// through unremapped. On this thread nothing else ever runs, and it is
    /// time-critical, so a key waits neither for our own work nor for a busy
    /// processor.
    /// </summary>
    public static class Hook
    {
        // ---- win32 ---------------------------------------------------------
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const uint WM_TIMER = 0x0113;
        const uint WM_CALL = 0x8001;          // WM_APP + 1: work is waiting for the hook thread

        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        const uint MAPVK_VSC_TO_VK_EX = 3;
        const int THREAD_PRIORITY_TIME_CRITICAL = 15;

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

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
        }

        // The key arrives by reference: Windows' own copy is read where it lies,
        // rather than marshalled into a new object on every keystroke.
        delegate IntPtr HookProc(int nCode, IntPtr wParam, ref KBDLLHOOKSTRUCT key);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, ref KBDLLHOOKSTRUCT key);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        static extern uint SendInput(uint n, ref INPUT input, int size);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr process, int flags,
                                                     System.Text.StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")]
        static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")]
        static extern bool SetThreadPriority(IntPtr thread, int priority);

        // ---- virtual keys we care about -------------------------------------
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        const int VK_LCONTROL = 0xA2, VK_CONTROL = 0x11;
        const int VK_MENU = 0x12;
        const int VK_LSHIFT = 0xA0, VK_SHIFT = 0x10;
        const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        const int VK_HOME = 0x24, VK_END = 0x23;
        const int VK_BACK = 0x08, VK_DELETE = 0x2E, VK_TAB = 0x09, VK_SPACE = 0x20;
        const int VK_F1 = 0x70, VK_F2 = 0x71, VK_F3 = 0x72, VK_F4 = 0x73, VK_F5 = 0x74;
        const int VK_F7 = 0x76, VK_F12 = 0x7B;
        const int VK_Q = 0x51, VK_M = 0x4D, VK_S = 0x53;
        const int VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1;
        const int VK_MEDIA_PLAY = 0xB3;
        const int VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF;
        const int VK_MASK = 0xE8;             // unassigned - see Bare

        // ---- state ----------------------------------------------------------
        // Everything below belongs to the hook thread unless it says otherwise.
        static IntPtr _hook = IntPtr.Zero;
        static HookProc _proc;          // kept alive; a collected delegate crashes the hook
        static volatile bool _running;  // the hook is installed - read from any thread
        static volatile bool _on;       // remapping is meant to be on, installed right now or not
        // _cmdDown and _winHeld are also read by the injection thread (see Bare),
        // so they are volatile and always set before the key they stand for is sent.
        static volatile bool _cmdDown;  // physical Command  (reports as LWin)
        static bool _optDown;           // physical Option   (reports as LAlt)
        static bool _altTabbing;        // Option+Tab switcher is open
        static volatile bool _winHeld;  // we are holding Win on Option's behalf
        static bool _altHeld;           // we are holding Alt on Option's behalf
        static bool _optUsed;           // a key was pressed while Option was held

        public static bool Running { get { return _running; } }
        public static event EventHandler Changed;

        /// <summary>
        /// Turn remapping on. From any thread; it returns once the hook is in,
        /// so Running can be read straight after.
        /// </summary>
        public static void Start()
        {
            if (_running) return;
            // The window's thread is the one that starts us, and the one the
            // indicator and the microphone are handed back to (see OnUi).
            SynchronizationContext here = SynchronizationContext.Current;
            if (here != null) _ui = here;

            EnsureThread();
            OnHookThread(Install);
            Wire();
            Raise();
        }

        static void Install()
        {
            if (_hook != IntPtr.Zero) return;
            // Starting is the moment to clear anything left down - by a previous
            // Keycap killed mid-press, say, or a key held while Keycap was off
            // whose up the hook would now handle differently.
            ReleaseHeld();
            StartWorker();
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                                     GetModuleHandle(null), 0);
            _lastEvent = Environment.TickCount;
            _rearmed = Environment.TickCount;
            if (_hook == IntPtr.Zero) return;

            _rearmWanted = false;
            _on = true;
            _running = true;

            // A key-up can be missed entirely - a focus change during a press is
            // enough - and then nothing arrives to trigger Reconcile. This sweeps
            // for that case while the keyboard is idle, and checks the hook is
            // still installed at all. A timer with no window ticks in this
            // thread's own message loop.
            if (_watchdog == UIntPtr.Zero)
                _watchdog = SetTimer(IntPtr.Zero, UIntPtr.Zero, 700, IntPtr.Zero);
        }

        static UIntPtr _watchdog;

        static void Tick()
        {
            if (_rearmWanted) { _rearmWanted = false; Rearm(); }
            Reconcile();
            CheckAlive();
        }

        // ---- the hook thread ------------------------------------------------

        static Thread _thread;
        static uint _threadId;
        static readonly object _threadGate = new object();
        static volatile SynchronizationContext _ui;

        sealed class Call
        {
            public Action Act;
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
        }

        static readonly Queue<Call> _calls = new Queue<Call>();

        /// <summary>
        /// The thread is made once and kept for the life of the process:
        /// pausing takes the hook off it, resuming puts it back on.
        /// </summary>
        static void EnsureThread()
        {
            lock (_threadGate)
            {
                if (_thread != null) return;
                ManualResetEvent ready = new ManualResetEvent(false);
                Thread t = new Thread(delegate () { Pump(ready); });
                t.IsBackground = true;
                t.Name = "keycap-hook";
                t.Start();
                ready.WaitOne();
                _thread = t;
            }
        }

        static void Pump(ManualResetEvent ready)
        {
            // Above everything that is not real-time, as AutoHotkey runs its
            // hook: what happens here is a handful of comparisons per key, and
            // it must not queue behind a compiler using every core.
            try { SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL); }
            catch { }

            // Asking for a message is what gives a thread its queue; without
            // one, the first thing posted to it would be lost.
            MSG m;
            PeekMessage(out m, IntPtr.Zero, 0, 0, 0);
            _threadId = GetCurrentThreadId();
            ready.Set();

            int logged = 0;
            for (; ; )
            {
                // The hook itself is called from inside GetMessage; what comes
                // back out of it is only the watchdog and the calls below.
                int got = GetMessage(out m, IntPtr.Zero, 0, 0);
                if (got == 0) break;
                if (got < 0) { Thread.Sleep(50); continue; }
                try
                {
                    if (m.hwnd == IntPtr.Zero && m.message == WM_TIMER) Tick();
                    else if (m.hwnd == IntPtr.Zero && m.message == WM_CALL) Drain();
                    else { TranslateMessage(ref m); DispatchMessage(ref m); }
                }
                catch (Exception ex)
                {
                    // The thread must outlive anything that goes wrong on it -
                    // without it there is no keyboard hook at all.
                    if (logged++ < 5) Program.LogError(ex);
                }
            }
        }

        /// <summary>
        /// Run something on the hook thread and wait for it. What the hook holds
        /// - which keys are down on whose behalf - is only ever touched there,
        /// so starting, stopping and re-arming all go through here.
        /// </summary>
        static void OnHookThread(Action act)
        {
            if (Thread.CurrentThread == _thread) { act(); return; }

            Call c = new Call();
            c.Act = act;
            lock (_calls) _calls.Enqueue(c);
            if (!PostThreadMessage(_threadId, WM_CALL, IntPtr.Zero, IntPtr.Zero))
            {
                c.Act = null;       // never delivered: it must not run later, out of turn
                return;
            }
            // Bounded, so a wedged hook thread cannot take the window with it.
            c.Done.WaitOne(5000);
        }

        static void Drain()
        {
            for (; ; )
            {
                Call c;
                lock (_calls)
                {
                    if (_calls.Count == 0) return;
                    c = _calls.Dequeue();
                }
                try
                {
                    Action act = c.Act;
                    if (act != null) act();
                }
                catch (Exception ex) { Program.LogError(ex); }
                finally { c.Done.Set(); }
            }
        }

        /// <summary>
        /// Hand something to the window's thread and carry on. The indicator is
        /// a window and belongs there, and so does anything slow: nothing on
        /// the hook thread may wait.
        /// </summary>
        static void OnUi(Action act)
        {
            SynchronizationContext ui = _ui;
            if (ui == null) { act(); return; }
            try
            {
                ui.Post(delegate
                {
                    try { act(); }
                    catch (Exception ex) { Program.LogError(ex); }
                }, null);
            }
            catch { }       // the window is shutting down, and took its queue with it
        }

        // ---- locking and waking ---------------------------------------------

        static bool _wired;
        static volatile bool _rearmWanted;   // set off-thread, acted on by the watchdog

        /// <summary>
        /// Lock the screen and the hook goes quiet. The lock screen is its own
        /// desktop and our hook is not on it, so nothing arrives - including
        /// the key-ups for whatever was held when the screen went away. Coming
        /// back, Windows does not reliably start feeding the hook again.
        ///
        /// CheckAlive does eventually catch that, but only once it has seen
        /// input the hook missed - which means typing a few unremapped keys
        /// first. A session or power transition is the announcement, so re-arm
        /// on those instead and the keyboard is mapped before the desktop is
        /// back.
        ///
        /// These fire on the SystemEvents thread, so they only raise a flag:
        /// the hook has to be installed from the thread that owns it.
        /// </summary>
        static void Wire()
        {
            if (_wired) return;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSession;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
            _wired = true;
        }

        static void Unwire()
        {
            if (!_wired) return;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSession;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
            _wired = false;
        }

        static void OnSession(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case Microsoft.Win32.SessionSwitchReason.SessionLock:
                case Microsoft.Win32.SessionSwitchReason.SessionUnlock:
                case Microsoft.Win32.SessionSwitchReason.SessionLogon:
                case Microsoft.Win32.SessionSwitchReason.ConsoleConnect:
                case Microsoft.Win32.SessionSwitchReason.RemoteConnect:
                    _rearmWanted = true;
                    break;
            }
        }

        static void OnPower(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) _rearmWanted = true;
        }

        /// <summary>
        /// Take the hook down and put it straight back, on the thread that owns
        /// it. Whatever we were holding goes first: after a lock the key-ups
        /// are lost, and a Ctrl left down is worse than no remapping at all.
        /// </summary>
        static void Rearm()
        {
            if (!_on) return;
            ReleaseAll();
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _rearmed = Environment.TickCount;
            _lastEvent = Environment.TickCount;
            _typedLen = 0;              // half a word from before the lock is not a trigger

            // Installing can fail while the session is still switching. Say so,
            // and let the next tick try again - and say so again once it is back.
            bool ok = _hook != IntPtr.Zero;
            if (!ok) _rearmWanted = true;
            if (ok != _running) { _running = ok; Raise(); }
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
            if (_cmdDown || _optDown || _winHeld || _altHeld || _altTabbing || _pressed.Count > 0) return;  // mid-chord
            if (unchecked(Environment.TickCount - _rearmed) < 3000) return;

            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return;

            // Mouse movement also counts as input here, so this is evidence
            // rather than proof - but re-arming costs nothing either way.
            bool silent = unchecked((int)li.dwTime - _lastEvent) > 1500;
            bool stale = unchecked(Environment.TickCount - _rearmed) > 30000;
            if (!silent && !stale) return;

            Rearm();
        }

        /// <summary>Let go of every held key - ours or anyone's - and stop
        /// remapping.</summary>
        public static void Panic()
        {
            Stop();
            ReleaseHeld();
            OnUi(delegate { Osd.Show("Remapping off - both Shifts pressed", Osd.Sym.MicOff, Theme.Warn); });
        }

        /// <summary>Drop every key we might be holding on the OS's behalf.</summary>
        static void ReleaseAll()
        {
            if (_altTabbing) { Key(VK_LMENU, false); _altTabbing = false; }
            if (_winHeld) { _winHeld = false; Key(VK_LWIN, false); }
            if (_altHeld) { Key(VK_LMENU, false); _altHeld = false; }
            if (_cmdDown) { _cmdDown = false; Key(VK_LCONTROL, false); }
            _optDown = false;
            _optUsed = false;

            // Remapped keys too: their ups would otherwise never be sent.
            foreach (int to in _pressed.Values)
                if (to != TEXT) Scan(to, false);
            _pressed.Clear();
        }

        /// <summary>
        /// Let go of every key Windows reports down, whoever pressed it.
        ///
        /// What we track is not the whole story. A key-down injected by another
        /// program, or by a Keycap that was killed mid-press, stays down with no
        /// key-up ever coming, and the keyboard looks dead. Mouse buttons are
        /// left alone.
        /// </summary>
        static void ReleaseHeld()
        {
            List<int> held = new List<int>();
            for (int vk = VK_BACK; vk <= 0xFE; vk++)
            {
                if (vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU) continue;   // also report through their left/right keys, which get released
                if (Down(vk)) held.Add(vk);
            }
            if (held.Count == 0) return;
            // A lone Win or Alt release opens Start or the menu bar; a key in
            // between masks it (as Bare does).
            if (held.Contains(VK_LWIN) || held.Contains(VK_RWIN) || held.Contains(VK_LMENU) || held.Contains(VK_RMENU)) Tap(VK_MASK);
            foreach (int vk in held) Key(vk, false);
        }

        /// <summary>Turn remapping off. From any thread, like Start.</summary>
        public static void Stop()
        {
            if (!_on) return;
            Unwire();
            _rearmWanted = false;
            OnHookThread(Uninstall);
            Raise();
        }

        static void Uninstall()
        {
            _on = false;
            _rearmWanted = false;
            if (_watchdog != UIntPtr.Zero) { KillTimer(IntPtr.Zero, _watchdog); _watchdog = UIntPtr.Zero; }
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _running = false;
            // Never leave a key stuck down.
            ReleaseAll();
        }

        /// <summary>
        /// Raised on whichever thread the change happened on: the caller's for
        /// Start and Stop, the hook's own when both Shifts or a failed re-arm
        /// turned remapping off. Listeners with windows to update must cross
        /// over themselves.
        /// </summary>
        static void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        // ---- sending --------------------------------------------------------

        static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

        static void Send(INPUT[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, InputSize);
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

        // One key goes out from the stack, with no array made to carry it:
        // Command, Option and every remapped key come through here.
        static void Key(int vk, bool down)
        {
            INPUT i = Vk(vk, down);
            SendInput(1, ref i, InputSize);
        }

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
            SendInput(1, ref i, InputSize);
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
        /// is holding Ctrl down on the OS side. Alt held for Option stays up.
        ///
        /// The work is handed to a worker thread rather than done here. A
        /// low-level hook callback has a few hundred milliseconds to return -
        /// Windows silently unhooks one that overruns, with no error and no
        /// notification, and the app then sits there doing nothing. Typing a
        /// phrase from inside the callback was more than enough to trip it.
        /// </summary>
        static void Bare(Action act)
        {
            bool cmd = _cmdDown, win = _winHeld, alt = _altHeld;   // read on the hook thread
            // Alt is not put back afterwards: pressed again with nothing after
            // it, letting go of Option would open the menu bar. The next key
            // that needs Alt presses it again.
            _altHeld = false;
            Post(delegate
            {
                // A key between Alt going down and up keeps its release from
                // opening the menu bar. 0xE8 is unassigned, so nothing reacts
                // to it - the same mask AutoHotkey uses.
                if (alt) { Tap(VK_MASK); Key(VK_LMENU, false); }
                if (cmd) Key(VK_LCONTROL, false);
                if (win) Key(VK_LWIN, false);
                act();
                // Put back only what is still held: a Command or Option let go
                // while this sat in the queue has already sent its key-up.
                // Checked again after the restore, for a release that lands in
                // between.
                if (win && _winHeld) { Key(VK_LWIN, true); if (!_winHeld) Key(VK_LWIN, false); }
                if (cmd && _cmdDown) { Key(VK_LCONTROL, true); if (!_cmdDown) Key(VK_LCONTROL, false); }
            });
        }

        // ---- the injection thread -------------------------------------------
        // One thread, one queue, so injections still happen in the order the
        // keystrokes arrived.
        static readonly Queue<Action> _work = new Queue<Action>();
        static Thread _worker;

        static void Post(Action act)
        {
            lock (_work)
            {
                _work.Enqueue(act);
                Monitor.Pulse(_work);
            }
        }

        static void StartWorker()
        {
            if (_worker != null) return;
            _worker = new Thread(delegate ()
            {
                for (; ; )
                {
                    Action a;
                    lock (_work)
                    {
                        while (_work.Count == 0) Monitor.Wait(_work);
                        a = _work.Dequeue();
                    }
                    try { a(); }
                    catch { }        // one bad phrase must not take the thread down
                }
            });
            _worker.IsBackground = true;
            _worker.Name = "keycap-inject";
            // What it sends is the other half of a shortcut the user is waiting
            // on, so it goes ahead of ordinary work too.
            _worker.Priority = ThreadPriority.Highest;
            _worker.Start();
        }

        static bool Shift
        {
            get { return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0; }
        }

        // ---- apps that read Command+arrow chords as Ctrl chords -------------
        // Forge Deck moves a grid cell on Ctrl+Shift+arrow and the grid's focus
        // on Ctrl+Alt+arrow: Command+Shift and Command+Option with an arrow,
        // typed the Mac way. Everywhere else those select to the line's or the
        // document's ends; in Forge Deck they reach the app as Ctrl chords.
        // The installed app is "Forge Deck.exe"; a dev build runs Electron from
        // inside a forge-deck checkout.
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        static bool ForgeDeckInFront()
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid == 0) return false;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                if (!QueryFullProcessImageName(h, 0, sb, ref size)) return false;
                string path = sb.ToString(0, size);
                string file = System.IO.Path.GetFileName(path);
                if (string.Equals(file, "Forge Deck.exe", StringComparison.OrdinalIgnoreCase)) return true;
                return string.Equals(file, "electron.exe", StringComparison.OrdinalIgnoreCase)
                    && path.IndexOf("\\forge-deck", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            finally { CloseHandle(h); }
        }

        // ---- the hook -------------------------------------------------------

        static IntPtr Callback(int code, IntPtr wParam, ref KBDLLHOOKSTRUCT k)
        {
            if (code < 0 || k.dwExtraInfo == Mark)
                return CallNextHookEx(_hook, code, wParam, ref k);

            int msg = (int)wParam;
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool handled = false;

            try { handled = Handle((int)k.vkCode, (int)k.scanCode, down); }
            catch { handled = false; }

            if (handled) return (IntPtr)1;
            return CallNextHookEx(_hook, code, wParam, ref k);
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

        /// <summary>The tail of what is being typed, for abbreviations - in
        /// capitals, since that is what a letter's virtual key is.</summary>
        static readonly char[] _typed = new char[40];
        static int _typedLen;

        /// <summary>
        /// Follow the letters going past and report a trigger word the moment
        /// one is completed.
        ///
        /// Only letters and digits are followed. That is all a trigger needs,
        /// and it avoids asking Windows what character a key produces -
        /// ToUnicode would advance the layout's dead-key state, so asking
        /// would break typing an accent.
        /// </summary>
        static Mapping.LivePhrase Track(Mapping.Snapshot map, int vk)
        {
            if (vk == VK_BACK)
            {
                if (_typedLen > 0) _typedLen--;
                return null;
            }

            bool typed = (vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39);
            if (!typed) { _typedLen = 0; return null; }   // space, punctuation, arrows...

            // Full: the oldest letter makes room, so it is always the last forty.
            if (_typedLen == _typed.Length)
            {
                Array.Copy(_typed, 1, _typed, 0, _typed.Length - 1);
                _typedLen--;
            }
            _typed[_typedLen++] = (char)vk;

            Mapping.LivePhrase p = map.FindWord(_typed, _typedLen);
            if (p != null) _typedLen = 0;
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
            if (!_cmdDown && !_optDown && !_winHeld && !_altHeld && !_altTabbing && _pressed.Count == 0) return;
            if (unchecked(Environment.TickCount - _lastEvent) < 4000) return;
            ReleaseAll();
        }

        static bool Handle(int vk, int sc, bool down)
        {
            _lastEvent = Environment.TickCount;

            // The mapping as it stands for this one key. It is replaced whole
            // when something changes, never edited, so there is nothing to lock.
            Mapping.Snapshot map = Mapping.Live;

            // ---- modifiers: Command -> Ctrl, Option -> Alt -----------------
            if (vk == VK_LWIN)
            {
                string dst = map.CmdDst;
                if (dst == "LWin") return false;          // left as the Windows key
                _cmdDown = down && dst == "LCtrl";
                int target = VkOf(dst);
                if (target == 0) return false;
                Key(target, down);
                return true;
            }
            if (vk == VK_LMENU)
            {
                string optDst = map.OptDst;
                if (optDst == "LAlt")
                {
                    // Option as Alt. Alt goes down with Option, so Alt+click and
                    // Alt+letter behave as on any Windows keyboard; the Mac
                    // editing combos below lift it out of the way (see Bare).
                    if (down)
                    {
                        if (_optDown) return true;          // auto-repeat
                        _optDown = true;
                        _optUsed = false;
                        Key(VK_LMENU, true);
                        _altHeld = true;
                    }
                    else
                    {
                        _optDown = false;
                        if (_altHeld) { Key(VK_LMENU, false); _altHeld = false; }
                    }
                    return true;
                }
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
                        _winHeld = false;
                        Key(VK_LWIN, false);
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

            if (!down) return ScanRemap(map, sc, false);

            // A remapped key held while Command or Option goes down keeps
            // repeating as what its press started. Otherwise the repeat goes
            // through as the physical key, and its up is then swallowed.
            if (_pressed.Count > 0 && _pressed.ContainsKey(sc)) return ScanRemap(map, sc, true);

            // ---- panic release ------------------------------------------------
            // Holding both Shift keys releases every held key, ours or anyone's,
            // and turns remapping off, so a wedged keyboard is always recoverable
            // from the keyboard itself.
            if ((vk == VK_LSHIFT || vk == 0xA1) && Down(VK_LSHIFT) && Down(0xA1))
            {
                Panic();
                return false;
            }

            // ---- user phrases ------------------------------------------------
            // Checked before the built-in shortcuts so a phrase can claim a
            // combination the defaults would otherwise swallow. Shift is only
            // asked after when there is a combination it could be part of.
            int held = (_cmdDown ? 1 : 0) | (_optDown ? 2 : 0);
            if (map.Combos.Length > 0)
            {
                int pmods = held | (Shift ? 4 : 0);
                if (pmods != 0)
                {
                    Mapping.LivePhrase ph = map.FindCombo(pmods, vk);
                    if (ph != null)
                    {
                        string body = ph.Text;
                        Bare(delegate { Text(body); });
                        _optUsed = true;
                        return true;
                    }
                }
            }

            // ---- typed abbreviations ------------------------------------------
            // A phrase can also be triggered by typing a word. Command and
            // Option mean the keystroke is a shortcut rather than typing, so
            // they break the word instead of extending it.
            if (held != 0) _typedLen = 0;
            else if (map.Words.Length > 0)
            {
                Mapping.LivePhrase word = Track(map, vk);
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
                // Forge Deck's grid keys (ForgeDeckInFront): the arrow goes
                // through untouched, under the Ctrl that Command holds down and
                // the Shift, or the Alt that Option holds down.
                if ((vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN)
                    && (Shift || _altHeld) && ForgeDeckInFront())
                    return false;
                switch (vk)
                {
                    case VK_LEFT: Bare(delegate { Tap(VK_HOME); }); return true;
                    case VK_RIGHT: Bare(delegate { Tap(VK_END); }); return true;
                    case VK_UP: Bare(delegate { TapCtrl(VK_HOME); }); return true;
                    case VK_DOWN: Bare(delegate { TapCtrl(VK_END); }); return true;
                    case VK_BACK: Bare(delegate { Tap(VK_DELETE); }); return true;   // the Del key the board does not have
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
                bool asAlt = map.OptAsAlt;
                // Option+Tab is the app switcher. Alt has to stay held while
                // Tab is tapped, or the list closes after one step - so swap
                // the Windows key we are holding for Alt and keep it down
                // until Option is released.
                if (vk == VK_TAB && !asAlt)
                {
                    if (!_altTabbing)
                    {
                        if (_winHeld) { _winHeld = false; Key(VK_LWIN, false); }
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
                if (vk >= VK_F1 && vk <= VK_F12)
                {
                    int f = vk;
                    Bare(delegate { Tap(f); });      // the real function key
                    return true;
                }

                // Everything else is an Alt shortcut - Alt+Tab included - or,
                // with Option as Windows, a Windows one: Option+E, Option+L...
                if (asAlt)
                {
                    if (!_altHeld) { Key(VK_LMENU, true); _altHeld = true; }
                }
                else if (!_winHeld) { _winHeld = true; Key(VK_LWIN, true); }
                return false;
            }

            // ---- function row ------------------------------------------------
            switch (vk)
            {
                case VK_F1:
                case VK_F2:
                    if (!map.BrightnessKeys) break;              // plain F1 / F2
                    Brightness.Step(vk == VK_F2 ? 1 : -1);       // DDC/CI is slow - done on its own thread
                    return true;
                case VK_F3: Bare(delegate { Tap(VK_TAB, VK_LWIN); }); return true;
                case VK_F4: Bare(delegate { Tap(VK_S, VK_LWIN); }); return true;   // the magnifier
                case VK_F5: OnUi(Audio.ToggleMic); return true;                    // Core Audio, and the indicator after it
                case VK_F7: Tap(VK_MEDIA_PREV); return true;
                case 0x77: Tap(VK_MEDIA_PLAY); return true;     // F8
                case 0x78: Tap(VK_MEDIA_NEXT); return true;     // F9
                case 0x79: Tap(VK_VOLUME_MUTE); return true;    // F10
                case 0x7A: Tap(VK_VOLUME_DOWN); return true;    // F11
                case 0x7B: Tap(VK_VOLUME_UP); return true;      // F12
            }

            return ScanRemap(map, sc, true);
        }

        /// <summary>Ctrl+key. Shift is left alone: if the user is holding it,
        /// the injected key already inherits it.</summary>
        static void TapCtrl(int vk) { Tap(vk, VK_CONTROL); }

        // ---- per-layout scancode fixes --------------------------------------

        // What each remapped key's down sent, by physical scancode. Its up lets
        // go of exactly that, not of whatever the layout and the rules say by
        // the time the key comes up.
        static readonly Dictionary<int, int> _pressed = new Dictionary<int, int>();
        const int TEXT = -1;            // a text rule typed its text; the up has nothing to release

        static bool ScanRemap(Mapping.Snapshot map, int phys, bool down)
        {
            int to;
            Mapping.LiveScan r;

            if (!down)
            {
                // Nothing recorded: the down went through as itself, so the
                // up does too.
                if (_pressed.Count == 0 || !_pressed.TryGetValue(phys, out to)) return false;
                _pressed.Remove(phys);
                if (to != TEXT) Scan(to, false);
                return true;
            }

            if (_pressed.Count > 0 && _pressed.TryGetValue(phys, out to))
            {
                // Auto-repeat keeps the output the press started with. Text is
                // typed again only while the rule still is a text rule.
                if (to == TEXT)
                {
                    r = map.FindScan(phys, InputLang.CurrentLangId());
                    if (r != null && r.Text != null) Text(Shift ? r.ShiftText : r.Text);
                }
                else Scan(to, true);
                return true;
            }

            r = map.FindScan(phys, InputLang.CurrentLangId());
            if (r == null) return false;

            if (r.Text != null)
            {
                Text(Shift ? r.ShiftText : r.Text);
                _pressed[phys] = TEXT;
                return true;
            }

            // A rule whose target is not a scancode cannot be carried out; the
            // key is left as it is.
            if (r.Broken) return false;

            Scan(r.To, true);
            _pressed[phys] = r.To;
            return true;
        }
    }
}
