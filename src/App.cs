// App.cs – start-up, the tray icon, both widgets switched on/off, administrator rights, autostart, quitting.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Battery Widget")]
[assembly: System.Reflection.AssemblyProduct("Battery Widget")]
[assembly: System.Reflection.AssemblyDescription("Battery levels for wireless gear + a system monitor")]
[assembly: System.Reflection.AssemblyVersion("4.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.1.1.0")]

namespace BatteryWidget
{
    public static class Program
    {
        public const string Version = "4.1.1";
        public const string AppId = "BatteryWidget";
        static Icon _icon;
        public static Icon AppIcon => _icon ?? (_icon = Draw.MakeIcon(32));
        static Mutex _single;

        [STAThread]
        static int Main(string[] args)
        {
            if (Native.OnWindows) try { Native.SetProcessDPIAware(); } catch { }
            Embedded.Hook();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) WidgetWindow.Dpi = g.DpiX / 96f; } catch { }
            bool restarted = args.Contains("--restarted"), demo = args.Contains("--demo");
            int pi = Array.IndexOf(args, "--preview");
            if (pi >= 0) return Preview.Run(args.Length > pi + 1 ? args[pi + 1] : ".");

            var cfg = Cfg.Load("BatteryWidget.json", BatteryView.Defaults(), Path.Combine(Cfg.RoamingDir, "config.json"));
            if (demo) cfg.Persist = false;
            if (Native.OnWindows && !demo && cfg.Bool("run_as_admin") && !Native.IsAdmin && !restarted)
                if (WinUtil.RelaunchAsAdmin("--restarted")) return 0;           // Windows asks once, then this copy steps aside

