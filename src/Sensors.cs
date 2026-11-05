// Sensors.cs – values for the system monitor: LibreHardwareMonitor (built in) + Windows' own counters.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace BatteryWidget
{
    public class Metric
    {
        public string Key, Name, Group, Kind, Unit = "", Source = "", Device = "";
        public double? Value, VMin, VMax;
        public Metric(string key, string name, string group, string kind, double? value, string unit, double? vmin = null, double? vmax = null,
                      string source = "", string device = "")
        { Key = key; Name = name; Group = group; Kind = kind; Value = value; Unit = unit; VMin = vmin; VMax = vmax; Source = source; Device = device; }
    }

    /// <summary>Loads the LibreHardwareMonitor DLLs that are packed inside the exe (or sit in a "lhm" folder next to it).</summary>
    public static class Embedded
    {
        static bool _hooked;
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string shortName = new AssemblyName(e.Name).Name;
                foreach (var dir in new[] { Path.Combine(Cfg.AppDir, "lhm"), Extracted() })
                {
                    if (dir == null) continue;
                    string p = Path.Combine(dir, shortName + ".dll");
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };
        }
        static string _dir;
        /// <summary>The sensor library unpacked next to the settings (once per version) – it runs from files, as it was built to.</summary>
        static string Extracted()
        {
            if (_dir != null) return _dir;
            var me = Assembly.GetExecutingAssembly();
            var names = me.GetManifestResourceNames().Where(n => n.StartsWith("lhm.")).ToList();
            if (names.Count == 0) return null;
            foreach (var root in new[] { Cfg.DataDir, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryWidget") })
            {
                try
                {
                    string dir = Path.Combine(root, "lhm-0.9.6");
                    Directory.CreateDirectory(dir);
                    foreach (var n in names)
                        using (var st = me.GetManifestResourceStream(n))
                        {
                            string p = Path.Combine(dir, n.Substring(4));
                            if (File.Exists(p) && new FileInfo(p).Length == st.Length) continue;
                            using (var f = File.Create(p)) st.CopyTo(f);
                        }
                    return _dir = dir;
                }
                catch { }
            }
            return null;
        }

        public static bool HasLhm =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("lhm.LibreHardwareMonitorLib.dll") ||
            File.Exists(Path.Combine(Cfg.AppDir, "lhm", "LibreHardwareMonitorLib.dll"));
    }

    public static class Pawn
    {
        public const string Url = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        public const string Sha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
        public static bool Installed
        {
            get
            {
                try { using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) return k != null; }
                catch { return false; }
            }
        }
        /// <summary>Downloads the official PawnIO installer, checks its checksum and starts it (it asks for permission itself).</summary>
        public static void Install()
        {
            System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
            byte[] data;
            using (var wc = new System.Net.WebClient()) { wc.Headers["User-Agent"] = "BatteryWidget"; data = wc.DownloadData(Url); }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string got = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                if (got != Sha256) throw new Exception("PawnIO_setup.exe isn't the expected file – not started");
            }
            string path = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
            File.WriteAllBytes(path, data);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// <summary>CPU, GPU, motherboard, memory, drive, PSU and cooler sensors. Only this class touches LHM types.</summary>
    public class LhmSource
    {
        object _computer;              // LibreHardwareMonitor.Hardware.Computer (kept as object so nothing loads early)
        public string State = "not started";
        bool _tried;

        public void Restart()
        {
            try { if (_computer != null) Close(); } catch { }
            _computer = null; _tried = false;
        }

        void Close() { ((LibreHardwareMonitor.Hardware.Computer)_computer).Close(); }

        void Load()
        {
            _tried = true;
            if (!Native.OnWindows) { State = "Windows only"; return; }
            if (!Embedded.HasLhm) { State = "missing"; return; }
            Embedded.Hook();
            Open();
        }

        void Open()
        {
            var c = new LibreHardwareMonitor.Hardware.Computer
            {
                IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true,
                IsControllerEnabled = true, IsStorageEnabled = true, IsPsuEnabled = true, IsBatteryEnabled = true
            };
            c.Open();
            _computer = c;
            State = "on";
        }

        static readonly Dictionary<string, string[]> Types = new Dictionary<string, string[]> {
            { "Temperature", new[] { "temp", "°C" } }, { "Load", new[] { "usage", "%" } }, { "Clock", new[] { "clock", "MHz" } },
            { "Power", new[] { "power", "W" } }, { "Fan", new[] { "fan", "RPM" } }, { "Voltage", new[] { "voltage", "V" } },
            { "Current", new[] { "current", "A" } }, { "Data", new[] { "data", "GB" } }, { "SmallData", new[] { "data", "MB" } },
            { "Throughput", new[] { "rate", "MB/s" } }, { "Control", new[] { "usage", "%" } }, { "Level", new[] { "usage", "%" } },
            { "Frequency", new[] { "other", "Hz" } }, { "Flow", new[] { "other", "L/h" } }, { "Factor", new[] { "other", "" } },
            { "Energy", new[] { "other", "mWh" } }, { "TimeSpan", new[] { "other", "s" } }, { "Noise", new[] { "other", "dBA" } },
            { "Humidity", new[] { "usage", "%" } } };
        static readonly Dictionary<string, string> Devices = new Dictionary<string, string> {
            { "Cpu", "CPU" }, { "GpuNvidia", "GPU" }, { "GpuAmd", "GPU" }, { "GpuIntel", "GPU" }, { "Memory", "RAM" }, { "Storage", "Disk" },
            { "Motherboard", "Board" }, { "SuperIO", "Board" }, { "EmbeddedController", "Board" }, { "Cooler", "Cooler" }, { "Psu", "PSU" },
            { "Battery", "Battery" }, { "Network", "Net" } };

        /// <summary>Without the driver the processor answers with zeros (0 °C, 0 MHz, 1.55 V on every core).
        /// Those aren't readings – they become "no value", and the state says what's missing.</summary>
        public static string Sanitize(List<Metric> ms, bool admin, bool pawnInstalled)
        {
            var cpuTemps = ms.Where(m => m.Device == "CPU" && m.Kind == "temp").ToList();
            bool cpuOk = cpuTemps.Count == 0 || cpuTemps.Any(m => m.Value > 1);
            if (!cpuOk)
                foreach (var m in ms)
                    if ((m.Device == "CPU" && m.Kind != "usage") || m.Device == "Board")
                        m.Value = null;
            foreach (var m in ms)
                if (m.Kind == "temp" && m.Value.HasValue && m.Value <= 0) m.Value = null;          // 0 °C is never real
            int n = ms.Count;
            if (cpuOk) return $"on · {n} sensors";
            if (!admin) return $"on · {n} sensors · no admin rights (no CPU/board sensors)";
            if (!pawnInstalled) return $"on · {n} sensors · PawnIO driver missing (no CPU/board sensors)";
            return $"on · {n} sensors · driver not responding (no CPU/board sensors)";
        }

        public static Metric Convert(string group, string hwType, string stype, string name, string ident, float? v, float? mn, float? mx)
        {
            string[] ku; if (!Types.TryGetValue(stype, out ku)) ku = new[] { "other", "" };
            double? val = v, vmin = mn, vmax = mx;
            if (stype == "Throughput") { val /= 1048576.0; vmin /= 1048576.0; vmax /= 1048576.0; }
            if (val.HasValue && (double.IsNaN(val.Value) || Math.Abs(val.Value) > 1e12)) val = null;
            string dev; Devices.TryGetValue(hwType, out dev);
            return new Metric("lhm:" + ident, name, group, ku[0], val, ku[1], vmin, vmax, "lhm", dev ?? "");
        }

        void Walk(LibreHardwareMonitor.Hardware.IHardware hw, string parent, List<Metric> outp)
        {
            hw.Update();
            string group = parent != null ? parent + " › " + hw.Name : hw.Name;
            foreach (var s in hw.Sensors)
                outp.Add(Convert(group, hw.HardwareType.ToString(), s.SensorType.ToString(), s.Name, s.Identifier.ToString(), s.Value, s.Min, s.Max));
            foreach (var sub in hw.SubHardware) Walk(sub, hw.Name, outp);
        }

        List<Metric> ReadAll()
        {
            var outp = new List<Metric>();
            foreach (var hw in ((LibreHardwareMonitor.Hardware.Computer)_computer).Hardware)
                try { Walk(hw, null, outp); } catch { }
            return outp;
        }

        public List<Metric> Read()
        {
            if (_computer == null)
            {
                if (_tried) return new List<Metric>();
                try { Load(); } catch (Exception ex) { State = "error: " + ex.GetType().Name + ": " + ex.Message; _computer = null; }
                if (_computer == null) return new List<Metric>();
            }
            var outp = ReadAll();
            State = Sanitize(outp, Native.IsAdmin, Pawn.Installed);
            return outp;
        }
    }

    /// <summary>Windows performance counters by their English names (works on Swedish Windows too).</summary>
    class Pdh
    {
        readonly IntPtr _q;
        readonly Dictionary<string, IntPtr> _c = new Dictionary<string, IntPtr>();
        public Pdh() { if (Native.PdhOpenQuery(null, IntPtr.Zero, out _q) != 0) throw new Exception("PDH unavailable"); }
        public bool Add(string name, string path)
        {
            IntPtr h;
            if (Native.PdhAddEnglishCounter(_q, path, IntPtr.Zero, out h) != 0) return false;
            _c[name] = h; return true;
        }
        public void Collect() => Native.PdhCollectQueryData(_q);
        const uint Fmt = 0x200 | 0x8000;     // double, no 100 % cap
        public double? Value(string name)
        {
            IntPtr h; if (!_c.TryGetValue(name, out h)) return null;
            Native.PDH_FMT_COUNTERVALUE v;
            return Native.PdhGetFormattedCounterValue(h, Fmt, IntPtr.Zero, out v) == 0 && (v.CStatus == 0 || v.CStatus == 1) ? v.doubleValue : (double?)null;
        }
        public double? Total(string name)
        {
            IntPtr h; if (!_c.TryGetValue(name, out h)) return null;
            int size = 0, count;
            Native.PdhGetFormattedCounterArray(h, Fmt, ref size, out count, IntPtr.Zero);
            if (size <= 0) return 0;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (Native.PdhGetFormattedCounterArray(h, Fmt, ref size, out count, buf) != 0) return null;
                int step = Marshal.SizeOf(typeof(Native.PDH_FMT_COUNTERVALUE_ITEM));
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    var it = (Native.PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(buf + i * step, typeof(Native.PDH_FMT_COUNTERVALUE_ITEM));
                    if (it.FmtValue.CStatus == 0 || it.FmtValue.CStatus == 1) sum += it.FmtValue.doubleValue;
                }
                return sum;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    public class WindowsSource
    {
        Pdh _pdh;
        double? _base;
        ulong _idle, _kern, _user;
        long _rx = -1, _tx = -1;
        DateTime _netAt;

        public WindowsSource()
        {
            if (!Native.OnWindows) return;
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) _base = System.Convert.ToDouble(k.GetValue("~MHz"));
            }
            catch { }
            try
            {
                _pdh = new Pdh();
                _pdh.Add("perf", @"\Processor Information(_Total)\% Processor Performance");
                _pdh.Add("gpu3d", @"\GPU Engine(*engtype_3D)\Utilization Percentage");
                _pdh.Add("dread", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
                _pdh.Add("dwrite", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
                _pdh.Collect();
            }
            catch { _pdh = null; }
        }

        public List<Metric> Read()
        {
            var o = new List<Metric>();
            if (!Native.OnWindows) return ReadPortable(o);
            const string cpu = "CPU (Windows)", mem = "Memory (Windows)", dsk = "Drives (Windows)", net = "Network (Windows)";
            try { _pdh?.Collect(); } catch { }
            FILETIME fi, fk, fu;
            if (Native.GetSystemTimes(out fi, out fk, out fu))
            {
                ulong di = fi.Value - _idle, dk = fk.Value - _kern, du = fu.Value - _user;
                if (_kern != 0 && dk + du > 0) o.Add(new Metric("sys:cpu.usage", "CPU load", cpu, "usage", Math.Max(0, Math.Min(100, (1 - di / (double)(dk + du)) * 100)), "%", source: "windows"));
                _idle = fi.Value; _kern = fk.Value; _user = fu.Value;
            }
            var perf = _pdh?.Value("perf");
            if (perf.HasValue && _base.HasValue && perf > 0) o.Add(new Metric("sys:cpu.clock", "CPU clock", cpu, "clock", _base * perf / 100, "MHz", source: "windows"));
            var g = _pdh?.Total("gpu3d");
            if (g.HasValue) o.Add(new Metric("sys:gpu.usage", "GPU load (3D)", "GPU (Windows)", "usage", Math.Min(100, g.Value), "%", source: "windows"));
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (Native.GlobalMemoryStatusEx(ref ms))
            {
                o.Add(new Metric("sys:ram.pct", "Memory used", mem, "usage", ms.dwMemoryLoad, "%", source: "windows"));
                o.Add(new Metric("sys:ram.used", "Memory used", mem, "data", (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0, "GB", 0, ms.ullTotalPhys / 1073741824.0, "windows"));
            }
            Drives(o, dsk);
            var dr = _pdh?.Value("dread"); var dw = _pdh?.Value("dwrite");
            if (dr.HasValue) o.Add(new Metric("sys:disk.read", "Disk read", dsk, "rate", dr / 1048576.0, "MB/s", source: "windows"));
            if (dw.HasValue) o.Add(new Metric("sys:disk.write", "Disk write", dsk, "rate", dw / 1048576.0, "MB/s", source: "windows"));
            Network(o, net);
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                if (ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery && ps.BatteryLifePercent <= 1)
                    o.Add(new Metric("sys:battery", "Battery", "Battery (Windows)", "usage", Math.Round(ps.BatteryLifePercent * 100), "%", source: "windows"));
            }
            catch { }
            return o;
        }

        List<Metric> ReadPortable(List<Metric> o)       // for previews/tests on other systems
        {
            Drives(o, "Drives (Windows)");
            Network(o, "Network (Windows)");
            return o;
        }

        static void Drives(List<Metric> o, string dsk)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                    string letter = d.Name.TrimEnd('\\', '/');
                    if (letter == "") letter = "/";
                    string tag = new string(letter.Where(char.IsLetterOrDigit).ToArray());
                    if (tag == "") tag = "root";
                    if (!Native.OnWindows && tag != "root") continue;
                    double total = d.TotalSize, free = d.AvailableFreeSpace;
                    o.Add(new Metric($"sys:disk.{tag}.pct", letter + " used", dsk, "usage", (total - free) * 100 / total, "%", source: "windows"));
                    o.Add(new Metric($"sys:disk.{tag}.free", letter + " free", dsk, "data", free / 1073741824.0, "GB", 0, total / 1073741824.0, "windows"));
                }
                catch { }
            }
        }

        void Network(List<Metric> o, string net)
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived; tx += s.BytesSent;
                }
            }
            catch { return; }
            var now = DateTime.UtcNow;
            if (_rx >= 0)
            {
                double dt = Math.Max(0.001, (now - _netAt).TotalSeconds);
                o.Add(new Metric("sys:net.down", "Download", net, "rate", Math.Max(0, (rx - _rx) / dt / 1048576.0), "MB/s", source: "windows"));
                o.Add(new Metric("sys:net.up", "Upload", net, "rate", Math.Max(0, (tx - _tx) / dt / 1048576.0), "MB/s", source: "windows"));
            }
            _rx = rx; _tx = tx; _netAt = now;
        }
    }

    public class SensorHub
    {
        public readonly WindowsSource Windows = new WindowsSource();
        public readonly LhmSource Lhm = new LhmSource();
        public Dictionary<string, string> Status = new Dictionary<string, string>();
        public readonly object Lock = new object();

        public virtual Dictionary<string, Metric> ReadAll()
        {
            lock (Lock)
            {
                var m = new Dictionary<string, Metric>();
                var st = new Dictionary<string, string>();
                try { foreach (var x in Lhm.Read()) m[x.Key] = x; } catch (Exception ex) { Lhm.State = "error: " + ex.Message; }
                st["lhm"] = Lhm.State;
                st["admin"] = Native.IsAdmin ? "yes" : "no";
                st["pawnio"] = Pawn.Installed ? "yes" : "no";
                try { var w = Windows.Read(); foreach (var x in w) m[x.Key] = x; st["windows"] = w.Count + " readings"; }
                catch (Exception ex) { st["windows"] = "error: " + ex.Message; }
                Status = st;
                return m;
            }
        }
    }
}
