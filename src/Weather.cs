// Weather.cs – the weather widget: now, today and tomorrow; click for the next 24 hours and 7 days.
// Forecast: Open-Meteo (free, no key). Location: Windows' location service, the IP address, or a place you pick.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class WxHour { public DateTime Time; public double Temp; public int Code; public int? Pop; public bool Day = true; }
    public class WxDay { public DateTime Date; public int Code; public double Max, Min; public int? Pop; public DateTime? Rise, Set; public double? Wind; }
    public class WxData
    {
        public double Temp, Feels, Humidity, Wind; public int Code; public bool Day = true; public DateTime Time;
        public List<WxHour> Hours = new List<WxHour>(); public List<WxDay> Days = new List<WxDay>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static double D(object o) => o == null ? double.NaN : Convert.ToDouble(o, Inv);
        static DateTime T(object o) => DateTime.Parse(Convert.ToString(o, Inv), Inv);
        static List<object> L(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as List<object> ?? new List<object>() : new List<object>();

        public static WxData Parse(string json)
        {
            var root = Cfg.FromJson(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("current")) throw new Exception("unexpected forecast");
            var c = Cfg.SubDict(root, "current");
            var w = new WxData { Temp = D(c["temperature_2m"]), Feels = D(c.ContainsKey("apparent_temperature") ? c["apparent_temperature"] : c["temperature_2m"]),
                Humidity = c.ContainsKey("relative_humidity_2m") ? D(c["relative_humidity_2m"]) : double.NaN, Wind = c.ContainsKey("wind_speed_10m") ? D(c["wind_speed_10m"]) : double.NaN,
                Code = Convert.ToInt32(c["weather_code"]), Day = !c.ContainsKey("is_day") || Convert.ToInt32(c["is_day"]) == 1, Time = T(c["time"]) };
            var h = Cfg.SubDict(root, "hourly");
            var ht = L(h, "time"); var htemp = L(h, "temperature_2m"); var hcode = L(h, "weather_code"); var hpop = L(h, "precipitation_probability"); var hday = L(h, "is_day");
            for (int i = 0; i < ht.Count; i++)
                w.Hours.Add(new WxHour { Time = T(ht[i]), Temp = D(htemp[i]), Code = Convert.ToInt32(hcode[i]), Pop = i < hpop.Count && hpop[i] != null ? (int?)Convert.ToInt32(hpop[i]) : null,
                    Day = i >= hday.Count || hday[i] == null || Convert.ToInt32(hday[i]) == 1 });
            var d = Cfg.SubDict(root, "daily");
            var dt = L(d, "time"); var dc = L(d, "weather_code"); var dmax = L(d, "temperature_2m_max"); var dmin = L(d, "temperature_2m_min");
            var dpop = L(d, "precipitation_probability_max"); var rise = L(d, "sunrise"); var set = L(d, "sunset"); var wind = L(d, "wind_speed_10m_max");
            for (int i = 0; i < dt.Count; i++)
                w.Days.Add(new WxDay { Date = T(dt[i]), Code = Convert.ToInt32(dc[i]), Max = D(dmax[i]), Min = D(dmin[i]),
                    Pop = i < dpop.Count && dpop[i] != null ? (int?)Convert.ToInt32(dpop[i]) : null,
                    Rise = i < rise.Count && rise[i] != null ? (DateTime?)T(rise[i]) : null, Set = i < set.Count && set[i] != null ? (DateTime?)T(set[i]) : null,
                    Wind = i < wind.Count && wind[i] != null ? (double?)D(wind[i]) : null });
            return w;
        }

        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "Clear"; case 1: return "Mostly clear"; case 2: return "Partly cloudy"; case 3: return "Overcast";
                case 45: return "Fog"; case 48: return "Freezing fog"; case 51: return "Light drizzle"; case 53: return "Drizzle"; case 55: return "Heavy drizzle";
                case 56: case 57: return "Freezing drizzle"; case 61: return "Light rain"; case 63: return "Rain"; case 65: return "Heavy rain";
                case 66: case 67: return "Freezing rain"; case 71: return "Light snow"; case 73: return "Snow"; case 75: return "Heavy snow"; case 77: return "Snow grains";
                case 80: return "Light showers"; case 81: return "Showers"; case 82: return "Heavy showers"; case 85: return "Snow showers"; case 86: return "Heavy snow showers";
                case 95: return "Thunderstorm"; case 96: case 99: return "Thunderstorm with hail";
            }
            return "Weather";
        }
    }

    public class Place { public string Name; public double Lat, Lon; public override string ToString() => Name; }

    public static class WxApi
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static WebClient Web() { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; var w = new WebClient { Encoding = System.Text.Encoding.UTF8 }; w.Headers["User-Agent"] = "BatteryWidget"; return w; }

        public static string ForecastUrl(double lat, double lon, bool fahrenheit, string wind) =>
            "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("F4", Inv) + "&longitude=" + lon.ToString("F4", Inv) +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
            "&hourly=temperature_2m,weather_code,precipitation_probability,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset,wind_speed_10m_max" +
            "&timezone=auto&forecast_days=7&temperature_unit=" + (fahrenheit ? "fahrenheit" : "celsius") + "&wind_speed_unit=" + wind;

        public static string Forecast(double lat, double lon, bool f, string wind) { using (var w = Web()) return w.DownloadString(ForecastUrl(lat, lon, f, wind)); }

        public static List<Place> Search(string q)
        {
            string json;
            using (var w = Web()) json = w.DownloadString("https://geocoding-api.open-meteo.com/v1/search?count=10&format=json&language=en&name=" + Uri.EscapeDataString(q));
            return ParsePlaces(json);
        }

        public static List<Place> ParsePlaces(string json)
        {
            var root = Cfg.FromJson(json) as Dictionary<string, object>;
            var res = root != null && root.TryGetValue("results", out var r) ? r as List<object> : null;
            return (res ?? new List<object>()).OfType<Dictionary<string, object>>().Select(p => new Place {
                Name = string.Join(", ", new[] { Cfg.S(p, "name"), Cfg.S(p, "admin1"), Cfg.S(p, "country") }.Where(x => !string.IsNullOrEmpty(x)).Distinct()),
                Lat = Cfg.N(p, "latitude") ?? 0, Lon = Cfg.N(p, "longitude") ?? 0 }).ToList();
        }

        /// <summary>Windows' location service first (if allowed in Settings › Privacy › Location), then the IP address.</summary>
        public static Place Locate()
        {
            Place ip = null;
            try
            {
                string json; using (var w = Web()) json = w.DownloadString("https://ipwho.is/?fields=success,latitude,longitude,city,country");
                var d = Cfg.FromJson(json) as Dictionary<string, object>;
                if (d != null && Cfg.S(d, "success", "True").ToLowerInvariant() != "false" && Cfg.N(d, "latitude").HasValue)
                    ip = new Place { Lat = Cfg.N(d, "latitude").Value, Lon = Cfg.N(d, "longitude").Value,
                        Name = string.Join(", ", new[] { Cfg.S(d, "city"), Cfg.S(d, "country") }.Where(x => !string.IsNullOrEmpty(x))) };
            }
            catch { }
            var win = WindowsLocation();
            if (win != null) { win.Name = ip?.Name ?? "My location"; return win; }
            return ip;
        }

        static Place WindowsLocation()
        {
            if (!Native.OnWindows) return null;
            try
            {
                var t = Type.GetType("System.Device.Location.GeoCoordinateWatcher, System.Device, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                if (t == null) return null;
                using (var watcher = (IDisposable)Activator.CreateInstance(t))
                {
                    bool ok = (bool)t.GetMethod("TryStart", new[] { typeof(bool), typeof(TimeSpan) }).Invoke(watcher, new object[] { false, TimeSpan.FromSeconds(4) });
                    if (!ok) return null;
                    var end = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < end)
                    {
                        var pos = t.GetProperty("Position").GetValue(watcher);
                        var loc = pos.GetType().GetProperty("Location").GetValue(pos);
                        bool unknown = (bool)loc.GetType().GetProperty("IsUnknown").GetValue(loc);
                        if (!unknown)
                            return new Place { Lat = (double)loc.GetType().GetProperty("Latitude").GetValue(loc), Lon = (double)loc.GetType().GetProperty("Longitude").GetValue(loc) };
                        Thread.Sleep(250);
                    }
                }
            }
            catch { }
            return null;
        }
    }

    public static class WxIcons
    {
        public static string Kind(int code)
        {
            if (code == 0) return "clear";
            if (code == 1 || code == 2) return "partly";
            if (code == 3) return "cloudy";
            if (code == 45 || code == 48) return "fog";
            if (code >= 51 && code <= 57) return "drizzle";
            if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return code == 65 || code == 82 ? "heavyrain" : "rain";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
            if (code >= 95) return "thunder";
            return "cloudy";
        }

        static void Cloud(Graphics g, float ox, float oy, float sc, Color c)
        {
            using (var b = new SolidBrush(c))          // filled piece by piece: the same result with every renderer
            {
                g.FillEllipse(b, ox + 3.4f * sc, oy + 8.6f * sc, 8.4f * sc, 8.4f * sc);
                g.FillEllipse(b, ox + 7.4f * sc, oy + 4.6f * sc, 10.4f * sc, 10.4f * sc);
                g.FillEllipse(b, ox + 13.4f * sc, oy + 9.4f * sc, 7.6f * sc, 7.6f * sc);
                g.FillRectangle(b, ox + 7.6f * sc, oy + 11 * sc, 9.8f * sc, 6 * sc);
            }
        }

        static void Sun(Graphics g, PointF c, float r, Color col)
        {
            using (var b = new SolidBrush(col)) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
            using (var p = new Pen(col, r * 0.32f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    g.DrawLine(p, c.X + (float)Math.Cos(a) * r * 1.45f, c.Y + (float)Math.Sin(a) * r * 1.45f, c.X + (float)Math.Cos(a) * r * 1.95f, c.Y + (float)Math.Sin(a) * r * 1.95f);
                }
        }

        public static void Paint(Graphics g, int code, bool day, RectangleF r, Theme t)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            Color sun = Theme.C("#FFC34D"), moon = Theme.C("#E8EAF6"), rain = Theme.C("#5AB4F5"), snow = Theme.C("#DDEBFF"), bolt = Theme.C("#FFD24A");
            bool darkBg = t.Bg.GetBrightness() < 0.55f || t.Bg.A < 160;
            Color cloud = darkBg ? Theme.C("#E9EDF5") : Theme.C("#B8C0CE"), grey = darkBg ? Theme.C("#AEB6C6") : Theme.C("#8E97A8");
            string k = Kind(code);
            switch (k)
            {
                case "clear":
                    if (day) Sun(g, new PointF(12, 12), 4.6f, sun); else Draw.Moon(g, new RectangleF(4.5f, 4.5f, 15, 15), moon);
                    break;
                case "partly":
                    if (day) Sun(g, new PointF(15.6f, 8.2f), 3.4f, sun); else Draw.Moon(g, new RectangleF(11.5f, 2.2f, 10, 10), moon);
                    Cloud(g, -1.2f, 3.6f, 1f, cloud);
                    break;
                case "cloudy": Cloud(g, 2.2f, -0.6f, 0.82f, grey); Cloud(g, -0.6f, 3.2f, 1f, cloud); break;
                case "fog":
                    using (var p = new Pen(cloud, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    { g.DrawLine(p, 4, 8, 20, 8); g.DrawLine(p, 2.5f, 12.5f, 18.5f, 12.5f); g.DrawLine(p, 5.5f, 17, 21.5f, 17); }
                    break;
                default:
                    Cloud(g, -0.4f, -2.4f, 1f, k == "thunder" || k == "heavyrain" ? grey : cloud);
                    using (var p = new Pen(k == "snow" ? snow : rain, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    using (var b = new SolidBrush(snow))
                    {
                        if (k == "drizzle") { g.DrawLine(p, 8, 17.5f, 7.4f, 19); g.DrawLine(p, 12.5f, 17.5f, 11.9f, 19); g.DrawLine(p, 17, 17.5f, 16.4f, 19); }
                        else if (k == "rain" || k == "heavyrain")
                        {
                            g.DrawLine(p, 8.6f, 17, 7.2f, 20.6f); g.DrawLine(p, 13.2f, 17, 11.8f, 20.6f);
                            if (k == "heavyrain") g.DrawLine(p, 17.6f, 17, 16.2f, 20.6f);
                        }
                        else if (k == "snow") foreach (var x in new[] { 8f, 12.5f, 17 }) g.FillEllipse(b, x - 1.3f, 18.2f - 1.3f, 2.6f, 2.6f);
                        else if (k == "thunder") Draw.Bolt(g, new RectangleF(10, 14, 6, 9), bolt);
                    }
                    break;
            }
            g.Restore(st);
        }
    }

    public class WeatherView : WidgetWindow
    {
        public static Dictionary<string, object> Defaults() => new Dictionary<string, object> {
            { "theme", "Twilight" }, { "accent", null }, { "mode", "desktop" }, { "style", "card" }, { "scale", 1.0 }, { "opacity", 1.0 },
            { "stacking", "bottom" }, { "tb_background", false }, { "tb_offset", null }, { "tb_embed", true }, { "tb_hide_fullscreen", true },
            { "locked", false }, { "pos", null }, { "loc_mode", "auto" }, { "lat", null }, { "lon", null }, { "place", "" },
            { "fahrenheit", false }, { "wind", "ms" }, { "interval_min", 30 }, { "cache", "" }, { "show_labels", true } };

        public WxData Data;
        public string Error;
        DateTime _fetched = DateTime.MinValue;
        bool _busy;
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 60000 };
        WeatherDetail _detail;
        string _geo = "";
        float _cardX, _cardInner, _cardM;
        RectangleF _icon, _temp, _today, _tomorrow;

        public WeatherView(Cfg cfg) : base(cfg)
        {
            Text = "Weather";
            try { if (C.Str("cache", "") != "") Data = WxData.Parse(C.Str("cache")); } catch { }
            _timer.Tick += (s, e) => { if ((DateTime.UtcNow - _fetched).TotalMinutes >= Math.Max(10, C.Int("interval_min", 30))) RefreshNow(); };
            _timer.Start();
        }

        protected override int DefaultTopOffset => 300;
        string Deg => "°";
        public string WindUnit => C.Str("wind", "ms") == "kmh" ? "km/h" : C.Str("wind", "ms") == "mph" ? "mph" : "m/s";
        static string T0(double v) => double.IsNaN(v) ? "–" : Math.Round(v).ToString(CultureInfo.InvariantCulture);
        public string PlaceName => C.Str("place", "") != "" ? C.Str("place") : "Locating…";

        public override void RefreshNow()
        {
            if (_busy) return;
            _busy = true;
            new Thread(() =>
            {
                string err = null, json = null; Place found = null;
                try
                {
                    double? lat = C.NumOrNull("lat"), lon = C.NumOrNull("lon");
                    if (C.Str("loc_mode", "auto") == "auto" && (lat == null || (DateTime.UtcNow - _fetched).TotalHours > 6 || _fetched == DateTime.MinValue))
                    {
                        found = WxApi.Locate();
                        if (found != null) { lat = found.Lat; lon = found.Lon; }
                    }
                    if (lat == null) throw new Exception("no location – right-click → Location → Set location…");
                    json = WxApi.Forecast(lat.Value, lon.Value, C.Bool("fahrenheit"), C.Str("wind", "ms"));
                }
                catch (Exception ex) { err = ex.Message; }
                if (IsHandleCreated) BeginInvoke((Action)(() => Arrived(json, found, err)));
                else _busy = false;
            }) { IsBackground = true }.Start();
        }

        public void Arrived(string json, Place found, string err)
        {
            _busy = false;
            if (found != null) { C["lat"] = found.Lat; C["lon"] = found.Lon; C["place"] = found.Name; }
            if (json != null)
            {
                try { Data = WxData.Parse(json); C["cache"] = json; Error = null; _fetched = DateTime.UtcNow; }
                catch (Exception ex) { Error = ex.Message; }
            }
            else Error = err;
            Save(); Relayout();
            if (_detail != null && !_detail.IsDisposed && _detail.Visible) _detail.Invalidate();
        }

        int IndexOfToday() => Data == null ? -1 : Data.Days.FindIndex(d => d.Date.Date == Data.Time.Date);

        // ---- layout ---------------------------------------------------------------------------
        protected override SizeF DoLayout(Graphics g)
        {
            if (TaskbarMode) return LayoutTaskbar(g);
            return C.Str("style", "card") == "compact" ? LayoutCompact(g) : LayoutCard(g);
        }

        void AddCell(RectangleF r, string key, string tip) => Cells.Add(new Cell { R = r, Key = key, Kind = "wx", Tip = tip });
        string TipNow => Data == null ? (Error != null ? "Weather: " + Error : "Getting the forecast…")
            : $"{PlaceName}\n{WxData.Describe(Data.Code)}, {T0(Data.Temp)}{Deg} (feels {T0(Data.Feels)}{Deg})\nClick for the next hours and days";

        SizeF LayoutCard(Graphics g)
        {
            float s = S, M = 14 * s, W = 286 * s, pad = 16 * s, x = M + pad, inner = W - 2 * pad, y = M + pad;
            _cardM = M; _cardX = x; _cardInner = inner;
            _icon = new RectangleF(x - 2 * s, y, 54 * s, 54 * s);
            _temp = new RectangleF(_icon.Right + 6 * s, y, 100 * s, 54 * s);
            AddCell(RectangleF.Union(_icon, _temp), "wx:now", TipNow);
            y += 54 * s + 30 * s;                         // feels-like line
            y += 12 * s;                                 // divider gap
            _today = new RectangleF(x, y, inner, 30 * s); AddCell(_today, "wx:today", "Click for the next hours and days");
            _tomorrow = new RectangleF(x, y + 32 * s, inner, 30 * s); AddCell(_tomorrow, "wx:tomorrow", "Click for the next hours and days");
            y += 64 * s;
            _geo = "card";
            return new SizeF(W + 2 * M, y + pad - 4 * s + M);
        }

        SizeF LayoutCompact(Graphics g)
        {
            float s = S, pad = 10 * s, D = 30 * s;
            _icon = new RectangleF(pad, pad, D, D);
            float tw = Fonts.Width(g, "-88°", Fonts.Get(17 * s, FontStyle.Regular, true, true));
            _temp = new RectangleF(_icon.Right + 6 * s, pad, tw, D);
            float hw = Fonts.Width(g, "↑88° ↓-88°  Tomorrow 88°", Fonts.Get(11.5f * s));
            _today = new RectangleF(_temp.Right + 10 * s, pad, hw, D);
            AddCell(new RectangleF(pad, pad, _today.Right - pad, D), "wx:now", TipNow);
            _geo = "compact";
            return new SizeF(_today.Right + pad + 4 * s, D + 2 * pad);
        }

        SizeF LayoutTaskbar(Graphics g)
        {
            float T = Tb.Thickness, sc = (float)C.Num("scale", 1), G = C.Bool("locked") ? 0 : 14 * Dpi;
            float D = Math.Max(18 * Dpi, Math.Min(Math.Min(T * 0.6f * sc, T - 8 * Dpi), 34 * Dpi)), fp = Math.Max(11 * Dpi, Math.Min(T * 0.32f * sc, 18 * Dpi));
            float w, h;
            if (Tb.Horizontal)
            {
                h = T;
                _icon = new RectangleF(8 * Dpi + G, (T - D) / 2, D, D);
                float tw = Fonts.Width(g, "-88°", Fonts.Get(fp, FontStyle.Regular, true, true));
                _temp = new RectangleF(_icon.Right + 5 * Dpi, 0, tw, T);
                float sw = C.Bool("show_labels", true) ? Fonts.Width(g, "↑88° ↓-88°", Fonts.Get(fp * 0.62f)) : 0;
                _today = new RectangleF(_temp.Right + 4 * Dpi, 0, sw, T);
                w = _today.Right + 8 * Dpi;
                if (G > 0) GripRect = new RectangleF(3 * Dpi, h * 0.2f, G - 2 * Dpi, h * 0.6f);
            }
            else
            {
                w = T;
                _icon = new RectangleF((T - D) / 2, 8 * Dpi + G, D, D);
                _temp = new RectangleF(0, _icon.Bottom + 2 * Dpi, T, fp * 1.3f);
                _today = RectangleF.Empty;
                h = _temp.Bottom + 8 * Dpi;
                if (G > 0) GripRect = new RectangleF(T * 0.2f, 3 * Dpi, T * 0.6f, G - 2 * Dpi);
            }
            AddCell(new RectangleF(_icon.X, 0, Math.Max(_today.Right, _temp.Right) - _icon.X, h), "wx:now", TipNow);
            _geo = "taskbar"; _fp = fp;
            return new SizeF(w, h);
        }
        float _fp;

        // ---- paint -------------------------------------------------------------------------------
        protected override void DoPaint(Graphics g)
        {
            if (_geo == "taskbar") { PaintTaskbarBackground(g); PaintTaskbar(g); return; }
            if (_geo == "compact") { PaintCompact(g); return; }
            PaintCard(g);
        }

        void Txt(Graphics g, float x, float baseY, string s, Font f, Color c, bool right = false, bool gold = false)
        {
            float w = Fonts.Width(g, s, f), x0 = right ? x - w : x;
            using (var b = gold && Pal.Metal ? Draw.Gold(new RectangleF(x0, baseY - Fonts.Ascent(f), Math.Max(1, w), Fonts.Ascent(f) * 1.1f)) : new SolidBrush(c)) Fonts.Text(g, s, f, b, x0, baseY);
        }

        void CardBackground(Graphics g)
        {
            float s = S, M = _cardM, rad = 18 * s;
            var card = new RectangleF(M, M, LayoutSize.Width - 2 * M, LayoutSize.Height - 2 * M);
            for (int i = 0; i < 8; i++)
            {
                float grow = (8 - i) * 1.4f * s;
                using (var b = new SolidBrush(Color.FromArgb(7, 0, 0, 0))) using (var p = Draw.Round(new RectangleF(card.X - grow, card.Y - grow + 4 * s, card.Width + 2 * grow, card.Height + 2 * grow), rad + grow)) g.FillPath(b, p);
            }
            Color bg = Pal.Bg, acc = Pal.Accent;
            Color end = Pal.Metal ? Color.FromArgb(bg.A, 24, 32, 43)
                : Color.FromArgb(bg.A, (int)Math.Min(255, bg.R * 0.8 + acc.R * 0.11), (int)Math.Min(255, bg.G * 0.8 + acc.G * 0.11), (int)Math.Min(255, bg.B * 0.8 + acc.B * 0.17 + 18));
            if (Pal.Glass) end = bg;
            using (var path = Draw.Round(card, rad))
            {
                if (!Backdrop(g, path, card)) using (var gb = new LinearGradientBrush(card, bg, end, LinearGradientMode.ForwardDiagonal)) g.FillPath(gb, path);
                if (Pal.Metal) using (var eb = Draw.Gold(card, false, 170)) using (var p = new Pen(eb, 1.3f * s)) g.DrawPath(p, path);
                else using (var p = new Pen(Pal.Border)) g.DrawPath(p, path);
            }
        }

        void PaintCard(Graphics g)
        {
            float s = S;
            CardBackground(g);
            bool dark = Pal.Bg.GetBrightness() < 0.55f;
            Color line = Pal.Metal ? Color.FromArgb(48, 201, 161, 90) : dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
            float right = _cardX + _cardInner;
            if (Data == null)
            {
                WxIcons.Paint(g, 3, true, _icon, Pal);
                Txt(g, _temp.X, _temp.Y + 36 * s, "–" + Deg, Fonts.Get(34 * s, FontStyle.Regular, true, true), Pal.Text);
                Txt(g, right, _icon.Y + 20 * s, Error != null ? "No forecast" : "Getting the forecast…", Fonts.Get(12 * s), Pal.Sub, true);
                if (Error != null) Txt(g, _cardX, _icon.Bottom + 22 * s, Fonts.Elide(g, Error, Fonts.Get(11 * s), _cardInner), Fonts.Get(11 * s), Pal.Sub);
                return;
            }
            WxIcons.Paint(g, Data.Code, Data.Day, _icon, Pal);
            var big = Fonts.Get(34 * s, FontStyle.Regular, true, true);
            Txt(g, _temp.X, _temp.Y + 39 * s, T0(Data.Temp) + Deg, big, Pal.Text, false, true);
            var f12 = Fonts.Get(12 * s);
            Txt(g, right, _icon.Y + 16 * s, Fonts.Elide(g, PlaceName, f12, 120 * s), f12, Pal.Sub, true);
            Txt(g, right, _icon.Y + 34 * s, WxData.Describe(Data.Code), Fonts.Get(12.5f * s, FontStyle.Regular, false, true), Pal.Text, true);
            string info = $"Feels {T0(Data.Feels)}{Deg}" + (double.IsNaN(Data.Wind) ? "" : $" · Wind {T0(Data.Wind)} {WindUnit}") + (double.IsNaN(Data.Humidity) ? "" : $" · {T0(Data.Humidity)}% humidity");
            Txt(g, _cardX, _icon.Bottom + 20 * s, Fonts.Elide(g, info, Fonts.Get(11.5f * s), _cardInner), Fonts.Get(11.5f * s), Pal.Sub);
            float dy = _icon.Bottom + 34 * s;
            using (var p = new Pen(line)) g.DrawLine(p, _cardX, dy, right, dy);
            int ti = IndexOfToday();
            DayRow(g, _today, "Today", ti >= 0 && ti < Data.Days.Count ? Data.Days[ti] : null);
            DayRow(g, _tomorrow, "Tomorrow", ti >= 0 && ti + 1 < Data.Days.Count ? Data.Days[ti + 1] : null);
        }

        void DayRow(Graphics g, RectangleF r, string label, WxDay d)
        {
            float s = S, mid = r.Y + r.Height / 2;
            Txt(g, r.X, mid + 4.5f * s, label, Fonts.Get(12.5f * s), Pal.Text);
            if (d == null) return;
            WxIcons.Paint(g, d.Code, true, new RectangleF(r.X + 92 * s, mid - 12 * s, 24 * s, 24 * s), Pal);
            var fn = Fonts.Get(14 * s, FontStyle.Regular, true, true);
            float mw = Txt2(g, r.Right, mid + 5 * s, T0(d.Min) + Deg, Fonts.Get(14 * s, FontStyle.Regular, true), Pal.Sub);
            Txt(g, r.Right - mw - 8 * s, mid + 5 * s, T0(d.Max) + Deg, fn, Pal.Text, true, true);
            if (d.Pop.HasValue && d.Pop > 0)
            {
                using (var b = new SolidBrush(Theme.C("#5AB4F5"))) g.FillEllipse(b, r.X + 126 * s, mid - 3 * s, 6 * s, 6 * s);
                Txt(g, r.X + 136 * s, mid + 4.5f * s, d.Pop + "%", Fonts.Get(11.5f * s), Pal.Sub);
            }
        }

        float Txt2(Graphics g, float right, float baseY, string s, Font f, Color c) { float w = Fonts.Width(g, s, f); Txt(g, right, baseY, s, f, c, true); return w; }

        void PaintCompact(Graphics g)
        {
            float s = S;
            var rect = new RectangleF(0.5f, 0.5f, LayoutSize.Width - 1, LayoutSize.Height - 1);
            using (var path = Draw.Round(rect, Math.Min(rect.Height / 2, 22 * s)))
            {
                if (!Backdrop(g, path, rect)) using (var b = new SolidBrush(Pal.Bg)) g.FillPath(b, path);
                using (var p = new Pen(Pal.Metal ? Draw.A(Pal.Accent, 120) : Pal.Border)) g.DrawPath(p, path);
            }
            WxIcons.Paint(g, Data?.Code ?? 3, Data?.Day ?? true, _icon, Pal);
            var big = Fonts.Get(17 * s, FontStyle.Regular, true, true);
            Txt(g, _temp.X, _temp.Y + _temp.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, Data == null ? "–" : T0(Data.Temp) + Deg, big, Pal.Text, false, true);
            int ti = IndexOfToday();
            if (Data != null && ti >= 0)
            {
                var d = Data.Days[ti];
                string tm = ti + 1 < Data.Days.Count ? $"  Tomorrow {T0(Data.Days[ti + 1].Max)}{Deg}" : "";
                Txt(g, _today.X, _today.Y + _today.Height / 2 + 4.5f * s, $"↑{T0(d.Max)}{Deg} ↓{T0(d.Min)}{Deg}{tm}", Fonts.Get(11.5f * s), Pal.Sub);
            }
        }

        void PaintTaskbar(Graphics g)
        {
            WxIcons.Paint(g, Data?.Code ?? 3, Data?.Day ?? true, _icon, Pal);
            var big = Fonts.Get(_fp, FontStyle.Regular, true, true);
            string t = Data == null ? "–" : T0(Data.Temp) + Deg;
            if (Tb.Horizontal) Txt(g, _temp.X, _temp.Y + _temp.Height / 2 + (Fonts.Ascent(big) - Fonts.Descent(big)) / 2, t, big, Pal.Text, false, true);
            else Txt(g, _temp.X + (_temp.Width - Fonts.Width(g, t, big)) / 2, _temp.Y + Fonts.Ascent(big), t, big, Pal.Text, false, true);
            int ti = IndexOfToday();
            if (Tb.Horizontal && C.Bool("show_labels", true) && Data != null && ti >= 0)
            {
                var d = Data.Days[ti]; var f = Fonts.Get(_fp * 0.62f);
                Txt(g, _today.X, _today.Y + _today.Height / 2 - 2 * Dpi, $"↑{T0(d.Max)}{Deg}", f, Pal.Sub);
                Txt(g, _today.X, _today.Y + _today.Height / 2 + _fp * 0.62f, $"↓{T0(d.Min)}{Deg}", f, Pal.Sub);
            }
        }

        // ---- interaction ------------------------------------------------------------------------------
        protected override void OnCellClick(Cell c) => ShowDetail();

        public void ShowDetail()
        {
            if (Data == null) { RefreshNow(); return; }
            if (_detail != null && !_detail.IsDisposed) { _detail.Close(); _detail = null; return; }
            _detail = new WeatherDetail(this);
            var me = new Rectangle(Left, Top, Width, Height);
            if (Embedded) { var p = PointToScreen(Point.Empty); me = new Rectangle(p, Size); }
            var scr = Screen.FromRectangle(me).WorkingArea;
            int x = Math.Max(scr.Left + 8, Math.Min(me.Left, scr.Right - _detail.Width - 8));
            int y = me.Bottom + 8 + _detail.Height <= scr.Bottom ? me.Bottom + 8 : Math.Max(scr.Top + 8, me.Top - _detail.Height - 8);
            if (!TaskbarMode && me.Right + 8 + _detail.Width <= scr.Right) { x = me.Right + 8; y = Math.Max(scr.Top + 8, Math.Min(me.Top, scr.Bottom - _detail.Height - 8)); }
            else if (!TaskbarMode && me.Left - 8 - _detail.Width >= scr.Left) { x = me.Left - 8 - _detail.Width; y = Math.Max(scr.Top + 8, Math.Min(me.Top, scr.Bottom - _detail.Height - 8)); }
            _detail.Location = new Point(x, y);
            _detail.Show();
            _detail.Activate();
        }

        public override float VisualInset => _geo == "card" ? _cardM : 0;
        public override float VisualRadius => _geo == "card" ? 18 * S : Math.Min(LayoutSize.Height / 2f, 22 * S);

        void Set(string k, object v, bool refetch = false)
        {
            C[k] = v; Save(); InvalidateTheme(); Relayout();
            Suite?.ThemeChanged(this, k, v);
            if (refetch) { _fetched = DateTime.MinValue; RefreshNow(); }
        }

        public void SetPlace(Place p)
        {
            if (p == null) { C["loc_mode"] = "auto"; C["lat"] = null; C["lon"] = null; C["place"] = ""; }
            else { C["loc_mode"] = "manual"; C["lat"] = p.Lat; C["lon"] = p.Lon; C["place"] = p.Name; }
            Save(); _fetched = DateTime.MinValue; Data = null; Relayout(); RefreshNow();
        }

        public override void FillMenu(ContextMenuStrip menu, bool common)
        {
            var t = MenuTheme; var it = menu.Items;
            Menus.Item(it, "Weather", null, null, false);
            Menus.Sep(it);
            Menus.Item(it, "Forecast for the next days…", ShowDetail);
            Menus.Item(it, "Refresh now", () => { _fetched = DateTime.MinValue; RefreshNow(); });
            Menus.Sep(it);
            var loc = Menus.Sub(it, "Location", t);
            bool auto = C.Str("loc_mode", "auto") == "auto";
            Menus.Item(loc.DropDownItems, "Automatic" + (auto && C.Str("place", "") != "" ? " – " + C.Str("place") : ""), () => SetPlace(null), auto);
            Menus.Item(loc.DropDownItems, (!auto ? C.Str("place") + " – " : "") + "Set location…", () => new LocationForm(this).ShowDialog(), !auto);
            var units = Menus.Sub(it, "Units", t);
            Menus.Item(units.DropDownItems, "Celsius (°C)", () => Set("fahrenheit", false, true), !C.Bool("fahrenheit"));
            Menus.Item(units.DropDownItems, "Fahrenheit (°F)", () => Set("fahrenheit", true, true), C.Bool("fahrenheit"));
            Menus.Sep(units.DropDownItems);
            foreach (var p in new[] { new[] { "ms", "Wind in m/s" }, new[] { "kmh", "Wind in km/h" }, new[] { "mph", "Wind in mph" } })
            { string k = p[0]; Menus.Item(units.DropDownItems, p[1], () => Set("wind", k, true), C.Str("wind", "ms") == k); }
            var upd = Menus.Sub(it, "Update every", t);
            foreach (var m0 in new[] { 15, 30, 60 }) { int m = m0; Menus.Item(upd.DropDownItems, m + " minutes", () => Set("interval_min", m), C.Int("interval_min", 30) == m); }
            Menus.Sep(it);
            var mode = Menus.Sub(it, "Show as", t);
            Menus.Item(mode.DropDownItems, "Desktop widget", () => { if (TaskbarMode) { C["mode"] = "desktop"; Save(); Suite?.RestartAll(); } }, !TaskbarMode);
            Menus.Item(mode.DropDownItems, "On the taskbar", () => { if (!TaskbarMode) { C["mode"] = "taskbar"; C["tb_offset"] = null; Save(); Suite?.RestartAll(); } }, TaskbarMode);
            var th = Menus.Sub(it, "Theme", t);
            foreach (var name in Themes.All.Keys) { string n = name; Menus.Item(th.DropDownItems, n == "Auto" ? "Auto (follow Windows)" : n, () => Set("theme", n), C.Str("theme", "Twilight") == n); }
            Suite?.LinkItem(th.DropDownItems);
            var look = Menus.Sub(it, "Look", t);
            if (!TaskbarMode)
            {
                Menus.Item(look.DropDownItems, "Card (now, today, tomorrow)", () => Set("style", "card"), C.Str("style", "card") == "card");
                Menus.Item(look.DropDownItems, "Compact (one line)", () => Set("style", "compact"), C.Str("style", "card") == "compact");
                Menus.Item(look.DropDownItems, "Blend with the wallpaper (frosted glass)", () => Set("blend", !C.Bool("blend")), C.Bool("blend"));
            }
            else Menus.Item(look.DropDownItems, "Today's high and low", () => Set("show_labels", !C.Bool("show_labels", true)), C.Bool("show_labels", true));
            Menus.Sep(look.DropDownItems);
            var size = Menus.Sub(look.DropDownItems, "Size", t);
            foreach (var v0 in new[] { 0.8, 0.9, 1.0, 1.15, 1.3, 1.5 }) { double v = v0; Menus.Item(size.DropDownItems, Math.Round(v * 100) + "%", () => Set("scale", v), Math.Abs(C.Num("scale", 1) - v) < 0.01); }
            var op = Menus.Sub(look.DropDownItems, "Opacity", t);
            foreach (var v0 in new[] { 1.0, 0.9, 0.8, 0.7, 0.6 }) { double v = v0; Menus.Item(op.DropDownItems, Math.Round(v * 100) + "%", () => { C["opacity"] = v; Save(); ApplyOpacity(); }, Math.Abs(C.Num("opacity", 1) - v) < 0.01); }
            var pos = Menus.Sub(it, "Position", t);
            if (TaskbarMode)
            {
                Menus.Item(pos.DropDownItems, "Drag the handle (the line) to move it", null, null, false);
                Menus.Item(pos.DropDownItems, "Move to the default spot", ResetTaskbarSpot);
                CrossItems(pos.DropDownItems);
            }
            else foreach (var p in new[] { new[] { "wallpaper", "Part of the wallpaper (behind desktop icons)" }, new[] { "bottom", "On the desktop (behind windows)" }, new[] { "normal", "Normal" }, new[] { "top", "Always on top" } })
                { string k = p[0]; Menus.Item(pos.DropDownItems, p[1], () => { C["stacking"] = k; Save(); Suite?.RestartAll(); }, C.Str("stacking", "bottom") == k); }
            Menus.Item(pos.DropDownItems, "Lock position", () => Set("locked", !C.Bool("locked")), C.Bool("locked"));
            Menus.Sep(it);
            if (common) Suite?.FillCommon(it, t);
        }

        public override void Shutdown() { _timer.Stop(); if (_detail != null && !_detail.IsDisposed) _detail.Close(); base.Shutdown(); }
    }

    /// <summary>The forecast in detail: next 24 hours and 7 days.</summary>
    public class WeatherDetail : Form
    {
        readonly WeatherView W;
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        public WeatherDetail(WeatherView w)
        {
            W = w;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
            DoubleBuffered = true;
            float s = WidgetWindow.Dpi;
            Size = new Size((int)(560 * s), (int)(470 * s));
            var pal = w.MenuTheme;
            BackColor = Color.FromArgb(255, pal.Bg.A < 200 ? Color.FromArgb(20, 22, 29) : pal.Bg);
            Deactivate += (o, e) => Close();
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Click += (o, e) => Close();
        }

        protected override void OnPaint(PaintEventArgs e) => PaintAll(e.Graphics);

        public void PaintAll(Graphics g)
        {
            Draw.Hq(g);
            var t = W.MenuTheme; float s = WidgetWindow.Dpi;
            var d = W.Data; if (d == null) return;
            Color text = Color.FromArgb(255, t.Text), sub = Color.FromArgb(255, t.Sub), line = Draw.A(t.Sub, 60);
            using (var p = new Pen(t.Metal ? Draw.A(t.Accent, 160) : Color.FromArgb(255, t.Border), 1.5f)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Func<float, bool, bool, Font> F = (px, num, bold) => Fonts.Get(px * s, FontStyle.Regular, num, bold);
            Action<string, float, float, Font, Color, bool> T = (str, px, py, f, c, right) =>
            {
                float w0 = Fonts.Width(g, str, f);
                using (var b = new SolidBrush(c)) Fonts.Text(g, str, f, b, right ? px - w0 : px, py);
            };
            float pad = 20 * s, x0 = pad, y = pad;
            WxIcons.Paint(g, d.Code, d.Day, new RectangleF(x0, y, 64 * s, 64 * s), t);
            T(Math.Round(d.Temp) + "°", x0 + 74 * s, y + 46 * s, F(44, true, true), text, false);
            T(W.PlaceName, Width - pad, y + 18 * s, F(15, false, true), text, true);
            T(WxData.Describe(d.Code), Width - pad, y + 40 * s, F(13, false, false), sub, true);
            int ti = d.Days.FindIndex(q => q.Date.Date == d.Time.Date);
            var today = ti >= 0 ? d.Days[ti] : null;
            var bits = new List<string> { $"Feels {Math.Round(d.Feels)}°" };
            if (today != null) bits.Insert(0, $"↑{Math.Round(today.Max)}° ↓{Math.Round(today.Min)}°");
            if (!double.IsNaN(d.Wind)) bits.Add($"Wind {Math.Round(d.Wind)} {W.WindUnit}");
            if (!double.IsNaN(d.Humidity)) bits.Add($"{Math.Round(d.Humidity)}% humidity");
            if (today?.Rise != null) bits.Add($"☀ {today.Rise:HH:mm} – {today.Set:HH:mm}");
            T(string.Join("  ·  ", bits), x0, y + 86 * s, F(12, false, false), sub, false);
            y += 104 * s;
            using (var p = new Pen(line)) g.DrawLine(p, x0, y, Width - pad, y);
            // next 24 hours, every 3 hours
            y += 12 * s;
            T("Next 24 hours", x0, y + 12 * s, F(12.5f, false, true), text, false);
            y += 22 * s;
            var hours = d.Hours.Where(h => h.Time >= d.Time.AddMinutes(-59)).Take(24).Where((h, i) => i % 3 == 0).ToList();
            float colW = (Width - 2 * pad) / Math.Max(1, hours.Count);
            for (int i = 0; i < hours.Count; i++)
            {
                var h = hours[i]; float cx = x0 + colW * i + colW / 2;
                string tm = i == 0 ? "Now" : h.Time.ToString("HH:mm", En);
                T(tm, cx - Fonts.Width(g, tm, F(11, false, false)) / 2, y + 12 * s, F(11, false, false), sub, false);
                WxIcons.Paint(g, h.Code, h.Day, new RectangleF(cx - 14 * s, y + 18 * s, 28 * s, 28 * s), t);
                string tv = Math.Round(h.Temp) + "°";
                T(tv, cx - Fonts.Width(g, tv, F(14, true, true)) / 2, y + 64 * s, F(14, true, true), text, false);
                if (h.Pop > 0) { string pp = h.Pop + "%"; T(pp, cx - Fonts.Width(g, pp, F(10.5f, false, false)) / 2, y + 80 * s, F(10.5f, false, false), Theme.C("#5AB4F5"), false); }
            }
            y += 90 * s;
            using (var p = new Pen(line)) g.DrawLine(p, x0, y, Width - pad, y);
            // the next 7 days with a min–max bar
            y += 10 * s;
            var days = d.Days.Skip(Math.Max(0, ti)).Take(7).ToList();
            if (days.Count == 0) return;
            double lo = days.Min(q => q.Min), hi = days.Max(q => q.Max), span = Math.Max(1, hi - lo);
            float rowH = Math.Min(30 * s, (Height - y - pad) / days.Count);
            float barL = x0 + 210 * s, barR = Width - pad - 44 * s;
            for (int i = 0; i < days.Count; i++)
            {
                var dd = days[i]; float my = y + rowH * i + rowH / 2;
                string name = i == 0 ? "Today" : i == 1 ? "Tomorrow" : dd.Date.ToString("dddd", En);
                T(name, x0, my + 5 * s, F(13, false, false), text, false);
                WxIcons.Paint(g, dd.Code, true, new RectangleF(x0 + 104 * s, my - 12 * s, 24 * s, 24 * s), t);
                if (dd.Pop > 0) T(dd.Pop + "%", x0 + 136 * s, my + 4.5f * s, F(11, false, false), Theme.C("#5AB4F5"), false);
                T(Math.Round(dd.Min) + "°", barL - 8 * s, my + 5 * s, F(13, true, false), sub, true);
                T(Math.Round(dd.Max) + "°", Width - pad, my + 5 * s, F(13, true, true), text, true);
                var track = new RectangleF(barL, my - 2.5f * s, barR - barL, 5 * s);
                using (var b = new SolidBrush(Draw.A(t.Sub, 50))) using (var pth = Draw.Round(track, 2.5f * s)) g.FillPath(b, pth);
                float a = (float)((dd.Min - lo) / span), b2 = (float)((dd.Max - lo) / span);
                var fill = new RectangleF(barL + track.Width * a, track.Y, Math.Max(track.Height, track.Width * (b2 - a)), track.Height);
                using (var br = t.Metal ? Draw.Gold(fill, true) : new LinearGradientBrush(new RectangleF(fill.X - 1, fill.Y, fill.Width + 2, fill.Height), Theme.C("#5AB4F5"), Theme.C("#FFB547"), LinearGradientMode.Horizontal))
                using (var pth = Draw.Round(fill, 2.5f * s)) g.FillPath(br, pth);
            }
        }
    }

    public class LocationForm : ThemedForm
    {
        public LocationForm(WeatherView w) : base(w.MenuTheme)
        {
            Text = "Location – Weather";
            ClientSize = new Size(460, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl("Where should the weather be for?", false, 13, true));
            var status = Lbl("Type a city or town and press Enter.", true);
            root.Controls.Add(status);
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var box = new TextBox { Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            var find = Btn("Search", true);
            row.Controls.Add(box, 0, 0); row.Controls.Add(find, 1, 0);
            root.Controls.Add(row);
            var list = new ListBox { Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.None, IntegralHeight = false };
            root.Controls.Add(list);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var use = Btn("Use this place", true); var auto = Btn("Automatic (my location)"); var cancel = Btn("Cancel");
            bar.Controls.AddRange(new Control[] { use, cancel, auto });
            root.Controls.Add(bar);
            Controls.Add(root);
            Action search = () =>
            {
                string q = box.Text.Trim(); if (q.Length < 2) return;
                status.Text = "Searching…"; find.Enabled = false;
                new Thread(() =>
                {
                    List<Place> res = null; string err = null;
                    try { res = WxApi.Search(q); } catch (Exception ex) { err = ex.Message; }
                    BeginInvoke((Action)(() =>
                    {
                        find.Enabled = true; list.Items.Clear();
                        if (err != null) { status.Text = "Search failed: " + err; return; }
                        foreach (var p in res) list.Items.Add(p);
                        status.Text = res.Count == 0 ? "Nothing found – try another spelling." : "Pick a place and press Use this place.";
                        if (res.Count > 0) list.SelectedIndex = 0;
                    }));
                }) { IsBackground = true }.Start();
            };
            find.Click += (s, e) => search();
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; search(); } };
            Action pick = () => { if (list.SelectedItem is Place p) { w.SetPlace(p); Close(); } };
            use.Click += (s, e) => pick();
            list.DoubleClick += (s, e) => pick();
            auto.Click += (s, e) => { w.SetPlace(null); Close(); };
            cancel.Click += (s, e) => Close();
        }
    }
}
