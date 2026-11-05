// Desktop.cs – making widgets part of the desktop: the wallpaper behind them (for the frosted-glass look),
// the layer behind the desktop icons, and a mouse hook so widgets there still answer clicks and drags.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BatteryWidget
{
    static class DeskNative
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint ms, out IntPtr res);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [StructLayout(LayoutKind.Sequential)] public struct MSLL { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
    }

    public static class Desk
    {
        /// <summary>The window that draws the wallpaper, behind the desktop icons (the Wallpaper Engine / Rainmeter trick).</summary>
        public static IntPtr WallpaperLayer()
        {
            if (!Native.OnWindows) return IntPtr.Zero;
            IntPtr progman = Native.FindWindow("Progman", null), r;
            DeskNative.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out r);      // asks Explorer to create it
            IntPtr worker = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);                      // Windows 11 24H2 and later
            if (worker != IntPtr.Zero && Native.IsWindowVisible(worker)) return worker;
            worker = IntPtr.Zero;
            DeskNative.EnumWindows((top, l) =>                                                               // earlier Windows
            {
                if (Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    var w = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    if (w != IntPtr.Zero && Native.IsWindowVisible(w)) worker = w;
                }
                return true;
            }, IntPtr.Zero);
            return worker;
        }

        public static void DarkTitle(Form f, Theme t)
        {
            if (!Native.OnWindows) return;
            try
            {
                Color bg = Color.FromArgb(255, t.Bg.A < 200 ? Color.FromArgb(20, 22, 29) : t.Bg);
                int dark = bg.GetBrightness() < 0.5f ? 1 : 0;
                DeskNative.DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);                                   // dark title bar
                int cap = bg.R | (bg.G << 8) | (bg.B << 16), txt = t.Text.R | (t.Text.G << 8) | (t.Text.B << 16), brd = t.Accent.R | (t.Accent.G << 8) | (t.Accent.B << 16);
                DeskNative.DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);                                    // Windows 11: title bar color
                DeskNative.DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);                                    // title text
                if (t.Metal) DeskNative.DwmSetWindowAttribute(f.Handle, 34, ref brd, 4);                      // gold border
            }
            catch { }
        }

        public static void SetRegion(Control w, GraphicsPath shape)
        {
            if (!Native.OnWindows || !w.IsHandleCreated) return;
            using (var g = w.CreateGraphics()) using (var reg = new Region(shape)) DeskNative.SetWindowRgn(w.Handle, reg.GetHrgn(g), true);
        }

        // ---- the mouse hook for widgets behind the desktop icons -------------------------------------------
        static IntPtr _hook;
        static DeskNative.HookProc _proc;
        static readonly List<WidgetWindow> _behind = new List<WidgetWindow>();
        static WidgetWindow _captured;

        public static void Track(WidgetWindow w, bool on)
        {
            if (on) { if (!_behind.Contains(w)) _behind.Add(w); } else _behind.Remove(w);
            if (_behind.Count > 0 && _hook == IntPtr.Zero && Native.OnWindows)
            {
                _proc = Hook;
                _hook = DeskNative.SetWindowsHookEx(14, _proc, DeskNative.GetModuleHandle(null), 0);          // WH_MOUSE_LL
            }
            else if (_behind.Count == 0 && _hook != IntPtr.Zero) { DeskNative.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        static readonly HashSet<string> DesktopClasses = new HashSet<string> { "SysListView32", "SHELLDLL_DefView", "WorkerW", "Progman" };
        static bool DesktopUnder(POINT p)
        {
            var h = DeskNative.WindowFromPoint(p);
            var sb = new StringBuilder(64);
            Native.GetClassName(h, sb, 64);
            return DesktopClasses.Contains(sb.ToString()) || _behind.Any(w => w.IsHandleCreated && w.Handle == h);
        }

        static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var m = (DeskNative.MSLL)Marshal.PtrToStructure(lParam, typeof(DeskNative.MSLL));
                int msg = wParam.ToInt32();
                var target = _captured ?? _behind.FirstOrDefault(w => w.Visible && w.ScreenVisual.Contains(m.pt.X, m.pt.Y));
                if (target != null && (_captured != null || DesktopUnder(m.pt)))
                {
                    if (msg == 0x200 && _captured == null) { target.BeginInvoke((Action)(() => target.HookMouse("move", MouseButtons.None, m.pt))); return CallNextHookEx(code, wParam, lParam); }
                    MouseButtons b = msg == 0x201 || msg == 0x202 || msg == 0x203 ? MouseButtons.Left : msg == 0x204 || msg == 0x205 ? MouseButtons.Right : MouseButtons.None;
                    string phase = msg == 0x201 || msg == 0x204 ? "down" : msg == 0x202 || msg == 0x205 ? "up" : msg == 0x200 ? "move" : msg == 0x203 ? "double" : null;
                    if (phase != null)
                    {
                        if (phase == "down") _captured = target;
                        if (phase == "up") _captured = null;
                        target.BeginInvoke((Action)(() => target.HookMouse(phase, b, m.pt)));
                        return new IntPtr(1);                         // the desktop doesn't see it (no selection box over the widget)
                    }
                }
            }
            return CallNextHookEx(code, wParam, lParam);
        }
        static IntPtr CallNextHookEx(int c, IntPtr w, IntPtr l) => DeskNative.CallNextHookEx(_hook, c, w, l);
    }

    /// <summary>The desktop wallpaper as Windows shows it on each screen – sharp and blurred – for widgets that blend in.</summary>
    public static class Wallpaper
    {
        static Bitmap _img, _blur;
        static DateTime _stamp, _checked;
        static string _file;
        public static Bitmap Sharp { get { Load(); return _img; } }

        static string FilePath()
        {
            string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(t)) return t;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")) { var p = k?.GetValue("WallPaper") as string; if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p; } } catch { }
            return null;
        }

        public static Bitmap Override;          // previews: use this picture as the wallpaper
        static void Load()
        {
            if (Override != null && _img == null)
            {
                _img = Override;
                _blur = new Bitmap(Math.Max(1, _img.Width / 18), Math.Max(1, _img.Height / 18));
                using (var g = Graphics.FromImage(_blur)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(_img, 0, 0, _blur.Width, _blur.Height); }
                return;
            }
            if (Override != null) return;
            if ((DateTime.UtcNow - _checked).TotalSeconds < 30 && (_img != null || _file == null && _checked != DateTime.MinValue)) return;
            _checked = DateTime.UtcNow;
            string f = FilePath();
            if (f == null) { _file = null; return; }
            var stamp = File.GetLastWriteTimeUtc(f);
            if (f == _file && stamp == _stamp && _img != null) return;
            _file = f; _stamp = stamp;
            try
            {
                var scr = Screen.PrimaryScreen.Bounds;
                string style = "10";
                try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")) style = (k?.GetValue("WallpaperStyle") as string) ?? "10"; } catch { }
                using (var src = new Bitmap(new MemoryStream(File.ReadAllBytes(f))))
                {
                    var img = new Bitmap(scr.Width, scr.Height);
                    using (var g = Graphics.FromImage(img))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.Black);
                        float sw = scr.Width / (float)src.Width, sh = scr.Height / (float)src.Height;
                        float k = style == "6" ? Math.Min(sw, sh) : style == "0" ? 1 : Math.Max(sw, sh);
                        if (style == "2" || style == "22") g.DrawImage(src, 0, 0, scr.Width, scr.Height);
                        else { float w = src.Width * k, h = src.Height * k; g.DrawImage(src, (scr.Width - w) / 2, (scr.Height - h) / 2, w, h); }
                    }
                    _img?.Dispose(); _img = img;
                    _blur?.Dispose();
                    _blur = new Bitmap(Math.Max(1, img.Width / 18), Math.Max(1, img.Height / 18));
                    using (var g = Graphics.FromImage(_blur)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(img, 0, 0, _blur.Width, _blur.Height); }
                }
            }
            catch { _img = null; _blur = null; }
        }

        /// <summary>Draws the wallpaper that's behind screenRect into dst (blurred: frosted glass).</summary>
        public static bool Paint(Graphics g, RectangleF dst, Rectangle screenRect, bool blurred)
        {
            Load();
            var img = blurred ? _blur : _img;
            if (img == null) return false;
            var scr = Screen.FromRectangle(screenRect).Bounds;
            float k = blurred ? img.Width / (float)_img.Width : 1;
            float fx = scr.Width / (float)Screen.PrimaryScreen.Bounds.Width, fy = scr.Height / (float)Screen.PrimaryScreen.Bounds.Height;
            var src = new RectangleF((screenRect.X - scr.X) / fx * k, (screenRect.Y - scr.Y) / fy * k, screenRect.Width / fx * k, screenRect.Height / fy * k);
            var st = g.Save();
            g.InterpolationMode = blurred ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var ia = new System.Drawing.Imaging.ImageAttributes()) { ia.SetWrapMode(WrapMode.TileFlipXY); g.DrawImage(img, Rectangle.Round(dst), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia); }
            g.Restore(st);
            return true;
        }
    }
}
