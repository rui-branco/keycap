using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Keycap
{
    public class BatteryInfo
    {
        public string Name;
        public int Percent;
        public ushort Vid;
        public ushort Pid;
        public string Usage;
        public byte ReportId;
    }

    /// <summary>
    /// Reads charge level straight from the HID stack.
    ///
    /// Windows exposes no battery property for the Magic Keyboard through PnP, and
    /// the keyboard collection carries no standard Battery Strength usage. Apple
    /// puts AbsoluteStateOfCharge (usage page 0x85, usage 0x65) in an input report
    /// on its vendor collection instead, so this walks every HID interface and
    /// reads whichever battery usage it finds.
    /// </summary>
    public static class Hid
    {
        const int DIGCF_PRESENT = 0x02;
        const int DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1;
        const uint FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        const int HidP_Input = 0;
        const int HidP_Feature = 2;
        const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct GUID_S { public uint a; public ushort b, c; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] d; }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize; public GUID_S InterfaceClassGuid; public int Flags; public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage;
            public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID, IsAlias2;
            public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
            public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
            public ushort BitSize, ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
            public uint UnitsExp, Units;
            public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
            public ushort U1, U2, U3, U4, U5, U6, U7, U8;   // Range / NotRange union
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(ref GUID_S g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_GetInputReport(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage,
            out uint value, IntPtr pp, byte[] report, int reportLen);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevsW(ref GUID_S g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr info, IntPtr devInfo, ref GUID_S g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr info, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, int detailSize, ref int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr sec,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static readonly IntPtr INVALID = new IntPtr(-1);

        static string UsageName(ushort page, ushort usage)
        {
            if (page == 0x85 && usage == 0x65) return "AbsoluteStateOfCharge";
            if (page == 0x85 && usage == 0x66) return "RemainingCapacity";
            if (page == 0x06 && usage == 0x20) return "BatteryStrength";
            return null;
        }

        static List<string> InterfacePaths()
        {
            List<string> paths = new List<string>();
            GUID_S guid = new GUID_S();
            guid.d = new byte[8];
            HidD_GetHidGuid(ref guid);

            IntPtr info = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (info == INVALID) return paths;

            try
            {
                int index = 0;
                while (true)
                {
                    SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref guid, index, ref data)) break;
                    index++;

                    int need = 0;
                    SetupDiGetDeviceInterfaceDetailW(info, ref data, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                    if (need <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetailW(info, ref data, buf, need, ref need, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(info); }
            return paths;
        }

        static IntPtr Open(string path)
        {
            IntPtr h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID)
                h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            return h;
        }

        static string Product(IntPtr h)
        {
            byte[] buf = new byte[512];
            if (HidD_GetProductString(h, buf, buf.Length))
                return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
            return "";
        }

        static BatteryInfo Probe(IntPtr h, IntPtr pp, HIDP_CAPS caps, int kind)
        {
            ushort n = kind == HidP_Feature ? caps.NumberFeatureValueCaps : caps.NumberInputValueCaps;
            int size = kind == HidP_Feature ? caps.FeatureReportByteLength : caps.InputReportByteLength;
            if (n == 0 || size < 2) return null;

            HIDP_VALUE_CAPS[] arr = new HIDP_VALUE_CAPS[n];
            ushort count = n;
            if (HidP_GetValueCaps(kind, arr, ref count, pp) != HIDP_STATUS_SUCCESS) return null;

            for (int i = 0; i < count; i++)
            {
                HIDP_VALUE_CAPS c = arr[i];
                ushort usage = c.IsRange != 0 ? c.U1 : c.U1;   // UsageMin and Usage share the slot
                string label = UsageName(c.UsagePage, usage);
                if (label == null) continue;

                byte[] report = new byte[size];
                report[0] = c.ReportID;
                bool ok = kind == HidP_Feature
                    ? HidD_GetFeature(h, report, size)
                    : HidD_GetInputReport(h, report, size);
                if (!ok) continue;

                uint value;
                if (HidP_GetUsageValue(kind, c.UsagePage, 0, usage, out value, pp, report, size)
                    != HIDP_STATUS_SUCCESS) continue;

                // AbsoluteStateOfCharge is already a percentage even when the
                // logical maximum is 255; only rescale when it cannot be one.
                int pct = (int)value;
                if (pct > 100 && c.LogicalMax > 0)
                    pct = (int)Math.Round(value * 100.0 / c.LogicalMax);
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;

                BatteryInfo b = new BatteryInfo();
                b.Percent = pct;
                b.Usage = label;
                b.ReportId = c.ReportID;
                return b;
            }
            return null;
        }

        public static List<BatteryInfo> Scan()
        {
            List<BatteryInfo> found = new List<BatteryInfo>();
            List<string> seen = new List<string>();

            foreach (string path in InterfacePaths())
            {
                IntPtr h = Open(path);
                if (h == INVALID) continue;
                IntPtr pp = IntPtr.Zero;
                try
                {
                    HIDD_ATTRIBUTES attrs = new HIDD_ATTRIBUTES();
                    attrs.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                    if (!HidD_GetAttributes(h, ref attrs)) continue;
                    if (!HidD_GetPreparsedData(h, out pp)) continue;

                    HIDP_CAPS caps = new HIDP_CAPS();
                    if (HidP_GetCaps(pp, ref caps) != HIDP_STATUS_SUCCESS) continue;

                    BatteryInfo b = Probe(h, pp, caps, HidP_Input);
                    if (b == null) b = Probe(h, pp, caps, HidP_Feature);
                    if (b == null) continue;

                    b.Vid = attrs.VendorID;
                    b.Pid = attrs.ProductID;
                    b.Name = Product(h);
                    if (string.IsNullOrEmpty(b.Name)) b.Name = "HID device";

                    string key = b.Vid.ToString("X4") + b.Pid.ToString("X4") + b.Usage;
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    found.Add(b);
                }
                catch { }
                finally
                {
                    if (pp != IntPtr.Zero) HidD_FreePreparsedData(pp);
                    CloseHandle(h);
                }
            }
            return found;
        }
    }
}
