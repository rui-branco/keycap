using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Keycap
{
    /// <summary>
    /// Brightness at the display's output, for a monitor that does not answer
    /// DDC/CI: instead of laying a veil over its picture (see Dimmer), the
    /// display's gamma ramp is scaled down, so everything the display shows
    /// is dimmed - windows made later, topmost dialogs, and the hardware mouse
    /// pointer, which is composited after every window and so stays bright
    /// under a veil. Keycap's own on-screen indicator dims too; that is the
    /// point of dimming at the output rather than in the framebuffer.
    ///
    /// Microsoft discourages SetDeviceGammaRamp, and it can fail silently: a
    /// driver or a display using HDR / Advanced Colour may return TRUE while
    /// refusing to apply the ramp. So a ramp is never trusted from its return
    /// value alone - it is read back and checked, and a display whose ramp
    /// did not take is remembered as refusing, so it goes straight to the veil
    /// rather than being asked again on every key press.
    ///
    /// The ramp also cannot carry the darkest levels: Windows refuses a ramp
    /// whose entries stray too far from the identity ramp, in practice below
    /// about half output. So the ramp DimMath.GammaRampScale sets stops at
    /// that floor; below it Dimmer lays HybridVeilAlpha of veil over the
    /// floored output, and the two together land on the same total darkness
    /// as the veil alone used to. A read-back, too, may lag the ramp it
    /// reports - some drivers settle over a few hundred milliseconds - so
    /// verification waits a little (WaitMatch) rather than judging on one
    /// instant read.
    ///
    /// The ramp that is scaled is the one the display already had, captured
    /// before Keycap first touches it, so a calibrated (ICC) display keeps its
    /// calibration - only how much of it gets out is reduced. The original is
    /// put back when the level reaches 100%, when full dimming or software
    /// dimming is switched off, when the display goes away or starts answering
    /// DDC/CI, and when Keycap exits: every one of those goes through Dimmer,
    /// which calls Restore, RestoreExcept or RestoreAll here.
    ///
    /// UI thread only, the same contract as Dimmer: Dimmer is the only caller,
    /// and Brightness only ever reaches Dimmer on the UI thread.
    /// </summary>
    public static class Gamma
    {
        /// <summary>One display's ramp: 256 entries per channel, each 0..65535.</summary>
        [StructLayout(LayoutKind.Sequential)]
        struct Ramp
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr dc, ref Ramp ramp);
        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, ref Ramp ramp);

        /// <summary>
        /// What is known of one software-dimmed display, by its monitor
        /// interface path - the key Dimmer uses too, rather than the
        /// \\.\DISPLAYn name Windows hands out afresh whenever displays come
        /// and go. The device name is kept because a ramp is set per device,
        /// and the original ramp because that, and not a straight line, is
        /// what gets scaled and what gets put back.
        /// </summary>
        class Entry
        {
            public string Device;      // "\\.\DISPLAYn" the ramp belongs to
            public Ramp Original;      // as captured, before Keycap touched it
            public bool Applied;       // a scaled ramp is currently on the display
        }

        static readonly Dictionary<string, Entry> _known =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Displays whose ramp read back unchanged after being set: path ->
        // the device name that refused. Such a display is left to the veil
        // rather than being asked again on every key press. The refusal is
        // forgotten when the device behind the path changes (a different
        // display may behave differently), when everything is restored, and
        // at the end of every scan - see RestoreExcept - so a display whose
        // circumstances change, HDR switched off say, is tried again.
        static readonly Dictionary<string, string> _refused =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Where the read-back is checked: spread across the ramp, so a ramp
        // that took only at the top or the bottom still shows. The top entry
        // matters most - it is what the brightest picture is scaled by.
        static readonly int[] Samples = new int[] { 64, 128, 192, 255 };

        // How far a read-back entry may sit from what it is compared against
        // and still count as that ramp. Drivers round and quantise, so an
        // exact match is too much to ask; a 10% brightness step moves the top
        // entry by thousands, so this cannot mistake one level for another.
        const int Tolerance = 600;

        static Ramp NewRamp()
        {
            Ramp r = new Ramp();
            r.Red = new ushort[256];
            r.Green = new ushort[256];
            r.Blue = new ushort[256];
            return r;
        }

        /// <summary>
        /// The display's own ramp with each entry scaled for
        /// <paramref name="percent"/>: by GammaRampScale, not GammaScale, so
        /// the ramp never asks Windows for more darkness than it will accept.
        /// Below the floor the shortfall is the veil's business, in Dimmer.
        /// </summary>
        static Ramp Scaled(Ramp original, int percent)
        {
            float scale = DimMath.GammaRampScale(percent);
            Ramp r = NewRamp();
            for (int i = 0; i < 256; i++)
            {
                r.Red[i] = DimMath.ScaleEntry(original.Red[i], scale);
                r.Green[i] = DimMath.ScaleEntry(original.Green[i], scale);
                r.Blue[i] = DimMath.ScaleEntry(original.Blue[i], scale);
            }
            return r;
        }

        /// <summary>Whether <paramref name="ramp"/> reads as
        /// <paramref name="reference"/> at the sampled points, in every
        /// channel: a display that honoured only the red ramp would otherwise
        /// count as dimmed while showing a colour-cast picture.</summary>
        static bool Matches(Ramp ramp, Ramp reference)
        {
            foreach (int i in Samples)
            {
                if (Math.Abs((int)ramp.Red[i] - (int)reference.Red[i]) > Tolerance) return false;
                if (Math.Abs((int)ramp.Green[i] - (int)reference.Green[i]) > Tolerance) return false;
                if (Math.Abs((int)ramp.Blue[i] - (int)reference.Blue[i]) > Tolerance) return false;
            }
            return true;
        }

        /// <summary>
        /// Whether the display's ramp reads as <paramref name="expected"/>,
        /// given a little time to become it: some drivers settle a ramp over
        /// a few hundred milliseconds, and one instant read-back can catch
        /// the ramp mid-transition and misjudge a set that in fact took (or
        /// a restore that in fact happened). The ramp is read straight away
        /// and then every 50 ms until it matches or the timeout runs out;
        /// the wait only ever happens when the first read does not already
        /// match, so a driver that answers at once pays nothing. This runs
        /// on the UI thread, like everything here, and only on apply and
        /// restore paths - a handful of calls per key press at the worst.
        /// </summary>
        static bool WaitMatch(IntPtr dc, Ramp expected, int timeoutMs)
        {
            int waited = 0;
            for (;;)
            {
                Ramp actual = NewRamp();
                if (GetDeviceGammaRamp(dc, ref actual) && Matches(actual, expected)) return true;
                if (waited >= timeoutMs) return false;
                Thread.Sleep(50);
                waited += 50;
            }
        }

        /// <summary>
        /// Dim the display at <paramref name="path"/> - \\.\DISPLAYn
        /// <paramref name="device"/> - to <paramref name="percent"/> of its own
        /// brightness, at the output. True when the display is dimmed this way
        /// (or needs no dimming, at 100%); false when it cannot be. A false
        /// does not quite promise the display is as it was: this method tries
        /// to put back anything of its own before returning, but a restore it
        /// cannot verify leaves the ramp on the display and the entry saying
        /// so. That is what IsApplied is for - the caller veils only when it
        /// says no ramp of ours remains, so the veil never doubles up on top
        /// of one.
        /// </summary>
        public static bool Apply(string path, string device, int percent)
        {
            if (path == null || string.IsNullOrEmpty(device)) return false;
            if (percent >= 100) { Restore(path); return true; }

            // A display that has already refused goes straight to the veil.
            string refusedFor;
            if (_refused.TryGetValue(path, out refusedFor))
            {
                if (string.Equals(refusedFor, device, StringComparison.OrdinalIgnoreCase))
                {
                    // A refusal is only recorded with nothing of ours left on
                    // the display, but if an earlier restore failed there may
                    // still be: try to put it back. Whether that worked is
                    // not decided here - IsApplied answers it afterwards,
                    // and the caller veils only if it says nothing remains.
                    Entry known;
                    if (_known.TryGetValue(path, out known) && known.Applied
                        && string.Equals(known.Device, device, StringComparison.OrdinalIgnoreCase))
                        Restore(path);
                    return false;
                }
                _refused.Remove(path);          // a different display sits here now
            }

            Entry e;
            if (_known.TryGetValue(path, out e)
                && !string.Equals(e.Device, device, StringComparison.OrdinalIgnoreCase))
            {
                // The \\.\DISPLAYn name behind this path now belongs to another
                // display. Windows resets a display's ramp when displays
                // change, so there is nothing to restore to the old name - the
                // new display is captured afresh below, as if never seen.
                _known.Remove(path);
                e = null;
            }

            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) { Undo(dc, e); return false; }

                if (e == null)
                {
                    // First touch: capture the ramp the display already has -
                    // the calibrated one, if it is calibrated - to scale and
                    // later to put back. Without it there is nothing honest to
                    // scale, so a display whose ramp cannot even be read is
                    // left to the veil.
                    Ramp original = NewRamp();
                    if (!GetDeviceGammaRamp(dc, ref original)) return false;
                    e = new Entry();
                    e.Device = device;
                    e.Original = original;
                    e.Applied = false;
                    _known[path] = e;
                }

                Ramp scaled = Scaled(e.Original, percent);
                if (!SetDeviceGammaRamp(dc, ref scaled)) { Undo(dc, e); return false; }

                // From here the scaled ramp may be on the display even though
                // this method goes on to return false - if, say, the read-back
                // below cannot be made. Applied says so, so Undo (and, later,
                // Restore and the refused-display shortcut above) knows there
                // is something of ours to put back rather than assuming a
                // failed attempt left the display as it was. Only the
                // silent-refusal check clears it again: a ramp that reads
                // back as the original really is not ours.
                e.Applied = true;

                // SetDeviceGammaRamp can return TRUE while silently refusing -
                // on HDR / Advanced Colour displays, notably - so the ramp is
                // read back rather than believed, and given a second or so to
                // settle into what it will report (WaitMatch) before it is
                // judged. A display that reads back as its original ramp has
                // refused, and is remembered as such: it uses the veil without
                // being asked again, rather than flickering through a failed
                // attempt on every key press.
                if (WaitMatch(dc, scaled, 1000)) { e.Applied = true; return true; }
                Ramp actual = NewRamp();
                if (GetDeviceGammaRamp(dc, ref actual) && Matches(actual, e.Original))
                {
                    e.Applied = false; _refused[path] = device; return false;
                }

                // It reads back as neither what was set nor what was there -
                // the display is doing something of its own with ramps, so it
                // is put back as it was and left to the veil like a refusal.
                Undo(dc, e);
                _refused[path] = device;
                return false;
            }
            catch (Exception)
            {
                // A native call failing must never take the caller down with
                // it: the veil is always there to fall back on.
                Undo(dc, e);
                return false;
            }
            finally
            {
                if (dc != IntPtr.Zero) try { DeleteDC(dc); } catch { }
            }
        }

        /// <summary>
        /// Undo a failed Apply: if a scaled ramp of ours is on the display,
        /// put its original back, so returning false never leaves the display
        /// dimmed underneath the veil the caller is about to lay over it. The
        /// BOOL SetDeviceGammaRamp returns is not believed here any more than
        /// anywhere else - Applied is cleared only when the ramp reads back
        /// as the original. A restore that fails, or cannot be verified,
        /// leaves Applied set, so a later Restore still knows there is
        /// something of ours to put back and tries again.
        /// </summary>
        static void Undo(IntPtr dc, Entry e)
        {
            if (e == null || !e.Applied || dc == IntPtr.Zero) return;
            try
            {
                if (!SetDeviceGammaRamp(dc, ref e.Original)) return;
                // The read-back is given time to settle (WaitMatch) rather
                // than judged on one instant read: a restore still in
                // transit must not be called done, nor a done one undone.
                if (WaitMatch(dc, e.Original, 800)) e.Applied = false;
            }
            catch { }
        }

        /// <summary>
        /// Whether a scaled ramp of ours may still be on the display at
        /// <paramref name="path"/> - an entry is known and says applied. This
        /// is the answer a caller needs before veiling the display: a veil
        /// laid while it is true would dim on top of the ramp. A display
        /// Gamma has never touched, or has confirmed restored, says false.
        /// </summary>
        public static bool IsApplied(string path)
        {
            if (path == null) return false;
            Entry e;
            return _known.TryGetValue(path, out e) && e.Applied;
        }

        /// <summary>
        /// Give the display at <paramref name="path"/> its own ramp back. The
        /// entry is forgotten only once that is confirmed - the device opened,
        /// the ramp set, and a read-back that matches the original - because
        /// the BOOL SetDeviceGammaRamp returns can lie, and a transient
        /// failure (the display asleep, its DC unobtainable on the way out)
        /// must not drop the only record that a scaled ramp is still on it.
        /// Kept, the entry says Applied, and the next Set, Keep or Clear
        /// retries; that retrying cannot loop, since each is a fresh call
        /// from a scan, a key press or an exit, never a spin here.
        ///
        /// True means it is safe to treat the display as unrestored by
        /// Keycap: either nothing of ours was on it, or the restore was
        /// confirmed. False means a scaled ramp may still be on it, the
        /// entry kept for a later retry - which is why the caller that wants
        /// to veil next (Dimmer) believes this, not its own hopes. A display
        /// that was never dimmed this way is a no-op returning true, which is
        /// what makes restoring on every exit path safe to do twice, and a
        /// display whose ramp needs no putting back (nothing applied) is
        /// forgotten at once.
        ///
        /// Confirmation here is patient, because this is the exit path as
        /// much as anything: the original is set and given up to a couple of
        /// seconds to read back as itself (WaitMatch), then set once more
        /// and given another second, and only then does Restore give up and
        /// keep the entry. A confirmed restore before the process dies is
        /// worth those few seconds; a driver that settles at once still
        /// returns on the first read, so the patience costs nothing there.
        /// </summary>
        public static bool Restore(string path)
        {
            if (path == null) return true;
            Entry e;
            if (!_known.TryGetValue(path, out e)) return true;
            if (!e.Applied) { _known.Remove(path); return true; }

            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = CreateDC("DISPLAY", e.Device, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;            // kept: a later call retries
                if (SetDeviceGammaRamp(dc, ref e.Original) && WaitMatch(dc, e.Original, 2000))
                {
                    _known.Remove(path);
                    return true;
                }
                // Not confirmed: set it once more and wait again before
                // giving up - a ramp still settling from the first set may
                // take the second, and this is often the last chance before
                // the process goes.
                if (SetDeviceGammaRamp(dc, ref e.Original) && WaitMatch(dc, e.Original, 1000))
                {
                    _known.Remove(path);
                    return true;
                }
                return false; // not provably ours to forget yet
            }
            catch { return false; }          // kept, Applied and all, for the next attempt
            finally
            {
                if (dc != IntPtr.Zero) try { DeleteDC(dc); } catch { }
            }
        }

        /// <summary>
        /// Give every display Keycap has dimmed this way its own ramp back.
        /// Only what Restore confirms goes away: an entry whose restore could
        /// not be confirmed stays known and Applied, so a later call can
        /// retry rather than exiting having silently stranded a ramp, and a
        /// refusal is forgotten only for a display about which nothing is
        /// left known - one still Applied keeps its record whole.
        /// </summary>
        public static void RestoreAll()
        {
            foreach (string p in new List<string>(_known.Keys)) Restore(p);
            foreach (string p in new List<string>(_refused.Keys))
                if (!_known.ContainsKey(p)) _refused.Remove(p);
        }

        /// <summary>
        /// Restore every display not in <paramref name="keep"/> - the ones
        /// that have gone, or started answering DDC/CI and so are dimmed on
        /// the monitor itself now. Their refusals go with them: the path is
        /// free for whatever display turns up there next.
        ///
        /// A kept display's refusal goes too - dropped only, never restored
        /// here. Refusals are judged on the display as it was: a user who
        /// turns HDR off should have the ramp tried again, not be veiled for
        /// the rest of the process's life. Dimmer.Keep runs at the end of
        /// every scan, after that scan's Dimmer.Set calls, so the retry
        /// happens on the next scan or the next brightness change rather
        /// than this one - once per scan, never per key press. There is no
        /// double-dimming in it either way: the retry is an ordinary Apply,
        /// which restores anything of its own before it can fail, and
        /// Dimmer takes the veil away whenever a ramp of ours is on the
        /// display - on an Apply that succeeds, and on one that fails with
        /// the ramp still applied - keeping it only where no ramp remains.
        /// </summary>
        public static void RestoreExcept(ICollection<string> keep)
        {
            HashSet<string> kept = new HashSet<string>(keep ?? new string[0],
                                                        StringComparer.OrdinalIgnoreCase);
            foreach (string p in new List<string>(_known.Keys))
                if (!kept.Contains(p)) Restore(p);
            foreach (string p in new List<string>(_refused.Keys))
                _refused.Remove(p);
        }
    }
}
