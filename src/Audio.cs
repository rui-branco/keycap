using System;
using System.Runtime.InteropServices;

namespace Keycap
{
    /// <summary>
    /// Microphone mute, through Core Audio. This targets the *default
    /// communications capture device* - the one a call app actually picks up -
    /// rather than guessing from device names the way the script had to.
    /// </summary>
    public static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                         [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr n);
            int UnregisterControlChangeNotify(IntPtr n);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float level, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float level);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
            int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float level);
            int GetChannelVolumeLevelScalar(uint ch, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        const int eCapture = 1;
        const int eCommunications = 2;
        const int CLSCTX_ALL = 23;

        static IAudioEndpointVolume Endpoint()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(eCapture, eCommunications, out dev) != 0 || dev == null)
                return null;

            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        /// <summary>null when there is no capture device at all.</summary>
        public static bool? IsMuted()
        {
            try
            {
                IAudioEndpointVolume v = Endpoint();
                if (v == null) return null;
                bool muted;
                if (v.GetMute(out muted) != 0) return null;
                return muted;
            }
            catch { return null; }
        }

        public static void ToggleMic()
        {
            try
            {
                IAudioEndpointVolume v = Endpoint();
                if (v == null) { Osd.Show("No microphone found", Osd.Sym.MicOff, Theme.Warn); return; }

                bool muted;
                if (v.GetMute(out muted) != 0)
                {
                    Osd.Show("Microphone unavailable", Osd.Sym.MicOff, Theme.Warn);
                    return;
                }

                Guid ctx = Guid.Empty;
                if (v.SetMute(!muted, ref ctx) != 0)
                {
                    Osd.Show("Microphone unavailable", Osd.Sym.MicOff, Theme.Warn);
                    return;
                }

                if (!muted) Osd.Show("Microphone muted", Osd.Sym.MicOff, Theme.Bad, true);
                else Osd.Show("Microphone on", Osd.Sym.Mic, Theme.Good);
            }
            catch
            {
                Osd.Show("Microphone unavailable", Osd.Sym.MicOff, Theme.Warn);
            }
        }
    }
}