            if (!demo)
            {
                bool created;
                _single = new Mutex(true, @"Local\BatteryWidget.Single", out created);
                if (!created)
                {
                    try { if (!_single.WaitOne(restarted ? 8000 : 150)) return 0; }       // another copy is running
                    catch (AbandonedMutexException) { }
                }
                WinUtil.HealAutostart(AppId);
            }
            Application.ThreadException += (s, e) => Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
            Updater.CleanUp();                                         // the previous exe after an update
            var suite = new Suite(cfg, demo);
            suite.Start();
            if (args.Contains("--updated")) WidgetWindow.Later(1500, () => Toast.Show("up", "Updated to " + Version, "Battery Widget is up to date.", suite.Battery.MenuTheme, 4500));
            Application.Run();
            suite.QuitAll();
            return 0;
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(Cfg.DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\r\n"); } catch { }
        }
    }

    public class Suite
    {
        public readonly Cfg Cfg;                 // the battery widget's settings – also holds the on/off switches
        public BatteryView Battery;
        public SystemView System;
        public AlwaysOnTop Pin;
        public WeatherView Weather;
        public NotifyIcon Tray;
        readonly bool _demo;

        public Suite(Cfg cfg, bool demo) { Cfg = cfg; _demo = demo; }

        public void Start()
        {
            if (!_demo && Cfg.NumOrNull("privacy_seen") == null)            // first start: what goes online, and the choice
                using (var f = new PrivacyForm(this, Themes.Resolve(Cfg.Str("theme", "Dark") == "Auto" ? "Dark" : Cfg.Str("theme", "Dark"), Cfg.Str("accent", ""), false, true))) f.ShowDialog();
            Tray = new NotifyIcon { Icon = Program.AppIcon, Text = "Battery Widget", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
            Tray.ContextMenuStrip.Opening += (s, e) => { e.Cancel = false; FillTray(Tray.ContextMenuStrip); };
            Tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) ToggleVisible(); };
            var poller = _demo ? null : new Poller(Cfg, Poller.Make());
            Battery = new BatteryView(Cfg, poller) { Suite = this };
            Battery.OnReadings(new List<Reading>(), false);        // last known devices right away
            if (Cfg.Bool("show_battery", true)) Battery.Start(); else if (poller != null) poller.Paused = true;
            poller?.Start();
            if (_demo) WidgetWindow.Later(200, () => Battery.OnReadings(Demo.Readings()));
            Pin = new AlwaysOnTop(this);
            if (!_demo) Pin.Start();
            if (Cfg.Bool("show_system", true)) WidgetWindow.Later(50, StartSystem);
            if (Cfg.Bool("show_weather", true)) WidgetWindow.Later(120, StartWeather);
            if (!_demo && Cfg.Bool("update_check", true)) WidgetWindow.Later(12000, () => CheckUpdates(false));   // quietly, in the background
        }

        public void StartWeather()
        {
            if (Weather != null) return;
            var wc = Cfg.Load("Weather.json", WeatherView.Defaults());
            if (_demo) wc.Persist = false;
            Weather = new WeatherView(wc) { Suite = this };
            Weather.Start();
            if (_demo) Weather.Arrived(Demo.WeatherJson(), new Place { Name = "Stockholm, Sweden", Lat = 59.33, Lon = 18.07 }, null);
            else Weather.RefreshNow();
        }

        public void StopWeather() { var w = Weather; Weather = null; if (w == null) return; w.Shutdown(); w.Close(); w.Dispose(); }
        public void SetWeather(bool on) { Cfg["show_weather"] = on; Cfg.Save(); if (on) StartWeather(); else StopWeather(); }

        // ---- updates (GitHub releases tagged vX.Y.Z) ------------------------------------------------
        void SetUpdateSource()
        {
            using (var f = new SourceForm(Battery.MenuTheme, Updater.Repo(Cfg)))
                if (f.ShowDialog() == DialogResult.OK) { Cfg["update_repo"] = f.Value; Cfg.Save(); CheckUpdates(true); }
        }
        bool _checking;
        public void CheckUpdates(bool manual)
        {
            if (_checking) return;
            string repo = Updater.Repo(Cfg);
            if (repo == "") { if (manual) Toast.Show("up", "Updates aren't set up yet", "The update source (a GitHub repository) hasn't been chosen.", Battery.MenuTheme, 4500); return; }
            _checking = true;
            new Thread(() =>
            {
                Updater.Release r = null; string err = null;
                try { r = Updater.Latest(repo); } catch (Exception ex) { err = ex.Message; }
                Battery.BeginInvoke((Action)(() =>
                {
                    _checking = false;
                    if (r != null && r.Url != null && !string.IsNullOrEmpty(r.Sha256) && Updater.Newer(r.Version, Program.Version) && (manual || Cfg.Str("update_skip", "") != r.Version))
                        new UpdateForm(this, r, Battery.MenuTheme).Show();
                    else if (manual)
                        Toast.Show("up", err != null ? "Couldn't check for updates" : "You have the latest version", err ?? "Battery Widget " + Program.Version, Battery.MenuTheme, 4000);
                }));
            }) { IsBackground = true }.Start();
        }

        public bool IsOn(WidgetWindow w) => w is BatteryView ? Cfg.Bool("show_battery", true) : w is WeatherView ? Cfg.Bool("show_weather", true) : Cfg.Bool("show_system", true);

        public static Cfg LoadSystemCfg()
        {
            var c = Cfg.Load("SystemMonitor.json", SystemView.Defaults(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemWidget", "config.json"));
            if (c.NumOrNull("card_v") == null) { c["card_v"] = 1; c["style"] = "card"; }
            if (c.NumOrNull("tb_v") == null) { c["tb_v"] = 1; c["tb_style"] = "icons"; c["tb_rows"] = 2; c["tb_max"] = 6; }   // the compact taskbar look
            return c;
        }

        public void StartSystem()
        {
            if (System != null) return;
            var scfg = LoadSystemCfg();
            if (_demo) scfg.Persist = false;
            var sampler = new Sampler(scfg, _demo ? new Demo.Hub() : new SensorHub());
            System = new SystemView(scfg, sampler) { Suite = this };
            System.Start();
            sampler.Start();
        }

        public void StopSystem()
        {
            var w = System; System = null;
            if (w == null) return;
            w.Shutdown();
            var hub = w.Sampler?.Hub;
            if (hub != null) new Thread(() => { lock (hub.Lock) hub.Lhm.Restart(); }) { IsBackground = true }.Start();
            w.Close(); w.Dispose();
        }

        public void SetBattery(bool on)
        {
            Cfg["show_battery"] = on; Cfg.Save();
            if (Battery.Poller != null) { Battery.Poller.Paused = !on; if (on) Battery.Poller.RefreshNow(); }
            if (on) { if (!Battery.IsHandleCreated) Battery.Start(); else { Battery.Show(); Battery.Relayout(); } }
            else Battery.Hide();
        }

        public void SetSystem(bool on) { Cfg["show_system"] = on; Cfg.Save(); if (on) StartSystem(); else StopSystem(); }

        public void ToggleVisible()
        {
            var ws = new WidgetWindow[] { Battery, System, Weather }.Where(w => w != null && IsOn(w) && w.IsHandleCreated).ToList();
            bool show = !ws.Any(w => w.Visible);
            foreach (var w in ws) w.Visible = show;
        }

        // ---- administrator rights (CPU temperatures) and autostart ---------------------------------------
        public void EnableAdmin()
        {
            Cfg["run_as_admin"] = true; Cfg.Save();
            if (!Native.IsAdmin) RelaunchAdmin();
        }
        public static void EnableAdmin(Suite s) => s?.EnableAdmin();

        void RelaunchAdmin() { SaveAll(); if (WinUtil.RelaunchAsAdmin("--restarted")) QuitAll(); }

        void ToggleAdmin()
        {
            Cfg["run_as_admin"] = !Cfg.Bool("run_as_admin"); Cfg.Save();
            if (Cfg.Bool("run_as_admin") && !Native.IsAdmin) RelaunchAdmin();
            else if (WinUtil.AutostartOn(Program.AppId)) ToggleAutostart(true);
        }

        public void ToggleAutostart(bool? force = null)
        {
            bool on = force ?? !WinUtil.AutostartOn(Program.AppId);
            try { WinUtil.SetAutostart(Program.AppId, on); } catch (Exception ex) { Notify("Could not change autostart: " + ex.Message, true); }
        }

        // ---- menus -------------------------------------------------------------------------------------
        void Switches(ToolStripItemCollection it)
        {
            Menus.Item(it, "Battery widget", () => SetBattery(!Cfg.Bool("show_battery", true)), Cfg.Bool("show_battery", true));
            Menus.Item(it, "System monitor", () => SetSystem(!Cfg.Bool("show_system", true)), Cfg.Bool("show_system", true));
            Menus.Item(it, "Weather", () => SetWeather(!Cfg.Bool("show_weather", true)), Cfg.Bool("show_weather", true));
        }

        void UpdateItems(ToolStripItemCollection it, Theme t)
        {
            var u = Menus.Sub(it, "Updates", t);
            Menus.Item(u.DropDownItems, "Check for updates now", () => CheckUpdates(true));
            Menus.Item(u.DropDownItems, "Check quietly at every start", () => { Cfg["update_check"] = !Cfg.Bool("update_check", true); Cfg.Save(); }, Cfg.Bool("update_check", true));
            Menus.Item(u.DropDownItems, "Set update source…", SetUpdateSource);
            string repo = Updater.Repo(Cfg);
            Menus.Item(u.DropDownItems, "You have " + Program.Version + (repo != "" ? " · from github.com/" + repo : " · no update source yet"), null, null, false);
        }

        public void FillCommon(ToolStripItemCollection it, Theme t)
        {
            var w = Menus.Sub(it, "Widgets", t);
            Switches(w.DropDownItems);
            Pin?.Menu(it, t);
            UpdateItems(it, t);
            if (Native.OnWindows)
            {
                Menus.Item(it, "Run as administrator (CPU temperatures)", ToggleAdmin, Cfg.Bool("run_as_admin") && Native.IsAdmin);
                Menus.Item(it, "Start with Windows", () => ToggleAutostart(), WinUtil.AutostartOn(Program.AppId));
            }
            Menus.Item(it, "Quit", QuitAll);
        }

        public void FillTray(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var t = Battery.MenuTheme;
            Menus.Style(menu, t);
            var it = menu.Items;
            Menus.Item(it, $"Battery Widget {Program.Version}", null, null, false);
            Menus.Sep(it);
            Switches(it);
            Menus.Sep(it);
            if (Cfg.Bool("show_battery", true))
            {
                var b = Menus.Sub(it, "Battery widget", t);
                var tmp = new ContextMenuStrip(); Battery.FillMenu(tmp, false);
                foreach (var x in tmp.Items.Cast<ToolStripItem>().ToList()) b.DropDownItems.Add(x);
            }
            if (Weather != null)
            {
                var wm = Menus.Sub(it, "Weather", t);
                var tmpw = new ContextMenuStrip(); Weather.FillMenu(tmpw, false);
                foreach (var x in tmpw.Items.Cast<ToolStripItem>().ToList()) wm.DropDownItems.Add(x);
            }
            if (System != null)
            {
                var s = Menus.Sub(it, "System monitor", t);
                var tmp = new ContextMenuStrip(); System.FillMenu(tmp, false);
                foreach (var x in tmp.Items.Cast<ToolStripItem>().ToList()) s.DropDownItems.Add(x);
            }
            Menus.Sep(it);
            Pin?.Menu(it, t);
            UpdateItems(it, t);
            if (Native.OnWindows)
            {
                Menus.Item(it, "Run as administrator (CPU temperatures)", ToggleAdmin, Cfg.Bool("run_as_admin") && Native.IsAdmin);
                Menus.Item(it, "Start with Windows", () => ToggleAutostart(), WinUtil.AutostartOn(Program.AppId));
            }
            Menus.Item(it, "Quit", QuitAll);
        }

        public void Notify(string msg, bool warn, string title = "Battery Widget")
        {
            try { Tray?.ShowBalloonTip(warn ? 8000 : 2500, title, msg, warn ? ToolTipIcon.Warning : ToolTipIcon.Info); } catch { }
        }

        public void UpdateTrayTip()
        {
            if (Tray == null) return;
            var lines = Battery.Items.Select(r => $"{r.Name}: {(r.Percent == null ? "—" : r.Percent + "%")}{(r.Online ? "" : " (off)")}");
            string s = string.Join("\n", lines);
            if (s == "") s = "Battery Widget";
            Tray.Text = s.Length > 63 ? s.Substring(0, 62) + "…" : s;      // Windows' limit for tray tooltips
        }

        // ---- one theme for all widgets (or each its own) ------------------------------------------------
        public IEnumerable<WidgetWindow> Widgets => new WidgetWindow[] { Battery, System, Weather }.Where(w => w != null && !w.IsDisposed);
        static readonly string[] Shared = { "theme", "accent", "blend" };
        public void ThemeChanged(WidgetWindow src, string key, object value)
        {
            WidgetWindow.Later(50, () => Pin?.Restyle());                   // the border follows the theme color
            if (!Cfg.Bool("link_themes", true) || !Shared.Contains(key)) return;
            foreach (var w in Widgets.Where(x => x != src)) { w.C[key] = value; w.Save(); w.InvalidateTheme(); w.Relayout(); }
        }
        public void LinkItem(ToolStripItemCollection it)
        {
            Menus.Sep(it);
            Menus.Item(it, "Same theme for all widgets", () =>
            {
                Cfg["link_themes"] = !Cfg.Bool("link_themes", true); Cfg.Save();
                if (Cfg.Bool("link_themes", true) && Battery != null) foreach (var k in Shared) ThemeChanged(Battery, k, Battery.C[k]);
            }, Cfg.Bool("link_themes", true));
        }

        /// <summary>First start: around the middle of the main screen – system monitor left, weather right, battery below.</summary>
        public Point DefaultSpot(WidgetWindow w)
        {
            var wa = Screen.PrimaryScreen.WorkingArea; int cx = wa.Left + wa.Width / 2, cy = wa.Top + wa.Height / 2, gap = (int)(16 * WidgetWindow.Dpi);
            var s = w.LayoutSize;
            if (w is SystemView) return new Point(cx - s.Width - gap, cy - s.Height / 2);
            if (w is WeatherView) return new Point(cx + gap, cy - s.Height / 2);
            return new Point(cx - s.Width / 2, cy + (int)(170 * WidgetWindow.Dpi));
        }

        /// <summary>Taskbar widgets line up from the clock: battery, weather, system monitor – each next to the one before.</summary>
        public double? TaskbarSlot(WidgetWindow w, TaskbarInfo tb)
        {
            double run = w.TrayOffset(tb), gap = 6 * WidgetWindow.Dpi;
            foreach (var o in new WidgetWindow[] { Battery, Weather, System })
            {
                if (o == w) return run;
                if (o == null || o.IsDisposed || !o.TaskbarMode || !IsOn(o) || o.LayoutSize.Width <= 10) continue;
                double off = o.C.NumOrNull("tb_offset") ?? run;
                run = off + (tb.Horizontal ? o.LayoutSize.Width : o.LayoutSize.Height) + gap;
            }
            return run;
        }

        // ---- widgets that stick together ---------------------------------------------------------------
        static bool Touch(Rectangle a, Rectangle b, int gap) { a.Inflate(gap, gap); return a.IntersectsWith(b); }
        /// <summary>The widgets snapped to this one (directly or through others). Alt = just this one.</summary>
        public List<WidgetWindow> Cluster(WidgetWindow w, bool alone)
        {
            var group = new List<WidgetWindow> { w };
            if (alone) return group;
            var desk = Widgets.Where(x => !x.TaskbarMode && x.Visible && x.SwitchedOn).ToList();
            int gap = (int)(16 * WidgetWindow.Dpi);
            for (int i = 0; i < group.Count; i++)
                foreach (var o in desk) if (!group.Contains(o) && Touch(group[i].ScreenVisual, o.ScreenVisual, gap)) group.Add(o);
            return group;
        }

        /// <summary>Pulls a dragged widget (and the ones stuck to it) against other widgets and the screen edges.</summary>
        public Point Snap(WidgetWindow w, List<WidgetWindow> group, Point from, Point d)
        {
            float dpi = WidgetWindow.Dpi;
            int gap = (int)(10 * dpi), th = (int)(18 * dpi);
            var start = w.ScreenVisual; start.Offset(from.X - w.Pos.X, from.Y - w.Pos.Y);
            var r = start; r.Offset(d);
            int bestX = th + 1, bestY = th + 1, dx = 0, dy = 0;
            Action<int, bool> consider = (diff, horizontal) =>
            {
                if (horizontal) { if (Math.Abs(diff) < Math.Abs(bestX)) { bestX = diff; dx = diff; } }
                else if (Math.Abs(diff) < Math.Abs(bestY)) { bestY = diff; dy = diff; }
            };
            foreach (var o in Widgets.Where(x => !group.Contains(x) && !x.TaskbarMode && x.Visible && x.SwitchedOn))
            {
                var v = o.ScreenVisual;
                if (r.Top < v.Bottom + th && r.Bottom > v.Top - th)
                { consider(v.Right + gap - r.Left, true); consider(v.Left - gap - r.Right, true); consider(v.Left - r.Left, true); consider(v.Right - r.Right, true); }
                if (r.Left < v.Right + th && r.Right > v.Left - th)
                { consider(v.Bottom + gap - r.Top, false); consider(v.Top - gap - r.Bottom, false); consider(v.Top - r.Top, false); consider(v.Bottom - r.Bottom, false); }
            }
            var wa = Screen.FromRectangle(r).WorkingArea;
            consider(wa.Left + gap - r.Left, true); consider(wa.Right - gap - r.Right, true);
            consider(wa.Top + gap - r.Top, false); consider(wa.Bottom - gap - r.Bottom, false);
            return new Point(d.X + (Math.Abs(bestX) <= th ? dx : 0), d.Y + (Math.Abs(bestY) <= th ? dy : 0));
        }

        // ---- leaving ---------------------------------------------------------------------------------------
        public void SaveAll() { Cfg.Save(); Battery?.Save(); System?.Save(); Weather?.Save(); }

        public void RestartAll()
        {
            SaveAll();
            try { global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(WinUtil.ExePath, "--restarted") { UseShellExecute = false }); } catch { }
            QuitAll();
        }

        public void QuitAll()
        {
            SaveAll();
            foreach (var w in new WidgetWindow[] { Weather, System, Battery }) if (w != null) try { w.Shutdown(); } catch { }
            try { Pin?.Dispose(); } catch { }                                 // pinned windows go back to normal
            if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
            Application.DoEvents();
            WinUtil.HardExit(0);
        }
    }

    public class SourceForm : ThemedForm
    {
        public string Value;
        public SourceForm(Theme t, string current) : base(t)
        {
            Text = "Update source – Battery Widget"; ClientSize = new Size(480, 200); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), RowCount = 4, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl("Where new versions are published", false, 12, true));
            root.Controls.Add(Lbl("A GitHub repository as owner/name, e.g. anna/BatteryWidget. Releases are tagged v3.1.0, v3.2.0 …", true));
            var box = new TextBox { Text = current, Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 8, 0, 8) };
            root.Controls.Add(box);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var ok = Btn("Save", true); var cancel = Btn("Cancel");
            bar.Controls.AddRange(new Control[] { ok, cancel });
            root.Controls.Add(bar); Controls.Add(root);
            ok.Click += (s, e) =>
            {
                string v = box.Text.Trim().Replace("https://github.com/", "").Trim('/');
                if (v != "" && System.Text.RegularExpressions.Regex.IsMatch(v, @"^[\w.-]+/[\w.-]+$") == false) { box.BackColor = Draw.A(T.Bad, 255); return; }
                Value = v; DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += (s, e) => Close();
            AcceptButton = ok;
        }
    }

    // ---- fake data for --demo and previews ----------------------------------------------------------------
    public static class Demo
    {
        public static string WeatherJson()
        {
            var now = new DateTime(2026, 11, 5, 14, 0, 0);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var ht = new List<string>(); var htemp = new List<string>(); var hcode = new List<string>(); var hpop = new List<string>(); var hday = new List<string>();
            int[] codes = { 3, 3, 61, 63, 61, 3, 2, 2, 1, 0, 0, 1, 2, 3, 3, 61, 61, 63, 3, 2, 1, 1, 0, 0 };
            for (int i = 0; i < 168; i++)
            {
                var t = now.Date.AddHours(i);
                ht.Add("\"" + t.ToString("yyyy-MM-ddTHH:mm", inv) + "\"");
                htemp.Add((5 + 3 * Math.Sin((t.Hour - 9) / 24.0 * 2 * Math.PI) - i / 60.0).ToString("F1", inv));
                hcode.Add(codes[i % 24].ToString()); hpop.Add((codes[i % 24] >= 61 ? 70 : codes[i % 24] == 3 ? 20 : 0).ToString());
                hday.Add(t.Hour >= 8 && t.Hour < 16 ? "1" : "0");
            }
            int[] dc = { 61, 3, 2, 80, 0, 71, 1 }; double[] mx = { 8.4, 6.1, 7.0, 5.2, 4.8, 1.9, 3.3 }, mn = { 3.1, 1.2, 2.5, 0.4, -1.8, -3.2, -2.0 }; int[] pop = { 70, 20, 10, 60, 0, 80, 5 };
            var days = Enumerable.Range(0, 7).Select(i => now.Date.AddDays(i)).ToList();
            Func<IEnumerable<string>, string> J = x => "[" + string.Join(",", x) + "]";
            return "{\"current\":{\"time\":\"2026-11-05T14:00\",\"temperature_2m\":7.3,\"apparent_temperature\":4.1,\"relative_humidity_2m\":81,\"weather_code\":61,\"wind_speed_10m\":5.2,\"is_day\":1}," +
                "\"hourly\":{\"time\":" + J(ht) + ",\"temperature_2m\":" + J(htemp) + ",\"weather_code\":" + J(hcode) + ",\"precipitation_probability\":" + J(hpop) + ",\"is_day\":" + J(hday) + "}," +
                "\"daily\":{\"time\":" + J(days.Select(d => "\"" + d.ToString("yyyy-MM-dd", inv) + "\"")) + ",\"weather_code\":" + J(dc.Select(c => c.ToString())) +
                ",\"temperature_2m_max\":" + J(mx.Select(v => v.ToString("F1", inv))) + ",\"temperature_2m_min\":" + J(mn.Select(v => v.ToString("F1", inv))) +
                ",\"precipitation_probability_max\":" + J(pop.Select(v => v.ToString())) +
                ",\"sunrise\":" + J(days.Select(d => "\"" + d.AddHours(7.4).ToString("yyyy-MM-ddTHH:mm", inv) + "\"")) +
                ",\"sunset\":" + J(days.Select(d => "\"" + d.AddHours(15.9).ToString("yyyy-MM-ddTHH:mm", inv) + "\"")) +
                ",\"wind_speed_10m_max\":" + J(days.Select(d => "6.5")) + "}}";
        }

        public static List<Reading> Readings() => new List<Reading> {
            new Reading("demo:mouse", "G502 X LIGHTSPEED", "mouse", 82), new Reading("demo:kb", "MonsGeek M1 V5", "keyboard", 54, true),
            new Reading("demo:hs", "HyperX Cloud III Wireless", "headset", 12, approx: true), new Reading("demo:bt", "EarFun OpenJump", "other", 74, online: false) };

        public class Hub : SensorHub
        {
            readonly DateTime _t0 = DateTime.UtcNow.AddSeconds(-20);
            readonly Random _r = new Random(3);
            public double Offset;
            public override Dictionary<string, Metric> ReadAll()
            {
                double t = (DateTime.UtcNow - _t0).TotalSeconds + Offset;
                Func<double, double, double, double> w = (a, b, f) => a + (b - a) * (0.5 + 0.5 * Math.Sin(t * f)) + (_r.NextDouble() - 0.5) * (b - a) * 0.06;
                string cpu = "AMD Ryzen 7 7800X3D", gpu = "NVIDIA GeForce RTX 4070";
                var ms = new List<Metric> {
                    new Metric("lhm:/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", cpu, "temp", w(46, 80, 0.3), "°C", 38, 84, "lhm", "CPU"),
                    new Metric("lhm:/amdcpu/0/load/0", "CPU Total", cpu, "usage", w(4, 60, 0.5), "%", 0, 100, "lhm", "CPU"),
                    new Metric("lhm:/amdcpu/0/power/0", "Package", cpu, "power", w(28, 96, 0.3), "W", 20, 110, "lhm", "CPU"),
                    new Metric("lhm:/gpu-nvidia/0/temperature/0", "GPU Core", gpu, "temp", w(36, 70, 0.2), "°C", 30, 72, "lhm", "GPU"),
                    new Metric("lhm:/gpu-nvidia/0/load/0", "GPU Core", gpu, "usage", w(2, 90, 0.25), "%", 0, 100, "lhm", "GPU"),
                    new Metric("lhm:/gpu-nvidia/0/power/0", "GPU Package", gpu, "power", w(18, 190, 0.25), "W", 10, 200, "lhm", "GPU"),
                    new Metric("sys:cpu.clock", "CPU clock", "CPU (Windows)", "clock", w(3600, 5400, 0.4), "MHz", source: "windows"),
                    new Metric("sys:cpu.usage", "CPU load", "CPU (Windows)", "usage", w(4, 60, 0.5), "%", source: "windows"),
                    new Metric("sys:ram.pct", "Memory used", "Memory (Windows)", "usage", w(28, 31, 0.1), "%", source: "windows"),
                    new Metric("sys:ram.used", "Memory used", "Memory (Windows)", "data", w(9.0, 9.4, 0.1), "GB", 0, 31.9, "windows"),
                    new Metric("sys:disk.C.pct", "C: used", "Drives (Windows)", "usage", 33, "%", source: "windows"),
                    new Metric("sys:disk.C.free", "C: free", "Drives (Windows)", "data", 1242, "GB", 0, 1863, "windows"),
                    new Metric("sys:net.down", "Download", "Network (Windows)", "rate", Math.Abs(w(0.02, 0.6, 0.7)), "MB/s", source: "windows"),
                    new Metric("sys:net.up", "Upload", "Network (Windows)", "rate", Math.Abs(w(0.005, 0.05, 0.9)), "MB/s", source: "windows") };
                Status = new Dictionary<string, string> { { "lhm", "on · demo" }, { "windows", "demo" } };
                return ms.ToDictionary(m => m.Key, m => m);
            }
        }
    }

    /// <summary>--preview DIR: renders the widgets to PNG files without showing windows (used for checking the look).</summary>
    public static class Preview
    {
        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            Func<Dictionary<string, object>, Dictionary<string, object>, Cfg> mk = (def, over) =>
            {
                var d = Cfg.DeepCopy(def);
                foreach (var kv in over) d[kv.Key] = kv.Value;
                return new Cfg(d, Path.Combine(dir, "unused.json")) { Persist = false };
            };
            // battery widget
            foreach (var v in new[] { new object[] { "battery_dark", "Dark", "desktop", "horizontal" }, new object[] { "battery_gold", "Gold", "desktop", "horizontal" },
                                      new object[] { "battery_vertical", "Sakura", "desktop", "vertical" }, new object[] { "battery_taskbar", "Auto", "taskbar", "horizontal" } })
            {
                var c = mk(BatteryView.Defaults(), new Dictionary<string, object> { { "theme", v[1] }, { "mode", v[2] }, { "layout", v[3] } });
                if ((string)v[2] == "taskbar") WinUtil.SimTaskbar = new TaskbarInfo { Edge = "bottom", Rect = new Rectangle(0, 1032, 1920, 48) };
                var w = new BatteryView(c, null) { SimLight = (string)v[2] == "taskbar" ? (bool?)false : null };
                w.OnReadings(Demo.Readings());
                using (var b = w.RenderToBitmap()) b.Save(Path.Combine(dir, v[0] + ".png"));
                WinUtil.SimTaskbar = null;
            }
            // system monitor
            foreach (var v in new[] { new object[] { "system_card_gold", "Gold", "card", "desktop" }, new object[] { "system_card_twilight", "Twilight", "card", "desktop" },
                                      new object[] { "system_rings", "Dark", "rings", "desktop" }, new object[] { "system_taskbar", "Auto", "text", "taskbar" }, new object[] { "system_taskbar_rings", "Gold", "text", "taskbar" } })
            {
                var c = mk(SystemView.Defaults(), new Dictionary<string, object> { { "theme", v[1] }, { "style", v[2] }, { "mode", v[3] }, { "columns", 3 } });
                if ((string)v[3] == "taskbar") WinUtil.SimTaskbar = new TaskbarInfo { Edge = "bottom", Rect = new Rectangle(0, 1032, 1920, 48) };
                if ((string)v[0] == "system_taskbar_rings") c["tb_style"] = "ringsc";
                var hub = new Demo.Hub();
                var w = new SystemView(c, null) { SimLight = (string)v[3] == "taskbar" ? (bool?)false : null };
                var ms = hub.ReadAll();
                c["items"] = Fmt.Suggest(ms).Select(k => (object)new Dictionary<string, object> { { "key", k } }).ToList();
                if ((string)v[2] == "card")
                {
                    var items = c.List("items");
                    items.Add(new Dictionary<string, object> { { "key", "sys:disk.C.free" }, { "label", "C: free" } });
                    items.Add(new Dictionary<string, object> { { "key", "sys:ram.used" }, { "label", "RAM used" } });
                    items.RemoveAll(i => Cfg.S((Dictionary<string, object>)i, "key") == "sys:ram.pct" || Cfg.S((Dictionary<string, object>)i, "key") == "sys:disk.C.pct");
                }
                for (int i = 0; i < 60; i++) { hub.Offset = i * 0.7; w.OnMetrics(hub.ReadAll()); }
                using (var b = w.RenderToBitmap()) b.Save(Path.Combine(dir, v[0] + ".png"));
                WinUtil.SimTaskbar = null;
            }
            foreach (var v in new[] { new[] { "weather_card", "Twilight", "card", "desktop" }, new[] { "weather_gold", "Gold", "card", "desktop" },
                                      new[] { "weather_compact", "Dark", "compact", "desktop" }, new[] { "weather_taskbar", "Auto", "card", "taskbar" } })
            {
                var c = mk(WeatherView.Defaults(), new Dictionary<string, object> { { "theme", v[1] }, { "style", v[2] }, { "mode", v[3] } });
                if (v[3] == "taskbar") WinUtil.SimTaskbar = new TaskbarInfo { Edge = "bottom", Rect = new Rectangle(0, 1032, 1920, 48) };
                var w = new WeatherView(c) { SimLight = v[3] == "taskbar" ? (bool?)false : null };
                w.Arrived(Demo.WeatherJson(), new Place { Name = "Stockholm, Sweden" }, null);
                using (var b = w.RenderToBitmap()) b.Save(Path.Combine(dir, v[0] + ".png"));
                if (v[0] == "weather_gold")
                {
                    var det = new WeatherDetail(w);
                    using (var b = new Bitmap(det.Width, det.Height)) { using (var g = Graphics.FromImage(b)) { g.Clear(det.BackColor); det.PaintAll(g); } b.Save(Path.Combine(dir, "weather_detail.png")); }
                    using (var b = Toast.Preview("moon", "Screen turns off after 10 minutes", "When the PC is idle. Change it any time with the moon button.", w.MenuTheme)) b.Save(Path.Combine(dir, "toast.png"));
                }
                WinUtil.SimTaskbar = null;
            }
            Console.WriteLine("previews in " + dir);
            return 0;
        }
    }
}
