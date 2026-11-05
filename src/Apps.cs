// Apps.cs – clicking a device opens its maker's app (G HUB, Synapse, GG, iCUE …). Standard install folders first,
// then every installed app (desktop and Store), else you pick one yourself; the choice is remembered per device.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class AppTarget
    {
        public string Name, Kind, Target, Args = "";        // Kind: exe | app (shell:AppsFolder id) | uri
        public Dictionary<string, object> ToDict() => new Dictionary<string, object> { { "name", Name }, { "kind", Kind }, { "target", Target }, { "args", Args } };
        public static AppTarget From(Dictionary<string, object> d) => d == null || Cfg.S(d, "target") == null ? null
            : new AppTarget { Name = Cfg.S(d, "name", "app"), Kind = Cfg.S(d, "kind", "exe"), Target = Cfg.S(d, "target"), Args = Cfg.S(d, "args", "") };
        public override string ToString() => Name;
    }

    public static class Apps
    {
        class Known { public string Name; public string[] Paths; public string[] Words; public string Uri, Args = ""; }
        static string PF => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        static string PF86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        static string LocalApp => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        static readonly Dictionary<string, Known[]> Table = new Dictionary<string, Known[]> {
            { "logitech", new[] {
                new Known { Name = "Logitech G HUB", Paths = new[] { @"{PF}\LGHUB\lghub.exe", @"{PF}\LGHUB\system_tray\lghub_system_tray.exe" }, Words = new[] { "G HUB", "LGHUB" } },
                new Known { Name = "Logi Options+", Paths = new[] { @"{PF}\LogiOptionsPlus\logioptionsplus.exe" }, Words = new[] { "Options+", "Logi Options" } } } },
            { "razer", new[] {
                new Known { Name = "Razer Synapse", Paths = new[] { @"{PF}\Razer\RazerAppEngine\RazerAppEngine.exe" }, Args = "--url-params=apps=synapse", Words = new[] { "Razer Synapse", "Synapse" } },
                new Known { Name = "Razer Synapse 3", Paths = new[] { @"{PF86}\Razer\Synapse3\WPFUI\Framework\Razer Synapse 3 Host\Razer Synapse 3.exe" }, Words = new[] { "Synapse 3" } } } },
            { "steelseries", new[] { new Known { Name = "SteelSeries GG", Paths = new[] { @"{PF}\SteelSeries\GG\SteelSeriesGG.exe" }, Words = new[] { "SteelSeries GG", "SteelSeries" } } } },
            { "corsair", new[] { new Known { Name = "Corsair iCUE", Paths = new[] { @"{PF}\Corsair\Corsair iCUE5 Software\iCUE.exe", @"{PF}\Corsair\CORSAIR iCUE 4 Software\iCUE.exe", @"{PF}\Corsair\CORSAIR iCUE 5 Software\iCUE.exe" }, Words = new[] { "iCUE" } } } },
            { "asus", new[] { new Known { Name = "ASUS Armoury Crate", Paths = new string[0], Words = new[] { "Armoury Crate", "ARMOURY CRATE" } } } },
            { "hyperx", new[] { new Known { Name = "HyperX NGENUITY", Paths = new string[0], Words = new[] { "NGENUITY" } } } },
            { "mchose", new[] { new Known { Name = "MCHOSE M HUB", Paths = new[] { @"{PF}\MCHOSE\M HUB\M HUB.exe", @"{PF86}\MCHOSE\M HUB\M HUB.exe" }, Words = new[] { "MCHOSE", "M HUB" } } } },
            { "monsgeek", new[] { new Known { Name = "MonsGeek / Akko driver", Paths = new string[0], Words = new[] { "MonsGeek", "Akko Cloud", "Akko", "Epomaker" } } } },
            { "atk", new[] { new Known { Name = "ATK / VXE hub", Paths = new string[0], Words = new[] { "ATK", "VXE", "Compx" } } } },
            { "keychron", new[] { new Known { Name = "Keychron Launcher (web)", Paths = new string[0], Words = new[] { "Keychron" }, Uri = "https://launcher.keychron.com" } } },
            { "bluetooth", new[] { new Known { Name = "Windows Bluetooth settings", Paths = new string[0], Words = new string[0], Uri = "ms-settings:bluetooth" } } },
        };

        public static Func<string, bool> Exists = File.Exists;          // tests swap this

        /// <summary>Which maker a device belongs to, from the source that read it and its USB vendor id.</summary>
        public static string Vendor(Reading r)
        {
            var m = Regex.Match(r.Detail ?? "", @"\b([0-9a-f]{4}):[0-9a-f]{4}\b");
            int vid = m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : -1;
            string src = r.Source ?? "", key = r.Key ?? "", name = (r.Name ?? "").ToLowerInvariant();
            if (src == "bluetooth" || key.StartsWith("bt:")) return "bluetooth";
            if (src == "ghub" || src == "logitech" || vid == 0x046D || key.StartsWith("logitech:")) return "logitech";
            if (src == "razer" || vid == 0x1532) return "razer";
            if (src == "asus" || vid == 0x0B05) return "asus";
            if (src == "rongyuan" || (vid >= 0 && RongYuanProvider.Vids.Contains(vid))) return "monsgeek";
            if (src == "compx" || vid == 0x373B || vid == 0x3554) return "atk";
            if (src == "keychron" || vid == 0x3434) return "keychron";
            if (src == "mchose" || vid == 0x3837 || vid == 0x5253) return "mchose";
            if (vid == 0x1038 || name.Contains("steelseries") || name.Contains("arctis")) return "steelseries";
            if (vid == 0x1B1C || name.Contains("corsair")) return "corsair";
            if (src == "hyperx" || vid == 0x03F0 || vid == 0x0951 || name.Contains("hyperx")) return "hyperx";
            if (name.Contains("logitech") || Regex.IsMatch(name, @"^g\d{3}\b")) return "logitech";
            if (name.Contains("razer")) return "razer";
            return "";
        }

        static string Expand(string p) => p.Replace("{PF}", PF).Replace("{PF86}", PF86).Replace("{LOCAL}", LocalApp);

        // ---- every installed app (Start menu: desktop programs and Store apps) -------------------------------
        static List<AppTarget> _installed; static DateTime _at;
        public static List<AppTarget> Installed()
        {
            if (_installed != null && (DateTime.UtcNow - _at).TotalMinutes < 10) return _installed;
            var list = new List<AppTarget>();
            if (Native.OnWindows)
            {
                var t = new Thread(() =>                      // the Shell needs a single-threaded apartment
                {
                    try
                    {
                        var type = Type.GetTypeFromProgID("Shell.Application");
                        object sh = Activator.CreateInstance(type);
                        object folder = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, sh, new object[] { "shell:AppsFolder" });
                        object items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                        int n = (int)items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null);
                        for (int i = 0; i < n; i++)
                        {
                            object it = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                            string nm = (string)it.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, it, null);
                            string path = (string)it.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, it, null);
                            if (!string.IsNullOrEmpty(nm) && !string.IsNullOrEmpty(path)) list.Add(new AppTarget { Name = nm, Kind = "app", Target = path });
                        }
                    }
                    catch { }
                });
                t.SetApartmentState(ApartmentState.STA); t.IsBackground = true; t.Start(); t.Join(8000);
            }
            _installed = list; _at = DateTime.UtcNow;
            return list;
        }

        /// <summary>The apps that fit this maker, best first: standard folders, then installed apps by name.</summary>
        public static List<AppTarget> Candidates(string vendor, string deviceName, Func<List<AppTarget>> installed = null)
        {
            var outp = new List<AppTarget>();
            Known[] known;
            if (!Table.TryGetValue(vendor ?? "", out known)) return outp;
            var ordered = known.ToList();
            if (vendor == "logitech" && Regex.IsMatch(deviceName ?? "", @"\b(MX|ERGO|POP|Lift|Signature|K\d{3}|M\d{3})\b", RegexOptions.IgnoreCase))
                ordered.Reverse();                             // MX and office devices belong to Logi Options+
            foreach (var k in ordered)
            {
                var exe = k.Paths.Select(Expand).FirstOrDefault(p => Exists(p));
                if (exe != null) { outp.Add(new AppTarget { Name = k.Name, Kind = "exe", Target = exe, Args = k.Args }); continue; }
                var hit = (installed ?? Installed)().FirstOrDefault(a => k.Words.Any(w => a.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
                if (hit != null) { outp.Add(new AppTarget { Name = hit.Name, Kind = "app", Target = hit.Target }); continue; }
                if (k.Uri != null) outp.Add(new AppTarget { Name = k.Name, Kind = "uri", Target = k.Uri });
            }
            return outp;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int pid, tid; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        /// <summary>Starts a program without Windows' "app starting" spinner next to the mouse pointer.</summary>
        static bool Quiet(string exe, string args, string dir)
        {
            if (!Native.OnWindows) return false;
            var si = new STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(STARTUPINFO)), dwFlags = 0x80 };   // STARTF_FORCEOFFFEEDBACK
            PROCESS_INFORMATION pi;
            if (!CreateProcess(null, new System.Text.StringBuilder("\"" + exe + "\" " + (args ?? "")), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, dir, ref si, out pi)) return false;
            Native.CloseHandle(pi.hProcess); Native.CloseHandle(pi.hThread);
            return true;
        }
        static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        public static bool Launch(AppTarget a)
        {
            try
            {
                if (a.Kind == "app") return Quiet(Explorer, @"shell:AppsFolder\" + a.Target, null) || Start("explorer.exe", @"shell:AppsFolder\" + a.Target);
                if (a.Kind == "uri") return Quiet(Explorer, "\"" + a.Target + "\"", null) || Start(a.Target, "");
                if (!File.Exists(a.Target)) return false;
                return Quiet(a.Target, a.Args, Path.GetDirectoryName(a.Target)) || Start(a.Target, a.Args);
            }
            catch { return false; }
        }
        static bool Start(string f, string args) { try { Process.Start(new ProcessStartInfo(f, args ?? "") { UseShellExecute = true }); return true; } catch { return false; } }
    }

    /// <summary>"Which app should open for this device?"</summary>
    public class AppPickerForm : ThemedForm
    {
        public AppTarget Choice;
        public AppPickerForm(Theme t, string device, List<AppTarget> found, List<AppTarget> others) : base(t)
        {
            Text = "Choose an app – Battery Widget";
            ClientSize = new Size(500, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; TopMost = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), RowCount = 4, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl("Which app should open for " + device + "?", false, 12, true));
            root.Controls.Add(Lbl(found.Count > 0 ? "Found on this PC – pick one, or browse for another program."
                                                  : "The maker's app wasn't found in its usual place. Pick one of your apps, or browse for the program.", true));
            var list = new ListBox { Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.None, IntegralHeight = false, Margin = new Padding(0, 8, 0, 8) };
            foreach (var a in found) list.Items.Add(a);
            if (found.Count > 0 && others.Count > 0) list.Items.Add("──────────");
            foreach (var a in others.Where(o => !found.Any(f => f.Target == o.Target)).OrderBy(o => o.Name)) list.Items.Add(a);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            root.Controls.Add(list);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var open = Btn("Open", true); var browse = Btn("Browse…"); var cancel = Btn("Cancel");
            bar.Controls.AddRange(new Control[] { open, cancel, browse });
            root.Controls.Add(bar); Controls.Add(root);
            Action pick = () => { if (list.SelectedItem is AppTarget a) { Choice = a; DialogResult = DialogResult.OK; Close(); } };
            open.Click += (s, e) => pick();
            list.DoubleClick += (s, e) => pick();
            cancel.Click += (s, e) => Close();
            browse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Title = "Choose the program for " + device })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    { Choice = new AppTarget { Name = Path.GetFileNameWithoutExtension(d.FileName), Kind = "exe", Target = d.FileName }; DialogResult = DialogResult.OK; Close(); }
            };
        }
    }
}
