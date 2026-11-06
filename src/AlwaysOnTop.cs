// AlwaysOnTop.cs – press a shortcut (Win+A by default) to keep the active window above all others,
// marked with a border in the theme's color. Press it again to let the window go.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BatteryWidget
{
    static class PinNative
    {
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string n);
        [StructLayout(LayoutKind.Sequential)] public struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }
    }

    /// <summary>The border around a window that stays on top (click-through, follows the window).</summary>
    public class PinBorder : Form
    {
        public readonly IntPtr Target;
        readonly AlwaysOnTop _owner;
        Rectangle _last;
        public PinBorder(AlwaysOnTop owner, IntPtr target)
        {
            _owner = owner; Target = target;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST);
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        public static Rectangle Frame(IntPtr h)
        {
            RECT r;
            if (PinNative.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Width > 0) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return WinUtil.WindowRect(h) ?? Rectangle.Empty;                                         // DWMWA_EXTENDED_FRAME_BOUNDS = what you see
        }

        /// <summary>Follows the window: position, minimized, closed – and stays just above it.</summary>
        public void Sync(bool force)
        {
            if (!PinNative.IsWindow(Target)) { _owner.Forget(Target); return; }
            if (PinNative.IsIconic(Target) || !Native.IsWindowVisible(Target)) { if (Visible) Hide(); return; }
            var f = Frame(Target);
            int t = _owner.Thickness;
            var outer = PinNative.IsZoomed(Target) ? f : Rectangle.Inflate(f, t, t);    // maximized: draw just inside the screen
            if (force || outer != _last) { _last = outer; Render(outer, t, PinNative.IsZoomed(Target)); }
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void Render(Rectangle outer, int t, bool inside)
        {
            if (outer.Width <= 0 || outer.Height <= 0) return;
            using (var bmp = new Bitmap(outer.Width, outer.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    Draw.Hq(g);
                    float rad = _owner.Rounded ? 8 * WidgetWindow.Dpi + (inside ? 0 : t) : 0;
                    var r = new RectangleF(t / 2f, t / 2f, outer.Width - t, outer.Height - t);
                    int a = (int)(255 * _owner.Opacity);
                    using (var path = Draw.Round(r, Math.Max(0.5f, rad)))
                    using (var brush = _owner.GoldBorder ? Draw.Gold(new RectangleF(0, 0, outer.Width, outer.Height), false, a) : (Brush)new SolidBrush(Draw.A(_owner.Color, a)))
                    using (var pen = new Pen(brush, t)) g.DrawPath(pen, path);
                }
                IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
                IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hb);
                try
                {
                    var size = new SIZE(bmp.Width, bmp.Height); var src = new POINT(0, 0); var pos = new POINT(outer.X, outer.Y);
                    var blend = new BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
                    Native.UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
                }
                finally { Native.SelectObject(mem, old); Native.DeleteObject(hb); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen); }
            }
        }

        public static Bitmap Preview(AlwaysOnTop o, Size win)     // for previews/tests
        {
            int t = o.Thickness;
            var bmp = new Bitmap(win.Width + 2 * t, win.Height + 2 * t);
            using (var g = Graphics.FromImage(bmp))
            {
                Draw.Hq(g);
                var r = new RectangleF(t / 2f, t / 2f, bmp.Width - t, bmp.Height - t);
                using (var path = Draw.Round(r, o.Rounded ? 8 + t : 0.5f))
                using (var brush = o.GoldBorder ? Draw.Gold(new RectangleF(0, 0, bmp.Width, bmp.Height), false, (int)(255 * o.Opacity)) : (Brush)new SolidBrush(Draw.A(o.Color, (int)(255 * o.Opacity))))
                using (var pen = new Pen(brush, t)) g.DrawPath(pen, path);
            }
            return bmp;
        }
    }

    /// <summary>Two soft notes – rising when a window goes on top, falling when it's let go.</summary>
    public static class Chime
    {
        public static byte[] Wave(bool up, double volume)
        {
            const int rate = 44100;
            double[] notes = up ? new[] { 784.0, 1175.0 } : new[] { 1175.0, 784.0 };     // G5 -> D6, or back down
            int per = (int)(rate * 0.11), n = per * notes.Length + (int)(rate * 0.12);
            var pcm = new short[n];
            for (int k = 0; k < notes.Length; k++)
                for (int i = 0; i < per + (int)(rate * 0.12); i++)
                {
                    int at = k * per + i; if (at >= n) break;
                    double t = i / (double)rate, env = Math.Min(1, t / 0.006) * Math.Exp(-t * 14);       // quick attack, soft ring-out
                    double v = Math.Sin(2 * Math.PI * notes[k] * t) + 0.25 * Math.Sin(4 * Math.PI * notes[k] * t);
                    pcm[at] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, pcm[at] + v * env * 9000 * volume));
                }
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                foreach (var s in pcm) w.Write(s);
                return ms.ToArray();
            }
        }
        public static void Play(bool up, double volume)
        {
            try { var p = new System.Media.SoundPlayer(new System.IO.MemoryStream(Wave(up, volume))); p.Play(); } catch { }
        }
    }

    public class AlwaysOnTop : IDisposable
    {
        readonly Suite _s;
        readonly Dictionary<IntPtr, PinBorder> _pins = new Dictionary<IntPtr, PinBorder>();
        readonly Timer _timer = new Timer { Interval = 40 };
        IntPtr _hook; PinNative.HookProc _proc;
        public static readonly string[] Shortcuts = { "Win+A", "Win+Ctrl+T", "Ctrl+Alt+T", "Win+Shift+A" };

        public AlwaysOnTop(Suite s) { _s = s; _timer.Tick += (o, e) => { foreach (var b in _pins.Values.ToList()) b.Sync(false); }; }
        Cfg C => _s.Cfg;
        public bool Enabled => C.Bool("aot_enabled", true);
        public string Shortcut => C.Str("aot_hotkey", "Win+A");
        public bool ShowBorder => C.Bool("aot_border", true);
        public int Thickness => Math.Max(1, (int)Math.Round(C.Num("aot_thickness", 3) * WidgetWindow.Dpi));
        public double Opacity => C.Num("aot_opacity", 1.0);
        public bool Rounded => C.Bool("aot_round", true);
        public string CustomColor => C.Str("aot_color", "");
        public bool Sound => C.Bool("aot_sound", true);
        public double Volume => C.Num("aot_volume", 0.6);
        Theme ThemeNow => _s.Battery?.MenuTheme ?? Themes.All["Dark"];
        public Color Color { get { if (CustomColor != "") try { return Theme.C(CustomColor); } catch { } return Color.FromArgb(255, ThemeNow.Accent); } }
        public bool GoldBorder => CustomColor == "" && ThemeNow.Metal;
        public IEnumerable<IntPtr> Pinned => _pins.Keys;

        public void Start()
        {
            if (!Native.OnWindows) return;
            _proc = Hook;
            _hook = PinNative.SetWindowsHookEx(13, _proc, PinNative.GetModuleHandle(null), 0);          // WH_KEYBOARD_LL
            _timer.Start();
        }

        /// <summary>"Win+Ctrl+T" -> required modifiers and key.</summary>
        public static bool Parse(string s, out bool win, out bool ctrl, out bool shift, out bool alt, out int vk)
        {
            var parts = (s ?? "").Split('+').Select(p => p.Trim().ToLowerInvariant()).ToList();
            win = parts.Contains("win"); ctrl = parts.Contains("ctrl"); shift = parts.Contains("shift"); alt = parts.Contains("alt");
            string key = parts.LastOrDefault() ?? "";
            vk = key.Length == 1 ? char.ToUpperInvariant(key[0]) : 0;
            return vk != 0 && (win || ctrl || alt);
        }

        static bool Down(int vk) => (PinNative.GetAsyncKeyState(vk) & 0x8000) != 0;

        IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && Enabled && (w.ToInt32() == 0x100 || w.ToInt32() == 0x104))                  // key down (also with Alt)
            {
                var k = (PinNative.KBD)Marshal.PtrToStructure(l, typeof(PinNative.KBD));
                bool win, ctrl, shift, alt; int vk;
                if ((k.flags & 0x10) == 0 && Parse(Shortcut, out win, out ctrl, out shift, out alt, out vk) && k.vk == vk &&
                    win == (Down(0x5B) || Down(0x5C)) && ctrl == Down(0x11) && shift == Down(0x10) && alt == Down(0x12))
                {
                    if (win) { PinNative.keybd_event(0xFF, 0, 0, UIntPtr.Zero); PinNative.keybd_event(0xFF, 0, 2, UIntPtr.Zero); }   // keeps Start from opening
                    _s.Battery?.BeginInvoke((Action)ToggleForeground);
                    return new IntPtr(1);                                                                // Windows doesn't see it (no Quick Settings)
                }
            }
            return PinNative.CallNextHookEx(_hook, code, w, l);
        }

        static readonly HashSet<string> Shell = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow" };

        public void ToggleForeground()
        {
            IntPtr h = PinNative.GetAncestor(Native.GetForegroundWindow(), 2);                                   // GA_ROOT
            if (h == IntPtr.Zero) return;
            var cls = new StringBuilder(128); Native.GetClassName(h, cls, 128);
            uint pid; Native.GetWindowThreadProcessId(h, out pid);
            if (Shell.Contains(cls.ToString()) || pid == (uint)Process.GetCurrentProcess().Id) return;
            if (_pins.ContainsKey(h)) Unpin(h, true); else Pin(h);
        }

        public static string Title(IntPtr h) { var sb = new StringBuilder(256); PinNative.GetWindowText(h, sb, 256); var t = sb.ToString().Trim(); return t == "" ? "Window" : t.Length > 60 ? t.Substring(0, 59) + "…" : t; }

        public void Pin(IntPtr h)
        {
            if (!Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE))
            {
                Toast.Show("dot", "Couldn't keep it on top", "That window runs as administrator – turn on Run as administrator.", ThemeNow, 4500);
                return;
            }
            var b = new PinBorder(this, h);
            _pins[h] = b;
            if (ShowBorder) b.Sync(true);
            if (Sound) Chime.Play(true, Volume);
            Toast.Show("up", "Always on top", Title(h), ThemeNow, 1800);
        }

        public void Unpin(IntPtr h, bool say)
        {
            PinBorder b;
            if (_pins.TryGetValue(h, out b)) { _pins.Remove(h); try { b.Close(); b.Dispose(); } catch { } }
            if (PinNative.IsWindow(h)) Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (say) { if (Sound) Chime.Play(false, Volume); Toast.Show("down", "No longer on top", Title(h), ThemeNow, 1500); }
        }

        public void Forget(IntPtr h) { PinBorder b; if (_pins.TryGetValue(h, out b)) { _pins.Remove(h); try { b.Close(); b.Dispose(); } catch { } } }

        public void Restyle()
        {
            foreach (var b in _pins.Values.ToList())
            {
                if (!ShowBorder) { if (b.Visible) b.Hide(); continue; }
                b.Sync(true);
            }
        }

        void Set(string k, object v) { C[k] = v; C.Save(); Restyle(); }

        public void Menu(ToolStripItemCollection it, Theme t)
        {
            var m = Menus.Sub(it, "Always on top", t); var mi = m.DropDownItems;
            Menus.Item(mi, $"Press {Shortcut} to keep the active window on top", null, null, false);
            Menus.Item(mi, "On", () => { C["aot_enabled"] = !Enabled; C.Save(); if (!Enabled) foreach (var h in _pins.Keys.ToList()) Unpin(h, false); }, Enabled);
            var sc = Menus.Sub(mi, "Shortcut", t);
            foreach (var s0 in Shortcuts) { string s = s0; Menus.Item(sc.DropDownItems, s + (s == "Win+A" ? "  (replaces Quick Settings)" : s == "Win+Ctrl+T" ? "  (like PowerToys)" : ""), () => { C["aot_hotkey"] = s; C.Save(); }, Shortcut == s); }
            Menus.Sep(mi);
            Menus.Item(mi, "Border around it", () => Set("aot_border", !ShowBorder), ShowBorder);
            var th = Menus.Sub(mi, "Border thickness", t);
            foreach (var v0 in new[] { 1, 2, 3, 4, 6, 8, 12 }) { int v = v0; Menus.Item(th.DropDownItems, v + " px", () => Set("aot_thickness", v), Math.Abs(C.Num("aot_thickness", 3) - v) < 0.1); }
            var op = Menus.Sub(mi, "Border opacity", t);
            foreach (var v0 in new[] { 1.0, 0.85, 0.7, 0.55, 0.4, 0.25 }) { double v = v0; Menus.Item(op.DropDownItems, Math.Round(v * 100) + "%", () => Set("aot_opacity", v), Math.Abs(Opacity - v) < 0.01); }
            var col = Menus.Sub(mi, "Border color", t);
            Menus.Item(col.DropDownItems, "Theme color" + (ThemeNow.Metal ? " (brushed gold)" : ""), () => Set("aot_color", ""), CustomColor == "", true, Draw.Swatch(Color.FromArgb(255, ThemeNow.Accent)));
            Menus.Item(col.DropDownItems, "Custom color…", () =>
            {
                using (var d = new ColorDialog { Color = Color, FullOpen = true }) if (d.ShowDialog() == DialogResult.OK) Set("aot_color", Theme.Hex(d.Color));
            }, CustomColor != "", true, CustomColor != "" ? Draw.Swatch(Color) : null);
            Menus.Item(mi, "Rounded corners", () => Set("aot_round", !Rounded), Rounded);
            Menus.Sep(mi);
            Menus.Item(mi, "Sound", () => { C["aot_sound"] = !Sound; C.Save(); if (Sound) Chime.Play(true, Volume); }, Sound);
            var vol = Menus.Sub(mi, "Sound volume", t);
            foreach (var v0 in new[] { 1.0, 0.6, 0.3, 0.15 }) { double v = v0; Menus.Item(vol.DropDownItems, Math.Round(v * 100) + "%", () => { C["aot_volume"] = v; C.Save(); Chime.Play(true, v); }, Math.Abs(Volume - v) < 0.01); }
            Menus.Sep(mi);
            if (_pins.Count == 0) Menus.Item(mi, "No window is on top right now", null, null, false);
            else
            {
                foreach (var h0 in _pins.Keys.ToList()) { IntPtr h = h0; Menus.Item(mi, "Let go: " + Title(h), () => Unpin(h, true)); }
                Menus.Item(mi, "Let go of all", () => { foreach (var h in _pins.Keys.ToList()) Unpin(h, false); });
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            if (_hook != IntPtr.Zero) { PinNative.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            foreach (var h in _pins.Keys.ToList()) Unpin(h, false);                                     // windows return to normal when the app quits
        }
    }

    /// <summary>First start: what the app connects to, and the choice to turn it off.</summary>
    public class PrivacyForm : ThemedForm
    {
        public PrivacyForm(Suite s, Theme t) : base(t)
        {
            Text = "Welcome – Battery Widget"; ClientSize = new Size(520, 330); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; TopMost = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 12), RowCount = 6, ColumnCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, 100));
            root.Controls.Add(Lbl("Your privacy", false, 13, true));
            root.Controls.Add(Lbl("Battery Widget has no accounts, ads or tracking, and keeps its settings on this PC. Two features use the internet – choose what you want:", true));
            var wx = new CheckBox { Text = "Weather – sends the chosen place to Open-Meteo; automatic location uses your IP address (ipwho.is)", Checked = s.Cfg.Bool("show_weather", true), AutoSize = true, ForeColor = ForeColor, MaximumSize = new Size(470, 0), Margin = new Padding(0, 10, 0, 4) };
            var up = new CheckBox { Text = "Look for updates at start – asks GitHub for the newest version", Checked = s.Cfg.Bool("update_check", true), AutoSize = true, ForeColor = ForeColor, MaximumSize = new Size(470, 0) };
            root.Controls.Add(wx); root.Controls.Add(up);
            root.Controls.Add(Lbl("You can change this later in the menus. Full details: Code signing policy on the project's GitHub page.", true));
            var ok = Btn("Continue", true);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft }; bar.Controls.Add(ok);
            root.Controls.Add(bar); Controls.Add(root);
            AcceptButton = ok;
            ok.Click += (o, e) => { s.Cfg["show_weather"] = wx.Checked; s.Cfg["update_check"] = up.Checked; s.Cfg["privacy_seen"] = 1; s.Cfg.Save(); Close(); };
        }
    }
}
