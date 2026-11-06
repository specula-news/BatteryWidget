// Draw.cs – themes, fonts and every icon, drawn as vectors (GDI+).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace BatteryWidget
{
    public class Theme
    {
        public Color Bg, Card, Border, Text, Sub, Accent, Good, Warn, Bad, Track;
        public bool Metal;
        public bool Glass;           // Windows 11 acrylic: frosted widgets, real acrylic menus
        public Theme(string bg, string card, string border, string text, string sub, string accent, string good, string warn, string bad,
                     string track, bool metal = false)
        { Bg = C(bg); Card = C(card); Border = C(border); Text = C(text); Sub = C(sub); Accent = C(accent); Good = C(good); Warn = C(warn); Bad = C(bad); Track = C(track); Metal = metal; }
        public Theme Clone() => (Theme)MemberwiseClone();
        public static Color C(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6) hex = "FF" + hex;
            return Color.FromArgb(Convert.ToInt32(hex, 16));
        }
        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static class Themes
    {
        public static readonly Dictionary<string, Theme> All = new Dictionary<string, Theme> {
            { "Auto", new Theme("#F2121419", "#FF1B1E26", "#FF2B303C", "#FFF4F6FA", "#FF9AA3B5", "#FF6C8CFF", "#FF34D399", "#FFFBBF24", "#FFF87171", "#FF2A2F3B") },
            { "Dark", new Theme("#F2121419", "#FF1B1E26", "#FF2B303C", "#FFF4F6FA", "#FF9AA3B5", "#FF6C8CFF", "#FF34D399", "#FFFBBF24", "#FFF87171", "#FF2A2F3B") },
            { "Light", new Theme("#F7F7F8FA", "#FFFFFFFF", "#FFE0E4EB", "#FF12151C", "#FF5B6577", "#FF2F5BFF", "#FF0E9F6E", "#FFD97706", "#FFDC2626", "#FFE6E9F0") },
            { "AMOLED", new Theme("#FF000000", "#FF0C0C0F", "#FF1F1F26", "#FFFFFFFF", "#FF8A8F9C", "#FF22D3EE", "#FF4ADE80", "#FFFACC15", "#FFFF5C5C", "#FF1C1C22") },
            { "Glass", new Theme("#A6101218", "#2EFFFFFF", "#47FFFFFF", "#FFFFFFFF", "#FFD0D6E2", "#FF8AB4FF", "#FF6EE7B7", "#FFFCD34D", "#FFFCA5A5", "#38FFFFFF") },
            { "Midnight", new Theme("#F20B1226", "#FF121C3A", "#FF1F2C57", "#FFE8EEFF", "#FF8E9CCB", "#FF7AA2FF", "#FF3FE0B0", "#FFFFC46B", "#FFFF6B8A", "#FF1B2748") },
            { "Nord", new Theme("#F22E3440", "#FF3B4252", "#FF4C566A", "#FFECEFF4", "#FFB8C0D0", "#FF88C0D0", "#FFA3BE8C", "#FFEBCB8B", "#FFBF616A", "#FF434C5E") },
            { "Neon", new Theme("#F20D0221", "#FF1A0B3B", "#FF3A1C7A", "#FFF8EEFF", "#FFB79BE0", "#FFFF2BD6", "#FF00F5A0", "#FFFFE45E", "#FFFF3864", "#FF2A1357") },
            { "Sand", new Theme("#F7F5EFE6", "#FFFFFBF5", "#FFE4D8C6", "#FF2B2118", "#FF7A6A58", "#FFC2570C", "#FF3F8F4F", "#FFC27C0E", "#FFC0392B", "#FFEADFCF") },
            { "Sakura", new Theme("#F22A0F22", "#FF3A1531", "#FF5A2350", "#FFFFF0F8", "#FFE0A3C8", "#FFFF5FB8", "#FF7CF0C5", "#FFFFD27A", "#FFFF6B8B", "#FF4A1C40") },
            { "Mocha", new Theme("#F21E1E2E", "#FF313244", "#FF45475A", "#FFCDD6F4", "#FFA6ADC8", "#FFCBA6F7", "#FFA6E3A1", "#FFF9E2AF", "#FFF38BA8", "#FF45475A") },
            { "Dracula", new Theme("#F2282A36", "#FF343746", "#FF44475A", "#FFF8F8F2", "#FFB4BAD6", "#FFBD93F9", "#FF50FA7B", "#FFF1FA8C", "#FFFF5555", "#FF44475A") },
            { "Rosé", new Theme("#F2191724", "#FF1F1D2E", "#FF26233A", "#FFE0DEF4", "#FF908CAA", "#FFEBBCBA", "#FF9CCFD8", "#FFF6C177", "#FFEB6F92", "#FF2E2A45") },
            { "Forest", new Theme("#F2101A14", "#FF16241B", "#FF22382A", "#FFE6F2E8", "#FF8FB39A", "#FF7FD99A", "#FF7FD99A", "#FFE8C468", "#FFEF7D6B", "#FF203327") },
            { "Mono", new Theme("#F2161616", "#FF1E1E1E", "#FF2E2E2E", "#FFFFFFFF", "#FF9A9A9A", "#FFFFFFFF", "#FFE8E8E8", "#FFBDBDBD", "#FFFF5A5A", "#FF333333") },
            { "Gold", new Theme("#F00A0C10", "#FF15171C", "#73C9A15A", "#FFF4ECDD", "#FF9D9484", "#FFD9B45F", "#FFD9B45F", "#FFE8913F", "#FFE5484D", "#30C9A15A", true) },
            { "Acrylic", new Theme("#B81F1F23", "#FF2B2B2E", "#2EFFFFFF", "#FFFFFFFF", "#FFC8C8CC", "#FF60CDFF", "#FF6CCB5F", "#FFFCE100", "#FFFF99A4", "#33FFFFFF") { Glass = true } },
            { "Twilight", new Theme("#EE3B2E4A", "#FF2A2237", "#30FFFFFF", "#FFF4F2F8", "#FFA9A7BA", "#FF4FB0F0", "#FF35D0BC", "#FFF39A55", "#FFFF6B7A", "#2EFFFFFF") },
        };

        static int? RegDword(string path, string name)
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path)) { var v = k?.GetValue(name); return v == null ? (int?)null : Convert.ToInt32(v); } }
            catch { return null; }
        }
        public static bool? WindowsLight(bool taskbar)
        {
            var v = RegDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", taskbar ? "SystemUsesLightTheme" : "AppsUseLightTheme");
            return v == null ? (bool?)null : v.Value != 0;
        }
        public static Color? WindowsAccent()
        {
            var v = RegDword(@"Software\Microsoft\Windows\DWM", "AccentColor");
            if (v == null) return null;
            uint u = (uint)v.Value;                                            // 0xAABBGGRR
            return Color.FromArgb(255, (int)(u & 0xFF), (int)((u >> 8) & 0xFF), (int)((u >> 16) & 0xFF));
        }

        /// <summary>The colors to draw with: the theme, Auto resolved, a custom accent, and readable text on a bare taskbar.</summary>
        public static Theme Resolve(string name, string accent, bool taskbar, bool taskbarBackground, bool? simLight = null)
        {
            Theme t;
            if (!All.TryGetValue(name ?? "", out t)) t = All["Dark"];
            t = t.Clone();
            if (name == "Auto")
            {
                bool light = WindowsLight(taskbar) ?? false;
                t = All[light ? "Light" : "Dark"].Clone();
                var a = WindowsAccent(); if (a.HasValue) t.Accent = a.Value;
            }
            if (taskbar && !taskbarBackground)
            {
                bool light = simLight ?? (WindowsLight(true) ?? false);
                if (light) { t.Text = Theme.C("#FF1B1B1F"); t.Sub = Theme.C("#A6000000"); t.Track = Theme.C("#24000000"); }
                else { t.Text = Theme.C("#FFFFFFFF"); t.Sub = Theme.C("#B3FFFFFF"); t.Track = Theme.C("#2EFFFFFF"); }
            }
            if (!string.IsNullOrEmpty(accent)) try { t.Accent = Theme.C(accent); } catch { }
            return t;
        }
    }

    public static class Fonts
    {
        static readonly Dictionary<string, Font> Cache = new Dictionary<string, Font>();
        static string _ui, _num;
        static string Pick(params string[] names)
        {
            var have = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            return names.FirstOrDefault(have.Contains) ?? FontFamily.GenericSansSerif.Name;
        }
        public static string Ui => _ui ?? (_ui = Pick("Segoe UI Variable Text", "Segoe UI", "DejaVu Sans"));
        public static string Num => _num ?? (_num = Pick("Segoe UI Variable Display", "Segoe UI", "DejaVu Sans"));
        public static Font Get(float px, FontStyle style = FontStyle.Regular, bool num = false, bool semibold = false)
        {
            string fam = num ? Num : Ui;
            if (semibold) { var sb = Pick(fam + " Semibold", "Segoe UI Semibold"); if (sb != FontFamily.GenericSansSerif.Name) fam = sb; else style |= FontStyle.Bold; }
            px = (float)Math.Max(6, Math.Round(px * 2) / 2);
            string key = $"{fam}|{px}|{style}";
            Font f;
            if (!Cache.TryGetValue(key, out f)) { f = new Font(fam, px, style, GraphicsUnit.Pixel); Cache[key] = f; }
            return f;
        }
        public static readonly StringFormat Typo = MakeTypo();
        static StringFormat MakeTypo() { var f = (StringFormat)StringFormat.GenericTypographic.Clone(); f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces; return f; }
        public static float Width(Graphics g, string s, Font f) => string.IsNullOrEmpty(s) ? 0 : g.MeasureString(s, f, 10000, Typo).Width;
        public static float Ascent(Font f) { var ff = f.FontFamily; return f.Size * ff.GetCellAscent(f.Style) / ff.GetEmHeight(f.Style); }
        public static float Descent(Font f) { var ff = f.FontFamily; return f.Size * ff.GetCellDescent(f.Style) / ff.GetEmHeight(f.Style); }
        /// <summary>Draws text with its baseline at y.</summary>
        public static void Text(Graphics g, string s, Font f, Brush b, float x, float baseline) => g.DrawString(s, f, b, x, baseline - Ascent(f), Typo);
        public static string Elide(Graphics g, string s, Font f, float w)
        {
            if (Width(g, s, f) <= w) return s;
            while (s.Length > 1 && Width(g, s + "…", f) > w) s = s.Substring(0, s.Length - 1);
            return s + "…";
        }
    }

    public static class Draw
    {
        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Max(0.1f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color A(Color c, int alpha) => Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        public static Color Mix(Color a, Color b, double t) => Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        public static Color Lighter(Color c, double f) => Color.FromArgb(c.A, Math.Min(255, (int)(c.R * f)), Math.Min(255, (int)(c.G * f)), Math.Min(255, (int)(c.B * f)));

        public static readonly Color[] GoldStops = { Theme.C("#FBE7A8"), Theme.C("#E5C06B"), Theme.C("#BC8C3C"), Theme.C("#8C6526") };
        public static Brush Gold(RectangleF r, bool horizontal = false, int alpha = 255)
        {
            if (r.Width < 1) r.Width = 1; if (r.Height < 1) r.Height = 1;
            var b = new LinearGradientBrush(r, Color.White, Color.Black, horizontal ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical);
            b.InterpolationColors = new ColorBlend { Colors = GoldStops.Select(c => A(c, alpha)).ToArray(), Positions = new[] { 0f, 0.35f, 0.7f, 1f } };
            return b;
        }

        // ---- device icons (24x24 grid) --------------------------------------------------
        static Pen P(Color c, float w = 1.9f) => new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        static void Dots(Graphics g, Color c, float w, params float[] xy) { using (var b = new SolidBrush(c)) for (int i = 0; i + 1 < xy.Length; i += 2) g.FillEllipse(b, xy[i] - w / 2, xy[i + 1] - w / 2, w, w); }

        public static void DeviceIcon(Graphics g, string kind, RectangleF r, Color c, int variant = 0)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var p = P(c))
            {
                if (kind == "mouse" && variant == 1)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(12, 2.2f, 16.9f, 2.2f, 18.7f, 5.6f, 18.7f, 10); path.AddLine(18.7f, 10, 18.4f, 15.6f);
                        path.AddBezier(18.4f, 15.6f, 18.1f, 19.8f, 15.5f, 21.8f, 12, 21.8f); path.AddBezier(12, 21.8f, 8.5f, 21.8f, 5.9f, 19.8f, 5.6f, 15.6f);
                        path.AddLine(5.6f, 15.6f, 5.3f, 10); path.AddBezier(5.3f, 10, 5.3f, 5.6f, 7.1f, 2.2f, 12, 2.2f); path.CloseFigure();
                        g.DrawPath(p, path);
                    }
                    g.DrawLine(p, 12, 2.5f, 12, 5); using (var rp = Round(new RectangleF(11, 5.2f, 2, 3.6f), 1)) g.DrawPath(p, rp);
                    g.DrawLine(p, 8, 11, 8, 12.4f); g.DrawLine(p, 8, 14, 8, 15.4f);
                }
                else if (kind == "mouse" && variant == 2)
                {
                    var s2 = g.Save(); g.TranslateTransform(12.6f, 11.2f); g.RotateTransform(-22);
                    using (var rp = Round(new RectangleF(-5, -9.2f, 10, 18.4f), 5)) g.DrawPath(p, rp);
                    g.DrawLine(p, 0, -9, 0, -2.6f); g.Restore(s2);
                    using (var path = new GraphicsPath()) { path.AddBezier(3.6f, 18.2f, 4.7f, 20.5f, 6.9f, 21.5f, 10.2f, 21.4f); g.DrawPath(p, path); }
                }
                else if (kind == "mouse")
                {
                    using (var rp = Round(new RectangleF(6.5f, 2, 11, 20), 5.5f)) g.DrawPath(p, rp);
                    g.DrawLine(p, 12, 2.2f, 12, 9.5f); g.DrawLine(p, 6.5f, 9.5f, 17.5f, 9.5f);
                }
                else if (kind == "keyboard" && variant == 1)
                {
                    g.DrawPolygon(p, new[] { new PointF(2.4f, 17.6f), new PointF(21.6f, 17.6f), new PointF(19.4f, 7.4f), new PointF(4.6f, 7.4f) });
                    Dots(g, c, 2.1f, 7.4f, 10.4f, 10.3f, 10.4f, 13.2f, 10.4f, 16.1f, 10.4f, 6.6f, 13.2f, 9.6f, 13.2f, 12.6f, 13.2f, 15.6f, 13.2f, 17.6f, 13.2f);
                    g.DrawLine(p, 8.6f, 15.6f, 15.4f, 15.6f);
                }
                else if (kind == "keyboard" && variant == 2)
                {
                    using (var rp = Round(new RectangleF(3.6f, 5, 16.8f, 15), 3.4f)) g.DrawPath(p, rp);
                    using (var rp = Round(new RectangleF(6.6f, 6.8f, 10.8f, 8.8f), 2.2f)) g.DrawPath(p, rp);
                    g.DrawLine(p, 7.2f, 15.4f, 4.6f, 19.3f); g.DrawLine(p, 16.8f, 15.4f, 19.4f, 19.3f);
                }
                else if (kind == "keyboard")
                {
                    using (var rp = Round(new RectangleF(2, 5.5f, 20, 13), 2.5f)) g.DrawPath(p, rp);
                    Dots(g, c, 2.1f, 6.2f, 9.6f, 9.4f, 9.6f, 12.6f, 9.6f, 15.8f, 9.6f, 17.9f, 12.4f, 7.8f, 12.4f, 11, 12.4f, 14.2f, 12.4f);
                    g.DrawLine(p, 8, 15.4f, 16, 15.4f);
                }
                else if (kind == "headset" && variant == 2)
                {
                    g.DrawEllipse(p, 8.2f - 3.2f, 7.6f - 3.2f, 6.4f, 6.4f); g.DrawLine(p, 9.9f, 10.4f, 9.9f, 19.6f);
                    g.DrawEllipse(p, 15.8f - 3.2f, 7.6f - 3.2f, 6.4f, 6.4f); g.DrawLine(p, 14.1f, 10.4f, 14.1f, 19.6f);
                }
                else if (kind == "headset")
                {
                    using (var path = new GraphicsPath()) { path.AddLine(4.5f, 13, 4.5f, 11); path.AddArc(4.5f, 3.5f, 15, 15, 180, 180); path.AddLine(19.5f, 11, 19.5f, 13); g.DrawPath(p, path); }
                    using (var rp = Round(new RectangleF(2.8f, 12, 4.4f, 8), 2)) g.DrawPath(p, rp);
                    using (var rp = Round(new RectangleF(16.8f, 12, 4.4f, 8), 2)) g.DrawPath(p, rp);
                    if (variant == 1)
                    {
                        using (var path = new GraphicsPath()) { path.AddBezier(5, 20, 5.9f, 21.6f, 7.7f, 22.2f, 10.2f, 21.9f); g.DrawPath(p, path); }
                        using (var b = new SolidBrush(c)) g.FillEllipse(b, 10.1f, 20.7f, 2.2f, 2.2f);
                    }
                }
                else if (kind == "controller" && variant == 1)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(7, 7.4f, 17, 7.4f); path.AddBezier(17, 7.4f, 20.6f, 7.4f, 22, 10, 22, 13.6f); path.AddBezier(22, 13.6f, 22, 17.2f, 20.6f, 18.6f, 19.2f, 18.6f);
                        path.AddBezier(19.2f, 18.6f, 17.6f, 18.6f, 16.8f, 16.2f, 15.4f, 15.2f); path.AddLine(15.4f, 15.2f, 8.6f, 15.2f);
                        path.AddBezier(8.6f, 15.2f, 7.2f, 16.2f, 6.4f, 18.6f, 4.8f, 18.6f); path.AddBezier(4.8f, 18.6f, 3.4f, 18.6f, 2, 17.2f, 2, 13.6f);
                        path.AddBezier(2, 13.6f, 2, 10, 3.4f, 7.4f, 7, 7.4f); path.CloseFigure(); g.DrawPath(p, path);
                    }
                    g.DrawLine(p, 5.8f, 11.4f, 9, 11.4f); g.DrawLine(p, 7.4f, 9.8f, 7.4f, 13); Dots(g, c, 2.4f, 15.8f, 10.4f, 18, 12.6f);
                }
                else if (kind == "controller")
                {
                    using (var rp = Round(new RectangleF(2.2f, 7, 19.6f, 11), 5.5f)) g.DrawPath(p, rp);
                    g.DrawLine(p, 6.2f, 12.5f, 9.4f, 12.5f); g.DrawLine(p, 7.8f, 10.9f, 7.8f, 14.1f); Dots(g, c, 2.4f, 15.6f, 11.2f, 17.8f, 13.6f);
                }
                else if (kind == "__scan__") { g.DrawLine(p, 12, 6, 12, 18); g.DrawLine(p, 6, 12, 18, 12); }
                else if (kind == "other" && variant == 1)
                {
                    using (var rp = Round(new RectangleF(6, 2.4f, 12, 19.2f), 2.6f)) g.DrawPath(p, rp);
                    g.DrawEllipse(p, 12 - 3.6f, 14.6f - 3.6f, 7.2f, 7.2f); g.DrawEllipse(p, 12 - 1.5f, 7 - 1.5f, 3, 3);
                }
                else if (kind == "other" && variant == 2)
                {
                    using (var rp = Round(new RectangleF(6.4f, 6.6f, 11.2f, 10.8f), 3.2f)) g.DrawPath(p, rp);
                    g.DrawLines(p, new[] { new PointF(8.8f, 6.6f), new PointF(9.4f, 2.6f), new PointF(14.6f, 2.6f), new PointF(15.2f, 6.6f) });
                    g.DrawLines(p, new[] { new PointF(8.8f, 17.4f), new PointF(9.4f, 21.4f), new PointF(14.6f, 21.4f), new PointF(15.2f, 17.4f) });
                    g.DrawLine(p, 12, 12, 12, 9.4f); g.DrawLine(p, 12, 12, 14, 12);
                }
                else
                {
                    using (var rp = Round(new RectangleF(2.5f, 7, 16.5f, 10), 2.4f)) g.DrawPath(p, rp);
                    g.DrawLine(p, 21.2f, 10.6f, 21.2f, 13.4f);
                }
            }
            g.Restore(st);
        }

        public static readonly string[][] DeviceIcons = {
            new[] { "mouse", "0", "Mouse" }, new[] { "mouse", "1", "Mouse – gaming" }, new[] { "mouse", "2", "Mouse – vertical" },
            new[] { "keyboard", "0", "Keyboard" }, new[] { "keyboard", "1", "Keyboard – low profile" }, new[] { "keyboard", "2", "Keycap" },
            new[] { "headset", "0", "Headphones" }, new[] { "headset", "1", "Headset with mic" }, new[] { "headset", "2", "Earbuds" },
            new[] { "controller", "0", "Controller" }, new[] { "controller", "1", "Controller – classic" },
            new[] { "other", "0", "Battery" }, new[] { "other", "1", "Speaker" }, new[] { "other", "2", "Watch" } };
        public static readonly string[][] ColorKinds = { new[] { "mouse", "Mouse" }, new[] { "keyboard", "Keyboard" }, new[] { "headset", "Headset" },
            new[] { "controller", "Controller" }, new[] { "other", "Other devices" } };

        // ---- system monitor icons ------------------------------------------------------------
        public static readonly string[][] SysIcons = { new[] { "cpu", "Processor" }, new[] { "gpu", "Graphics card" }, new[] { "temp", "Thermometer" },
            new[] { "power", "Power" }, new[] { "clock", "Clock speed" }, new[] { "usage", "Load" }, new[] { "fan", "Fan" }, new[] { "ram", "Memory" },
            new[] { "disk", "Drive" }, new[] { "down", "Download" }, new[] { "up", "Upload" }, new[] { "net", "Network" }, new[] { "battery", "Battery" },
            new[] { "volt", "Voltage" }, new[] { "dot", "Dot" } };

        public static void SysIcon(Graphics g, string icon, RectangleF r, Color c)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var p = P(c))
            using (var b = new SolidBrush(c))
            {
                switch (icon)
                {
                    case "cpu":
                        using (var rp = Round(new RectangleF(6, 6, 12, 12), 2)) g.DrawPath(p, rp);
                        using (var rp = Round(new RectangleF(9.5f, 9.5f, 5, 5), 0.8f)) g.DrawPath(p, rp);
                        foreach (var v in new[] { 9f, 12, 15 }) { g.DrawLine(p, v, 3, v, 6); g.DrawLine(p, v, 18, v, 21); g.DrawLine(p, 3, v, 6, v); g.DrawLine(p, 18, v, 21, v); }
                        break;
                    case "gpu":
                        using (var rp = Round(new RectangleF(2, 6.5f, 17, 11), 2)) g.DrawPath(p, rp);
                        g.DrawEllipse(p, 8.4f - 3.2f, 12 - 3.2f, 6.4f, 6.4f); g.DrawLine(p, 14, 9.6f, 14, 14.4f); g.DrawLine(p, 16.4f, 9.6f, 16.4f, 14.4f);
                        g.DrawLine(p, 21.6f, 4.8f, 21.6f, 19.2f); g.DrawLine(p, 5.5f, 20, 13.5f, 20);
                        break;
                    case "temp":
                        g.DrawEllipse(p, 12 - 3.2f, 17.4f - 3.2f, 6.4f, 6.4f);
                        using (var path = new GraphicsPath()) { path.AddLine(10.2f, 15, 10.2f, 4.6f); path.AddArc(10.2f, 2.8f, 3.6f, 3.6f, 180, 180); path.AddLine(13.8f, 4.6f, 13.8f, 15); g.DrawPath(p, path); }
                        g.DrawLine(p, 12, 15.6f, 12, 9.4f); g.DrawLine(p, 16.2f, 6.4f, 18, 6.4f); g.DrawLine(p, 16.2f, 9.4f, 18, 9.4f); g.DrawLine(p, 16.2f, 12.4f, 18, 12.4f);
                        break;
                    case "power":
                        g.DrawPolygon(p, new[] { new PointF(13.6f, 2.4f), new PointF(6, 13.4f), new PointF(11.4f, 13.4f), new PointF(10.4f, 21.6f), new PointF(18, 10.4f), new PointF(12.6f, 10.4f) });
                        break;
                    case "clock":
                        g.DrawArc(p, 3, 4.5f, 18, 18, 150, 240);
                        g.DrawLine(p, 12, 13.5f, 16.4f, 9.2f); g.FillEllipse(b, 12 - 1.4f, 13.5f - 1.4f, 2.8f, 2.8f);
                        break;
                    case "usage":
                        foreach (var rr in new[] { new RectangleF(3.6f, 13, 3.8f, 7.5f), new RectangleF(10.1f, 8.5f, 3.8f, 12), new RectangleF(16.6f, 3.5f, 3.8f, 17) })
                            using (var rp = Round(rr, 1)) g.DrawPath(p, rp);
                        break;
                    case "fan":
                        var s2 = g.Save(); g.TranslateTransform(12, 12);
                        for (int i = 0; i < 4; i++) { g.DrawEllipse(p, -2.4f, -9.6f, 4.8f, 7.2f); g.RotateTransform(90); }
                        g.Restore(s2); g.DrawEllipse(p, 12 - 1.6f, 12 - 1.6f, 3.2f, 3.2f);
                        break;
                    case "ram":
                        using (var rp = Round(new RectangleF(2.4f, 7.4f, 19.2f, 8.4f), 1.2f)) g.DrawPath(p, rp);
                        foreach (var x in new[] { 5f, 10.5f, 16 }) g.DrawRectangle(p, x, 9.6f, 3, 4);
                        foreach (var x in new[] { 4.6f, 7.6f, 10.6f, 13.4f, 16.4f, 19.4f }) g.DrawLine(p, x, 15.8f, x, 18.4f);
                        break;
                    case "disk":
                        using (var rp = Round(new RectangleF(3, 5, 18, 14), 2.6f)) g.DrawPath(p, rp);
                        g.DrawLine(p, 3.4f, 13.6f, 20.6f, 13.6f); g.FillEllipse(b, 16, 15.4f, 2, 2);
                        break;
                    case "down": g.DrawLine(p, 12, 3.6f, 12, 16.6f); g.DrawLine(p, 7, 11.8f, 12, 16.8f); g.DrawLine(p, 17, 11.8f, 12, 16.8f); g.DrawLine(p, 5, 20.6f, 19, 20.6f); break;
                    case "up": g.DrawLine(p, 12, 17, 12, 4); g.DrawLine(p, 7, 9, 12, 3.8f); g.DrawLine(p, 17, 9, 12, 3.8f); g.DrawLine(p, 5, 20.6f, 19, 20.6f); break;
                    case "net":
                        g.DrawLine(p, 8, 4, 8, 19); g.DrawLine(p, 4.4f, 7.6f, 8, 4); g.DrawLine(p, 11.6f, 7.6f, 8, 4);
                        g.DrawLine(p, 16, 20, 16, 5); g.DrawLine(p, 12.4f, 16.4f, 16, 20); g.DrawLine(p, 19.6f, 16.4f, 16, 20);
                        break;
                    case "battery":
                        using (var rp = Round(new RectangleF(2.5f, 7, 16.5f, 10), 2.4f)) g.DrawPath(p, rp);
                        g.DrawLine(p, 21.2f, 10.6f, 21.2f, 13.4f);
                        break;
                    case "volt":
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(2.5f, 12, 5, 3.5f, 7.5f, 3.5f, 10, 12); path.AddBezier(10, 12, 12.5f, 20.5f, 15, 20.5f, 17.5f, 12);
                            path.AddBezier(17.5f, 12, 18.7f, 8, 20, 6.6f, 21.5f, 6.6f); g.DrawPath(p, path);
                        }
                        break;
                    case "__scan__": g.DrawLine(p, 12, 6, 12, 18); g.DrawLine(p, 6, 12, 18, 12); break;
                    default: g.FillEllipse(b, 12 - 3.2f, 12 - 3.2f, 6.4f, 6.4f); break;
                }
            }
            g.Restore(st);
        }

        /// <summary>A crescent moon built from two arcs, so its edges stay smooth.</summary>
        public static void Moon(Graphics g, RectangleF r, Color c)
        {
            float R = r.Width / 2;
            var O = new PointF(r.X + R, r.Y + R);
            float dx = r.Width * 0.40f, dy = -r.Height * 0.22f;
            var C = new PointF(O.X + dx, O.Y + dy);
            double L = Math.Sqrt(dx * dx + dy * dy), h = Math.Sqrt(Math.Max(0, R * R - L * L / 4));
            var M = new PointF(O.X + dx / 2, O.Y + dy / 2);
            float nx = (float)(-dy / L), ny = (float)(dx / L);
            var P1 = new PointF((float)(M.X + nx * h), (float)(M.Y + ny * h));
            var P2 = new PointF((float)(M.X - nx * h), (float)(M.Y - ny * h));
            Func<PointF, PointF, double> ang = (ctr, pt) => Math.Atan2(pt.Y - ctr.Y, pt.X - ctr.X) * 180 / Math.PI;
            double a1 = ang(O, P1), a2 = ang(O, P2), ad = Math.Atan2(dy, dx) * 180 / Math.PI;
            Func<double, double> norm = a => { while (a < 0) a += 360; while (a >= 360) a -= 360; return a; };
            // outer: from P1 to P2 the long way (away from the bite)
            double sweepO = norm(a2 - a1);
            if (Math.Abs(norm(ad - a1)) < sweepO) sweepO -= 360;      // that way goes through the bite – go the other way
            double b2 = ang(C, P2), b1 = ang(C, P1);
            double sweepC = norm(b1 - b2);
            if (Math.Abs(norm(ad + 180 - b2)) > sweepC) sweepC -= 360; // the cut's arc must pass the side facing the moon
            using (var path = new GraphicsPath())
            {
                path.AddArc(O.X - R, O.Y - R, 2 * R, 2 * R, (float)a1, (float)sweepO);
                path.AddArc(C.X - R, C.Y - R, 2 * R, 2 * R, (float)b2, (float)sweepC);
                path.CloseFigure();
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }
        }

        public static void Bolt(Graphics g, RectangleF r, Color c)
        {
            float[] pts = { 0.62f, 0, 0.1f, 0.58f, 0.46f, 0.58f, 0.34f, 1, 0.92f, 0.38f, 0.54f, 0.38f };
            var ps = new PointF[6];
            for (int i = 0; i < 6; i++) ps[i] = new PointF(r.X + pts[i * 2] * r.Width, r.Y + pts[i * 2 + 1] * r.Height);
            using (var b = new SolidBrush(c)) g.FillPolygon(b, ps);
        }

        // ---- app icon: a charged battery inside a ring gauge -----------------------------------
        public static Bitmap AppIcon(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Hq(g);
                float s = size;
                bool small = size <= 24, mid = size > 24 && size <= 40;
                var tile = new RectangleF(s * 0.03f, s * 0.03f, s * 0.94f, s * 0.94f);
                using (var path = Round(tile, s * 0.24f))
                using (var bg = new LinearGradientBrush(tile, Theme.C("#1A2140"), Theme.C("#0A0D1A"), LinearGradientMode.ForwardDiagonal))
                    g.FillPath(bg, path);
                Color green = Theme.C("#3BF0A6"), cyan = Theme.C("#2BC8FF"), white = Theme.C("#F4F7FF");
                RectangleF body, cap; float stroke;
                if (small)
                {
                    float bw = s * 0.42f, bh = s * 0.62f;
                    body = new RectangleF(s * 0.5f - bw / 2, s * 0.5f - bh / 2 + s * 0.05f, bw, bh);
                    cap = new RectangleF(s * 0.5f - bw * 0.25f, body.Y - s * 0.08f, bw * 0.5f, s * 0.09f);
                    stroke = Math.Max(1.4f, s * 0.085f);
                }
                else
                {
                    float w = s * (mid ? 0.09f : 0.072f);
                    var ring = mid ? new RectangleF(s * 0.15f, s * 0.15f, s * 0.70f, s * 0.70f) : new RectangleF(s * 0.17f, s * 0.17f, s * 0.66f, s * 0.66f);
                    ring.Inflate(-w / 2, -w / 2);
                    using (var tp = new Pen(Color.FromArgb(34, 255, 255, 255), w)) g.DrawEllipse(tp, ring);
                    using (var gb = new LinearGradientBrush(ring, green, cyan, LinearGradientMode.Vertical))
                    using (var ap = new Pen(gb, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(ap, ring, -90, 288);
                    float bw = mid ? s * 0.25f : s * 0.20f, bh = mid ? s * 0.38f : s * 0.32f;
                    body = new RectangleF(s * 0.5f - bw / 2, s * 0.5f - bh / 2 + s * 0.03f, bw, bh);
                    cap = new RectangleF(s * 0.5f - bw * 0.22f, body.Y - s * 0.045f, bw * 0.44f, s * 0.05f);
                    stroke = Math.Max(1.2f, s * (mid ? 0.045f : 0.028f));
                }
                using (var wb = new SolidBrush(white)) using (var cp = Round(cap, s * 0.012f)) g.FillPath(wb, cp);
                using (var pen = new Pen(white, stroke)) using (var bp = Round(body, s * 0.04f)) g.DrawPath(pen, bp);
                var inner = RectangleF.Inflate(body, -stroke * 1.25f, -stroke * 1.25f);
                var lvl = new RectangleF(inner.X, inner.Y + inner.Height * 0.22f, inner.Width, inner.Height * 0.78f);
                using (var fb = new LinearGradientBrush(new RectangleF(lvl.X, lvl.Y - 1, lvl.Width, lvl.Height + 2), cyan, green, LinearGradientMode.Vertical))
                using (var lp = Round(lvl, s * 0.02f)) g.FillPath(fb, lp);
                if (!small && !mid) Bolt(g, new RectangleF(body.X + body.Width * 0.2f, body.Y + body.Height * 0.24f, body.Width * 0.6f, body.Height * 0.56f), Theme.C("#0A0D1A"));
            }
            return bmp;
        }

        public static Icon MakeIcon(int size)
        {
            using (var b = AppIcon(size)) return Icon.FromHandle(b.GetHicon());
        }

        /// <summary>A small picture of an icon, for menus and lists.</summary>
        public static Bitmap Preview(bool sys, string kind, int variant, Color c, int size = 20)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                Hq(g);
                var r = new RectangleF(size * 0.1f, size * 0.1f, size * 0.8f, size * 0.8f);
                if (sys) SysIcon(g, kind, r, c); else DeviceIcon(g, kind, r, c, variant);
            }
            return bmp;
        }

        public static Bitmap Swatch(Color c, int size = 16)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                Hq(g);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, 1.5f, 1.5f, size - 3, size - 3);
                using (var p = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawEllipse(p, 1.5f, 1.5f, size - 3, size - 3);
            }
            return bmp;
        }
    }
}
