// Hid.cs – USB/Bluetooth HID access straight through Windows (hid.dll + setupapi), like hidapi.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BatteryWidget
{
    public class HidInfo
    {
        public string Path = "", Product = "", Manufacturer = "";
        public int Vid, Pid, UsagePage, Usage, Interface = -1, InLen, OutLen, FeatLen;
        public string VidPid => $"{Vid:x4}:{Pid:x4}";
        public override string ToString() => $"{VidPid} {UsagePage:x4}/{Usage:x4} if{Interface} {Product}";
    }

    public interface IHidDevice : IDisposable
    {
        HidInfo Info { get; }
        bool Write(byte[] data);                       // output report (first byte = report id)
        byte[] Read(int timeoutMs);                    // next input report, or null on timeout
        bool SendFeature(byte[] data);                 // feature report (first byte = report id)
        byte[] GetFeature(byte reportId, int length);  // incl. the report id at [0], or null
        byte[] GetInputReport(byte reportId, int length);
        /// <summary>Standard HID battery (Battery Strength / Battery System usages): percent or null.</summary>
        int? StandardBattery(out bool charging);
        bool HasStandardBattery { get; }
    }

    public abstract class HidBackend
    {
        public static HidBackend Current = new WinHid();
        public abstract List<HidInfo> Enumerate();
        public abstract IHidDevice Open(HidInfo info);
        public List<HidInfo> Enumerate(int vid)
        {
            var all = Enumerate();
            return vid == 0 ? all : all.FindAll(i => i.Vid == vid);
        }
    }

    /// <summary>The real thing: hid.dll through SetupDi + CreateFile, overlapped reads with time-outs.</summary>
    public class WinHid : HidBackend
    {
        List<HidInfo> _cache;
        DateTime _cacheAt = DateTime.MinValue;
        readonly object _lock = new object();

        public override List<HidInfo> Enumerate()
        {
            lock (_lock)
            {
                if (_cache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < 2) return new List<HidInfo>(_cache);
                var list = new List<HidInfo>();
                if (!Native.OnWindows) return list;
                Guid g; Native.HidD_GetHidGuid(out g);
                IntPtr set = Native.SetupDiGetClassDevs(ref g, null, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
                if (set == Native.INVALID_HANDLE) return list;
                try
                {
                    for (int i = 0; ; i++)
                    {
                        var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                        if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                        string path = DetailPath(set, ref d);
                        if (path == null) continue;
                        var info = Describe(path);
                        if (info != null) list.Add(info);
                    }
                }
                finally { Native.SetupDiDestroyDeviceInfoList(set); }
                _cache = list; _cacheAt = DateTime.UtcNow;
                return new List<HidInfo>(list);
            }
        }

        static string DetailPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d)
        {
            int need;
            Native.SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out need, IntPtr.Zero);
            if (need <= 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(need);
            try
            {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);     // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, out need, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(buf + 4);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static HidInfo Describe(string path)
        {
            IntPtr h = Native.CreateFile(path, 0, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return null;
            try
            {
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (!Native.HidD_GetAttributes(h, ref a)) return null;
                var info = new HidInfo { Path = path, Vid = a.VendorID, Pid = a.ProductID };
                var sb = new byte[512];
                if (Native.HidD_GetProductString(h, sb, sb.Length)) info.Product = Utf16(sb);
                sb = new byte[512];
                if (Native.HidD_GetManufacturerString(h, sb, sb.Length)) info.Manufacturer = Utf16(sb);
                IntPtr pp;
                if (Native.HidD_GetPreparsedData(h, out pp))
                {
                    var caps = new HIDP_CAPS();
                    if (Native.HidP_GetCaps(pp, ref caps) == Native.HIDP_STATUS_SUCCESS)
                    {
                        info.UsagePage = caps.UsagePage; info.Usage = caps.Usage;
                        info.InLen = caps.InputReportByteLength; info.OutLen = caps.OutputReportByteLength;
                        info.FeatLen = caps.FeatureReportByteLength;
                    }
                    Native.HidD_FreePreparsedData(pp);
                }
                int mi = path.ToLowerInvariant().IndexOf("&mi_", StringComparison.Ordinal);
                if (mi >= 0 && mi + 6 <= path.Length)
                {
                    int n;
                    if (int.TryParse(path.Substring(mi + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out n)) info.Interface = n;
                }
                return info;
            }
            finally { Native.CloseHandle(h); }
        }

        static string Utf16(byte[] b)
        {
            var s = Encoding.Unicode.GetString(b);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public override IHidDevice Open(HidInfo info)
        {
            if (!Native.OnWindows) return null;
            IntPtr h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero,
                Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            bool rw = h != Native.INVALID_HANDLE;
            if (!rw)   // keyboards and mice can't be opened for reading – feature reports still work without access
                h = Native.CreateFile(info.Path, 0, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == Native.INVALID_HANDLE) return null;
            return new WinHidDevice(info, h, rw);
        }
    }

    public class WinHidDevice : IHidDevice
    {
        public HidInfo Info { get; }
        IntPtr _h, _event, _ov, _buf;
        readonly bool _rw;
        IntPtr _pp;
        List<HIDP_VALUE_CAPS> _battery;

        public WinHidDevice(HidInfo info, IntPtr h, bool rw)
        {
            Info = info; _h = h; _rw = rw;
            _event = Native.CreateEvent(IntPtr.Zero, true, false, null);
            int ovSize = IntPtr.Size == 8 ? 32 : 20;
            _ov = Marshal.AllocHGlobal(ovSize);
            _buf = Marshal.AllocHGlobal(Math.Max(64, Math.Max(info.InLen, info.OutLen)) + 8);
        }

        void ResetOverlapped()
        {
            int ovSize = IntPtr.Size == 8 ? 32 : 20;
            for (int i = 0; i < ovSize; i++) Marshal.WriteByte(_ov, i, 0);
            Marshal.WriteIntPtr(_ov, IntPtr.Size * 2 + 8, _event);   // OVERLAPPED.hEvent
        }

        static byte[] Fit(byte[] data, int len)
        {
            if (len <= 0) len = data.Length;
            var b = new byte[len];
            Array.Copy(data, b, Math.Min(len, data.Length));
            return b;
        }

        public bool Write(byte[] data)
        {
            if (!_rw || Info.OutLen <= 0 || data.Length > Info.OutLen) return false;   // like hidapi: never truncate
            var b = Fit(data, Info.OutLen);
            Marshal.Copy(b, 0, _buf, b.Length);
            ResetOverlapped();
            int n;
            if (!Native.WriteFile(_h, _buf, b.Length, out n, _ov))
            {
                if (Marshal.GetLastWin32Error() != 997) return false;          // ERROR_IO_PENDING
                if (Native.WaitForSingleObject(_event, 1000) != 0) { Native.CancelIoEx(_h, _ov); Native.GetOverlappedResult(_h, _ov, out n, true); return false; }
                if (!Native.GetOverlappedResult(_h, _ov, out n, false)) return false;
            }
            return true;
        }

        public byte[] Read(int timeoutMs)
        {
            if (!_rw || Info.InLen <= 0) { Thread.Sleep(Math.Min(timeoutMs, 50)); return null; }
            ResetOverlapped();
            int n;
            if (!Native.ReadFile(_h, _buf, Info.InLen, out n, _ov))
            {
                if (Marshal.GetLastWin32Error() != 997) return null;
                if (Native.WaitForSingleObject(_event, (uint)Math.Max(1, timeoutMs)) != 0)
                {
                    Native.CancelIoEx(_h, _ov);
                    Native.GetOverlappedResult(_h, _ov, out n, true);
                    return null;
                }
                if (!Native.GetOverlappedResult(_h, _ov, out n, false)) return null;
            }
            if (n <= 0) return null;
            var r = new byte[n];
            Marshal.Copy(_buf, r, 0, n);
            if (r[0] == 0 && n > 1) { var t = new byte[n - 1]; Array.Copy(r, 1, t, 0, n - 1); r = t; }   // like hidapi: no report ids -> drop the 0
            return r;
        }

        public bool SendFeature(byte[] data)
        {
            var b = Fit(data, Info.FeatLen > 0 ? Info.FeatLen : data.Length);   // Windows wants exactly the report length
            return Native.HidD_SetFeature(_h, b, b.Length);
        }

        public byte[] GetFeature(byte reportId, int length)
        {
            int len = Info.FeatLen > 0 ? Info.FeatLen : length;
            var b = new byte[Math.Max(len, 1)];
            b[0] = reportId;
            return Native.HidD_GetFeature(_h, b, b.Length) ? b : null;
        }

        public byte[] GetInputReport(byte reportId, int length)
        {
            int len = Info.InLen > 0 ? Info.InLen : length;
            var b = new byte[Math.Max(len, 1)];
            b[0] = reportId;
            return Native.HidD_GetInputReport(_h, b, b.Length) ? b : null;
        }

        // --- standard HID battery through the preparsed data -------------------------------
        static bool IsBatteryUsage(int page, int usage) =>
            (page == 0x06 && usage == 0x20) || (page == 0x85 && (usage == 0x64 || usage == 0x65 || usage == 0x66));

        List<HIDP_VALUE_CAPS> BatteryCaps(out IntPtr pp)
        {
            pp = _pp;
            if (_battery != null) return _battery;
            _battery = new List<HIDP_VALUE_CAPS>();
            if (!Native.HidD_GetPreparsedData(_h, out _pp)) return _battery;
            pp = _pp;
            var caps = new HIDP_CAPS();
            if (Native.HidP_GetCaps(_pp, ref caps) != Native.HIDP_STATUS_SUCCESS) return _battery;
            foreach (var kv in new[] { new KeyValuePair<int, int>(Native.HidP_Input, caps.NumberInputValueCaps),
                                       new KeyValuePair<int, int>(Native.HidP_Feature, caps.NumberFeatureValueCaps) })
            {
                if (kv.Value <= 0) continue;
                var arr = new HIDP_VALUE_CAPS[kv.Value];
                ushort n = (ushort)kv.Value;
                if (Native.HidP_GetValueCaps(kv.Key, arr, ref n, _pp) != Native.HIDP_STATUS_SUCCESS) continue;
                for (int i = 0; i < n; i++)
                {
                    var c = arr[i];
                    bool hit = false;
                    for (int u = c.UsageMin; u <= (c.IsRange != 0 ? c.UsageMax : c.UsageMin); u++)
                        if (IsBatteryUsage(c.UsagePage, u)) hit = true;
                    if (hit) { c.IsAlias = (byte)kv.Key; _battery.Add(c); }     // IsAlias slot reused: report type
                }
            }
            return _battery;
        }

        public bool HasStandardBattery { get { IntPtr pp; return BatteryCaps(out pp).Count > 0; } }

        public int? StandardBattery(out bool charging)
        {
            charging = false;
            IntPtr pp;
            var caps = BatteryCaps(out pp);
            foreach (var c in caps)
            {
                int type = c.IsAlias;
                byte[] rep = type == Native.HidP_Feature ? GetFeature(c.ReportID, Info.FeatLen) : GetInputReport(c.ReportID, Info.InLen);
                if (rep == null) continue;
                for (int u = c.UsageMin; u <= (c.IsRange != 0 ? c.UsageMax : c.UsageMin); u++)
                {
                    if (!IsBatteryUsage(c.UsagePage, u)) continue;
                    uint v;
                    if (Native.HidP_GetUsageValue(type, c.UsagePage, c.LinkCollection, (ushort)u, out v, pp, rep, (uint)rep.Length) != Native.HIDP_STATUS_SUCCESS)
                        continue;
                    int lo = c.LogicalMin, hi = c.LogicalMax;
                    if (hi <= lo) { lo = 0; hi = c.UsagePage == 0x85 ? 100 : 255; }
                    if (v < lo || v > hi) continue;
                    return (int)Math.Round((v - lo) * 100.0 / (hi - lo));
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (_pp != IntPtr.Zero) { Native.HidD_FreePreparsedData(_pp); _pp = IntPtr.Zero; }
            if (_h != IntPtr.Zero && _h != Native.INVALID_HANDLE) { Native.CancelIoEx(_h, IntPtr.Zero); Native.CloseHandle(_h); }
            _h = IntPtr.Zero;
            if (_event != IntPtr.Zero) { Native.CloseHandle(_event); _event = IntPtr.Zero; }
            if (_ov != IntPtr.Zero) { Marshal.FreeHGlobal(_ov); _ov = IntPtr.Zero; }
            if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; }
        }
    }
}
