// Native.cs – the Windows functions the app calls directly (no extra libraries needed).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryWidget
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(string g, uint p) { fmtid = new Guid(g); pid = p; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    // HIDP_VALUE_CAPS (72 bytes, no pointers – same on 32 and 64 bit). Offsets from hidpi.h.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(3)] public byte IsAlias;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin;   // == Usage when IsRange == 0
        [FieldOffset(58)] public ushort UsageMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile,
        ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    public static class Native
    {
        // ---- user32 ------------------------------------------------------------
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFOEX mi);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong32(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong32(IntPtr h, int idx, int v);
        public static long GetWindowLong(IntPtr h, int idx) => IntPtr.Size == 8 ? GetWindowLongPtr64(h, idx).ToInt64() : GetWindowLong32(h, idx);
        public static void SetWindowLong(IntPtr h, int idx, long v) { if (IntPtr.Size == 8) SetWindowLongPtr64(h, idx, new IntPtr(v)); else SetWindowLong32(h, idx, (int)v); }
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT pt, ref SIZE sz, IntPtr hdcSrc,
            ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        // ---- gdi32 --------------------------------------------------------------
        [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);

        // ---- shell32 -------------------------------------------------------------
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("shell32.dll")] public static extern bool IsUserAnAdmin();

        // ---- kernel32 ------------------------------------------------------------
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(IntPtr h, byte[] buf, int n, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(IntPtr h, IntPtr buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(IntPtr h, IntPtr buf, int n, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(IntPtr h, IntPtr ov, out int n, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(IntPtr h, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateEvent(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);

        // ---- hid -------------------------------------------------------------------
        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern bool HidD_GetProductString(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetManufacturerString(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr pp);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value,
            IntPtr pp, byte[] report, uint len);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetInputReport(IntPtr h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetOutputReport(IntPtr h, byte[] buf, int len);

        // ---- setupapi ---------------------------------------------------------------
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")] public static extern IntPtr SetupDiGetClassDevsAll(IntPtr g, string enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll")] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d,
            IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] public static extern bool SetupDiEnumDeviceInfo(IntPtr set, int idx, ref SP_DEVINFO_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA d,
            StringBuilder sb, int size, out int required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA d,
            ref DEVPROPKEY key, out uint type, byte[] buf, int size, out int required, int flags);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        // ---- powrprof (screen-off timer) ------------------------------------------------
        [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerReadDCValueIndex(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerWriteACValueIndex(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerWriteDCValueIndex(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr root, IntPtr scheme);

        // ---- pdh (performance counters by their English names) ----------------------------
        [StructLayout(LayoutKind.Sequential)] public struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double doubleValue; }
        [StructLayout(LayoutKind.Sequential)] public struct PDH_FMT_COUNTERVALUE_ITEM { public IntPtr szName; public PDH_FMT_COUNTERVALUE FmtValue; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQuery(string src, IntPtr user, out IntPtr q);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounter(IntPtr q, string path, IntPtr user, out IntPtr c);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr q);
        [DllImport("pdh.dll")] public static extern uint PdhGetFormattedCounterValue(IntPtr c, uint fmt, IntPtr type, out PDH_FMT_COUNTERVALUE v);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArray(IntPtr c, uint fmt, ref int size, out int count, IntPtr buf);

        // ---- constants -------------------------------------------------------------
        public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
        public const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000L, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80,
            WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_BOTTOM = new IntPtr(1), HWND_NOTOPMOST = new IntPtr(-2);
        public const uint LWA_COLORKEY = 1, LWA_ALPHA = 2, ULW_ALPHA = 2;
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_RW = 3, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);
        public const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10, DIGCF_ALLCLASSES = 4;
        public const int HIDP_STATUS_SUCCESS = 0x00110000, HidP_Input = 0, HidP_Output = 1, HidP_Feature = 2;
        public static bool IsAdmin { get { try { return IsUserAnAdmin(); } catch { return false; } } }
        public static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
    }
}
