// BatteryView.cs – the battery widget: ring + device icon + percentage per device.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    /// <summary>Runs every enabled source in its own thread; the first round reports as each source answers.</summary>
    public class Poller
    {
        public readonly List<Provider> Providers;
        readonly Cfg _c;
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        volatile bool _stop, _force;
        public volatile bool Paused;
        int _seq;
        bool _first = true;
        public event Action<int, List<Reading>, bool> Updated;

        public Poller(Cfg c, List<Provider> provs) { _c = c; Providers = provs; }
        public void Start() => new Thread(Run) { IsBackground = true, Name = "battery-poller" }.Start();
        public void RefreshNow() { _force = true; _wake.Set(); }
        public void Stop() { _stop = true; _wake.Set(); }

        void Run()
        {
            while (!_stop)
            {
                bool force = _force; _force = false;
                if (!Paused) Cycle(force);
                _wake.WaitOne(Math.Max(10, _c.Int("interval_s", 60)) * 1000);
            }
        }

        void Cycle(bool force)
        {
            var on = Cfg.SubDict(_c.D, "providers");
            var provs = Providers.Where(p => !(on.TryGetValue(p.Id, out var v) && v is bool b && !b)).ToList();
            var results = new List<Reading>[provs.Count];
            var lk = new object();
            bool progressive = _first;
            var threads = provs.Select((p, i) => new Thread(() =>
            {
                var r = p.Run(force);
                Tuple<int, List<Reading>, bool> payload = null;
                lock (lk)
                {
                    results[i] = r;
                    if (progressive) payload = Tuple.Create(++_seq, Prov.Dedupe(results.Where(x => x != null).SelectMany(x => x)), results.All(x => x != null));
                }
                if (payload != null) Updated?.Invoke(payload.Item1, payload.Item2, payload.Item3);
            }) { IsBackground = true }).ToList();
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join(90000);
            if (!progressive || provs.Count == 0)
            {
                Tuple<int, List<Reading>, bool> payload;
                lock (lk) payload = Tuple.Create(++_seq, Prov.Dedupe(results.Where(x => x != null).SelectMany(x => x)), true);
                Updated?.Invoke(payload.Item1, payload.Item2, payload.Item3);
            }
            _first = false;
        }

        public static List<Provider> Make() => new List<Provider> { new GHubProvider(), new LogitechProvider(), new RazerProvider(), new AsusRogProvider(),
            new RongYuanProvider(), new CompxProvider(), new HyperXProvider(), new KeychronProvider(), new MchoseProvider(),
            new HeadsetProvider(), new HeadsetControlProvider(), new HidBatteryProvider(), new BluetoothProvider() };
    }

    public class BatteryView : WidgetWindow
    {
        public static Dictionary<string, object> Defaults() => new Dictionary<string, object> {
            { "theme", "Dark" }, { "accent", null }, { "mode", "desktop" }, { "layout", "horizontal" }, { "scale", 1.0 }, { "opacity", 1.0 },
            { "stacking", "bottom" }, { "show_names", false }, { "show_moon", true }, { "background", true }, { "tb_background", false },
            { "tb_offset", null }, { "tb_embed", true }, { "tb_hide_fullscreen", true }, { "locked", false }, { "pos", null },
            { "interval_s", 60 }, { "low_threshold", 15 }, { "notify", true }, { "ring_mode", "level" }, { "ring_color", "#7C5CFF" },
            { "kind_colors", new Dictionary<string, object>() }, { "icon_variants", new Dictionary<string, object>() },
            { "providers", new Dictionary<string, object>() }, { "hidden", new List<object>() }, { "order", new List<object>() },
            { "known", new Dictionary<string, object>() }, { "overrides", new Dictionary<string, object>() }, { "extra_ids", new Dictionary<string, object>() },
            { "apps", new Dictionary<string, object>() }, { "vendor_apps", new Dictionary<string, object>() }, { "show_battery", true }, { "show_system", true }, { "show_weather", true }, { "run_as_admin", false }, { "update_check", true }, { "update_repo", "" }, { "update_skip", "" } };

        public readonly Poller Poller;
        public List<Reading> Items = new List<Reading>();
        public List<Reading> Last = new List<Reading>();
        int _seq = -1;
        bool _hadFinal;
        readonly HashSet<string> _alerted = new HashSet<string>();
        ScannerForm _scanner;
        // geometry
        float _d, _fp, _gap; bool _stacked, _names;

        public BatteryView(Cfg cfg, Poller poller) : base(cfg)
        {
            Poller = poller;
            Text = "Battery Widget";
            if (poller != null) poller.Updated += (seq, r, final) => { if (IsHandleCreated) BeginInvoke((Action)(() => OnPoll(seq, r, final))); };
            SyncExtra();
        }

        public void SyncExtra()
        {
            var extra = C.Dict("extra_ids");
            Func<string, HashSet<string>> get = k => new HashSet<string>(extra.TryGetValue(k, out var v) && v is List<object> l ? l.Select(Convert.ToString) : new string[0]);
            RongYuanProvider.Extra = get("rongyuan"); CompxProvider.Extra = get("compx");
        }

        public override void RefreshNow() => Poller?.RefreshNow();

        void OnPoll(int seq, List<Reading> readings, bool final)
        {
            if (seq <= _seq) return;
            _seq = seq;
            OnReadings(readings, final);
        }

        public Reading Display(Reading r)
        {
            var o = Cfg.SubDict(C.Dict("overrides"), r.Key);
            if (o.Count == 0) return r;
            var d = r.Copy();
            d.Name = Cfg.S(o, "name") ?? r.Name;
            d.Kind = Cfg.S(o, "kind") ?? r.Kind;
            return d;
        }

        public void OnReadings(List<Reading> readings, bool final = true)
        {
            bool pending = !final && !_hadFinal;
            var aliases = C.Dict("aliases");
            foreach (var kv in Prov.Aliases) aliases[kv.Key] = kv.Value;
            readings = readings.Select(r =>
            {
                object to; if (!aliases.TryGetValue(r.Key, out to)) return r;
                var c = r.Copy(); c.Key = Convert.ToString(to); return c;
            }).GroupBy(r => r.Key).Select(g => g.OrderByDescending(r => r.Online).First()).ToList();
            var knownAll = C.Dict("known"); var ov = C.Dict("overrides");
            foreach (var kv in aliases)
            {
                string from = kv.Key, to = Convert.ToString(kv.Value);
                if (ov.ContainsKey(from) && !ov.ContainsKey(to)) ov[to] = ov[from];
                ov.Remove(from); knownAll.Remove(from);
                C.SetStrings("hidden", C.Strings("hidden").Where(k => k != from));
                C.SetStrings("order", C.Strings("order").Select(k => k == from ? to : k).Distinct());
            }
            if (final) _hadFinal = true;
            Last = readings;
            var known = C.Dict("known");
            var live = readings.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in readings)
            {
                if (r.Online && r.Percent.HasValue)
                    known[r.Key] = new Dictionary<string, object> { { "name", r.Name }, { "kind", r.Kind }, { "percent", r.Percent.Value }, { "ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds() } };
                else if (!known.ContainsKey(r.Key))
                    known[r.Key] = new Dictionary<string, object> { { "name", r.Name }, { "kind", r.Kind }, { "percent", null }, { "ts", 0 } };
            }
            var hidden = new HashSet<string>(C.Strings("hidden"));
            var items = new List<Reading>();
            foreach (var kv in known)
            {
                if (hidden.Contains(kv.Key)) continue;
                var info = kv.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
                int? lastPct = Cfg.N(info, "percent").HasValue ? (int?)(int)Cfg.N(info, "percent").Value : null;
                Reading r;
                if (live.TryGetValue(kv.Key, out r))
                {
                    if (!r.Online && r.Percent == null) { r = r.Copy(); r.Percent = lastPct; r.Charging = false; }
                }
                else r = new Reading(kv.Key, Cfg.S(info, "name", kv.Key), Cfg.S(info, "kind", "other"), lastPct, false, pending);
                items.Add(Display(r));
            }
            var order = C.Strings("order");
            var kindOrder = new Dictionary<string, int> { { "mouse", 0 }, { "keyboard", 1 }, { "headset", 2 }, { "controller", 3 }, { "other", 4 } };
            Items = items.OrderBy(r => { int i = order.IndexOf(r.Key); return i < 0 ? 10000 : i; })
                         .ThenBy(r => kindOrder.ContainsKey(r.Kind) ? kindOrder[r.Kind] : 9).ThenBy(r => r.Name.ToLowerInvariant()).ToList();
            CheckLow();
            Save();
            Relayout();
            Suite?.UpdateTrayTip();
            _scanner?.OnLiveUpdate();
        }

        void CheckLow()
        {
            int thr = C.Int("low_threshold", 15);
            foreach (var r in Items)
            {
                if (!r.Online || r.Percent == null) continue;
                if (r.Percent <= thr && !r.Charging && _alerted.Add(r.Key)) { if (C.Bool("notify", true)) Suite?.Notify($"{r.Name}: {r.Percent}%", true, "Low battery"); }
                else if ((r.Percent > thr + 5 || r.Charging)) _alerted.Remove(r.Key);
            }
        }

        // ---- colors ---------------------------------------------------------------------------------
        public void IconSpec(Reading r, out int variant, out Color? color)
        {
            var o = Cfg.SubDict(C.Dict("overrides"), r.Key);
            var iv = C.Dict("icon_variants");
            double? v = Cfg.N(o, "variant") ?? Cfg.N(iv, r.Kind);
            variant = (int)(v ?? 0);
            string c = Cfg.S(o, "color") ?? Cfg.S(C.Dict("kind_colors"), r.Kind);
            color = null;
            if (!string.IsNullOrEmpty(c)) try { color = Theme.C(c); } catch { }
        }

        Color LevelColor(Reading r)
        {
            if (r.Percent == null) return Pal.Sub;
            int thr = Math.Max(C.Int("low_threshold", 15), 10);
            return r.Percent <= thr ? Pal.Bad : r.Percent < Math.Max(40, thr * 2) ? Pal.Warn : Pal.Good;
        }

        Color RingColor(Reading r)
        {
            int thr = Math.Max(C.Int("low_threshold", 15), 10);
            if (r.Percent.HasValue && r.Online && !r.Charging && r.Percent <= thr) return Pal.Bad;
            switch (C.Str("ring_mode", "level"))
            {
                case "accent": return Pal.Accent;
                case "custom": try { return Theme.C(C.Str("ring_color", "#7C5CFF")); } catch { return Pal.Accent; }
                case "icon": int v; Color? c; IconSpec(r, out v, out c); return c ?? Pal.Accent;
            }
            return LevelColor(r);
        }

        // ---- layout ---------------------------------------------------------------------------------
        static readonly Reading ScanItem = new Reading("__scan__", "Find devices", "__scan__");

        float NumWidth(Graphics g, float fp) => Fonts.Width(g, "100", Fonts.Get(fp, FontStyle.Regular, true, true)) + Fonts.Width(g, "%", Fonts.Get(fp * 0.62f, FontStyle.Regular, true)) + fp * 0.08f;

        protected override SizeF DoLayout(Graphics g)
        {
            var items = Items.Count > 0 ? Items : new List<Reading> { ScanItem };
            return TaskbarMode ? LayoutTaskbar(g, items) : LayoutDesktop(g, items);
        }

        void AddCell(RectangleF r, Reading it) => Cells.Add(new Cell { R = r, Key = it.Key, Kind = it.Kind, Item = it, Tip = TipFor(it) });

        string TipFor(Reading r)
        {
            if (r.Kind == "__scan__") return "No devices yet – click to find devices";
            string open = "\nClick to open its app";
            string pct = r.Percent == null ? "—" : (r.Approx ? "~" : "") + r.Percent + "%";
            string state = !r.Online ? "offline" : r.Charging ? "charging" : "";
            string kind = char.ToUpper(r.Kind[0]) + r.Kind.Substring(1);
            return $"{r.Name}\n{pct}  {(kind == "Other" ? "Device" : kind)}{(state != "" ? ", " + state : "")}{open}";
        }

        SizeF LayoutDesktop(Graphics g, List<Reading> items)
        {
            float s = S;
            bool horiz = C.Str("layout", "horizontal") != "vertical";
            _names = C.Bool("show_names");
            _d = 30 * s; _fp = 15 * s; _gap = 8 * s; _stacked = false;
            float padx = 12 * s, pady = 9 * s, between = horiz ? 16 * s : 9 * s;
            float itemW = _d + _gap + Math.Max(NumWidth(g, _fp), _names ? 96 * s : 0);
            float itemH = _names ? Math.Max(_d, _fp * 1.2f + 14 * s) : _d;
            float m = C.Bool("show_moon", true) ? 22 * s : 0;
            int n = items.Count;
            for (int i = 0; i < n; i++)
                AddCell(horiz ? new RectangleF(padx + i * (itemW + between), pady, itemW, itemH) : new RectangleF(padx, pady + i * (itemH + between), itemW, itemH), items[i]);
            float w, h;
            if (horiz)
            {
                w = padx * 2 + n * itemW + (n - 1) * between; h = pady * 2 + itemH;
                if (m > 0) { MoonRect = new RectangleF(w - padx + between * 0.35f, pady + (itemH - m) / 2, m, m); w += between * 0.35f + m; }
            }
            else
            {
                w = padx * 2 + itemW; h = pady * 2 + n * itemH + (n - 1) * between;
                if (m > 0) { MoonRect = new RectangleF(padx + (_d - m) / 2, h - pady + between * 0.6f, m, m); h += between * 0.6f + m; }
            }
            return new SizeF(w, h);
        }

        SizeF LayoutTaskbar(Graphics g, List<Reading> items)
        {
            float T = Tb.Thickness, sc = (float)C.Num("scale", 1);
            int n = items.Count;
            bool moon = C.Bool("show_moon", true);
            float G = C.Bool("locked") ? 0 : 14 * Dpi;
            _names = false;
            float w, h;
            if (Tb.Horizontal || T >= 110 * Dpi)
            {
                _d = Math.Max(16 * Dpi, Math.Min(Math.Min(T * 0.56f * sc, T - 8 * Dpi), 34 * Dpi));
                _fp = Math.Max(10 * Dpi, Math.Min(T * 0.30f * sc, 18 * Dpi));
                _gap = Math.Max(4 * Dpi, _d * 0.22f); _stacked = false;
                float between = Math.Max(10 * Dpi, _d * 0.5f), padx = 8 * Dpi, itemW = _d + _gap + NumWidth(g, _fp);
                if (Tb.Horizontal)
                {
                    h = T;
                    float x0 = padx + G;
                    for (int i = 0; i < n; i++) AddCell(new RectangleF(x0 + i * (itemW + between), 0, itemW, h), items[i]);
                    w = x0 + n * itemW + (n - 1) * between + padx;
                    if (moon) { float m = Math.Max(16 * Dpi, _d * 0.78f); MoonRect = new RectangleF(w - padx + between * 0.5f, (h - m) / 2, m, m); w += between * 0.5f + m; }
                    if (G > 0) GripRect = new RectangleF(3 * Dpi, h * 0.2f, G - 2 * Dpi, h * 0.6f);
                }
                else
                {
                    w = T; float itemH = _d + 6 * Dpi, y0 = 8 * Dpi + G;
                    for (int i = 0; i < n; i++) AddCell(new RectangleF((T - itemW) / 2, y0 + i * (itemH + 6 * Dpi), itemW, itemH), items[i]);
                    h = y0 + n * itemH + (n - 1) * 6 * Dpi + 8 * Dpi;
                    if (moon) { float m = Math.Max(16 * Dpi, _d * 0.78f); MoonRect = new RectangleF((T - m) / 2, h - 2 * Dpi, m, m); h += 6 * Dpi + m; }
                    if (G > 0) GripRect = new RectangleF(T * 0.2f, 3 * Dpi, T * 0.6f, G - 2 * Dpi);
                }
            }
            else
            {
                _d = Math.Max(16 * Dpi, Math.Min(T * 0.42f * sc, 30 * Dpi));
                _fp = Math.Max(9.5f * Dpi, Math.Min(T * 0.21f * sc, 15 * Dpi));
                _gap = 3 * Dpi; _stacked = true;
                float itemH = _d + 3 * Dpi + _fp * 1.25f, between = 10 * Dpi, pad = 8 * Dpi, y0 = pad + G;
                w = T;
                for (int i = 0; i < n; i++) AddCell(new RectangleF(2, y0 + i * (itemH + between), T - 4, itemH), items[i]);
                h = y0 + n * itemH + (n - 1) * between + pad;
                if (moon) { float m = Math.Max(16 * Dpi, _d * 0.8f); MoonRect = new RectangleF((T - m) / 2, h - pad + between, m, m); h += between + m; }
                if (G > 0) GripRect = new RectangleF(T * 0.2f, 3 * Dpi, T * 0.6f, G - 2 * Dpi);
            }
            return new SizeF(w, h);
        }

        // ---- paint -----------------------------------------------------------------------------------
        protected override void DoPaint(Graphics g)
        {
            if (TaskbarMode) PaintTaskbarBackground(g);
            else if (C.Bool("background", true))
            {
                var rect = new RectangleF(0.5f, 0.5f, LayoutSize.Width - 1, LayoutSize.Height - 1);
                float rad = C.Str("layout", "horizontal") != "vertical" ? Math.Min(rect.Height / 2, 22 * S) : 18 * S;
                using (var path = Draw.Round(rect, rad))
                {
                    if (!Backdrop(g, path, rect)) using (var b = new SolidBrush(Pal.Bg)) g.FillPath(b, path);
                    using (var p = new Pen(Pal.Metal ? Draw.A(Pal.Accent, 150) : Pal.Border, 1)) g.DrawPath(p, path);
                }
            }
            else using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, 0, 0, LayoutSize.Width, LayoutSize.Height);
            foreach (var c in Cells) PaintChip(g, c.R, (Reading)c.Item);
            if (MoonRect.HasValue) PaintMoon(g, MoonRect.Value);
        }

        void PaintChip(Graphics g, RectangleF rect, Reading r)
        {
            var st = g.Save();
            float alpha = r.Online ? 1f : 0.42f;
            RectangleF ring;
            if (_stacked) ring = new RectangleF(rect.X + rect.Width / 2 - _d / 2, rect.Top, _d, _d);
            else ring = new RectangleF(rect.Left, rect.Top + rect.Height / 2 - _d / 2, _d, _d);
            Ring(g, ring, r, alpha);
            if (_stacked) Pct(g, new RectangleF(rect.Left, ring.Bottom + _gap, rect.Width, _fp * 1.25f), r, true, alpha);
            else
            {
                float tx = ring.Right + _gap;
                if (_names && r.Kind != "__scan__")
                {
                    var line = new RectangleF(tx, rect.Top, rect.Right - tx, _fp * 1.2f);
                    Pct(g, line, r, false, alpha);
                    var f = Fonts.Get(10.5f * S);
                    using (var b = new SolidBrush(Draw.A(Pal.Sub, (int)(Pal.Sub.A * alpha))))
                        Fonts.Text(g, Fonts.Elide(g, r.Name, f, rect.Right - tx), f, b, tx, line.Bottom + Fonts.Ascent(f));
                }
                else Pct(g, new RectangleF(tx, rect.Top, rect.Right - tx, rect.Height), r, false, alpha);
            }
            g.Restore(st);
        }

        static Color Fade(Color c, float a) => Draw.A(c, (int)(c.A * a));

        void Ring(Graphics g, RectangleF box, Reading r, float alpha)
        {
            float w = Math.Max(1.8f, box.Width * 0.085f);
            var rr = RectangleF.Inflate(box, -w / 2, -w / 2);
            using (var p = new Pen(Fade(r.Kind == "__scan__" ? Pal.Sub : Pal.Track, alpha), w)) { if (r.Kind == "__scan__") p.DashStyle = DashStyle.Dash; g.DrawEllipse(p, rr); }
            if (r.Percent > 0)
            {
                bool gold = Pal.Metal && RingColor(r).ToArgb() != Pal.Bad.ToArgb() && C.Str("ring_mode", "level") != "custom";
                using (var brush = gold ? Draw.Gold(rr) : new SolidBrush(Fade(RingColor(r), alpha)))
                using (var p = new Pen(brush, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, rr, -90, 360f * Math.Min(100, r.Percent.Value) / 100f);
            }
            float inset = box.Width * 0.25f;
            int variant; Color? custom;
            IconSpec(r, out variant, out custom);
            var ic = custom ?? Draw.A(Pal.Text, (int)(Pal.Text.A * 0.92));
            Draw.DeviceIcon(g, r.Kind, RectangleF.Inflate(box, -inset, -inset), Fade(ic, alpha), variant);
            if (r.Charging && r.Online)
            {
                float b = box.Width * 0.44f;
                var badge = new RectangleF(box.Right - b * 0.78f, box.Bottom - b * 0.78f, b, b);
                using (var br = new SolidBrush(Pal.Good)) g.FillEllipse(br, badge);
                Draw.Bolt(g, RectangleF.Inflate(badge, -b * 0.27f, -b * 0.18f), Pal.Good.GetBrightness() > 0.47f ? Color.FromArgb(20, 22, 28) : Color.White);
            }
        }

        void Pct(Graphics g, RectangleF rect, Reading r, bool center, float alpha)
        {
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            var small = Fonts.Get(_fp * 0.62f, FontStyle.Regular, true);
            string num, sign = "", pre = "";
            if (r.Kind == "__scan__") { num = "Scan"; big = Fonts.Get(_fp * 0.78f, FontStyle.Regular, false, true); }
            else if (r.Percent == null) num = "–";
            else { num = r.Percent.ToString(); sign = "%"; pre = r.Approx ? "~" : ""; }
            float wPre = pre != "" ? Fonts.Width(g, pre, small) : 0;
            float wNum = Fonts.Width(g, num, big), wSign = sign != "" ? Fonts.Width(g, sign, small) + _fp * 0.05f : 0;
            float x = center ? rect.Left + rect.Width / 2 - (wPre + wNum + wSign) / 2 : rect.Left;
            float baseY = rect.Top + rect.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2;
            bool low = r.Percent.HasValue && r.Online && !r.Charging && r.Percent <= C.Int("low_threshold", 15);
            using (var sb = new SolidBrush(Fade(Pal.Sub, alpha)))
            using (var nb = new SolidBrush(Fade(low ? Pal.Bad : r.Kind == "__scan__" ? Pal.Sub : Pal.Text, alpha)))
            {
                if (pre != "") { Fonts.Text(g, pre, small, sb, x, baseY); x += wPre; }
                Fonts.Text(g, num, big, nb, x, baseY);
                x += wNum + _fp * 0.05f;
                if (sign != "") Fonts.Text(g, sign, small, sb, x, baseY);
            }
        }

        // ---- interaction -------------------------------------------------------------------------------
        protected override void OnScanClicked() => OpenScanner();

        // ---- the maker's app -----------------------------------------------------------------------------
        protected override void OnCellClick(Cell c) { if (c.Item is Reading r && r.Kind != "__scan__") OpenApp(r, false); }

        Reading Raw(Reading shown) => Last.FirstOrDefault(x => x.Key == shown.Key) ?? shown;     // source and USB id, before renaming

        AppTarget Saved(Reading r, string vendor)
        {
            var apps = C.Dict("apps");
            return AppTarget.From(apps.TryGetValue(r.Key, out var a) ? a as Dictionary<string, object> : null)
                ?? (vendor != "" ? AppTarget.From(Cfg.SubDict(C.Dict("vendor_apps"), vendor).Count > 0 ? Cfg.SubDict(C.Dict("vendor_apps"), vendor) : null) : null);
        }

        public string AppName(Reading shown)
        {
            var r = Raw(shown); string v = Apps.Vendor(r);
            var s = Saved(r, v);
            if (s != null) return s.Name;
            var c = Apps.Candidates(v, r.Name, () => new List<AppTarget>());           // quick: standard folders only
            return c.Count > 0 ? c[0].Name : null;
        }

        /// <summary>Opens the device's app: the one you chose, else the maker's app where it's normally installed,
        /// else an installed app with the right name, else asks you.</summary>
        public void OpenApp(Reading shown, bool ask)
        {
            var r = Raw(shown); string vendor = Apps.Vendor(r), name = shown.Name;
            var saved = ask ? null : Saved(r, vendor);
            if (saved != null && Apps.Launch(saved)) return;
            new Thread(() =>
            {
                var found = Apps.Candidates(vendor, r.Name);
                List<AppTarget> all = null;
                if (ask || found.Count == 0) all = Apps.Installed();
                BeginInvoke((Action)(() =>
                {
                    if (!ask && found.Count > 0 && Apps.Launch(found[0])) return;
                    using (var f = new AppPickerForm(MenuTheme, name, found, all ?? new List<AppTarget>()))
                    {
                        if (f.ShowDialog() != DialogResult.OK || f.Choice == null) return;
                        C.Dict("apps")[r.Key] = f.Choice.ToDict();
                        if (vendor != "" && vendor != "bluetooth") C.Dict("vendor_apps")[vendor] = f.Choice.ToDict();     // other devices of the same maker too
                        Save();
                        if (!Apps.Launch(f.Choice)) Toast.Show("up", "Couldn't open " + f.Choice.Name, "Right-click the device → Choose app…", MenuTheme, 4000);
                    }
                }));
            }) { IsBackground = true }.Start();
        }

        public void OpenScanner()
        {
            if (_scanner == null || _scanner.IsDisposed) _scanner = new ScannerForm(this);
            _scanner.Show();
            _scanner.Activate();
        }

        protected override void OnReorder(string key, int index)
        {
            var keys = Items.Select(r => r.Key).Where(k => k != key).ToList();
            keys.Insert(Math.Min(index, keys.Count), key);
            var rest = C.Strings("order").Where(k => !keys.Contains(k));
            C.SetStrings("order", keys.Concat(rest));
            Save();
            OnReadings(Last);
        }

        public void ApplyScan(ScanResult res)
        {
            var extra = C.Dict("extra_ids");
            foreach (var kv in res.DeepFound)
            {
                var cur = extra.TryGetValue(kv.Key, out var v) && v is List<object> l ? l.Select(Convert.ToString).ToList() : new List<string>();
                extra[kv.Key] = cur.Union(kv.Value).OrderBy(x => x).Cast<object>().ToList();
            }
            SyncExtra();
            OnReadings(Prov.Dedupe(res.Readings.Concat(Last)));
            RefreshNow();
        }

        void Set(string key, object value, bool relayout = true)
        {
            C[key] = value;
            Save();
            InvalidateTheme();
            Relayout();
            Suite?.ThemeChanged(this, key, value);
        }

        public override void FillMenu(ContextMenuStrip menu, bool common)
        {
            var t = MenuTheme;
            var it = menu.Items;
            if (MenuCell?.Item is Reading clicked && clicked.Kind != "__scan__")
            {
                string app = AppName(clicked);
                Menus.Item(it, app != null ? "Open " + app : "Open the app for " + clicked.Name, () => OpenApp(clicked, false));
                Menus.Item(it, "Choose app for " + clicked.Name + "…", () => OpenApp(clicked, true));
                Menus.Sep(it);
            }
            Menus.Item(it, $"Battery Widget {Program.Version}", null, null, false);
            Menus.Sep(it);
            Menus.Item(it, "Find devices…", OpenScanner);
            Menus.Item(it, "Refresh now", RefreshNow);
            Menus.Sep(it);
            var mode = Menus.Sub(it, "Show as", t);
            Menus.Item(mode.DropDownItems, "Desktop widget", () => SetMode("desktop"), !TaskbarMode);
            Menus.Item(mode.DropDownItems, "On the taskbar", () => SetMode("taskbar"), TaskbarMode);
            ThemeMenu(it, t, true);
            var look = Menus.Sub(it, "Look", t);
            var li = look.DropDownItems;
            if (!TaskbarMode)
            {
                bool horiz = C.Str("layout", "horizontal") != "vertical";
                Menus.Item(li, "Horizontal", () => Set("layout", "horizontal"), horiz);
                Menus.Item(li, "Vertical", () => Set("layout", "vertical"), !horiz);
                Menus.Sep(li);
                Menus.Item(li, "Show names", () => Set("show_names", !C.Bool("show_names")), C.Bool("show_names"));
                Menus.Item(li, "Background", () => Set("background", !C.Bool("background", true)), C.Bool("background", true));
                Menus.Item(li, "Blend with the wallpaper (frosted glass)", () => Set("blend", !C.Bool("blend")), C.Bool("blend"));
            }
            else
            {
                Menus.Item(li, "Follows the taskbar (horizontal / vertical)", null, null, false);
                Menus.Item(li, "Background", () => Set("tb_background", !C.Bool("tb_background")), C.Bool("tb_background"));
            }
            Menus.Item(li, "Moon button (screen-off timer)", () => Set("show_moon", !C.Bool("show_moon", true)), C.Bool("show_moon", true));
            SizeOpacity(li, t);
            PositionMenu(it, t);
            var scr = Menus.Sub(it, "Screen off after", t);
            FillScreenMenu(scr.DropDownItems);
            Menus.Sep(it);
            var up = Menus.Sub(it, "Updates and alerts", t);
            foreach (var p in new[] { new[] { "30", "Every 30 seconds" }, new[] { "60", "Every minute" }, new[] { "300", "Every 5 minutes" }, new[] { "600", "Every 10 minutes" } })
            { int v = int.Parse(p[0]); Menus.Item(up.DropDownItems, p[1], () => { Set("interval_s", v, false); RefreshNow(); }, C.Int("interval_s", 60) == v); }
            Menus.Sep(up.DropDownItems);
            Menus.Item(up.DropDownItems, "Low battery notifications", () => Set("notify", !C.Bool("notify", true), false), C.Bool("notify", true));
            var thr = Menus.Sub(up.DropDownItems, "Warn at", t);
            foreach (var v0 in new[] { 5, 10, 15, 20, 25, 30 }) { int v = v0; Menus.Item(thr.DropDownItems, v + "%", () => Set("low_threshold", v), C.Int("low_threshold", 15) == v); }
            var dev = Menus.Sub(it, "Devices", t);
            var known = C.Dict("known");
            if (known.Count > 0)
            {
                var hidden = new HashSet<string>(C.Strings("hidden"));
                foreach (var kv in known.OrderBy(k => Cfg.S(k.Value as Dictionary<string, object>, "name", k.Key).ToLowerInvariant()))
                {
                    string key = kv.Key;
                    string name = Cfg.S(Cfg.SubDict(C.Dict("overrides"), key), "name") ?? Cfg.S(kv.Value as Dictionary<string, object>, "name", key);
                    Menus.Item(dev.DropDownItems, name, () => ToggleHidden(key), !hidden.Contains(key));
                }
                Menus.Sep(dev.DropDownItems);
                Menus.Item(dev.DropDownItems, "Rename and change icons…", OpenScanner);
                Menus.Item(dev.DropDownItems, "Reset order", () => { C.SetStrings("order", new string[0]); Save(); OnReadings(Last); });
                Menus.Item(dev.DropDownItems, "Forget offline devices", ForgetOffline);
            }
            else Menus.Item(dev.DropDownItems, "No devices yet – Find devices…", OpenScanner);
            var src = Menus.Sub(it, "Data sources", t);
            var on = C.Dict("providers");
            if (Poller != null)
                foreach (var p in Poller.Providers)
                {
                    var pid = p.Id;
                    bool en = !(on.TryGetValue(pid, out var v) && v is bool b && !b);
                    var mi = Menus.Item(src.DropDownItems, p.Label + (en && p.Error != null ? "  ⚠" : ""), () => { on[pid] = !(on.TryGetValue(pid, out var vv) && vv is bool bb && !bb) ? false : true; Save(); RefreshNow(); }, en);
                    if (p.Error != null) mi.ToolTipText = p.Error;
                }
            Menus.Sep(it);
            if (common) Suite?.FillCommon(it, t);
        }

        public void ThemeMenu(ToolStripItemCollection it, Theme t, bool deviceIcons)
        {
            var th = Menus.Sub(it, "Theme", t);
            foreach (var name in Themes.All.Keys)
            {
                string n = name;
                Menus.Item(th.DropDownItems, n == "Auto" ? "Auto (follow Windows)" : n, () => Set("theme", n, false), C.Str("theme", "Dark") == n);
            }
            Menus.Sep(th.DropDownItems);
            Suite?.LinkItem(th.DropDownItems);
            Menus.Item(th.DropDownItems, "Custom accent color…", () => PickColor("accent", Pal.Accent));
            if (!string.IsNullOrEmpty(C.Str("accent", ""))) Menus.Item(th.DropDownItems, "Reset accent color", () => Set("accent", null, false));
            Menus.Sep(th.DropDownItems);
            var ring = Menus.Sub(th.DropDownItems, "Ring color", t);
            string mode = C.Str("ring_mode", "level");
            Menus.Item(ring.DropDownItems, "By battery level (green / yellow / red)", () => Set("ring_mode", "level", false), mode == "level");
            Menus.Item(ring.DropDownItems, "Accent color", () => Set("ring_mode", "accent", false), mode == "accent");
            Menus.Item(ring.DropDownItems, "Same as the icon color", () => Set("ring_mode", "icon", false), mode == "icon");
            Menus.Item(ring.DropDownItems, "Custom color…", () => { if (PickColor("ring_color", Theme.C(C.Str("ring_color", "#7C5CFF")))) Set("ring_mode", "custom", false); },
                mode == "custom", true, Draw.Swatch(Theme.C(C.Str("ring_color", "#7C5CFF"))));
            Menus.Sep(ring.DropDownItems);
            Menus.Item(ring.DropDownItems, "Low battery is always red", null, null, false);
            var kc = C.Dict("kind_colors");
            var iv = C.Dict("icon_variants");
            var col = Menus.Sub(th.DropDownItems, "Icon colors", t);
            foreach (var k in Draw.ColorKinds)
            {
                string kind = k[0];
                string cur = Cfg.S(kc, kind);
                Menus.Item(col.DropDownItems, k[1] + "…", () => PickKindColor(kind), null, true,
                    Draw.Preview(false, kind, (int)(Cfg.N(iv, kind) ?? 0), cur != null ? Theme.C(cur) : Color.FromArgb(255, t.Text)));
            }
            if (kc.Count > 0) { Menus.Sep(col.DropDownItems); Menus.Item(col.DropDownItems, "Reset icon colors", () => { kc.Clear(); Set("kind_colors", kc, false); }); }
            var icons = Menus.Sub(th.DropDownItems, "Icons", t);
            foreach (var k in Draw.ColorKinds)
            {
                string kind = k[0];
                var sub = Menus.Sub(icons.DropDownItems, k[1], t);
                int curV = (int)(Cfg.N(iv, kind) ?? 0);
                string cc = Cfg.S(kc, kind);
                foreach (var d in Draw.DeviceIcons.Where(x => x[0] == kind))
                {
                    int v = int.Parse(d[1]);
                    Menus.Item(sub.DropDownItems, d[2] + (curV == v ? "   ✓" : ""), () => { iv[kind] = v; Set("icon_variants", iv, false); }, null, true,
                        Draw.Preview(false, kind, v, cc != null ? Theme.C(cc) : Color.FromArgb(255, t.Text)));
                }
            }
            Menus.Item(icons.DropDownItems, "Per device: Find devices…", OpenScanner);
        }

        public void SizeOpacity(ToolStripItemCollection li, Theme t)
        {
            var size = Menus.Sub(li, "Size", t);
            foreach (var v0 in new[] { 0.8, 0.9, 1.0, 1.15, 1.3, 1.5 }) { double v = v0; Menus.Item(size.DropDownItems, Math.Round(v * 100) + "%", () => Set("scale", v), Math.Abs(C.Num("scale", 1) - v) < 0.01); }
            var op = Menus.Sub(li, "Opacity", t);
            foreach (var v0 in new[] { 1.0, 0.9, 0.8, 0.7, 0.6 }) { double v = v0; Menus.Item(op.DropDownItems, Math.Round(v * 100) + "%", () => { C["opacity"] = v; Save(); ApplyOpacity(); }, Math.Abs(C.Num("opacity", 1) - v) < 0.01); }
        }

        public void PositionMenu(ToolStripItemCollection it, Theme t)
        {
            var pos = Menus.Sub(it, "Position", t);
            var pi = pos.DropDownItems;
            if (TaskbarMode)
            {
                Menus.Item(pi, "Drag the handle (the line) to move it", null, null, false);
                Menus.Item(pi, "Shift + drag a device to change the order", null, null, false);
                Menus.Sep(pi);
                Menus.Item(pi, "Inside the taskbar (recommended)", () => { if (!C.Bool("tb_embed", true)) { C["tb_embed"] = true; Save(); Suite?.RestartAll(); } }, C.Bool("tb_embed", true));
                Menus.Item(pi, "Floating above the taskbar", () => { if (C.Bool("tb_embed", true)) { C["tb_embed"] = false; Save(); Suite?.RestartAll(); } }, !C.Bool("tb_embed", true));
                Menus.Sep(pi);
                Menus.Item(pi, "Move next to the tray", ResetTaskbarSpot);
                CrossItems(pi);
                if (!C.Bool("tb_embed", true)) Menus.Item(pi, "Hide during full-screen games and video", () => Set("tb_hide_fullscreen", !C.Bool("tb_hide_fullscreen", true), false), C.Bool("tb_hide_fullscreen", true));
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
        }

        public void SetMode(string mode)
        {
            if (C.Str("mode", "desktop") == mode) return;
            C["mode"] = mode; C["tb_offset"] = null; Save();
            Suite?.RestartAll();
        }

        bool PickColor(string key, Color cur)
        {
            using (var dlg = new ColorDialog { Color = cur, FullOpen = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return false;
                Set(key, Theme.Hex(dlg.Color), false);
                return true;
            }
        }

        void PickKindColor(string kind)
        {
            var kc = C.Dict("kind_colors");
            string cur = Cfg.S(kc, kind);
            using (var dlg = new ColorDialog { Color = cur != null ? Theme.C(cur) : Pal.Accent, FullOpen = true })
                if (dlg.ShowDialog() == DialogResult.OK) { kc[kind] = Theme.Hex(dlg.Color); Set("kind_colors", kc, false); }
        }

        void ToggleHidden(string key)
        {
            var h = C.Strings("hidden");
            if (h.Contains(key)) h.Remove(key); else h.Add(key);
            C.SetStrings("hidden", h); Save(); OnReadings(Last);
        }

        void ForgetOffline()
        {
            var live = new HashSet<string>(Items.Where(r => r.Online).Select(r => r.Key));
            var known = C.Dict("known");
            foreach (var k in known.Keys.ToList()) if (!live.Contains(k)) known.Remove(k);
            Last = Last.Where(r => r.Online).ToList();
            Save(); OnReadings(Last);
        }

        public override float VisualRadius => !TaskbarMode && C.Str("layout", "horizontal") != "vertical" ? Math.Min(LayoutSize.Height / 2f, 22 * S) : 18 * S;
        public override void Shutdown() { base.Shutdown(); Poller?.Stop(); }
    }
}
