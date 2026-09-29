using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Keycap
{
    /// <summary>
    /// Brightness for external monitors, set on the monitor itself over DDC/CI -
    /// VCP code 0x10, the same setting its own buttons change. Windows only
    /// drives brightness for built-in panels, so for an external screen there is
    /// no key for it and no API short of talking to the monitor. A monitor that
    /// does not answer is dimmed in software instead, with a veil over its
    /// picture (see Dimmer), unless that is switched off in Settings.
    ///
    /// DDC/CI is slow: one read or write takes 40-100 ms over the monitor's I2C
    /// line, and one that goes unanswered takes longer. The keyboard hook has a
    /// few hundred milliseconds before Windows silently unhooks it, so nothing
    /// here talks to a monitor on the caller's thread. Requests are written down
    /// and one worker carries them out. A burst of them - F2 held on key repeat -
    /// folds into one step of the right size, rather than a queue of writes the
    /// monitor would still be working through long after the key was let go.
    ///
    /// The capability string is never consulted. Some monitors leave brightness
    /// out of the one they report, or fail to report one at all, while accepting
    /// the command just fine - so the only honest test is to ask.
    /// </summary>
    public static class Brightness
    {
        /// <summary>
        /// One display as it was last read. Displays() hands out copies, so a
        /// caller holding one never races the worker.
        /// </summary>
        public class Display
        {
            public string Device;      // "\\.\DISPLAY1" - the key everything is matched on
            public string Name;        // "CX158" from the monitor's EDID, else "Display 1"
            public Size Size;          // rcMonitor width x height
            public bool Primary;
            public bool Supported;     // answered VCP 0x10 with a maximum above zero
            public bool Soft;          // dimmed by Keycap rather than set on the monitor
            public int Level, Max;     // raw VCP values; a percentage, out of 100, when Soft

            /// <summary>Level as a share of what this monitor calls its maximum.</summary>
            public int Percent
            {
                get { return Max > 0 ? (int)Math.Round(Level * 100.0 / Max) : 0; }
            }
        }

        /// <summary>A cached display, and when what it says was last true.</summary>
        class Entry : Display
        {
            /// <summary>The monitor's interface path - which monitor this is, see Lookup.</summary>
            public string Path;

            /// <summary>When Level or Supported was last read or written.</summary>
            public DateTime Known;
        }

        /// <summary>
        /// A display as Windows has it at this moment. The handle is only good
        /// until the displays change, so these are made for one operation and
        /// never kept.
        /// </summary>
        class Mon
        {
            public IntPtr Handle;
            public string Device;
            public string Path;         // the monitor itself, as InterfacePath gives it
            public Size Size;
            public bool Primary;
        }

        // ---- native ---------------------------------------------------------

        const byte VcpBrightness = 0x10;
        const int MONITOR_DEFAULTTONEAREST = 2;
        const int MONITORINFOF_PRIMARY = 1;
        const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

        [DllImport("dxva2.dll")]
        static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint n);
        [DllImport("dxva2.dll")]
        static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint n, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")]
        static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")]
        static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
        [DllImport("dxva2.dll")]
        static extern bool SetVCPFeature(IntPtr h, byte code, uint value);

        // ---- state ----------------------------------------------------------

        // How long a level is believed. Our own writes keep it fresh; past a
        // few seconds the monitor's buttons may have moved it, so it is read
        // again before a step. A monitor that did not answer is not asked again
        // on a key press at all - only by the next full scan - so a press never
        // waits on a monitor that never answers.
        static readonly TimeSpan TrustLevel = TimeSpan.FromSeconds(5);

        static readonly object _lock = new object();
        static Thread _worker;
        static SynchronizationContext _ui;

        // The second scan after a display change, held here so it is not
        // collected before it fires.
        static System.Threading.Timer _settle;

        // Work asked for and not yet done. Each kind folds into one: the steps
        // add up, the monitor under the mouse is the latest one seen,
        // and each display's slider is at whatever it was set to last.
        static int _steps;
        static IntPtr _stepMon;
        static bool _scanPending;
        static readonly Dictionary<string, int> _sets =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // What is known of each display, by device and in Windows' order.
        static readonly Dictionary<string, Entry> _byDevice =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        static readonly List<Entry> _order = new List<Entry>();
        static bool _scanned;

        // The monitors' EDID names from WMI: { instance name, friendly name }.
        // Only the worker touches it.
        static List<string[]> _names;

        // ---- the surface ----------------------------------------------------

        /// <summary>What is known about the displays changed. Always raised on the UI thread.</summary>
        public static event EventHandler Changed;

        /// <summary>True once every display has been read at least once.</summary>
        public static bool Scanned
        {
            get { lock (_lock) return _scanned; }
        }

        /// <summary>
        /// Start the worker, and have it read every display straight away, so
        /// the names and levels are already known by the first press of F1 or
        /// F2 rather than fetched while it waits. Called once, on the UI thread,
        /// so there is a context to hand the indicator and Changed back to.
        /// </summary>
        public static void Start()
        {
            if (_worker != null) return;
            _ui = SynchronizationContext.Current;
            if (_ui == null) _ui = new WindowsFormsSynchronizationContext();

            _worker = new Thread(Run);
            _worker.IsBackground = true;
            _worker.Name = "keycap-brightness";
            _worker.Start();
            Scan();

            // A monitor plugged in, unplugged, rearranged or woken: read them
            // all again - now, and once more three seconds on. A monitor is
            // often not ready for DDC/CI for a moment after a display change,
            // and the first scan alone would mark it silent. Another change
            // inside those three seconds pushes the second scan back.
            _settle = new System.Threading.Timer(delegate { Scan(); }, null,
                                                 Timeout.Infinite, Timeout.Infinite);
            SystemEvents.DisplaySettingsChanged += delegate
            {
                Scan();
                _settle.Change(3000, Timeout.Infinite);
            };
        }

        /// <summary>Every display as last read, in Windows' order.</summary>
        public static List<Display> Displays()
        {
            List<Display> list = new List<Display>();
            lock (_lock)
                foreach (Entry e in _order) list.Add(Copy(e));
            return list;
        }

        /// <summary>Read every display again, from scratch.</summary>
        public static void Scan()
        {
            lock (_lock)
            {
                _scanPending = true;
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>
        /// One press of F1 (-1) or F2 (+1). This runs inside the keyboard hook,
        /// so it only writes the request down and returns. The monitor under
        /// the mouse is taken here, at the moment of the press, since the
        /// pointer may have moved on by the time the worker gets to it.
        /// </summary>
        public static void Step(int direction)
        {
            POINT p;
            IntPtr mon = GetCursorPos(out p) ? MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;
            lock (_lock)
            {
                _steps += direction;
                _stepMon = mon;
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>
        /// A display's level from its slider. A drag sets it many times a second;
        /// only the latest value is sent, when the monitor is ready for it.
        /// </summary>
        public static void Set(string device, int percent)
        {
            lock (_lock)
            {
                _sets[device] = Math.Max(0, Math.Min(100, percent));
                Monitor.Pulse(_lock);
            }
        }

        // ---- the worker -----------------------------------------------------

        static void Run()
        {
            // Per-monitor DPI awareness for this thread, for good. The app as a
            // whole is DPI-unaware, so Windows would report every scaled display
            // at its 96-DPI size - a 2880 x 1800 panel at 150% as 1920 x 1200.
            // Aware, GetMonitorInfo hands back real device pixels. The thread
            // owns no windows, so there is nothing for it to rescale. Pre-1607
            // builds have no such export; there it keeps the scaled size.
            // -4 is DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.
            try { SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }

            for (; ; )
            {
                bool scan;
                int steps;
                IntPtr mon;
                List<KeyValuePair<string, int>> sets;
                lock (_lock)
                {
                    while (!_scanPending && _steps == 0 && _sets.Count == 0) Monitor.Wait(_lock);
                    scan = _scanPending;
                    steps = _steps;
                    mon = _stepMon;
                    sets = new List<KeyValuePair<string, int>>(_sets);
                    _scanPending = false;
                    _steps = 0;
                    _sets.Clear();
                }

                // One failure must not take the thread down with it - there
                // would be nothing left to carry out the next press.
                if (scan)
                {
                    try { ScanNow(); }
                    catch (Exception ex) { Program.LogError(ex); }
                }
                foreach (KeyValuePair<string, int> s in sets)
                {
                    try { SetNow(s.Key, s.Value); }
                    catch (Exception ex) { Program.LogError(ex); }
                }
                if (steps != 0)
                {
                    try { StepNow(steps, mon); }
                    catch (Exception ex) { Program.LogError(ex); }
                }
            }
        }

        static void ScanNow()
        {
            _names = ReadNames();
            List<Entry> all = new List<Entry>();
            List<string> softPaths = new List<string>();
            List<IntPtr> softMons = new List<IntPtr>();
            List<int> softLevels = new List<int>();
            foreach (Mon m in Monitors())
            {
                Entry e = NewEntry(m);
                Probe(e, m.Handle);
                all.Add(e);
                lock (_lock)
                    if (e.Soft)
                    {
                        softPaths.Add(e.Path);
                        softMons.Add(m.Handle);
                        softLevels.Add(e.Level);
                    }
            }
            lock (_lock)
            {
                _order.Clear();
                _order.AddRange(all);
                _byDevice.Clear();
                foreach (Entry e in all) _byDevice[e.Device] = e;
                _scanned = true;
            }

            // The veils follow the scan: saved levels come back at launch,
            // after a display change each veil goes back over its own monitor,
            // and the veil of a monitor that has gone - or started answering -
            // is taken away.
            OnUi(delegate
            {
                for (int i = 0; i < softPaths.Count; i++)
                    Dimmer.Set(softPaths[i], softMons[i], softLevels[i]);
                Dimmer.Keep(softPaths);
                if (!Mapping.SoftDim) Dimmer.Clear();
            });
            RaiseChanged();
        }

        static void SetNow(string device, int percent)
        {
            Mon m = Find(Monitors(), device);
            if (m == null) return;          // unplugged since the slider moved
            Entry e = Lookup(m);

            bool soft;
            int max;
            lock (_lock)
            {
                if (!Controllable(e)) return;
                soft = e.Soft;
                max = e.Max;
            }
            if (soft)
            {
                lock (_lock) { e.Level = percent; e.Known = DateTime.UtcNow; }
                Dim(e.Path, m.Handle, percent);
                RaiseChanged();
                return;
            }
            int raw = (int)Math.Round(percent * max / 100.0);
            bool ok = Write(m.Handle, raw);
            lock (_lock)
            {
                if (ok) { e.Level = raw; e.Known = DateTime.UtcNow; }
                else e.Supported = false;
            }
            RaiseChanged();
        }

        static void StepNow(int steps, IntPtr mon)
        {
            string pointed = DeviceOf(mon);
            List<Mon> mons = Monitors();
            List<Entry> current = Current(mons);
            Entry here;
            List<Entry> targets = Targets(current, pointed, out here);

            if (targets.Count == 0)
            {
                // DDC fails now and then - right after a display change, while a
                // monitor wakes - and a monitor marked silent is otherwise only
                // asked again on a full scan. This is the one place a press pays
                // for asking every display, and only when nothing else would
                // work: an ordinary press never waits on a monitor that never
                // answers.
                for (int i = 0; i < mons.Count; i++) Probe(current[i], mons[i].Handle);
                targets = Targets(current, pointed, out here);
            }

            // Every pill below goes on the display the mouse is on - where the
            // user is looking - whichever display actually changed.
            if (targets.Count == 0)
            {
                OnUi(delegate { Osd.Flash("No display here supports brightness control", Osd.Sym.Sun, Theme.Warn, mon); });
                RaiseChanged();     // the reads just taken may have changed what Settings shows
                return;
            }

            bool hereIsTarget = targets.Contains(here);
            int from;
            lock (_lock) from = (hereIsTarget ? here : targets[0]).Percent;
            int percent = Snap(from, steps, Mapping.BrightnessStep);

            // Every target gets the same percentage, so in "all together" mode
            // the displays move as one rather than each keeping its own offset.
            List<Entry> done = new List<Entry>();
            foreach (Entry t in targets)
            {
                IntPtr h = Find(mons, t.Device).Handle;
                bool soft;
                int max;
                lock (_lock)
                {
                    soft = t.Soft;
                    max = t.Max;
                }
                if (soft)
                {
                    // Dimmed in software: no DDC at all, so it cannot fail.
                    lock (_lock) { t.Level = percent; t.Known = DateTime.UtcNow; }
                    Dim(t.Path, h, percent);
                    done.Add(t);
                    continue;
                }
                int raw = (int)Math.Round(percent * max / 100.0);
                bool ok = Write(h, raw);
                lock (_lock)
                {
                    if (ok) { t.Level = raw; t.Known = DateTime.UtcNow; }
                    else t.Supported = false;
                }
                if (ok) done.Add(t);
            }

            // Showing a new level when no monitor took it would be a lie. The
            // ones that refused are already marked, so Settings drops their
            // sliders and the next press looks elsewhere.
            if (done.Count == 0)
            {
                OnUi(delegate { Osd.Flash("Brightness could not be set", Osd.Sym.Sun, Theme.Warn, mon); });
                RaiseChanged();
                return;
            }

            OnUi(delegate { Osd.Level(Osd.Sym.Sun, percent, Theme.Text, mon); });
            RaiseChanged();
        }

        /// <summary>
        /// Where a press goes. Normally the display under the mouse; if that
        /// one cannot be controlled, the ones that can - so with one
        /// controllable monitor the keys always reach it, wherever the pointer
        /// is. With "all together", every controllable display. With software
        /// dimming on every display is controllable, so the press always lands
        /// where the pointer is; the fallback only matters with it off.
        /// <paramref name="here"/> is the pointed-at display's entry, if any.
        /// </summary>
        static List<Entry> Targets(List<Entry> current, string pointed, out Entry here)
        {
            here = null;
            List<Entry> targets = new List<Entry>();
            lock (_lock)
            {
                foreach (Entry e in current)
                    if (string.Equals(e.Device, pointed, StringComparison.OrdinalIgnoreCase)) here = e;
                if (!Mapping.BrightnessAll && here != null && Controllable(here)) targets.Add(here);
                else
                    foreach (Entry e in current)
                        if (Controllable(e)) targets.Add(e);
            }
            return targets;
        }

        /// <summary>
        /// Move along a grid of the step size, one line per step, so 43 goes to
        /// 50 and then 60 rather than 53 and 63 - the levels stay round numbers
        /// however they started.
        /// </summary>
        static int Snap(int p, int steps, int step)
        {
            if (step < 1) step = 1;
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                if (steps > 0) p = Math.Min(100, (p / step + 1) * step);
                else p = Math.Max(0, ((p + step - 1) / step - 1) * step);
            }
            return p;
        }

        // ---- what is known --------------------------------------------------

        /// <summary>
        /// Every display Windows has right now, each known and recent enough to
        /// act on: a new one is named and read, and one whose level has gone
        /// stale is read again. One that did not answer - dimmed in software or
        /// not - stays that way until the next full scan: Settings opening,
        /// Check again, a display change. That is what its row in Settings
        /// already tells the user.
        /// </summary>
        static List<Entry> Current(List<Mon> mons)
        {
            List<Entry> list = new List<Entry>();
            foreach (Mon m in mons)
            {
                Entry e = Lookup(m);
                bool stale;
                lock (_lock) stale = e.Supported && !Fresh(e);
                if (stale) Probe(e, m.Handle);
                list.Add(e);
            }
            return list;
        }

        /// <summary>
        /// The entry for a display, named, read and added if it is new. The
        /// \\.\DISPLAYn names are handed out in whatever order the displays come
        /// up, and swap after a monitor sleeps or reconnects, so an entry cached
        /// under one may now describe a different monitor. The interface path is
        /// the monitor itself: a cached entry is reused only while its path
        /// still matches, and is replaced by a freshly read one otherwise.
        /// </summary>
        static Entry Lookup(Mon m)
        {
            Entry old;
            lock (_lock)
            {
                if (_byDevice.TryGetValue(m.Device, out old)
                    && string.Equals(old.Path, m.Path, StringComparison.OrdinalIgnoreCase))
                {
                    // A resolution change keeps the device and the path.
                    old.Size = m.Size;
                    old.Primary = m.Primary;
                    return old;
                }
            }
            Entry e = NewEntry(m);
            Probe(e, m.Handle);
            lock (_lock)
            {
                _byDevice[e.Device] = e;
                int at = old == null ? -1 : _order.IndexOf(old);
                if (at >= 0) _order[at] = e;
                else _order.Add(e);
            }
            return e;
        }

        static Entry NewEntry(Mon m)
        {
            Entry e = new Entry();
            e.Device = m.Device;
            e.Path = m.Path;
            e.Size = m.Size;
            e.Primary = m.Primary;
            e.Name = NameOf(m.Device, m.Path);
            return e;
        }

        static bool Fresh(Entry e)
        {
            TimeSpan age = DateTime.UtcNow - e.Known;
            return age >= TimeSpan.Zero && age < TrustLevel;
        }

        /// <summary>
        /// Ask the display for its brightness, and note whether it answered. One
        /// that does not is dimmed in software instead, at the level last saved
        /// for it, if Settings allows - and one that was, and now answers, has
        /// its veil taken away so the real brightness takes over again: DDC/CI
        /// has been switched on in its menu, say.
        /// </summary>
        static void Probe(Entry e, IntPtr mon)
        {
            int level, max;
            bool ok = Read(mon, out level, out max);
            // A veil is keyed by the path, so without one there is nothing to dim.
            bool soft = !ok && Mapping.SoftDim && e.Path != null;
            int saved = soft ? Mapping.DimFor(e.Path) : 100;
            bool wasSoft;
            lock (_lock)
            {
                wasSoft = e.Soft;
                e.Supported = ok;
                e.Soft = soft;
                if (ok) { e.Level = level; e.Max = max; }
                else if (soft) { e.Level = saved; e.Max = 100; }
                e.Known = DateTime.UtcNow;
            }
            if (ok && wasSoft)
            {
                string path = e.Path;
                OnUi(delegate { Dimmer.Set(path, mon, 100); });
            }
        }

        /// <summary>A display F1, F2 and its slider can act on - over DDC/CI, or in software.</summary>
        static bool Controllable(Entry e)
        {
            return e.Supported || e.Soft;
        }

        /// <summary>
        /// Set a software level: the veil over the monitor, and the level saved
        /// for it so it comes back at the next launch.
        /// </summary>
        static void Dim(string path, IntPtr mon, int percent)
        {
            OnUi(delegate
            {
                Dimmer.Set(path, mon, percent);
                Mapping.SetDim(path, percent);
            });
        }

        static Display Copy(Entry e)
        {
            Display d = new Display();
            d.Device = e.Device;
            d.Name = e.Name;
            d.Size = e.Size;
            d.Primary = e.Primary;
            d.Supported = e.Supported;
            d.Soft = e.Soft;
            d.Level = e.Level;
            d.Max = e.Max;
            return d;
        }

        static void OnUi(Action act)
        {
            SynchronizationContext ui = _ui;
            if (ui == null) return;
            ui.Post(delegate { act(); }, null);
        }

        static void RaiseChanged()
        {
            OnUi(delegate
            {
                EventHandler h = Changed;
                if (h != null) h(null, EventArgs.Empty);
            });
        }

        // ---- displays -------------------------------------------------------

        static List<Mon> Monitors()
        {
            List<Mon> list = new List<Mon>();
            MonitorEnumProc proc = delegate (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data)
            {
                MONITORINFOEX mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref mi))
                {
                    Mon m = new Mon();
                    m.Handle = hMon;
                    m.Device = mi.szDevice;
                    m.Size = new Size(mi.rcMonitor.Right - mi.rcMonitor.Left,
                                      mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    m.Primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                    list.Add(m);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);     // native code holds it for the whole call
            foreach (Mon m in list) m.Path = InterfacePath(m.Device);
            return list;
        }

        static Mon Find(List<Mon> mons, string device)
        {
            foreach (Mon m in mons)
                if (string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        static string DeviceOf(IntPtr mon)
        {
            if (mon == IntPtr.Zero) return null;
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            return GetMonitorInfo(mon, ref mi) ? mi.szDevice : null;
        }

        // ---- DDC/CI ---------------------------------------------------------

        /// <summary>
        /// The physical monitors behind a display, opened for one operation.
        /// The handles are never kept: they go stale when the displays change,
        /// and a stale one fails just like a monitor that stopped answering.
        /// </summary>
        static PHYSICAL_MONITOR[] Open(IntPtr mon)
        {
            uint n;
            if (mon == IntPtr.Zero || !GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out n) || n == 0)
                return null;
            PHYSICAL_MONITOR[] pm = new PHYSICAL_MONITOR[n];
            return GetPhysicalMonitorsFromHMONITOR(mon, n, pm) ? pm : null;
        }

        /// <summary>
        /// The brightness of the first physical monitor behind this display that
        /// answers - there is more than one only when displays are cloned.
        /// DDC/CI drops the odd command, so when none answers they are all
        /// asked once more, 50 ms later, before the display is called silent.
        /// </summary>
        static bool Read(IntPtr mon, out int level, out int max)
        {
            level = 0;
            max = 0;
            PHYSICAL_MONITOR[] pm = Open(mon);
            if (pm == null) return false;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(50);
                    foreach (PHYSICAL_MONITOR p in pm)
                    {
                        uint cur, top;
                        if (GetVCPFeatureAndVCPFeatureReply(p.hPhysicalMonitor, VcpBrightness,
                                                            IntPtr.Zero, out cur, out top) && top > 0)
                        {
                            level = (int)cur;
                            max = (int)top;
                            return true;
                        }
                    }
                }
                return false;
            }
            finally { DestroyPhysicalMonitors((uint)pm.Length, pm); }
        }

        /// <summary>
        /// Send a level to every physical monitor behind this display. MCCS asks
        /// for at least 50 ms between commands, and a monitor rushed sooner may
        /// drop the next one, so each write waits that long before anything
        /// else goes out. DDC/CI drops the odd command even so, so a write that
        /// fails is sent once more.
        /// </summary>
        static bool Write(IntPtr mon, int raw)
        {
            PHYSICAL_MONITOR[] pm = Open(mon);
            if (pm == null) return false;
            try
            {
                bool any = false;
                foreach (PHYSICAL_MONITOR p in pm)
                {
                    bool ok = SetVCPFeature(p.hPhysicalMonitor, VcpBrightness, (uint)raw);
                    Thread.Sleep(50);
                    if (!ok)
                    {
                        ok = SetVCPFeature(p.hPhysicalMonitor, VcpBrightness, (uint)raw);
                        Thread.Sleep(50);
                    }
                    if (ok) any = true;
                }
                return any;
            }
            finally { DestroyPhysicalMonitors((uint)pm.Length, pm); }
        }

        // ---- names ----------------------------------------------------------

        /// <summary>
        /// The monitor's own name from its EDID - "CX158" - else "Display 1".
        /// The names Windows keeps with the display devices are mostly
        /// "Generic PnP Monitor", so the EDID one comes from WMI, matched to the
        /// device through the monitor's interface path.
        /// </summary>
        static string NameOf(string device, string path)
        {
            if (_names == null) _names = ReadNames();
            if (path != null)
                foreach (string[] n in _names)
                    if (n[0].StartsWith(path + "_", StringComparison.OrdinalIgnoreCase)) return n[1];

            int i = device.Length;
            while (i > 0 && char.IsDigit(device[i - 1])) i--;
            return i < device.Length ? "Display " + device.Substring(i) : "Display";
        }

        /// <summary>
        /// "\\.\DISPLAY1" to the path WMI names its monitor by. The interface
        /// name reads \\?\DISPLAY#APT4778#5&amp;92adb24&amp;0&amp;UID4353#{guid};
        /// WMI's instance is DISPLAY\APT4778\5&amp;92adb24&amp;0&amp;UID4353_0.
        /// </summary>
        static string InterfacePath(string device)
        {
            DISPLAY_DEVICE dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            if (!EnumDisplayDevices(device, 0, ref dd, EDD_GET_DEVICE_INTERFACE_NAME)) return null;
            string id = dd.DeviceID;
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith(@"\\?\")) id = id.Substring(4);
            int cut = id.IndexOf("#{");
            if (cut >= 0) id = id.Substring(0, cut);
            return id.Replace('#', '\\');
        }

        static List<string[]> ReadNames()
        {
            List<string[]> names = new List<string[]>();
            try
            {
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(@"root\wmi",
                           "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                using (ManagementObjectCollection all = q.Get())
                    foreach (ManagementBaseObject o in all)
                    {
                        string instance = o["InstanceName"] as string;
                        ushort[] raw = o["UserFriendlyName"] as ushort[];
                        if (instance == null || raw == null) continue;
                        StringBuilder sb = new StringBuilder();
                        foreach (ushort ch in raw)
                        {
                            if (ch == 0) break;
                            sb.Append((char)ch);
                        }
                        string name = sb.ToString().Trim();
                        if (name.Length > 0) names.Add(new string[] { instance, name });
                    }
            }
            catch (Exception ex)
            {
                // Only the names are lost - every display still gets "Display N".
                Program.LogError(ex);
            }
            return names;
        }
    }
}
