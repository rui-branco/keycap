using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Keycap
{
    /// <summary>
    /// Brightness in software, for a monitor that does not answer DDC/CI: a
    /// black veil over its whole picture, click-through and never focused,
    /// whose opacity is the dimming. It can only take light away - 100% is
    /// the monitor's own brightness - but it lets F1 and F2 work on a screen
    /// that would otherwise ignore them. With full dimming on (see Gamma) the
    /// display's output is scaled instead, and the veil is the fallback for a
    /// display that refuses its ramp. The two are never allowed to dim twice:
    /// above the floor Windows accepts in a ramp, the ramp alone carries the
    /// level and there is no veil; below it the ramp sits at that floor and
    /// a lighter veil makes up the difference, the two together letting
    /// through exactly GammaScale of the picture - never less.
    ///
    /// UI thread only. There is one veil per monitor, keyed by the monitor's
    /// interface path rather than its \\.\DISPLAYn name, which Windows hands
    /// out afresh whenever displays come and go.
    /// </summary>
    public static class Dimmer
    {
        // Kept as an alias for the shared darkness, so a reader following the
        // veil's opacity back to its floor lands on the one definition both
        // dimmings use; DimMath is where it lives now.
        const float MaxDark = DimMath.MaxDark;

        const int LWA_ALPHA = 2;
        const int WDA_EXCLUDEFROMCAPTURE = 0x11;
        const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
        }

        // The same shape with the device name on the end, for handing a
        // display to Gamma: a ramp is set per \\.\DISPLAYn, not per handle.
        // Brightness keeps its own copy of this helper for its own work; the
        // duplication is the codebase's way with these small native structs.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int key, byte alpha, int flags);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, int affinity);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// The veil itself. Layered, so its opacity can be set; transparent to
        /// the mouse, so every click lands on what is under it; never activated
        /// and kept out of Alt+Tab, so it is only ever seen as the dimming.
        /// </summary>
        class Veil : Form
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TRANSPARENT = 0x20;
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_LAYERED = 0x80000;
            const int WS_EX_TOPMOST = 0x8;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW
                                | WS_EX_LAYERED | WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            public Veil()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                // Sized in device pixels by Place; WinForms scaling on top
                // would undo that.
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Color.Black;
            }
        }

        static readonly Dictionary<string, Veil> _veils =
            new Dictionary<string, Veil>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Dim the monitor at <paramref name="path"/> to <paramref name="percent"/>
        /// of its own brightness. 100 takes the dimming away - veil closed and
        /// ramp restored alike. The window is made and moved inside Osd's DPI
        /// borrow, so like the pill it is per-monitor aware and covers the
        /// monitor's real pixels on a scaled display.
        ///
        /// With full dimming on, the display's output is scaled instead. Above
        /// the floor Windows accepts in a ramp, that is the whole dimming and
        /// any veil is closed. Below the floor the ramp can only go so dark,
        /// so a veil makes up the rest - HybridVeilAlpha over the floored
        /// output, the two together letting through exactly GammaScale of
        /// the picture, never darker. Only a display whose ramp will not
        /// take at all - Gamma.Apply says no, and IsApplied says no ramp of
        /// ours is left on it - falls back to the veil alone, at the full
        /// VeilAlpha. A ramp that may still be on the display takes any
        /// existing veil down with it, whichever branch found it, rather
        /// than leaving the two dimming past the target until the next Set.
        /// With full dimming off, any ramp an earlier setting left on the
        /// display is restored before the veil takes over, for the same
        /// reason. The next Set, scan or key press is what retries an
        /// unconfirmed restore.
        /// </summary>
        public static void Set(string path, IntPtr monitor, int percent)
        {
            if (path == null) return;
            if (percent >= 100) { Close(path); Gamma.Restore(path); return; }

            // The veil's share of this Set, decided by the branch taken: the
            // full VeilAlpha on the veil-only paths below, or the hybrid
            // top-up over a floored ramp where full dimming is at work.
            float alpha = DimMath.VeilAlpha(percent);
            if (Mapping.FullDimActive)
            {
                if (Gamma.Apply(path, DeviceOf(monitor), percent))
                {
                    float veilAlpha = DimMath.HybridVeilAlpha(percent);
                    if (veilAlpha <= 0.002f)
                    {
                        // The ramp carries this level by itself - at 41% the
                        // top-up is a fraction of one veil step, invisible -
                        // so the output alone is the dimming, as it was
                        // before the floor mattered. There is no veil to
                        // raise the pill over, either; the pill dims with
                        // everything else on this display, which is intended.
                        Close(path);
                        return;
                    }
                    // Below the floor: the ramp is at the darkest Windows
                    // accepts and this veil over it makes up the difference,
                    // so the picture reaches the level - the pointer and
                    // dialogs are already as dim as the output can make
                    // them, and the veil darkens what it covers the rest of
                    // the way.
                    alpha = veilAlpha;
                }
                else
                {
                    // The ramp would not take, and Apply has put back anything
                    // of its own it could verify. But if a ramp of ours is still
                    // on the display - its undo could not be confirmed - any
                    // veil would dim on top of it, so this Set ends here: the
                    // display stays at its ramp level, and the next Set retries.
                    // A veil already up - this display was veil-dimmed before
                    // full dimming was switched on - has to come down for the
                    // same reason; leaving it would dim the ramp's picture a
                    // second time. It is closed only here, after the failure,
                    // never before Apply: closing it up front would tear it
                    // down and rebuild it, pill-raise and all, on every refused
                    // key press.
                    if (Gamma.IsApplied(path)) { Close(path); return; }
                    // No ramp remains: the veil alone, at full strength.
                }
            }
            else
            {
                // Full dimming is off (or software dimming with it): give back
                // any ramp it left on this display before veiling it. Only a
                // confirmed restore earns the veil - an unconfirmed one means
                // the ramp may still be scaling the output, and veiling over
                // it would dim twice, so a veil already up comes down too
                // while the ramp is still Keycap's to retry. A display Gamma
                // never touched restores trivially and veils exactly as
                // before.
                if (!Gamma.Restore(path)) { Close(path); return; }
            }

            bool created = false;
            IntPtr ctx = Osd.EnterDpi();
            try
            {
                Rectangle r;
                if (!Area(monitor, out r)) return;     // the handle went stale; the next scan puts it right

                Veil v;
                if (!_veils.TryGetValue(path, out v))
                {
                    v = new Veil();
                    _veils[path] = v;
                    created = true;
                }
                // Bounds every time, not only when it is made - the monitor may
                // have moved or changed resolution since.
                Place(v, r);
                SetLayeredWindowAttributes(v.Handle, 0, (byte)Math.Round(alpha * 255), LWA_ALPHA);
                if (created)
                {
                    v.Show();
                    // Screenshots and screen shares get the real picture: the
                    // dimming is how this screen is viewed, not what is on it.
                    // Builds before 2004 do not know the flag, and simply refuse.
                    SetWindowDisplayAffinity(v.Handle, WDA_EXCLUDEFROMCAPTURE);
                }
            }
            finally { Osd.LeaveDpi(ctx); }

            // A new veil lands on top of every topmost window, the pill included.
            if (created) Osd.Raise();
        }

        /// <summary>
        /// Close the veils of every monitor not listed - the ones that have
        /// gone - and give back the gamma ramps of those, too: a display that
        /// unplugged, or started answering DDC/CI and so is no longer dimmed
        /// in software at all, must not be left with its output scaled.
        /// </summary>
        public static void Keep(ICollection<string> paths)
        {
            HashSet<string> keep = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string p in new List<string>(_veils.Keys))
                if (!keep.Contains(p)) Close(p);
            Gamma.RestoreExcept(paths);
        }

        /// <summary>
        /// Close every veil and restore every gamma ramp. Safe to call twice:
        /// the veils are gone the second time, and a ramp already confirmed
        /// restored is no longer Gamma's to restore - which is what lets exit
        /// paths call it without coordinating with each other. A restore
        /// Gamma could not confirm stays its business, and a later call here,
        /// or any Set or Keep, is what retries it.
        /// </summary>
        public static void Clear()
        {
            foreach (string p in new List<string>(_veils.Keys)) Close(p);
            Gamma.RestoreAll();
        }

        static void Close(string path)
        {
            Veil v;
            if (!_veils.TryGetValue(path, out v)) return;
            _veils.Remove(path);
            try { v.Close(); v.Dispose(); }
            catch { }
        }

        /// <summary>The \\.\DISPLAYn name of a monitor handle - what a gamma
        /// ramp is set on. Null when the handle has gone stale.</summary>
        static string DeviceOf(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero) return null;
            MONITORINFOEX mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            return GetMonitorInfo(monitor, ref mi) ? mi.szDevice : null;
        }

        /// <summary>The monitor's full area, taskbar included, in device pixels.</summary>
        static bool Area(IntPtr monitor, out Rectangle r)
        {
            r = Rectangle.Empty;
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi)) return false;
            r = Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
            return true;
        }

        /// <summary>
        /// Straight through SetWindowPos rather than Form.Bounds: WinForms caps
        /// a form at the largest size a window can be dragged to, measured at
        /// the app's own 96-DPI scale, which can come out smaller than a scaled
        /// monitor's real pixels. The z-order is left alone, so an update never
        /// lifts the veil over the pill.
        /// </summary>
        static void Place(Veil v, Rectangle r)
        {
            SetWindowPos(v.Handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
