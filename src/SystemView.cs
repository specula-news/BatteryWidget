// SystemView.cs – the system monitor: a card (CPU/GPU panels with history, network, bars), rings or text.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class Tile { public string Key, Kind, Label, Text, Unit, Level, Color, Template, Tip; public double? Frac; public bool Online; public Metric M; }

    public class Sampler
    {
        public readonly SensorHub Hub;
        readonly Cfg _c;
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        volatile bool _stop;
        public event Action<Dictionary<string, Metric>> Updated;
        public Sampler(Cfg c, SensorHub hub) { _c = c; Hub = hub; }
        public void Start() => new Thread(() =>
        {
            while (!_stop)
            {
                try { Updated?.Invoke(Hub.ReadAll()); } catch { }
                _wake.WaitOne(Math.Max(250, _c.Int("interval_ms", 1000)));
            }
        }) { IsBackground = true, Name = "sensor-sampler" }.Start();
        public void RefreshNow() => _wake.Set();
        public void Stop() { _stop = true; _wake.Set(); }
    }

    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string N(double v, int d) => v.ToString("F" + d, Inv);

        public static string Device(Metric m)
        {
            if (!string.IsNullOrEmpty(m.Device)) return m.Device;
            string g = m.Group.ToLowerInvariant(), n = m.Name.ToLowerInvariant();
            if (m.Key.StartsWith("sys:"))
            {
                string p = m.Key.Length >= 7 ? m.Key.Substring(0, 7) : m.Key;
                return p == "sys:cpu" ? "CPU" : p == "sys:gpu" ? "GPU" : p == "sys:ram" ? "RAM" : p == "sys:dis" ? "Disk" : p == "sys:net" ? "Net" : p == "sys:bat" ? "Battery" : "";
            }
            if (g.StartsWith("gpu") || n.Contains("gpu")) return "GPU";
            if (g.StartsWith("cpu") || new[] { "cpu", "core", "tctl", "package", "ccd" }.Any(n.Contains)) return "CPU";
            if (g.Contains("memory") || g.Contains("dimm") || n.Contains("memory")) return "RAM";
            return "";
        }

        public static string Label(Metric m)
        {
            var sys = new Dictionary<string, string> { { "sys:cpu.usage", "CPU load" }, { "sys:cpu.clock", "CPU clock" }, { "sys:gpu.usage", "GPU load" },
                { "sys:ram.pct", "RAM" }, { "sys:ram.used", "RAM used" }, { "sys:disk.read", "Disk read" }, { "sys:disk.write", "Disk write" },
                { "sys:net.down", "Down" }, { "sys:net.up", "Up" }, { "sys:battery", "Battery" } };
            if (m.Key.StartsWith("sys:")) return sys.ContainsKey(m.Key) ? sys[m.Key] : m.Name;
            string dev = Device(m);
            if (m.Kind == "fan")
            {
                var lab = System.Text.RegularExpressions.Regex.Replace(m.Name.Replace("#", ""), @"\bfan\b", "fan", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                lab = System.Text.RegularExpressions.Regex.Replace(lab, @"\s+", " ").Trim();
                if (lab.ToLowerInvariant().StartsWith("fan")) lab = "Fan" + lab.Substring(3);
                return lab.Length <= 16 ? lab : lab.Substring(0, 15) + "…";
            }
            var words = new Dictionary<string, string> { { "temp", "temp" }, { "power", "power" }, { "clock", "clock" }, { "usage", "load" }, { "voltage", "volt" }, { "current", "amps" } };
            if (dev != "" && words.ContainsKey(m.Kind)) return dev + " " + words[m.Kind];
            return m.Name.Length <= 16 ? m.Name : m.Name.Substring(0, 15) + "…";
        }

        public static string Icon(Metric m)
        {
            if (m.Key == "sys:net.down") return "down";
            if (m.Key == "sys:net.up") return "up";
            if (m.Key == "sys:disk.read") return "disk";
            if (m.Kind == "fan") return "fan";
            var byDev = new Dictionary<string, string> { { "CPU", "cpu" }, { "GPU", "gpu" }, { "RAM", "ram" }, { "Disk", "disk" }, { "Net", "net" }, { "Battery", "battery" }, { "PSU", "power" }, { "Cooler", "fan" } };
            string dev = Device(m);
            if (byDev.ContainsKey(dev)) return byDev[dev];
            var byKind = new Dictionary<string, string> { { "temp", "temp" }, { "power", "power" }, { "clock", "clock" }, { "usage", "usage" }, { "voltage", "volt" }, { "current", "volt" }, { "rate", "net" }, { "data", "disk" } };
            return byKind.ContainsKey(m.Kind) ? byKind[m.Kind] : "dot";
        }

        /// <summary>("64","°C"), ("4.85","GHz"), ("1.21","TB") …</summary>
        public static string[] Value(Metric m)
        {
            if (!m.Value.HasValue) return new[] { "–", "" };
            double v = m.Value.Value; string u = (m.Unit ?? "").Trim(), k = m.Kind;
            if (k == "temp") return new[] { N(v, 0), u.ToLowerInvariant().Contains("f") ? "°F" : "°C" };
            if (u == "%") return new[] { N(v, 0), "%" };
            if (k == "clock") { double mhz = u.ToLowerInvariant() == "ghz" ? v * 1000 : v; return mhz >= 1000 ? new[] { N(mhz / 1000, 2), "GHz" } : new[] { N(mhz, 0), "MHz" }; }
            if (k == "power") return new[] { N(v, Math.Abs(v) < 10 ? 1 : 0), u == "" ? "W" : u };
            if (k == "fan") return new[] { N(v, 0), "rpm" };
            if (k == "voltage") return new[] { N(v, Math.Abs(v) < 2 ? 3 : 2), u == "" ? "V" : u };
            if (k == "rate")
            {
                double mb = u.ToUpperInvariant().StartsWith("KB") ? v / 1024 : u.ToUpperInvariant().StartsWith("GB") ? v * 1024 : v;
                return mb < 1 ? new[] { N(mb * 1024, 0), "KB/s" } : new[] { N(mb, mb < 10 ? 1 : 0), "MB/s" };
            }
            if (k == "data")
            {
                var sc = new Dictionary<string, double> { { "KB", 1 / 1048576.0 }, { "MB", 1 / 1024.0 }, { "GB", 1 }, { "TB", 1024 } };
                double s;
                if (!sc.TryGetValue(u.ToUpperInvariant(), out s)) return new[] { N(v, Math.Abs(v) < 10 ? 1 : 0), u };
                double gb = v * s;
                if (Math.Abs(gb) >= 1000) { double tb = gb / 1024; return new[] { N(tb, Math.Abs(tb) < 10 ? 2 : Math.Abs(tb) < 100 ? 1 : 0), "TB" }; }
                if (Math.Abs(gb) >= 1) return new[] { N(gb, Math.Abs(gb) < 10 ? 1 : 0), "GB" };
                return new[] { N(gb * 1024, 0), "MB" };
            }
            double a = Math.Abs(v);
            return new[] { N(v, a >= 100 || v == Math.Floor(v) ? 0 : a >= 10 ? 1 : 2), u };
        }

        public static string Template(Metric m, string text)
        {
            if (m == null) return "888";
            if (m.Unit == "%") return "100";
            var t = new Dictionary<string, string> { { "temp", "188" }, { "clock", "8.88" }, { "power", "888" }, { "fan", "8888" }, { "voltage", "8.888" }, { "rate", "888" }, { "data", "8888" } };
            return t.ContainsKey(m.Kind) ? t[m.Kind] : new string(text.Select(ch => char.IsDigit(ch) ? '8' : ch).ToArray()) + "8";
        }

        /// <summary>(ring max, warn, critical, low is bad)</summary>
        public static Tuple<double?, double?, double?, bool> Limits(Metric m, Dictionary<string, object> it, double peak)
        {
            double? mx = null, warn = null, crit = null; bool low = false;
            string k = m.Kind;
            if (k == "temp") { mx = 100; warn = 75; crit = 90; }
            else if (m.Unit == "%")
            {
                mx = 100;
                if (m.Key == "sys:battery") { warn = 25; crit = 10; low = true; }
                else if (k == "usage" && (m.Key.StartsWith("sys:disk") || m.Key.StartsWith("sys:ram") || m.Name.ToLowerInvariant().Contains("memory"))) { warn = 85; crit = 95; }
            }
            else if (k == "data" && m.VMax.HasValue)
            {
                mx = m.VMax;
                if (m.Key.EndsWith(".free")) { warn = 0.15 * m.VMax; crit = 0.05 * m.VMax; low = true; } else { warn = 0.85 * m.VMax; crit = 0.95 * m.VMax; }
            }
            else if (k == "clock") mx = Math.Max(Math.Max(peak, m.VMax ?? 0), 1000);
            else if (k == "power") mx = Math.Max(Math.Max(peak, m.VMax ?? 0), 50);
            else if (k == "fan") mx = Math.Max(Math.Max(peak, m.VMax ?? 0), 1500);
            else if (k == "rate") mx = Math.Max(peak, 1);
            else mx = Math.Max(Math.Max(peak, m.VMax ?? 0), Math.Max(Math.Abs(m.Value ?? 0), 1e-9));
            double? umax = Cfg.N(it, "max"), uw = Cfg.N(it, "warn"), uc = Cfg.N(it, "crit");
            return Tuple.Create(umax ?? mx, it.ContainsKey("warn") ? uw : warn, it.ContainsKey("crit") ? uc : crit, low);
        }

        public static string Level(double v, double? warn, double? crit, bool low)
        {
            if (low) { if (crit.HasValue && v <= crit) return "bad"; if (warn.HasValue && v <= warn) return "warn"; }
            else { if (crit.HasValue && v >= crit) return "bad"; if (warn.HasValue && v >= warn) return "warn"; }
            return warn.HasValue || crit.HasValue ? "good" : "none";
        }

        static string Find(Dictionary<string, Metric> ms, string kind, string dev, params string[] names)
        {
            var cands = ms.Values.Where(m => m.Kind == kind && Device(m) == dev && !m.Key.StartsWith("sys:")).ToList();
            foreach (var w in names) { var m = cands.FirstOrDefault(c => c.Name.StartsWith(w, StringComparison.OrdinalIgnoreCase)); if (m != null) return m.Key; }
            return null;
        }

        public static List<string> Suggest(Dictionary<string, Metric> ms)
        {
            var picks = new[] {
                Find(ms, "temp", "CPU", "CPU Package", "CPU (Tctl/Tdie)", "Core (Tctl/Tdie)", "Package", "Core (Tctl)", "CPU Die (average)", "Core Max", "Core Average", "CPU"),
                Find(ms, "usage", "CPU", "Total CPU Usage", "CPU Total") ?? "sys:cpu.usage",
                Find(ms, "clock", "CPU", "Average Effective Clock", "Cores (Average", "Core Clocks", "Core 0 Clock") ?? "sys:cpu.clock",
                Find(ms, "power", "CPU", "CPU Package Power", "Package", "CPU PPT", "CPU Power", "CPU Cores"),
                Find(ms, "temp", "GPU", "GPU Temperature", "GPU Core", "GPU Hot Spot"),
                Find(ms, "usage", "GPU", "GPU Core Load", "GPU Core", "GPU D3D Usage", "GPU Utilization", "D3D 3D") ?? "sys:gpu.usage",
                Find(ms, "power", "GPU", "GPU Power", "GPU Package", "Total Board Power", "GPU Power (Total)", "TGP"),
                "sys:ram.pct",
                ms.ContainsKey("sys:disk.C.pct") ? "sys:disk.C.pct" : ms.Keys.FirstOrDefault(k => k.StartsWith("sys:disk.") && k.EndsWith(".pct")),
                "sys:net.down", "sys:net.up" };
            var outp = new List<string>();
            foreach (var p in picks) if (p != null && ms.ContainsKey(p) && !outp.Contains(p)) outp.Add(p);
            return outp;
        }
    }

    public class SystemView : WidgetWindow
    {
        public static Dictionary<string, object> Defaults() => new Dictionary<string, object> {
            { "theme", "Twilight" }, { "accent", null }, { "mode", "desktop" }, { "style", "card" }, { "columns", 0 }, { "show_labels", true },
            { "scale", 1.0 }, { "opacity", 1.0 }, { "stacking", "bottom" }, { "background", true }, { "tb_background", false }, { "tb_offset", null },
            { "tb_embed", true }, { "tb_hide_fullscreen", true }, { "tb_rows", 2 }, { "tb_style", "icons" }, { "tb_max", 6 }, { "locked", false }, { "pos", null }, { "interval_ms", 1000 },
            { "ring_mode", "level" }, { "ring_color", "#FF8A3D" }, { "items", new List<object>() }, { "first_run", true }, { "card_v", 1 }, { "show_moon", false } };

        public readonly Sampler Sampler;
        public Dictionary<string, Metric> Metrics = new Dictionary<string, Metric>();
        public List<Tile> Tiles = new List<Tile>();
        public readonly Dictionary<string, double> Peaks = new Dictionary<string, double>();
        readonly Dictionary<string, Queue<double>> _hist = new Dictionary<string, Queue<double>>();
        PickerForm _picker;
        int _updates; bool _suggested;
        string _geo = "";
        float _D, _fp, _lp, _gap, _isz, _lw, _vw; bool _labels; int _rows;
        readonly List<Block> _blocks = new List<Block>();
        readonly List<float> _dividers = new List<float>();
        float _cardM, _cardX, _cardInner;

        class Block { public string Type, Dev; public RectangleF R; public List<Tile> Tiles = new List<Tile>(); public Dictionary<string, Tile> Slots = new Dictionary<string, Tile>(); public Dictionary<string, string> SlotKind = new Dictionary<string, string>(); public List<Tile> Extras = new List<Tile>(); }

        public SystemView(Cfg cfg, Sampler sampler) : base(cfg)
        {
            Sampler = sampler;
            Text = "System monitor";
            if (sampler != null) sampler.Updated += ms => { if (IsHandleCreated) BeginInvoke((Action)(() => OnMetrics(ms))); };
        }

        protected override int DefaultTopOffset => 140;
        public override void RefreshNow() => Sampler?.RefreshNow();
        protected override bool ReorderVertical => TaskbarMode ? !Tb.Horizontal : C.Int("columns") == 1 || C.Str("style") == "card";

        public List<Dictionary<string, object>> Items => C.List("items").OfType<Dictionary<string, object>>().ToList();

        public void OnMetrics(Dictionary<string, Metric> ms)
        {
            Metrics = ms;
            foreach (var m in ms.Values) if (m.Value.HasValue) Peaks[m.Key] = Math.Max(Peaks.ContainsKey(m.Key) ? Peaks[m.Key] : 0, m.Value.Value);
            foreach (var it in Items)
            {
                Metric m; string key = Cfg.S(it, "key");
                if (key != null && ms.TryGetValue(key, out m) && m.Value.HasValue)
                {
                    Queue<double> q;
                    if (!_hist.TryGetValue(key, out q)) _hist[key] = q = new Queue<double>();
                    q.Enqueue(m.Value.Value); while (q.Count > 60) q.Dequeue();
                }
            }
            _updates++;
            bool ready = ms.ContainsKey("sys:net.down") || _updates >= 3;
            if (Items.Count == 0 && C.Bool("first_run", true) && !_suggested && ms.Count > 0 && ready)
            {
                _suggested = true;
                C["items"] = Fmt.Suggest(ms).Select(k => (object)new Dictionary<string, object> { { "key", k } }).ToList();
                C["first_run"] = false;
                Save();
                Later(400, OpenPicker);
            }
            Rebuild();
            if (_picker != null && !_picker.IsDisposed && _picker.Visible) _picker.OnMetrics(ms);
            HintOnce();
        }

        public void Rebuild() { Tiles = BuildTiles(); Relayout(); }

        List<Tile> BuildTiles()
        {
            var outp = new List<Tile>();
            foreach (var it in Items)
            {
                string key = Cfg.S(it, "key");
                if (key == null) continue;
                Metric m; Metrics.TryGetValue(key, out m);
                string label = Cfg.S(it, "label") ?? (m != null ? Fmt.Label(m) : key.Split('|').Last());
                string icon = Cfg.S(it, "icon") ?? (m != null ? Fmt.Icon(m) : "dot");
                if (m == null || !m.Value.HasValue)
                {
                    outp.Add(new Tile { Key = key, Kind = icon, Label = label, Text = "–", Unit = "", Level = "none", Online = false, Color = Cfg.S(it, "color"), Template = "888",
                        Tip = label + "\nNo value right now" + (key.StartsWith("sys:") ? "" : " – CPU sensors need administrator rights and PawnIO") });
                    continue;
                }
                var tu = Fmt.Value(m);
                var lim = Fmt.Limits(m, it, Peaks.ContainsKey(key) ? Peaks[key] : 0);
                double? frac = lim.Item1 > 0 ? Math.Max(0, Math.Min(1, m.Value.Value / lim.Item1.Value)) : (double?)null;
                string extra = "";
                if (m.VMin.HasValue && m.VMax.HasValue && m.Source == "lhm")
                {
                    var lo = Fmt.Value(new Metric(m.Key, m.Name, m.Group, m.Kind, m.VMin, m.Unit)); var hi = Fmt.Value(new Metric(m.Key, m.Name, m.Group, m.Kind, m.VMax, m.Unit));
                    extra = $"\nmin {lo[0]}{tu[1]} · max {hi[0]}{tu[1]}";
                }
                outp.Add(new Tile { Key = key, Kind = icon, Label = label, Text = tu[0], Unit = tu[1], Frac = frac, Level = Fmt.Level(m.Value.Value, lim.Item2, lim.Item3, lim.Item4),
                    Online = true, Color = Cfg.S(it, "color"), Template = Fmt.Template(m, tu[0]), Tip = $"{label}: {tu[0]} {tu[1]}\n{m.Group}\n{m.Name}{extra}", M = m });
            }
            return outp;
        }

        bool _hinted;
        /// <summary>A chosen CPU value can't be read: say once what to do about it.</summary>
        void HintOnce()
        {
            if (_hinted || Suite == null || _updates < 3) return;
            string st = null;
            if (Sampler == null || !Sampler.Hub.Status.TryGetValue("lhm", out st) || st == null) return;
            bool missing = Tiles.Any(t => !t.Online && t.Key.StartsWith("lhm:"));
            if (!missing) return;
            _hinted = true;
            if (st.Contains("no admin")) Suite.Notify("CPU temperature needs administrator rights: right-click → Run as administrator (CPU temperatures).", true, "System monitor");
            else if (st.Contains("PawnIO")) Suite.Notify("CPU temperature needs the free PawnIO driver: right-click → Choose what to show… → Install PawnIO.", true, "System monitor");
            else if (st.Contains("not responding")) Suite.Notify("PawnIO is installed but not answering yet – restart Windows once.", true, "System monitor");
        }

        // ---- colors ---------------------------------------------------------------------------------
        Color TileRing(Tile t)
        {
            if (t.Level == "bad") return Pal.Bad;
            if (!string.IsNullOrEmpty(t.Color)) try { return Theme.C(t.Color); } catch { }
            string mode = C.Str("ring_mode", "level");
            if (mode == "custom") try { return Theme.C(C.Str("ring_color", "#FF8A3D")); } catch { }
            if (mode == "level" && t.Level == "good") return Pal.Good;
            if (mode == "level" && t.Level == "warn") return Pal.Warn;
            return Pal.Accent;
        }
        Color TileValue(Tile t) => t.Level == "bad" ? Pal.Bad : t.Level == "warn" && C.Str("ring_mode", "level") == "level" ? Pal.Warn : Pal.Text;
        bool GoldFor(Tile t) => Pal.Metal && t.Level != "warn" && t.Level != "bad" && string.IsNullOrEmpty(t.Color);

        // ---- layout -----------------------------------------------------------------------------------
        static readonly Tile ScanTile = new Tile { Key = "__scan__", Kind = "__scan__", Label = "Choose", Text = "+", Unit = "", Level = "none", Online = true, Template = "8", Tip = "Choose what to show" };

        float TextW(Graphics g, Tile t, float fp) => Fonts.Width(g, t.Template, Fonts.Get(fp, FontStyle.Regular, true, true)) + Fonts.Width(g, t.Unit ?? "", Fonts.Get(fp * 0.62f, FontStyle.Regular, true)) + fp * 0.12f;
        float LabelW(Graphics g, Tile t, float lp) => Fonts.Width(g, t.Label, Fonts.Get(lp)) + 3;
        void Add(RectangleF r, Tile t) => Cells.Add(new Cell { R = r, Key = t.Key, Kind = t.Kind, Tip = t.Tip, Item = t });

        protected override SizeF DoLayout(Graphics g)
        {
            var tiles = Tiles.Count > 0 ? Tiles : new List<Tile> { ScanTile };
            if (TaskbarMode) return LayoutTaskbar(g, tiles);
            if (C.Str("style", "card") == "card") return LayoutCard(g, tiles);
            return LayoutDesktop(g, tiles);
        }

        SizeF LayoutDesktop(Graphics g, List<Tile> tiles)
        {
            float s = S;
            bool text = C.Str("style") == "text";
            _labels = C.Bool("show_labels", true);
            int n = tiles.Count, cols = C.Int("columns");
            cols = cols <= 0 ? n : Math.Max(1, Math.Min(cols, n));
            int rows = (int)Math.Ceiling(n / (double)cols);
            float padx = 12 * s, pady = 10 * s, cw, ch, gx, gy;
            if (text)
            {
                _fp = 14 * s; _lp = 11.5f * s; _isz = 16 * s;
                _lw = _labels ? tiles.Max(t => LabelW(g, t, _lp)) : 0; _vw = tiles.Max(t => TextW(g, t, _fp));
                cw = _isz + 8 * s + (_labels ? _lw + 14 * s : 0) + _vw; ch = Math.Max(_isz, _fp * 1.35f); gx = 22 * s; gy = 5 * s; _geo = "text";
            }
            else
            {
                _D = 32 * s; _fp = 15 * s; _lp = 10.5f * s; _gap = 8 * s;
                float tw = tiles.Max(t => Math.Max(TextW(g, t, _fp), _labels ? LabelW(g, t, _lp) : 0));
                cw = _D + _gap + tw; ch = Math.Max(_D, _fp * 1.2f + (_labels ? _lp * 1.35f : 0)); gx = 18 * s; gy = 10 * s; _geo = "rings";
            }
            for (int i = 0; i < n; i++) { int r = i / cols, c = i % cols; Add(new RectangleF(padx + c * (cw + gx), pady + r * (ch + gy), cw, ch), tiles[i]); }
            float w = padx * 2 + cols * cw + (cols - 1) * gx, h = pady * 2 + rows * ch + (rows - 1) * gy;
            _radius = rows == 1 ? Math.Min(h / 2, 22 * s) : 16 * s;
            return new SizeF(w, h);
        }
        float _radius;

        SizeF LayoutTaskbar(Graphics g, List<Tile> tiles)
        {
            float T = Tb.Thickness, sc = (float)C.Num("scale", 1);
            string tbs = C.Str("tb_style", "icons");
            int max = C.Int("tb_max", 6);
            if (max > 0 && tiles.Count > max) tiles = tiles.Take(max).ToList();       // the rest stays on the desktop card
            bool labels = C.Bool("show_labels", true), text = tbs != "rings" && tbs != "ringsc";
            if (Tb.Horizontal && tbs == "icons" && tiles[0].Kind != "__scan__")
            {
                float Gi = C.Bool("locked") ? 0 : 14 * Dpi;
                _rows = C.Int("tb_rows", 2) == 1 ? 1 : 2;
                _fp = Math.Max(10 * Dpi, Math.Min(T * (_rows == 2 ? 0.27f : 0.34f) * sc, 17 * Dpi));
                int cols = (int)Math.Ceiling(tiles.Count / (double)_rows);
                float x = 8 * Dpi + Gi;
                for (int c = 0; c < cols; c++)
                {
                    var col = tiles.Skip(c * _rows).Take(_rows).ToList();
                    float cw = col.Max(t => _fp * 1.05f + 4 * Dpi + TextW(g, t, _fp));
                    for (int r = 0; r < col.Count; r++) Add(new RectangleF(x, r * T / _rows, cw, T / _rows), col[r]);
                    x += cw + 12 * Dpi;
                }
                _geo = "tb_icons";
                if (Gi > 0) GripRect = new RectangleF(3 * Dpi, T * 0.2f, Gi - 2 * Dpi, T * 0.6f);
                return new SizeF(x - 12 * Dpi + 8 * Dpi, T);
            }
            float G = C.Bool("locked") ? 0 : 14 * Dpi, w, h;
            if (Tb.Horizontal)
            {
                h = T;
                if (text)
                {
                    _rows = C.Int("tb_rows", 1) == 2 ? 2 : 1;
                    _fp = Math.Max(9.5f * Dpi, Math.Min(T * (_rows == 2 ? 0.24f : 0.30f) * sc, 17 * Dpi)); _lp = _fp * 0.82f; _labels = labels;
                    int cols = (int)Math.Ceiling(tiles.Count / (double)_rows);
                    float x = 8 * Dpi + G;
                    for (int c = 0; c < cols; c++)
                    {
                        var col = tiles.Skip(c * _rows).Take(_rows).ToList();
                        float cw;
                        if (_rows == 2)
                        {
                            cw = col.Max(t => (labels ? LabelW(g, t, _lp) + 5 * Dpi : 0) + TextW(g, t, _fp));
                            for (int r = 0; r < col.Count; r++) Add(new RectangleF(x, r * T / 2, cw, T / 2), col[r]);
                        }
                        else { cw = Math.Max(TextW(g, col[0], _fp), labels ? LabelW(g, col[0], _lp) : 0); Add(new RectangleF(x, 0, cw, T), col[0]); }
                        x += cw + 14 * Dpi;
                    }
                    w = x - 14 * Dpi + 8 * Dpi; _geo = "tb_text";
                }
                else
                {
                    _D = Math.Max(16 * Dpi, Math.Min(Math.Min(T * 0.56f * sc, T - 8 * Dpi), 34 * Dpi));
                    _fp = Math.Max(10 * Dpi, Math.Min(T * (labels ? 0.27f : 0.30f) * sc, 17 * Dpi)); _lp = Math.Max(8.5f * Dpi, _fp * 0.66f);
                    _labels = labels && T >= 36 * Dpi; _gap = Math.Max(4 * Dpi, _D * 0.22f);
                    if (tbs == "ringsc")                     // compact rings: smaller, no labels, closer together
                    {
                        _D = Math.Max(16 * Dpi, Math.Min(T * 0.52f * sc, 30 * Dpi)); _fp = Math.Max(10 * Dpi, Math.Min(T * 0.30f * sc, 16 * Dpi));
                        _labels = false; _gap = Math.Max(3 * Dpi, _D * 0.18f);
                    }
                    float x = 8 * Dpi + G, between = tbs == "ringsc" ? Math.Max(8 * Dpi, _D * 0.34f) : Math.Max(10 * Dpi, _D * 0.45f);
                    foreach (var t in tiles) { float cw = _D + _gap + Math.Max(TextW(g, t, _fp), _labels ? LabelW(g, t, _lp) : 0); Add(new RectangleF(x, 0, cw, T), t); x += cw + between; }
                    w = x - between + 8 * Dpi; _geo = "rings";
                }
                if (G > 0) GripRect = new RectangleF(3 * Dpi, h * 0.2f, G - 2 * Dpi, h * 0.6f);
            }
            else
            {
                w = T;
                _D = Math.Max(16 * Dpi, Math.Min(T * 0.42f * sc, 30 * Dpi)); _fp = Math.Max(9.5f * Dpi, Math.Min(T * 0.20f * sc, 14 * Dpi)); _lp = Math.Max(8 * Dpi, _fp * 0.75f);
                _labels = labels && T >= 56 * Dpi;
                float itemH = (text ? 0 : _D + 3 * Dpi) + _fp * 1.3f + (_labels ? _lp * 1.3f : 0), y = 8 * Dpi + G;
                foreach (var t in tiles) { Add(new RectangleF(2, y, T - 4, itemH), t); y += itemH + 8 * Dpi; }
                h = y; _geo = text ? "tb_stack_text" : "tb_stack";
                if (G > 0) GripRect = new RectangleF(T * 0.2f, 3 * Dpi, T * 0.6f, G - 2 * Dpi);
            }
            return new SizeF(w, h);
        }

        List<Block> CardBlocks(List<Tile> tiles)
        {
            var blocks = new List<Block>();
            var dev = new Dictionary<string, Block>();
            Block rates = null, bars = null, rows = null;
            foreach (var t in tiles)
            {
                var m = t.M ?? (Metrics.ContainsKey(t.Key) ? Metrics[t.Key] : null);
                string d = m != null ? Fmt.Device(m) : "", kind = m?.Kind ?? "";
                if (t.Kind == "__scan__") { blocks.Add(new Block { Type = "rows", Tiles = { t } }); continue; }
                if ((d == "CPU" || d == "GPU") && new[] { "temp", "clock", "usage", "power", "fan" }.Contains(kind))
                {
                    Block b;
                    if (!dev.TryGetValue(d, out b)) { b = new Block { Type = "device", Dev = d }; dev[d] = b; blocks.Add(b); }
                    string slot = kind == "clock" ? "clock" : kind == "temp" ? "temp" : kind == "usage" ? "load" : "right";
                    if (slot == "right" && b.Slots.ContainsKey("right") && kind == "power" && b.SlotKind["right"] == "fan") { b.Extras.Add(b.Slots["right"]); b.Slots.Remove("right"); }
                    if (b.Slots.ContainsKey(slot)) b.Extras.Add(t); else { b.Slots[slot] = t; b.SlotKind[slot] = kind; }
                    b.Tiles.Add(t);
                }
                else if (kind == "rate") { if (rates == null) { rates = new Block { Type = "rates" }; blocks.Add(rates); } rates.Tiles.Add(t); }
                else if (m != null && (m.Unit == "%" || (kind == "data" && m.VMax.HasValue))) { if (bars == null) { bars = new Block { Type = "bars" }; blocks.Add(bars); } bars.Tiles.Add(t); }
                else { if (rows == null) { rows = new Block { Type = "rows" }; blocks.Add(rows); } rows.Tiles.Add(t); }
            }
            return blocks;
        }

        SizeF LayoutCard(Graphics g, List<Tile> tiles)
        {
            float s = S, M = 14 * s, W = 286 * s, pad = 16 * s, x = M + pad, inner = W - 2 * pad, y = M + pad;
            _blocks.Clear(); _dividers.Clear();
            var blocks = CardBlocks(tiles);
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (i > 0) { y += 11 * s; _dividers.Add(y); y += 12 * s; }
                float h;
                if (b.Type == "device")
                {
                    h = 12 * s + 24 * s + 20 * s + ((b.Slots.ContainsKey("load") || b.Slots.ContainsKey("temp")) ? 8 * s + 42 * s : 0) + b.Extras.Count * 20 * s + 12 * s;
                    var r = new RectangleF(x - 4 * s, y, inner + 8 * s, h);
                    foreach (var t in b.Tiles) Add(r, t);
                }
                else if (b.Type == "rates") { h = b.Tiles.Count * 34 * s; for (int k = 0; k < b.Tiles.Count; k++) Add(new RectangleF(x, y + k * 34 * s, inner, 34 * s), b.Tiles[k]); }
                else if (b.Type == "bars") { h = b.Tiles.Count * 40 * s - 6 * s; for (int k = 0; k < b.Tiles.Count; k++) Add(new RectangleF(x, y + k * 40 * s, inner, 34 * s), b.Tiles[k]); }
                else { h = b.Tiles.Count * 24 * s; for (int k = 0; k < b.Tiles.Count; k++) Add(new RectangleF(x, y + k * 24 * s, inner, 24 * s), b.Tiles[k]); }
                b.R = new RectangleF(x, y, inner, h);
                _blocks.Add(b);
                y += h;
            }
            _cardM = M; _cardX = x; _cardInner = inner; _geo = "card";
            return new SizeF(W + 2 * M, y + pad + M);
        }

        // ---- painting -----------------------------------------------------------------------------------
        protected override void DoPaint(Graphics g)
        {
            if (_geo == "card") { PaintCard(g); return; }
            if (TaskbarMode) PaintTaskbarBackground(g);
            else if (C.Bool("background", true))
            {
                var rect = new RectangleF(0.5f, 0.5f, LayoutSize.Width - 1, LayoutSize.Height - 1);
                using (var path = Draw.Round(rect, _radius)) { if (!Backdrop(g, path, rect)) using (var b = new SolidBrush(Pal.Bg)) g.FillPath(b, path); using (var p = new Pen(Pal.Border)) g.DrawPath(p, path); }
            }
            else using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, 0, 0, LayoutSize.Width, LayoutSize.Height);
            foreach (var c in Cells)
            {
                var t = (Tile)c.Item;
                var st = g.Save();
                float a = t.Online || t.Kind == "__scan__" ? 1 : 0.45f;
                if (_geo == "tb_icons") TbIcon(g, c.R, t, a);
                else if (_geo == "text") TextRow(g, c.R, t, a);
                else if (_geo == "tb_text") TbText(g, c.R, t, a);
                else if (_geo.StartsWith("tb_stack")) Stack(g, c.R, t, a, _geo == "tb_stack");
                else RingTile(g, c.R, t, a);
                g.Restore(st);
            }
        }

        static Color F(Color c, float a) => Draw.A(c, (int)(c.A * a));

        void Ring(Graphics g, RectangleF box, Tile t, float a)
        {
            float w = Math.Max(1.8f, box.Width * 0.085f);
            var rr = RectangleF.Inflate(box, -w / 2, -w / 2);
            using (var p = new Pen(F(t.Kind == "__scan__" ? Pal.Sub : Pal.Track, a), w)) { if (t.Kind == "__scan__") p.DashStyle = DashStyle.Dash; g.DrawEllipse(p, rr); }
            if (t.Frac > 0)
                using (var br = GoldFor(t) ? Draw.Gold(rr) : new SolidBrush(F(TileRing(t), a)))
                using (var p = new Pen(br, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, rr, -90, (float)(360 * Math.Min(1, t.Frac.Value)));
            float inset = box.Width * 0.25f;
            Color ic = !string.IsNullOrEmpty(t.Color) ? Theme.C(t.Color) : Draw.A(Pal.Text, (int)(Pal.Text.A * 0.92));
            Draw.SysIcon(g, t.Kind, RectangleF.Inflate(box, -inset, -inset), F(ic, a));
        }

        void Value(Graphics g, float x, float baseY, Tile t, float fp, float a, string align = "left", float width = 0)
        {
            var big = t.Kind == "__scan__" ? Fonts.Get(fp * 0.8f, FontStyle.Regular, false, true) : Fonts.Get(fp, FontStyle.Regular, true, true);
            var small = Fonts.Get(fp * 0.62f, FontStyle.Regular, true);
            float tw = Fonts.Width(g, t.Text, big) + (t.Unit != "" ? Fonts.Width(g, t.Unit, small) + fp * 0.06f : 0);
            if (align == "right") x += width - tw; else if (align == "center") x += (width - tw) / 2;
            using (var vb = new SolidBrush(F(t.Kind == "__scan__" ? Pal.Sub : TileValue(t), a))) Fonts.Text(g, t.Text, big, vb, x, baseY);
            if (t.Unit != "") using (var ub = new SolidBrush(F(Pal.Sub, a))) Fonts.Text(g, t.Unit, small, ub, x + Fonts.Width(g, t.Text, big) + fp * 0.06f, baseY);
        }

        void Label(Graphics g, RectangleF r, Tile t, float lp, float a, bool center = false)
        {
            var f = Fonts.Get(lp);
            string s = Fonts.Elide(g, t.Label, f, r.Width);
            float x = center ? r.X + (r.Width - Fonts.Width(g, s, f)) / 2 : r.X;
            using (var b = new SolidBrush(F(Pal.Sub, a))) Fonts.Text(g, s, f, b, x, r.Y + r.Height / 2 + (Fonts.Ascent(f) - Fonts.Descent(f)) / 2);
        }

        void RingTile(Graphics g, RectangleF r, Tile t, float a)
        {
            var ring = new RectangleF(r.Left, r.Top + r.Height / 2 - _D / 2, _D, _D);
            Ring(g, ring, t, a);
            float tx = ring.Right + _gap;
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            if (_labels && t.Kind != "__scan__")
            {
                float block = _fp * 1.05f + _lp * 1.25f, top = r.Top + r.Height / 2 - block / 2;
                Value(g, tx, top + Fonts.Ascent(big) * 0.98f, t, _fp, a);
                Label(g, new RectangleF(tx, top + _fp * 1.05f, r.Right - tx, _lp * 1.25f), t, _lp, a);
            }
            else Value(g, tx, r.Top + r.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, t, _fp, a);
        }

        void TextRow(Graphics g, RectangleF r, Tile t, float a)
        {
            Color ic = !string.IsNullOrEmpty(t.Color) ? Theme.C(t.Color) : TileRing(t);
            Draw.SysIcon(g, t.Kind, new RectangleF(r.Left, r.Top + r.Height / 2 - _isz / 2, _isz, _isz), F(ic, a));
            float x = r.Left + _isz + 8 * S;
            if (_labels) Label(g, new RectangleF(x, r.Top, _lw + 2, r.Height), t, _lp, a);
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            Value(g, r.Right - _vw, r.Top + r.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, t, _fp, a, "right", _vw);
        }

        void TbIcon(Graphics g, RectangleF r, Tile t, float a)
        {
            float isz = _fp * 1.05f;
            Color ic = !string.IsNullOrEmpty(t.Color) ? Theme.C(t.Color) : t.Level == "bad" || t.Level == "warn" ? TileRing(t) : Pal.Sub;
            Draw.SysIcon(g, t.Kind, new RectangleF(r.X, r.Y + (r.Height - isz) / 2, isz, isz), F(ic, a));
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            Value(g, r.X + isz + 4 * Dpi, r.Y + r.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, t, _fp, a);
        }

        void TbText(Graphics g, RectangleF r, Tile t, float a)
        {
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            if (_rows == 2)
            {
                float baseY = r.Top + r.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, x = r.Left;
                if (_labels) { float lw = LabelW(g, t, _lp); Label(g, new RectangleF(x, r.Top, lw + 2, r.Height), t, _lp, a); x += lw + 5 * Dpi; }
                Value(g, x, baseY, t, _fp, a);
            }
            else if (_labels)
            {
                float block = _lp * 1.2f + _fp * 1.05f, top = r.Top + r.Height / 2 - block / 2;
                Label(g, new RectangleF(r.Left, top, r.Width, _lp * 1.2f), t, _lp, a, true);
                Value(g, r.Left, top + _lp * 1.2f + Fonts.Ascent(big) * 0.98f, t, _fp, a, "center", r.Width);
            }
            else Value(g, r.Left, r.Top + r.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, t, _fp, a, "center", r.Width);
        }

        void Stack(Graphics g, RectangleF r, Tile t, float a, bool rings)
        {
            float y = r.Top;
            if (rings) { Ring(g, new RectangleF(r.Left + r.Width / 2 - _D / 2, y, _D, _D), t, a); y += _D + 3 * Dpi; }
            Value(g, r.Left, y + Fonts.Ascent(Fonts.Get(_fp, FontStyle.Regular, true, true)), t, _fp, a, "center", r.Width);
            y += _fp * 1.3f;
            if (_labels) Label(g, new RectangleF(r.Left, y, r.Width, _lp * 1.3f), t, _lp, a, true);
        }

        // the card ----------------------------------------------------------------------------------------
        void PaintCard(Graphics g)
        {
            float s = S, M = _cardM, rad = 18 * s;
            var card = new RectangleF(M, M, LayoutSize.Width - 2 * M, LayoutSize.Height - 2 * M);
            for (int i = 0; i < 8; i++)
            {
                float grow = (8 - i) * 1.4f * s;
                var r = new RectangleF(card.X - grow, card.Y - grow + 4 * s, card.Width + 2 * grow, card.Height + 2 * grow);
                using (var b = new SolidBrush(Color.FromArgb(7, 0, 0, 0))) using (var p = Draw.Round(r, rad + grow)) g.FillPath(b, p);
            }
            Color bg = Pal.Bg, end, panel, line;
            if (Pal.Metal) { end = Color.FromArgb(bg.A, 24, 32, 43); panel = Color.FromArgb(95, 0, 0, 0); line = Color.FromArgb(48, 201, 161, 90); }
            else if (Pal.Glass) { end = bg; panel = Color.FromArgb(60, 0, 0, 0); line = Color.FromArgb(26, 255, 255, 255); }   // Windows 11 acrylic: one even dark glass
            else
            {
                var acc = Pal.Accent;
                end = Color.FromArgb(bg.A, (int)Math.Min(255, bg.R * 0.8 + acc.R * 0.11), (int)Math.Min(255, bg.G * 0.8 + acc.G * 0.11), (int)Math.Min(255, bg.B * 0.8 + acc.B * 0.17 + 18));
                bool dark = bg.GetBrightness() < 0.55f;
                panel = dark ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(18, 0, 0, 0);
                line = dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
            }
            using (var path = Draw.Round(card, rad))
            {
                if (!Backdrop(g, path, card)) using (var gb = new LinearGradientBrush(card, bg, end, LinearGradientMode.ForwardDiagonal)) g.FillPath(gb, path);
                if (Pal.Metal)
                    using (var eb = new LinearGradientBrush(card, Color.White, Color.Black, LinearGradientMode.ForwardDiagonal))
                    {
                        eb.InterpolationColors = new ColorBlend { Colors = new[] { Color.FromArgb(210, 251, 231, 168), Color.FromArgb(120, 140, 101, 38), Color.FromArgb(170, 229, 192, 107), Color.FromArgb(140, 140, 101, 38) },
                            Positions = new[] { 0f, 0.45f, 0.8f, 1f } };
                        using (var p = new Pen(eb, 1.3f * s)) g.DrawPath(p, path);
                    }
                else using (var p = new Pen(Pal.Border)) g.DrawPath(p, path);
            }
            using (var lp = new Pen(line)) foreach (var dy in _dividers) g.DrawLine(lp, _cardX, dy, _cardX + _cardInner, dy);
            foreach (var b in _blocks)
            {
                if (b.Type == "device") CardDevice(g, b, panel, line);
                else if (b.Type == "rates") CardRates(g, b);
                else if (b.Type == "bars") CardBars(g, b);
                else CardRows(g, b);
            }
        }

        float Txt(Graphics g, float x, float baseY, string text, Font f, Color c, bool right = false, bool gold = false)
        {
            float w = Fonts.Width(g, text, f), x0 = right ? x - w : x;
            using (var br = gold && Pal.Metal ? Draw.Gold(new RectangleF(x0, baseY - Fonts.Ascent(f), w, Fonts.Ascent(f) * 1.1f)) : new SolidBrush(c)) Fonts.Text(g, text, f, br, x0, baseY);
            return w;
        }

        void CardDevice(Graphics g, Block b, Color panelCol, Color lineCol)
        {
            float s = S;
            var pr = new RectangleF(b.R.X - 4 * s, b.R.Y, b.R.Width + 8 * s, b.R.Height);
            using (var path = Draw.Round(pr, 12 * s)) { using (var br = new SolidBrush(panelCol)) g.FillPath(br, path); using (var p = new Pen(lineCol)) g.DrawPath(p, path); }
            float x = b.R.Left + 8 * s, right = b.R.Right - 8 * s, y = b.R.Top + 12 * s;
            Draw.SysIcon(g, b.Dev == "CPU" ? "cpu" : "gpu", new RectangleF(x, y + 3 * s, 17 * s, 17 * s), Pal.Metal ? Pal.Accent : Pal.Text);
            string title = b.Dev;
            Tile t;
            if (b.Slots.TryGetValue("clock", out t))
            {
                string val = t.Text;
                double d; if (t.Unit == "GHz" && double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) val = d.ToString("F1", CultureInfo.InvariantCulture);
                title = t.Online ? $"{b.Dev}: {val} {t.Unit}" : $"{b.Dev}: –";
            }
            Txt(g, x + 23 * s, y + 17 * s, title, Fonts.Get(14.5f * s, FontStyle.Regular, false, true), Pal.Text);
            if (b.Slots.TryGetValue("temp", out t))
            {
                var col = t.Level == "bad" ? Pal.Bad : t.Level == "warn" ? Pal.Warn : Pal.Accent;
                Txt(g, right, y + 17 * s, t.Online ? t.Text + t.Unit : "–", Fonts.Get(15 * s, FontStyle.Regular, true, true), col, true, t.Level != "warn" && t.Level != "bad");
            }
            y += 24 * s;
            if (b.Slots.TryGetValue("load", out t)) Txt(g, x, y + 13 * s, t.Online ? $"Load: {t.Text}{t.Unit}" : "Load: –", Fonts.Get(12 * s), Pal.Sub);
            if (b.Slots.TryGetValue("right", out t)) Txt(g, right, y + 13 * s, t.Online ? $"{t.Text} {t.Unit}" : "–", Fonts.Get(12 * s), Pal.Sub, true);
            y += 20 * s;
            Tile h = b.Slots.ContainsKey("load") ? b.Slots["load"] : b.Slots.ContainsKey("temp") ? b.Slots["temp"] : null;
            if (h != null) { y += 8 * s; Spark(g, new RectangleF(b.R.Left + 2 * s, y, b.R.Width - 4 * s, 42 * s), h); y += 42 * s; }
            foreach (var e in b.Extras)
            {
                Txt(g, x, y + 14 * s, e.Label, Fonts.Get(11.5f * s), Pal.Sub);
                Txt(g, right, y + 14 * s, (e.Text + " " + e.Unit).Trim(), Fonts.Get(11.5f * s), Pal.Text, true);
                y += 20 * s;
            }
        }

        void Spark(Graphics g, RectangleF box, Tile t)
        {
            Queue<double> q;
            if (!_hist.TryGetValue(t.Key, out q) || q.Count < 2) return;
            var vals = q.ToArray();
            double lo, hi;
            if (t.M != null && t.M.Unit == "%") { lo = 0; hi = 100; }
            else { lo = vals.Min(); hi = vals.Max(); double span = Math.Max(hi - lo, 4); lo -= span * 0.15; hi += span * 0.15; }
            float step = box.Width / 59f;
            var pts = vals.Select((v, i) => new PointF(box.Right - (vals.Length - 1 - i) * step, (float)(box.Bottom - (Math.Min(Math.Max(v, lo), hi) - lo) / (hi - lo) * box.Height))).ToArray();
            bool gold = GoldFor(t);
            var acc = t.Level == "warn" || t.Level == "bad" ? TileRing(t) : Pal.Accent;
            using (var area = new GraphicsPath())
            {
                area.AddLines(pts);
                area.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, pts[pts.Length - 1].X, box.Bottom);
                area.AddLine(pts[pts.Length - 1].X, box.Bottom, pts[0].X, box.Bottom);
                area.CloseFigure();
                var top = gold ? Color.FromArgb(80, 229, 192, 107) : Draw.A(acc, 70);
                var bot = gold ? Color.FromArgb(0, 140, 101, 38) : Draw.A(acc, 0);
                using (var fb = new LinearGradientBrush(new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2), top, bot, LinearGradientMode.Vertical)) g.FillPath(fb, area);
            }
            Brush lb;
            if (gold)
            {
                var gb = new LinearGradientBrush(box, Color.White, Color.Black, LinearGradientMode.Horizontal);
                gb.InterpolationColors = new ColorBlend { Colors = new[] { Theme.C("#9C7230"), Theme.C("#F3D58A"), Theme.C("#D9B45F") }, Positions = new[] { 0f, 0.5f, 1f } };
                lb = gb;
            }
            else lb = new SolidBrush(acc);
            using (lb) using (var p = new Pen(lb, 1.6f * S) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
        }

        void CardRates(Graphics g, Block b)
        {
            float s = S;
            foreach (var c in Cells.Where(c => b.Tiles.Contains((Tile)c.Item)))
            {
                var t = (Tile)c.Item; var r = c.R;
                bool up = t.Kind == "up" || t.Key.EndsWith(".up") || t.Key.Contains("write");
                Draw.SysIcon(g, t.Kind == "down" || t.Kind == "up" ? t.Kind : "down", new RectangleF(r.Left, r.Top + r.Height / 2 - 8 * s, 15 * s, 15 * s), up ? Pal.Warn : Pal.Good);
                Txt(g, r.Left + 21 * s, r.Top + r.Height / 2 + 4.5f * s, t.Label, Fonts.Get(12.5f * s), Pal.Sub);
                float baseY = r.Top + r.Height / 2 + 7 * s;
                float uw = Txt(g, r.Right, baseY, t.Unit.Replace("B/s", "/s"), Fonts.Get(19 * s, FontStyle.Regular, true), Pal.Sub, true);
                Txt(g, r.Right - uw - 6 * s, baseY, t.Online ? t.Text : "–", Fonts.Get(23 * s, FontStyle.Regular, true), Pal.Text, true);
            }
        }

        void CardBars(Graphics g, Block b)
        {
            float s = S;
            foreach (var c in Cells.Where(c => b.Tiles.Contains((Tile)c.Item)))
            {
                var t = (Tile)c.Item; var r = c.R;
                Color ic = !string.IsNullOrEmpty(t.Color) ? Theme.C(t.Color) : Pal.Text;
                Draw.SysIcon(g, t.Kind, new RectangleF(r.Left, r.Top + 1 * s, 16 * s, 16 * s), ic);
                string unit = t.Unit == "%" ? t.Unit : " " + t.Unit;
                Txt(g, r.Left + 23 * s, r.Top + 14 * s, t.Online ? $"{t.Label}: {t.Text}{unit}" : $"{t.Label}: –", Fonts.Get(12.5f * s), Pal.Text);
                var bar = new RectangleF(r.Left, r.Top + 24 * s, r.Width, 6 * s);
                using (var tb = new SolidBrush(Pal.Track)) using (var bp = Draw.Round(bar, 3 * s)) g.FillPath(tb, bp);
                if (t.Frac > 0)
                {
                    var fr = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, (float)(bar.Width * t.Frac.Value)), bar.Height);
                    Brush fb;
                    if (GoldFor(t))
                    {
                        var lg = new LinearGradientBrush(new RectangleF(fr.X - 1, fr.Y, fr.Width + 2, fr.Height), Color.White, Color.Black, LinearGradientMode.Horizontal);
                        lg.InterpolationColors = new ColorBlend { Colors = new[] { Theme.C("#8C6526"), Theme.C("#D9B45F"), Theme.C("#FBE7A8") }, Positions = new[] { 0f, 0.6f, 1f } };
                        fb = lg;
                    }
                    else { var col = TileRing(t); fb = new LinearGradientBrush(new RectangleF(fr.X - 1, fr.Y, fr.Width + 2, fr.Height), Draw.Lighter(col, 0.87), col, LinearGradientMode.Horizontal); }
                    using (fb) using (var fp = Draw.Round(fr, 3 * s)) g.FillPath(fb, fp);
                }
            }
        }

        void CardRows(Graphics g, Block b)
        {
            float s = S;
            foreach (var c in Cells.Where(c => b.Tiles.Contains((Tile)c.Item)))
            {
                var t = (Tile)c.Item; var r = c.R;
                Color ic = !string.IsNullOrEmpty(t.Color) ? Theme.C(t.Color) : Pal.Sub;
                Draw.SysIcon(g, t.Kind, new RectangleF(r.Left, r.Top + r.Height / 2 - 7 * s, 14 * s, 14 * s), ic);
                Txt(g, r.Left + 21 * s, r.Top + r.Height / 2 + 4.5f * s, t.Label, Fonts.Get(12 * s), Pal.Sub);
                Txt(g, r.Right, r.Top + r.Height / 2 + 5 * s, (t.Text + " " + t.Unit).Trim(), Fonts.Get(13 * s, FontStyle.Regular, true, true), TileValue(t), true);
            }
        }

        // ---- interaction -------------------------------------------------------------------------------------
        protected override void OnScanClicked() => OpenPicker();

        public void OpenPicker()
        {
            if (_picker == null || _picker.IsDisposed) _picker = new PickerForm(this);
            _picker.OnMetrics(Metrics);
            _picker.Show(); _picker.Activate();
        }

        protected override void OnReorder(string key, int index)
        {
            var items = Items;
            var moving = items.FirstOrDefault(i => Cfg.S(i, "key") == key);
            if (moving == null) return;
            items.Remove(moving);
            items.Insert(Math.Min(index, items.Count), moving);
            C["items"] = items.Cast<object>().ToList();
            Save(); Rebuild();
        }

        void Set(string key, object value)
        {
            C[key] = value; Save(); InvalidateTheme(); Rebuild();
            Suite?.ThemeChanged(this, key, value);
        }
        public override float VisualInset => _geo == "card" ? _cardM : 0;
        public override float VisualRadius => _geo == "card" ? 18 * S : _radius;

        public override void FillMenu(ContextMenuStrip menu, bool common)
        {
            var t = MenuTheme; var it = menu.Items;
            Menus.Item(it, "System monitor", null, null, false);
            Menus.Sep(it);
            Menus.Item(it, "Choose what to show…", OpenPicker);
            Menus.Sep(it);
            var mode = Menus.Sub(it, "Show as", t);
            Menus.Item(mode.DropDownItems, "Desktop widget", () => SetMode("desktop"), !TaskbarMode);
            Menus.Item(mode.DropDownItems, "On the taskbar", () => SetMode("taskbar"), TaskbarMode);
            var th = Menus.Sub(it, "Theme", t);
            foreach (var name in Themes.All.Keys) { string n = name; Menus.Item(th.DropDownItems, n == "Auto" ? "Auto (follow Windows)" : n, () => Set("theme", n), C.Str("theme", "Twilight") == n); }
            Suite?.LinkItem(th.DropDownItems);
            Menus.Item(th.DropDownItems, "Custom accent color…", () => Pick("accent", Pal.Accent));
            if (!string.IsNullOrEmpty(C.Str("accent", ""))) Menus.Item(th.DropDownItems, "Reset accent color", () => Set("accent", null));
            var ring = Menus.Sub(th.DropDownItems, "Ring color", t);
            string rm = C.Str("ring_mode", "level");
            Menus.Item(ring.DropDownItems, "By level (green / yellow / red)", () => Set("ring_mode", "level"), rm == "level");
            Menus.Item(ring.DropDownItems, "Accent color", () => Set("ring_mode", "accent"), rm == "accent");
            Menus.Item(ring.DropDownItems, "Custom color…", () => { if (Pick("ring_color", Theme.C(C.Str("ring_color", "#FF8A3D")))) Set("ring_mode", "custom"); }, rm == "custom", true, Draw.Swatch(Theme.C(C.Str("ring_color", "#FF8A3D"))));
            Menus.Sep(ring.DropDownItems);
            Menus.Item(ring.DropDownItems, "Critical values are always red", null, null, false);
            var look = Menus.Sub(it, "Look", t); var li = look.DropDownItems;
            string style = C.Str("style", "card");
            if (TaskbarMode)
            {
                string tbs = C.Str("tb_style", "icons");
                Menus.Item(li, "Icons and values (compact)", () => Set("tb_style", "icons"), tbs == "icons");
                Menus.Item(li, "Labels and values", () => Set("tb_style", "labels"), tbs == "labels");
                Menus.Item(li, "Compact rings", () => Set("tb_style", "ringsc"), tbs == "ringsc");
                Menus.Item(li, "Rings with labels", () => Set("tb_style", "rings"), tbs == "rings");
                var mx = Menus.Sub(li, "Show on the taskbar", t);
                foreach (var n0 in new[] { 4, 6, 8, 12, 0 }) { int n = n0; Menus.Item(mx.DropDownItems, n == 0 ? "All values" : $"First {n} values", () => Set("tb_max", n), C.Int("tb_max", 6) == n); }
                Menus.Item(li, "Drag a value to move it · the handle moves the widget", null, null, false);
            }
            else
            {
                Menus.Item(li, "Card (CPU panel, history, network, memory)", () => Set("style", "card"), style == "card");
                Menus.Item(li, "Rings (icon, value, label)", () => Set("style", "rings"), style == "rings");
                Menus.Item(li, "Text (label and value)", () => Set("style", "text"), style == "text");
            }
            Menus.Sep(li);
            if (!TaskbarMode && style != "card")
            {
                int cols = C.Int("columns");
                foreach (var p in new[] { new[] { "0", "One row" }, new[] { "1", "One column" }, new[] { "2", "Grid – 2 columns" }, new[] { "3", "Grid – 3 columns" }, new[] { "4", "Grid – 4 columns" } })
                { int v = int.Parse(p[0]); Menus.Item(li, p[1], () => Set("columns", v), cols == v); }
                Menus.Sep(li);
                Menus.Item(li, "Background", () => Set("background", !C.Bool("background", true)), C.Bool("background", true));
            }
            else if (TaskbarMode)
            {
                if (C.Str("tb_style", "icons") != "rings") Menus.Item(li, "Two rows", () => Set("tb_rows", C.Int("tb_rows", 1) == 2 ? 1 : 2), C.Int("tb_rows", 1) == 2);
                Menus.Item(li, "Background", () => Set("tb_background", !C.Bool("tb_background")), C.Bool("tb_background"));
            }
            if (!TaskbarMode) Menus.Item(li, "Blend with the wallpaper (frosted glass)", () => Set("blend", !C.Bool("blend")), C.Bool("blend"));
            if (TaskbarMode || style != "card") Menus.Item(li, "Labels", () => Set("show_labels", !C.Bool("show_labels", true)), C.Bool("show_labels", true));
            var size = Menus.Sub(li, "Size", t);
            foreach (var v0 in new[] { 0.8, 0.9, 1.0, 1.15, 1.3, 1.5 }) { double v = v0; Menus.Item(size.DropDownItems, Math.Round(v * 100) + "%", () => Set("scale", v), Math.Abs(C.Num("scale", 1) - v) < 0.01); }
            var op = Menus.Sub(li, "Opacity", t);
            foreach (var v0 in new[] { 1.0, 0.9, 0.8, 0.7, 0.6 }) { double v = v0; Menus.Item(op.DropDownItems, Math.Round(v * 100) + "%", () => { C["opacity"] = v; Save(); ApplyOpacity(); }, Math.Abs(C.Num("opacity", 1) - v) < 0.01); }
            var pos = Menus.Sub(it, "Position", t); var pi = pos.DropDownItems;
            if (TaskbarMode)
            {
                Menus.Item(pi, "Drag the handle (the line) to move it", null, null, false);
                Menus.Item(pi, "Shift + drag a value to change the order", null, null, false);
                Menus.Sep(pi);
                Menus.Item(pi, "Inside the taskbar (recommended)", () => { if (!C.Bool("tb_embed", true)) { C["tb_embed"] = true; Save(); Suite?.RestartAll(); } }, C.Bool("tb_embed", true));
                Menus.Item(pi, "Floating above the taskbar", () => { if (C.Bool("tb_embed", true)) { C["tb_embed"] = false; Save(); Suite?.RestartAll(); } }, !C.Bool("tb_embed", true));
                Menus.Sep(pi);
                Menus.Item(pi, "Move to the default spot", ResetTaskbarSpot);
                CrossItems(pi);
            }
            else
            {
                Menus.Item(pi, "Drag anywhere to move · widgets snap together", null, null, false);
                Menus.Item(pi, "Alt + drag moves one on its own · Shift + drag reorders", null, null, false);
                Menus.Sep(pi);
                foreach (var p in new[] { new[] { "wallpaper", "Part of the wallpaper (behind desktop icons)" }, new[] { "bottom", "On the desktop (behind windows)" }, new[] { "normal", "Normal" }, new[] { "top", "Always on top" } })
                { string k = p[0]; Menus.Item(pi, p[1], () => { C["stacking"] = k; Save(); Suite?.RestartAll(); }, C.Str("stacking", "bottom") == k); }
            }
            Menus.Item(pi, "Lock position", () => Set("locked", !C.Bool("locked")), C.Bool("locked"));
            var upd = Menus.Sub(it, "Update every", t);
            foreach (var p in new[] { new[] { "500", "0.5 seconds" }, new[] { "1000", "second" }, new[] { "2000", "2 seconds" }, new[] { "5000", "5 seconds" } })
            { int v = int.Parse(p[0]); Menus.Item(upd.DropDownItems, p[1], () => { C["interval_ms"] = v; Save(); RefreshNow(); }, C.Int("interval_ms", 1000) == v); }
            Menus.Sep(it);
            if (common) Suite?.FillCommon(it, t);
        }

        void SetMode(string mode) { if (C.Str("mode", "desktop") == mode) return; C["mode"] = mode; C["tb_offset"] = null; Save(); Suite?.RestartAll(); }

        bool Pick(string key, Color cur)
        {
            using (var dlg = new ColorDialog { Color = cur, FullOpen = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                Set(key, Theme.Hex(dlg.Color)); return true;
            }
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Sampler?.Stop();
            if (_picker != null && !_picker.IsDisposed) _picker.Close();
        }
    }
}
