// Scanner.cs – "Find devices": runs every source, explains what's missing, lets you rename and restyle devices.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class SourceStatus { public string Id, Label, Status, Error, Note, Hint; public int Count, Online, Ms; }
    public class InvGroup { public string VidPid, Product = "", Manufacturer = "", Vendor, Hint, Kind; public int Vid, Pid; public bool Wireless; public List<HidInfo> Ifaces = new List<HidInfo>(); }
    public class ScanResult
    {
        public List<Reading> Readings = new List<Reading>();
        public List<SourceStatus> Sources = new List<SourceStatus>();
        public List<InvGroup> Unclaimed = new List<InvGroup>(), Inventory = new List<InvGroup>();
        public Dictionary<string, HashSet<string>> DeepFound = new Dictionary<string, HashSet<string>>();
        public bool Deep; public string Report = "";
    }

    public static class Scan
    {
        static readonly Dictionary<int, string> Vendors = new Dictionary<int, string> {
            { 0x046D, "Logitech" }, { 0x1532, "Razer" }, { 0x1038, "SteelSeries" }, { 0x1B1C, "Corsair" }, { 0x0951, "HyperX" }, { 0x03F0, "HyperX (HP)" },
            { 0x0B05, "ASUS" }, { 0x3151, "MonsGeek/Akko" }, { 0x145F, "Akko" }, { 0x342D, "Epomaker" }, { 0x347A, "Feker" }, { 0x374A, "Womier" },
            { 0x379A, "DrunkDeer" }, { 0x373B, "ATK/VXE" }, { 0x3554, "Compx (VXE…)" }, { 0x258A, "Sinowealth (Glorious, Redragon…)" },
            { 0x248A, "Telink (generic 2.4G)" }, { 0x3434, "Keychron" }, { 0x19F5, "NuPhy" }, { 0x31E3, "Wooting" }, { 0x1E7D, "ROCCAT" },
            { 0x054C, "Sony" }, { 0x045E, "Microsoft" }, { 0x2DC8, "8BitDo" }, { 0x3367, "Endgame Gear" }, { 0x361D, "Lamzu" },
            { 0x05AC, "Apple ID (also used by Keychron K-series)" }, { 0x3837, "MCHOSE" }, { 0x5253, "MCHOSE (Realtek)" } };
        static readonly Dictionary<int, string> Hints = new Dictionary<int, string> {
            { 0x046D, "Wake the device (move the mouse, press a key, put the headset on) and scan again." },
            { 0x1532, "Wake the device and scan again." },
            { 0x1038, "SteelSeries Arctis and Arctis Nova headsets are read directly – turn the headset on and scan again. Other SteelSeries models aren't supported yet." },
            { 0x0951, "HyperX Cloud II Wireless and Cloud Flight are read directly – turn the headset on and scan again." },
            { 0x03F0, "HyperX Cloud II / III / III S / Alpha / II Core Wireless are read directly – turn the headset on and scan again." },
            { 0x1B1C, "Corsair Void, Virtuoso and HS-series wireless headsets are read directly – turn the headset on. Corsair wireless mice and keyboards can't be read yet." },
            { 0x0B05, "Press a key to wake the keyboard and scan again." },
            { 0x3151, "Press a key on the keyboard and scan again – the dongle only knows the level after the keyboard sends it." },
            { 0x145F, "Press a key on the keyboard and scan again." }, { 0x342D, "Press a key on the keyboard and scan again." },
            { 0x373B, "Move the mouse to wake it and scan again." }, { 0x3554, "Move the mouse to wake it and scan again." },
            { 0x3434, "Move the mouse and scan again. Keychron keyboards on the Keychron Link can't report their battery with Keychron's normal firmware." },
            { 0x3837, "Move the mouse to wake it and scan again." }, { 0x054C, "Sony INZONE / Pulse headsets aren't supported yet." } };

        public static List<InvGroup> Inventory()
        {
            var groups = new Dictionary<string, InvGroup>();
            foreach (var e in HidBackend.Current.Enumerate())
            {
                InvGroup g;
                if (!groups.TryGetValue(e.VidPid, out g)) groups[e.VidPid] = g = new InvGroup { VidPid = e.VidPid, Vid = e.Vid, Pid = e.Pid };
                if (g.Product == "") g.Product = e.Product ?? "";
                if (g.Manufacturer == "") g.Manufacturer = e.Manufacturer ?? "";
                g.Ifaces.Add(e);
            }
            foreach (var g in groups.Values)
            {
                g.Vendor = Vendors.ContainsKey(g.Vid) ? Vendors[g.Vid] : (g.Manufacturer != "" ? g.Manufacturer : "Unknown");
                // wireless = the name says so, or it's a receiver/headset we know; wired keyboards (like a Corsair K70) never count
                g.Wireless = Prov.LooksWireless(g.Product, g.Manufacturer) || HeadsetProvider.Find(g.Vid, g.Pid) != null
                             || (g.Vid == 0x046D && g.Pid >= 0x0A00 && g.Pid <= 0x0BFF);
                g.Kind = Prov.GuessKind(g.Product + " " + g.Manufacturer);
                if (g.Kind == "other") g.Kind = g.Ifaces.Any(i => i.UsagePage == 1 && i.Usage == 2) ? "mouse" : g.Ifaces.Any(i => i.UsagePage == 1 && i.Usage == 6) ? "keyboard" : "other";
                bool vendorChannel = g.Ifaces.Any(i => i.UsagePage >= 0xFF00 || i.UsagePage == 0x008C || i.UsagePage == 0x01C0);
                g.Hint = !vendorChannel ? "This receiver only carries key presses and mouse movement – it has no channel to ask for the battery, so no app can read it. Over Bluetooth, Windows may show the level."
                    : Hints.ContainsKey(g.Vid) ? Hints[g.Vid] : "Unknown protocol. Try Deep scan; if it still isn't found, copy the report and share it.";
            }
            return groups.Values.OrderBy(g => !g.Wireless).ThenBy(g => g.Vendor.ToLowerInvariant()).ThenBy(g => g.Product.ToLowerInvariant()).ToList();
        }

        public static ScanResult All(List<Provider> provs, bool deep, Action<string> say)
        {
            var res = new ScanResult { Deep = deep };
            var all = new List<Reading>();
            foreach (var p in provs)
            {
                say?.Invoke("Checking " + p.Label + "…");
                var t0 = DateTime.UtcNow;
                p.Deep = deep;
                List<Reading> r;
                try { r = p.Run(true); } finally { p.Deep = false; }
                all.AddRange(r);
                res.Sources.Add(new SourceStatus { Id = p.Id, Label = p.Label, Status = p.Error != null ? "error" : r.Any(x => x.Online) ? "ok" : "none",
                    Count = r.Count, Online = r.Count(x => x.Online), Error = p.Error, Note = p.Note, Hint = p.Hint, Ms = (int)(DateTime.UtcNow - t0).TotalMilliseconds });
            }
            res.Readings = Prov.Dedupe(all);
            say?.Invoke("Listing USB devices…");
            res.Inventory = Inventory();
            var rx = new Regex(@"\b([0-9a-f]{4}):([0-9a-f]{4})\b");
            var claimed = new HashSet<string>(all.Select(r => rx.Match(r.Detail ?? "")).Where(m => m.Success).Select(m => m.Value));
            res.Unclaimed = res.Inventory.Where(g => g.Wireless && !claimed.Contains(g.VidPid) && g.Product.Trim() != "").ToList();
            foreach (var r in res.Readings)
            {
                var m = rx.Match(r.Detail ?? "");
                if (!m.Success) continue;
                int vid = Convert.ToInt32(m.Groups[1].Value, 16);
                if ((r.Source == "rongyuan" && !RongYuanProvider.Vids.Contains(vid)) || (r.Source == "compx" && !CompxProvider.Vids.Contains(vid)))
                {
                    if (!res.DeepFound.ContainsKey(r.Source)) res.DeepFound[r.Source] = new HashSet<string>();
                    res.DeepFound[r.Source].Add(m.Value);
                }
            }
            res.Report = Report(res);
            say?.Invoke("Done.");
            return res;
        }

        public static string Report(ScanResult res)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Battery Widget {Program.Version} scan – {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($".NET {Environment.Version} · {Environment.OSVersion}{(res.Deep ? " · deep scan" : "")} · admin: {(Native.IsAdmin ? "yes" : "no")}");
            sb.AppendLine().AppendLine("Sources");
            foreach (var s in res.Sources)
                sb.AppendLine($"  [{(s.Status == "ok" ? "ok " : s.Status == "error" ? "!! " : "-- ")}] {s.Label}: {s.Online} online, {s.Count} total{(s.Error ?? s.Note) is string x && x != "" ? " – " + x : ""}  ({s.Ms} ms)");
            sb.AppendLine().AppendLine("Devices with battery");
            foreach (var r in res.Readings)
                sb.AppendLine($"  {r.Name,-32} {r.Kind,-10} {(r.Percent == null ? "—" : (r.Approx ? "~" : "") + r.Percent + "%"),5} {(r.Charging ? "charging " : "")}{(r.Online ? "" : "offline ")}[{r.Source}] {r.Detail}");
            if (res.Readings.Count == 0) sb.AppendLine("  (none)");
            sb.AppendLine().AppendLine("Wireless-looking devices without a battery reading");
            foreach (var g in res.Unclaimed) sb.AppendLine($"  {g.VidPid}  {(g.Product == "" ? "?" : g.Product)} ({g.Vendor}) – {g.Hint}");
            if (res.Unclaimed.Count == 0) sb.AppendLine("  (none)");
            sb.AppendLine().AppendLine("All HID interfaces  (vid:pid  usage page/usage  interface  product)");
            foreach (var g in res.Inventory)
                foreach (var i in g.Ifaces.OrderBy(i => i.UsagePage).ThenBy(i => i.Usage))
                    sb.AppendLine($"  {g.VidPid}  {i.UsagePage:x4}/{i.Usage:x4}  if{i.Interface,-3} {g.Product} ({g.Manufacturer})");
            return sb.ToString();
        }
    }

    /// <summary>Dialogs in the widget's theme.</summary>
    public class ThemedForm : Form
    {
        protected Theme T;
        protected Color BgSolid, CardSolid, BorderSolid, SubSolid;
        protected ThemedForm(Theme t)
        {
            T = t;
            BgSolid = Color.FromArgb(255, t.Bg); if (t.Bg.A < 200) BgSolid = Color.FromArgb(20, 22, 29);
            CardSolid = Draw.Mix(BgSolid, Color.FromArgb(255, t.Card), t.Card.A / 255.0);
            BorderSolid = Draw.Mix(BgSolid, Color.FromArgb(255, t.Border), t.Border.A / 255.0);
            SubSolid = Draw.Mix(BgSolid, Color.FromArgb(255, t.Sub), t.Sub.A / 255.0);
            BackColor = BgSolid; ForeColor = Color.FromArgb(255, t.Text);
            Font = new Font(Fonts.Ui, 9.75f);
            Icon = Program.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
        }
        bool _scaled;
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!_scaled && WidgetWindow.Dpi > 1.01f) { _scaled = true; Scale(new SizeF(WidgetWindow.Dpi, WidgetWindow.Dpi)); }    // sizes were written for 100 %
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Desk.DarkTitle(this, T); }
        /// <summary>Rows made after the window opened need the same scaling.</summary>
        protected T0 Fit<T0>(T0 c) where T0 : Control { if (WidgetWindow.Dpi > 1.01f) c.Scale(new SizeF(WidgetWindow.Dpi, WidgetWindow.Dpi)); return c; }
        protected Label Lbl(string text, bool sub = false, float size = 9.75f, bool bold = false) =>
            new Label { Text = text, AutoSize = true, MaximumSize = new Size(Math.Max(200, ClientSize.Width - 40), 0), ForeColor = sub ? SubSolid : ForeColor, Font = new Font(Fonts.Ui, size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent };
        protected Button Btn(string text, bool primary = false)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 3, 8, 3), Cursor = Cursors.Hand,
                BackColor = primary ? Color.FromArgb(255, T.Accent) : CardSolid, ForeColor = primary ? (T.Accent.GetBrightness() > 0.6f ? Color.Black : Color.White) : ForeColor };
            b.FlatAppearance.BorderColor = primary ? Color.FromArgb(255, T.Accent) : BorderSolid;
            return b;
        }
        protected Panel Card()
        {
            var p = new Panel { BackColor = CardSolid, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 0, 6) };
            p.Paint += (s, e) => { using (var pen = new Pen(BorderSolid)) e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1); };
            return p;
        }
    }

    public class ScannerForm : ThemedForm
    {
        readonly BatteryView W;
        readonly FlowLayoutPanel _body;
        readonly Label _status;
        readonly Button _scan, _deep;
        ScanResult _result;
        bool _busy, _auto;

        public ScannerForm(BatteryView w) : base(w.MenuTheme)
        {
            W = w;
            Text = "Find devices – Battery Widget";
            ClientSize = new Size(760, 700);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(20, 14, 20, 12), BackColor = BgSolid };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl("Find devices", false, 16, true));
            var intro = Lbl("Wake your devices first: move the mouse, press a key, put the headset on. Then scan. Nothing is changed on your devices – the scanner only reads.", true);
            intro.MaximumSize = new Size(700, 0);
            root.Controls.Add(intro);
            var bar = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 4), WrapContents = false };
            _scan = Btn("Scan", true); _scan.Click += (s, e) => StartScan(false);
            _deep = Btn("Deep scan"); _deep.Click += (s, e) => StartScan(true);
            new ToolTip().SetToolTip(_deep, "Also asks receivers from unknown brands with the MonsGeek/Akko and ATK/VXE protocols.");
            _status = Lbl("", true); _status.Margin = new Padding(10, 8, 0, 0);
            bar.Controls.AddRange(new Control[] { _scan, _deep, _status });
            root.Controls.Add(bar);
            _body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = BgSolid };
            _body.Resize += (s, e) => { foreach (Control c in _body.Controls) c.Width = _body.ClientSize.Width - 24; };
            root.Controls.Add(_body);
            var foot = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var copy = Btn("Copy report"); copy.Click += (s, e) => { if (_result != null) { Clipboard.SetText(_result.Report); _status.Text = "Report copied to the clipboard."; } else _status.Text = "Scan first, then copy the report."; };
            var done = Btn("Done"); done.Click += (s, e) => Close();
            foot.Controls.AddRange(new Control[] { copy, done });
            root.Controls.Add(foot);
            Controls.Add(root);
            Rebuild();
            Shown += (s, e) => { if (!_auto) { _auto = true; StartScan(false); } };
        }

        public void OnLiveUpdate() { if (Visible && !_busy) BeginInvoke((Action)Rebuild); }

        void Section(string title, string sub = null)
        {
            var l = Lbl(title, false, 11, true); l.Margin = new Padding(0, 10, 0, 2); _body.Controls.Add(l);
            if (sub != null) { var s = Lbl(sub, true); s.MaximumSize = new Size(690, 0); _body.Controls.Add(s); }
        }

        void Rebuild()
        {
            _body.SuspendLayout();
            foreach (Control c in _body.Controls.Cast<Control>().ToList()) { _body.Controls.Remove(c); c.Dispose(); }
            var known = W.C.Dict("known");
            var live = W.Last.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
            if (_result != null) foreach (var r in _result.Readings) live[r.Key] = r;
            var keys = known.Keys.Concat(live.Keys).Distinct().ToList();
            Section("Your devices", keys.Count == 0 ? "Nothing yet. Press Scan – and if something is still missing, check the tips further down."
                                                     : "Rename a device, pick its icon and color, or untick to hide it.");
            foreach (var k in keys) _body.Controls.Add(Fit(DeviceRow(k, known.ContainsKey(k) ? known[k] as Dictionary<string, object> : null, live.ContainsKey(k) ? live[k] : null)));
            if (_result != null && _result.Unclaimed.Count > 0)
            {
                Section("Found, but no battery reading yet");
                foreach (var g in _result.Unclaimed)
                {
                    var p = Card(); p.Height = 76;
                    var t = Lbl(g.Product == "" ? "Unnamed device" : g.Product, false, 10, true); t.Location = new Point(52, 6);
                    var m = Lbl($"{g.Vendor} · {g.VidPid}", true, 8.5f); m.Location = new Point(52, 26);
                    var h = Lbl(g.Hint); h.Location = new Point(52, 44); h.MaximumSize = new Size(620, 0);
                    p.Controls.AddRange(new Control[] { IconBox(g.Kind, 0, null), t, m, h });
                    p.Height = Math.Max(76, h.Bottom + 8);
                    _body.Controls.Add(Fit(p));
                }
            }
            Section("Sources");
            if (_result == null) _body.Controls.Add(Lbl("Scan to check every source.", true));
            else foreach (var s in _result.Sources.Where(x => !((x.Id == "headsetcontrol" || x.Id == "ghub") && x.Status != "ok" && x.Error == null)))
                _body.Controls.Add(Fit(SourceRow(s)));
            foreach (Control c in _body.Controls) c.Width = _body.ClientSize.Width - 24;
            _body.ResumeLayout();
        }

        PictureBox IconBox(string kind, int variant, Color? color)
        {
            var bmp = new Bitmap(34, 34);
            using (var g = Graphics.FromImage(bmp))
            {
                Draw.Hq(g);
                var acc = color ?? Color.FromArgb(255, T.Accent);
                using (var b = new SolidBrush(Draw.A(acc, 44))) using (var p = Draw.Round(new RectangleF(0, 0, 34, 34), 9)) g.FillPath(b, p);
                Draw.DeviceIcon(g, kind, new RectangleF(7, 7, 20, 20), acc, variant);
            }
            return new PictureBox { Image = bmp, Size = new Size(34, 34), Location = new Point(10, 8), BackColor = Color.Transparent };
        }

        Panel DeviceRow(string key, Dictionary<string, object> info, Reading r)
        {
            var ov = Cfg.SubDict(W.C.Dict("overrides"), key);
            string origName = r?.Name ?? Cfg.S(info, "name", key), origKind = r?.Kind ?? Cfg.S(info, "kind", "other");
            var row = Card(); row.Height = 52;
            Func<Reading> probe = () => new Reading(key, origName, Cfg.S(ov, "kind") ?? origKind);
            int v; Color? c; W.IconSpec(W.Display(probe()), out v, out c);
            var icon = IconBox(Cfg.S(ov, "kind") ?? origKind, v, c);
            var name = new TextBox { Text = Cfg.S(ov, "name") ?? origName, BorderStyle = BorderStyle.None, BackColor = CardSolid, ForeColor = ForeColor,
                Font = new Font(Fonts.Ui, 10.5f, FontStyle.Bold), Location = new Point(54, 8), Width = 270 };
            var meta = Lbl(r != null ? (r.Detail != "" ? r.Detail : r.Source) : "not seen since the last restart", true, 8.5f); meta.Location = new Point(54, 30);
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = BgSolid, ForeColor = ForeColor, Width = 170,
                Location = new Point(340, 13), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
            foreach (var d in Draw.DeviceIcons) combo.Items.Add(d);
            combo.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var d = (string[])combo.Items[e.Index];
                e.DrawBackground();
                using (var pv = Draw.Preview(false, d[0], int.Parse(d[1]), Color.FromArgb(255, T.Text), 18)) e.Graphics.DrawImage(pv, e.Bounds.X + 2, e.Bounds.Y + 1);
                TextRenderer.DrawText(e.Graphics, d[2], combo.Font, new Point(e.Bounds.X + 24, e.Bounds.Y + 2), (e.State & DrawItemState.Selected) != 0 ? Color.White : ForeColor);
            };
            string curKind = Cfg.S(ov, "kind") ?? origKind;
            int idx = Array.FindIndex(Draw.DeviceIcons, d => d[0] == curKind && int.Parse(d[1]) == v);
            if (idx < 0) idx = Array.FindIndex(Draw.DeviceIcons, d => d[0] == curKind);
            combo.SelectedIndex = Math.Max(0, idx);
            var colorBtn = Btn(""); colorBtn.AutoSize = false; colorBtn.Size = new Size(30, 26); colorBtn.Location = new Point(518, 12);
            colorBtn.Image = Draw.Swatch(c ?? Color.FromArgb(255, T.Text));
            new ToolTip().SetToolTip(colorBtn, "Icon color for this device · right-click to reset");
            int? pct = r?.Percent ?? (Cfg.N(info, "percent").HasValue ? (int?)(int)Cfg.N(info, "percent").Value : null);
            var pl = Lbl(pct == null ? "—" : ((r != null && r.Approx) ? "~" : "") + pct + "%", !(r != null && r.Online), 12, true); pl.Location = new Point(560, 13);
            var show = new CheckBox { Text = "Show", Checked = !W.C.Strings("hidden").Contains(key), ForeColor = SubSolid, Location = new Point(625, 15), AutoSize = true };
            Action refreshIcon = () =>
            {
                int vv; Color? cc; var dr = W.Display(new Reading(key, origName, Cfg.S(ov, "kind") ?? origKind));
                W.IconSpec(dr, out vv, out cc);
                icon.Image = IconBox(dr.Kind, vv, cc).Image; colorBtn.Image = Draw.Swatch(cc ?? Color.FromArgb(255, T.Text));
            };
            Action store = () =>
            {
                var all = W.C.Dict("overrides");
                if (ov.Count > 0) all[key] = ov; else all.Remove(key);
                W.Save(); W.OnReadings(W.Last);
            };
            name.Leave += (s, e) =>
            {
                string val = name.Text.Trim();
                if (val == "" || val == origName) { ov.Remove("name"); name.Text = origName; } else ov["name"] = val;
                store();
            };
            combo.SelectedIndexChanged += (s, e) =>
            {
                var d = (string[])combo.SelectedItem;
                if (d[0] == origKind) ov.Remove("kind"); else ov["kind"] = d[0];
                ov["variant"] = int.Parse(d[1]);
                store(); refreshIcon();
            };
            colorBtn.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) { if (ov.Remove("color")) { store(); refreshIcon(); } return; }
                int vv; Color? cc; W.IconSpec(W.Display(probe()), out vv, out cc);
                using (var dlg = new ColorDialog { Color = cc ?? Color.FromArgb(255, T.Accent), FullOpen = true })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { ov["color"] = Theme.Hex(dlg.Color); store(); refreshIcon(); }
            };
            show.CheckedChanged += (s, e) =>
            {
                var h = W.C.Strings("hidden");
                if (show.Checked) h.Remove(key); else if (!h.Contains(key)) h.Add(key);
                W.C.SetStrings("hidden", h); W.Save(); W.OnReadings(W.Last);
            };
            row.Controls.AddRange(new Control[] { icon, name, meta, combo, colorBtn, pl, show });
            return row;
        }

        Panel SourceRow(SourceStatus s)
        {
            var p = Card(); p.Height = 48;
            var dot = Lbl("●", false, 10); dot.ForeColor = s.Status == "ok" ? T.Good : s.Status == "error" ? T.Bad : SubSolid; dot.Location = new Point(10, 7);
            var t = Lbl(s.Label, false, 10, true); t.Location = new Point(30, 5);
            string msg = s.Status == "ok" ? (s.Note != "" ? s.Note : s.Online + " device(s)") : s.Status == "error" ? s.Error : (s.Note != "" ? s.Note + ". " : "") + s.Hint;
            var m = Lbl(msg, true, 8.75f); m.Location = new Point(30, 25); m.MaximumSize = new Size(500, 0);
            p.Controls.AddRange(new Control[] { dot, t, m });
            p.Height = Math.Max(48, m.Bottom + 8);
            return p;
        }

        void StartScan(bool deep)
        {
            if (_busy) return;
            _busy = true; _scan.Enabled = _deep.Enabled = false;
            _status.Text = deep ? "Deep scan… this takes a little longer" : "Scanning…";
            var provs = W.Poller?.Providers ?? Poller.Make();
            new Thread(() =>
            {
                ScanResult res = null; string err = null;
                try { res = Scan.All(provs, deep, m => BeginInvoke((Action)(() => _status.Text = m))); }
                catch (Exception ex) { err = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    _busy = false; _scan.Enabled = _deep.Enabled = true;
                    if (res == null) { _status.Text = "The scan failed: " + err; return; }
                    _result = res;
                    W.ApplyScan(res);
                    int n = res.Readings.Count(r => r.Online), extra = res.Unclaimed.Count;
                    _status.Text = $"{n} device{(n == 1 ? "" : "s")} with battery" + (extra > 0 ? $" · {extra} more need{(extra == 1 ? "s" : "")} a look" : "");
                    Rebuild();
                }));
            }) { IsBackground = true }.Start();
        }

        void Download(Button b)
        {
            b.Enabled = false; b.Text = "Downloading…";
            new Thread(() =>
            {
                string err = null;
                try { HeadsetControlProvider.Download(m => BeginInvoke((Action)(() => _status.Text = m))); } catch (Exception ex) { err = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    if (err == null) { _status.Text = "HeadsetControl installed – scanning again…"; StartScan(false); }
                    else { _status.Text = "Download failed (" + err + "). Opening the releases page instead."; try { System.Diagnostics.Process.Start(HeadsetControlProvider.Releases); } catch { } b.Enabled = true; b.Text = "Download HeadsetControl"; }
                }));
            }) { IsBackground = true }.Start();
        }
    }
}
