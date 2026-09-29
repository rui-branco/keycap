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
    /// that would otherwise ignore them.
    ///
    /// UI thread only. There is one veil per monitor, keyed by the monitor's
    /// interface path rather than its \\.\DISPLAYn name, which Windows hands
    /// out afresh whenever displays come and go.
    /// </summary>
    public static class Dimmer
    {
        // The darkest a veil goes. Even at 0% it lets 15% of the picture
        // through: a screen dimmed to pure black would leave no way to find
        // the pointer, the Settings window or the key that brings it back.
        const float MaxDark = 0.85f;

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

        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
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
        /// of its own brightness. 100 takes the veil away. The window is made
        /// and moved inside Osd's DPI borrow, so like the pill it is per-monitor
        /// aware and covers the monitor's real pixels on a scaled display.
        /// </summary>
        public static void Set(string path, IntPtr monitor, int percent)
        {
            if (path == null) return;
            if (percent >= 100) { Close(path); return; }

            float alpha = (100 - Math.Max(0, percent)) / 100f * MaxDark;
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

        /// <summary>Close the veils of every monitor not listed - the ones that have gone.</summary>
        public static void Keep(ICollection<string> paths)
        {
            HashSet<string> keep = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            foreach (string p in new List<string>(_veils.Keys))
                if (!keep.Contains(p)) Close(p);
        }

        /// <summary>Close every veil.</summary>
        public static void Clear()
        {
            foreach (string p in new List<string>(_veils.Keys)) Close(p);
        }

        static void Close(string path)
        {
            Veil v;
            if (!_veils.TryGetValue(path, out v)) return;
            _veils.Remove(path);
            try { v.Close(); v.Dispose(); }
            catch { }
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
