// WidgetWindow.cs – what both widgets share: the window (desktop or inside the taskbar), position, dragging,
// reordering, the moon, tooltips and themed menus. Subclasses only lay out and paint their content.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class Cell { public RectangleF R; public string Key, Kind, Tip; public object Item; }

    public abstract class WidgetWindow : Form
    {
        public Cfg C;
        public Suite Suite;
        public readonly bool TaskbarMode, Embedded;
        public bool? SimLight;                                  // previews: pretend the taskbar is light/dark
        protected readonly List<Cell> Cells = new List<Cell>();
        protected RectangleF? MoonRect, GripRect;
        protected TaskbarInfo Tb;
        protected Color Key = Color.FromArgb(32, 32, 32);
        protected bool HoverMoon, HoverGrip;
        protected Cell MenuCell;                                 // what was under the mouse when the menu opened
        protected bool Dragging => _dragScreen.HasValue;
        Theme _pal; DateTime _palAt = DateTime.MinValue;
        Bitmap _frame;
        Size _size = new Size(10, 10);
        int _x, _y;                                             // position: screen pixels, or inside the taskbar when embedded
        IntPtr _tray;
        readonly Timer _tick = new Timer { Interval = 1000 }, _z = new Timer { Interval = 120 };
        Native.WinEventProc _hookProc; IntPtr _hook;
        readonly ToolTip _tip = new ToolTip { InitialDelay = 350, ReshowDelay = 80, ShowAlways = true };
        string _tipText;
        Point? _dragScreen; Point _dragFrom; bool _dragMoved, _pressMoon, _pressScan; string _reorderKey; Cell _pressCell;
        int _keyTick; bool _fsHidden, _ahHidden;
        public static float Dpi = 1f;
        bool _childAlpha;                                       // embedded with real transparency (Windows 8+), else the color key
        IntPtr _layer;                                          // the wallpaper layer when we live behind the desktop icons
        List<WidgetWindow> _group = new List<WidgetWindow>(); List<Point> _groupStart = new List<Point>();
        public bool WallpaperMode => !TaskbarMode && C != null && C.Str("stacking", "bottom") == "wallpaper";
        public static int AcrylicTint = 215;
        public bool Blend => !TaskbarMode && C != null && (C.Bool("blend") || Pal.Glass);
        bool KeyPainted => (Embedded && !_childAlpha) || (TaskbarMode && SimLight.HasValue);
        bool OwnPaint => (Embedded && !_childAlpha) || WallpaperMode;
        public virtual float VisualInset => 0;
        /// <summary>How far the content sits above (-) or below (+) the middle of the taskbar.</summary>
        float Cross => TaskbarMode && C != null ? (float)C.Num("tb_cross", 0) * Dpi : 0;
        PointF CrossVec => Tb != null && !Tb.Horizontal ? new PointF(Cross, 0) : new PointF(0, Cross);
        MouseEventArgs Content(MouseEventArgs e) => Cross == 0 ? e : new MouseEventArgs(e.Button, e.Clicks, (int)(e.X - CrossVec.X), (int)(e.Y - CrossVec.Y), e.Delta);
        public void Nudge(float px)
        {
            float lim = (Tb?.Thickness ?? 48) * 0.3f / Dpi;
            C["tb_cross"] = px == 0 ? 0 : Math.Max(-lim, Math.Min(lim, (float)C.Num("tb_cross", 0) + px)); Save(); Render();
        }
        public void CrossItems(ToolStripItemCollection pi)
        {
            Menus.Item(pi, "Move up a little", () => Nudge(Tb != null && !Tb.Horizontal ? -2 : -2));
            Menus.Item(pi, "Move down a little", () => Nudge(2));
            Menus.Item(pi, "Center vertically", () => Nudge(0), Math.Abs(C.Num("tb_cross", 0)) < 0.1);
            Menus.Item(pi, "Drag the handle up out of the taskbar to put it on the desktop", null, null, false);
        }
        public virtual float VisualRadius => 16 * S;
        public Point Pos => new Point(_x, _y);
        public Rectangle ScreenVisual
        {
            get { int i = (int)VisualInset; return new Rectangle(_x + i, _y + i, Math.Max(1, _size.Width - 2 * i), Math.Max(1, _size.Height - 2 * i)); }
        }
        public GraphicsPath VisualShape()
        {
            float i = VisualInset;
            return Draw.Round(new RectangleF(i, i, _size.Width - 2 * i, _size.Height - 2 * i), VisualRadius);
        }

        /// <summary>Frosted glass: the wallpaper behind the widget, blurred and tinted. False = paint the normal background.</summary>
        protected bool Backdrop(Graphics g, GraphicsPath shape, RectangleF r)
        {
            if (!Blend) return false;
            var st = g.Save();
            g.SetClip(shape);
            bool ok = Wallpaper.Paint(g, r, new Rectangle(_x + (int)r.X, _y + (int)r.Y, Math.Max(1, (int)r.Width), Math.Max(1, (int)r.Height)), true);
            if (ok)
            {
                int tint = Pal.Glass ? AcrylicTint : Math.Min(140, (int)(Pal.Bg.A * 0.48));       // Windows acrylic is a dense dark layer
                using (var b = new SolidBrush(Color.FromArgb(tint, Pal.Bg.R, Pal.Bg.G, Pal.Bg.B))) g.FillPath(b, shape);
                using (var lg = new LinearGradientBrush(r, Color.FromArgb(Pal.Glass ? 7 : 30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical)) g.FillPath(lg, shape);
            }
            g.Restore(st);
            return ok;
        }

        protected WidgetWindow(Cfg cfg)
        {
            C = cfg;
            TaskbarMode = cfg.Str("mode") == "taskbar";
            Embedded = TaskbarMode && cfg.Bool("tb_embed", true) && Native.OnWindows;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Icon = Program.AppIcon;
            _tick.Tick += (s, e) => TaskbarTick();
            _z.Tick += (s, e) => { if (TaskbarMode && !Embedded && Visible && WinUtil.CoveredByTaskbar(Handle)) WinUtil.SetTopmost(Handle); };
        }

        // ---- settings shortcuts --------------------------------------------------------------
        public float S => Dpi * (float)C.Num("scale", 1);
        public Theme Pal
        {
            get
            {
                if (_pal == null || (DateTime.UtcNow - _palAt).TotalSeconds > 3)
                { _pal = Themes.Resolve(C.Str("theme", "Dark"), C.Str("accent", ""), TaskbarMode, C.Bool("tb_background"), SimLight); _palAt = DateTime.UtcNow; }
                return _pal;
            }
        }
        public void InvalidateTheme() { _pal = null; }
        public virtual void Save() { C.Save(); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (C == null) return cp;          // Windows Forms asks before our constructor has run
                cp.ExStyle |= (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                if (!Embedded && !WallpaperMode) cp.ExStyle |= (int)Native.WS_EX_LAYERED;
                if (TaskbarMode && !Embedded || C.Str("stacking") == "top") cp.ExStyle |= (int)Native.WS_EX_TOPMOST;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            // "On the desktop": stay behind every other window
            if (m.Msg == 0x46 && !TaskbarMode && C.Str("stacking", "bottom") == "bottom" && Native.OnWindows)
            {
                var wp = Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                var p = (WINDOWPOS)wp;
                p.hwndInsertAfter = Native.HWND_BOTTOM;
                p.flags &= ~Native.SWP_NOZORDER;
                Marshal.StructureToPtr(p, m.LParam, false);
            }
            base.WndProc(ref m);
        }
        [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

        // ---- what subclasses provide -----------------------------------------------------------
        /// <summary>Fill Cells / MoonRect / GripRect and return the size.</summary>
        protected abstract SizeF DoLayout(Graphics g);
        protected abstract void DoPaint(Graphics g);
        public abstract void FillMenu(ContextMenuStrip menu, bool common);
        protected virtual void OnScanClicked() { }
        protected virtual void OnCellClick(Cell c) { }
        protected virtual void OnReorder(string key, int index) { }
        public virtual void RefreshNow() { }
        protected virtual bool ReorderVertical => TaskbarMode ? !Tb.Horizontal : C.Str("layout") == "vertical";

        // ---- layout + rendering ------------------------------------------------------------------
        public void Relayout()
        {
            if (IsDisposed) return;
            Cells.Clear(); MoonRect = null; GripRect = null;
            if (TaskbarMode) Tb = WinUtil.Taskbar();
            SizeF sz;
            using (var b = new Bitmap(1, 1)) using (var g = Graphics.FromImage(b)) { Draw.Hq(g); sz = DoLayout(g); }
            _size = new Size(Math.Max(4, (int)Math.Ceiling(sz.Width)), Math.Max(4, (int)Math.Ceiling(sz.Height)));
            Render();
            if (TaskbarMode) DockToTaskbar(); else ApplyBounds();
            if (WallpaperMode && IsHandleCreated) using (var shape = VisualShape()) Desk.SetRegion(this, shape);   // only the widget itself shows
        }

        public Bitmap RenderToBitmap()
        {
            var bmp = new Bitmap(_size.Width, _size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Draw.Hq(g);
                if (KeyPainted) g.Clear(Key);                  // the taskbar's color, cut out by Windows
                else if (WallpaperMode) { if (!Wallpaper.Paint(g, new RectangleF(0, 0, bmp.Width, bmp.Height), new Rectangle(_x, _y, bmp.Width, bmp.Height), false)) g.Clear(Color.FromArgb(255, Pal.Bg)); }
                else g.Clear(Color.FromArgb(1, 0, 0, 0));      // nearly invisible, but the whole widget answers the mouse
                if (Cross != 0) g.TranslateTransform(CrossVec.X, CrossVec.Y);   // nudged up or down inside the taskbar
                DoPaint(g);
            }
            return bmp;
        }
        public Size LayoutSize => _size;

        void Render()
        {
            var old = _frame;
            _frame = RenderToBitmap();
            old?.Dispose();
            if (!IsHandleCreated) return;
            if (OwnPaint) Invalidate();
            else PushLayered();
        }

        void PushLayered()
        {
            if (!Native.OnWindows || _frame == null) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
            IntPtr hb = _frame.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hb);
            try
            {
                var size = new SIZE(_frame.Width, _frame.Height);
                var src = new POINT(0, 0);
                var pos = new POINT(_x, _y);
                var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = (byte)Math.Round(255 * C.Num("opacity", 1)), AlphaFormat = 1 };
                if (_childAlpha) _childOk = UpdateLayeredChild(Handle, IntPtr.Zero, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
                else Native.UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally { Native.SelectObject(mem, old); Native.DeleteObject(hb); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        bool _childOk;
        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
        static extern bool UpdateLayeredChild(IntPtr h, IntPtr hdcDst, IntPtr pptDst, ref SIZE sz, IntPtr hdcSrc, ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);
        protected override void OnPaintBackground(PaintEventArgs e) { if (!OwnPaint) base.OnPaintBackground(e); }
        protected override void OnPaint(PaintEventArgs e) { if (OwnPaint && _frame != null) e.Graphics.DrawImageUnscaled(_frame, 0, 0); }

        public void MoveWindow(int x, int y)
        {
            bool moved = x != _x || y != _y;
            _x = x; _y = y;
            if (!IsHandleCreated) return;
            int wx = x, wy = y;
            if (_layer != IntPtr.Zero) { var lr = WinUtil.WindowRect(_layer) ?? Rectangle.Empty; wx -= lr.Left; wy -= lr.Top; }
            if (Native.OnWindows) Native.SetWindowPos(Handle, IntPtr.Zero, wx, wy, _size.Width, _size.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            else SetBounds(x, y, _size.Width, _size.Height);
            if (moved && (Blend || WallpaperMode)) { Render(); return; }      // what's behind it changed
            if (!OwnPaint) PushLayered();
        }

        void ApplyBounds() => MoveWindow(_x, _y);

        // ---- showing / the taskbar ---------------------------------------------------------------
        public void Start()
        {
            Relayout();                                  // size first, so the spot can be chosen
            PlaceDefault();
            Show();
            if (Embedded)
            {
                _tray = WinUtil.TrayHwnd;
                if (_tray != IntPtr.Zero)
                {
                    long st = Native.GetWindowLong(Handle, Native.GWL_STYLE);
                    Native.SetWindowLong(Handle, Native.GWL_STYLE, (st & ~Native.WS_POPUP) | Native.WS_CHILD);
                    Native.SetParent(Handle, _tray);
                }
                _childAlpha = false;                    // the proven way: the background color is cut out (color key)
                Render(); RefreshKey(true);
                RaiseInTaskbar();
                Later(2000, () => CheckShown(0));
            }
            if (WallpaperMode)
            {
                _layer = Desk.WallpaperLayer();
                if (_layer != IntPtr.Zero)
                {
                    long st = Native.GetWindowLong(Handle, Native.GWL_STYLE);
                    Native.SetWindowLong(Handle, Native.GWL_STYLE, (st & ~Native.WS_POPUP) | Native.WS_CHILD);
                    Native.SetParent(Handle, _layer);
                    Desk.Track(this, true);
                    Later(400, () => { if (!Native.IsWindowVisible(Handle)) LeaveWallpaper(); else Later(2500, () => CheckWallpaper(0)); });
                }
                else LeaveWallpaper();
            }
            if (TaskbarMode)
            {
                _tick.Start();
                if (!Embedded)
                {
                    _z.Start();
                    _hookProc = (h, ev, hw, o, c, t, tm) => { foreach (int ms in new[] { 0, 40, 120, 300, 700 }) Later(ms, () => { if (Visible) WinUtil.SetTopmost(Handle); }); };
                    if (Native.OnWindows) _hook = Native.SetWinEventHook(3, 3, IntPtr.Zero, _hookProc, 0, 0, 0);
                }
            }
            Relayout();
        }

        public static void Later(int ms, Action a)
        {
            if (ms <= 0) { a(); return; }
            var t = new Timer { Interval = ms };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        public virtual void PlaceDefault()
        {
            if (TaskbarMode) return;
            var pos = C.List("pos");
            var p = pos.Count == 2 ? new Point(Convert.ToInt32(pos[0]), Convert.ToInt32(pos[1])) : (Suite?.DefaultSpot(this) ?? Center());
            p = KeepOnScreen(p);
            _x = p.X; _y = p.Y;
        }

        Point Center() { var wa = Screen.PrimaryScreen.WorkingArea; return new Point(wa.Left + (wa.Width - _size.Width) / 2, wa.Top + (wa.Height - _size.Height) / 2); }

        /// <summary>Moves a position so the whole widget is on a screen (the one it overlaps most, else the main one).</summary>
        public Point KeepOnScreen(Point p)
        {
            int i = (int)VisualInset;
            var r = new Rectangle(p.X + i, p.Y + i, Math.Max(1, _size.Width - 2 * i), Math.Max(1, _size.Height - 2 * i));
            var scr = Screen.AllScreens.OrderByDescending(s => { var x = Rectangle.Intersect(s.WorkingArea, r); return x.Width * x.Height; }).First();
            var wa = Rectangle.Intersect(scr.WorkingArea, r).IsEmpty ? Screen.PrimaryScreen.WorkingArea : scr.WorkingArea;
            int x0 = Math.Max(wa.Left, Math.Min(r.X, wa.Right - r.Width)), y0 = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - r.Height));
            return new Point(x0 - i, y0 - i);
        }

        void LeaveWallpaper()
        {
            Desk.Track(this, false);
            C["stacking"] = "bottom"; Save();
            Toast.Show("dot", "Kept it on the desktop", "This PC doesn't let widgets into the wallpaper layer.", MenuTheme, 5000);
            Later(1500, () => Suite?.RestartAll());
        }

        /// <summary>Compares what the screen shows with what we drew; if the widget isn't there, leave the wallpaper layer.</summary>
        void CheckWallpaper(int tries)
        {
            if (_layer == IntPtr.Zero || _frame == null || !IsHandleCreated || !Native.OnWindows) return;
            var v = ScreenVisual; int ok = 0, bad = 0;
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                for (int gy = 1; gy <= 3; gy++)
                    for (int gx = 1; gx <= 3; gx++)
                    {
                        var p = new POINT(v.X + v.Width * gx / 4, v.Y + v.Height * gy / 4);
                        var sb = new System.Text.StringBuilder(64);
                        var h = DeskNative.WindowFromPoint(p);
                        Native.GetClassName(h, sb, 64);
                        if (h != Handle && !new[] { "SysListView32", "SHELLDLL_DefView", "WorkerW", "Progman" }.Contains(sb.ToString())) continue;   // a window covers it now
                        uint c = Native.GetPixel(dc, p.X, p.Y);
                        var f = _frame.GetPixel(Math.Min(_frame.Width - 1, p.X - _x), Math.Min(_frame.Height - 1, p.Y - _y));
                        int dist = Math.Abs((int)(c & 0xFF) - f.R) + Math.Abs((int)((c >> 8) & 0xFF) - f.G) + Math.Abs((int)((c >> 16) & 0xFF) - f.B);
                        if (dist < 60) ok++; else bad++;
                    }
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
            if (ok + bad < 3) { if (tries < 6) Later(3000, () => CheckWallpaper(tries + 1)); return; }
            if (bad > ok) LeaveWallpaper();
        }
        protected virtual int DefaultTopOffset => 0;
        public virtual double DefaultTaskbarOffset(TaskbarInfo tb) => Suite?.TaskbarSlot(this, tb) ?? TrayOffset(tb);
        public double TrayOffset(TaskbarInfo tb)
        {
            var tray = WinUtil.TrayArea();
            if (tray.HasValue)
            {
                if (tb.Horizontal && tray.Value.Width < tb.Rect.Width * 0.6) return tb.Rect.Right - tray.Value.Left + 6;
                if (!tb.Horizontal && tray.Value.Height < tb.Rect.Height * 0.6) return tb.Rect.Bottom - tray.Value.Top + 6;
            }
            return (tb.Horizontal ? 300 : 140) * Dpi;
        }

        public void DockToTaskbar()
        {
            var tb = Tb ?? WinUtil.Taskbar();
            int L = tb.Horizontal ? tb.Rect.Width : tb.Rect.Height, size = tb.Horizontal ? _size.Width : _size.Height;
            double off = C.NumOrNull("tb_offset") ?? DefaultTaskbarOffset(tb);
            double along = Math.Max(0, Math.Min(L - off - size, L - size));
            double across = (tb.Thickness - (tb.Horizontal ? _size.Height : _size.Width)) / 2.0;
            double rx = tb.Horizontal ? along : across, ry = tb.Horizontal ? across : along;
            if (Embedded && _tray != IntPtr.Zero)
            {
                var tr = WinUtil.WindowRect(_tray) ?? tb.Rect;
                rx += tb.Rect.Left - tr.Left; ry += tb.Rect.Top - tr.Top;
                MoveWindow((int)Math.Round(rx), (int)Math.Round(ry));
            }
            else MoveWindow((int)Math.Round(tb.Rect.Left + rx), (int)Math.Round(tb.Rect.Top + ry));
        }

        public void StorePosition()
        {
            if (TaskbarMode && Tb != null)
            {
                int along = Tb.Horizontal ? _x : _y, L = Tb.Horizontal ? Tb.Rect.Width : Tb.Rect.Height;
                if (!Embedded) along -= Tb.Horizontal ? Tb.Rect.Left : Tb.Rect.Top;
                else if (_tray != IntPtr.Zero) { var tr = WinUtil.WindowRect(_tray) ?? Tb.Rect; along -= Tb.Horizontal ? Tb.Rect.Left - tr.Left : Tb.Rect.Top - tr.Top; }
                C["tb_offset"] = L - along - (Tb.Horizontal ? _size.Width : _size.Height);
            }
            else C["pos"] = new List<object> { _x, _y };
            Save();
        }

        /// <summary>On top of the taskbar's own parts (Windows 11 draws its icons in a layer that could cover us).</summary>
        void RaiseInTaskbar()
        {
            if (Embedded && IsHandleCreated && Native.OnWindows)
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>Is the widget really visible inside the taskbar? If Windows hides it, switch to floating above the
        /// taskbar instead of staying invisible.</summary>
        void CheckShown(int failures)
        {
            if (!Embedded || _frame == null || !IsHandleCreated || !Visible || !WinUtil.TaskbarVisible()) return;
            Point? painted = null;
            for (int y = _frame.Height / 2; y < _frame.Height && painted == null; y++)
                for (int x = (int)(GripRect?.Right ?? 0) + 2; x < _frame.Width; x++)
                {
                    var c = _frame.GetPixel(x, y);
                    if (c.A == 255 && Math.Abs(c.R - Key.R) + Math.Abs(c.G - Key.G) + Math.Abs(c.B - Key.B) > 60) { painted = new Point(x, y); break; }
                }
            if (painted == null) return;
            var p = PointToScreen(painted.Value);
            IntPtr hit = DeskNative.WindowFromPoint(new POINT(p.X, p.Y));
            if (hit == Handle) return;
            if (failures < 1) { RaiseInTaskbar(); Later(2500, () => CheckShown(failures + 1)); return; }
            C["tb_embed"] = false; Save();
            Toast.Show("dot", "Showing it above the taskbar", "Windows didn't let it inside the taskbar on this PC.", MenuTheme, 5000);
            Later(1500, () => Suite?.RestartAll());
        }

        public void ResetTaskbarSpot() { C["tb_offset"] = null; Save(); DockToTaskbar(); }

        void RefreshKey(bool force)
        {
            if (!Embedded || _childAlpha) return;
            var c = WinUtil.SampleTaskbarColor() ?? ((Themes.WindowsLight(true) ?? false) ? Color.FromArgb(238, 238, 238) : Color.FromArgb(32, 32, 32));
            bool changed = c.ToArgb() != Key.ToArgb();
            Key = c;
            if (force || changed || !WinUtil.IsLayered(Handle)) WinUtil.SetColorKey(Handle, Key, (byte)Math.Round(255 * C.Num("opacity", 1)));
            if (changed) Render();
        }

        public bool SwitchedOn => Suite == null || Suite.IsOn(this);

        void TaskbarTick()
        {
            if (!TaskbarMode || !SwitchedOn) return;
            var tb = WinUtil.Taskbar();
            if (Tb == null || tb.Rect != Tb.Rect || tb.Edge != Tb.Edge) Relayout();
            if (!_childAlpha && (++_keyTick % 15 == 0 || (Embedded && !WinUtil.IsLayered(Handle)))) RefreshKey(false);
            if (Embedded)
            {
                RaiseInTaskbar();
                var tray = WinUtil.TrayHwnd;
                if (tray != IntPtr.Zero && tray != _tray) Suite?.RestartAll();     // Explorer restarted
                return;
            }
            bool fs = C.Bool("tb_hide_fullscreen", true) && WinUtil.FullscreenApp(), ah = tb.AutoHide && !WinUtil.TaskbarVisible();
            if (fs != _fsHidden || ah != _ahHidden) { _fsHidden = fs; _ahHidden = ah; Visible = !(fs || ah); }
            if (Visible && WinUtil.CoveredByTaskbar(Handle)) WinUtil.SetTopmost(Handle);
        }

        public void ApplyOpacity()
        {
            if (Embedded && !_childAlpha) RefreshKey(true); else if (!OwnPaint) PushLayered(); else Render();
        }

        public virtual void Shutdown()
        {
            _tick.Stop(); _z.Stop();
            if (_hook != IntPtr.Zero) { Native.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            try { if ((Embedded || _layer != IntPtr.Zero) && IsHandleCreated) Native.SetParent(Handle, IntPtr.Zero); } catch { }
            Desk.Track(this, false);
            try { Hide(); } catch { }
        }

        // ---- mouse --------------------------------------------------------------------------------
        protected Cell CellAt(PointF p) => Cells.FirstOrDefault(c => c.R.Contains(p));

        bool _pressGrip, _tearOff; float _crossStart;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            e = Content(e);
            _pressGrip = GripRect.HasValue && GripRect.Value.Contains(e.Location); _tearOff = false; _crossStart = (float)C.Num("tb_cross", 0);
            if (e.Button != MouseButtons.Left) return;
            _pressMoon = MoonRect.HasValue && MoonRect.Value.Contains(e.Location);
            var cell = CellAt(e.Location);
            _pressScan = cell != null && cell.Kind == "__scan__";
            _pressCell = cell;
            _dragMoved = false; _reorderKey = null;
            bool onGrip = GripRect.HasValue && GripRect.Value.Contains(e.Location);
            if (cell != null && cell.Kind != "__scan__" && !_pressMoon && !onGrip && ((ModifierKeys & Keys.Shift) != 0 || (TaskbarMode && Cells.Count > 1)))
            { _reorderKey = cell.Key; _reorderMoved = false; _reorderStart = Cursor.Position; return; }
            if (!_pressMoon && !C.Bool("locked"))
            {
                _dragScreen = Cursor.Position; _dragFrom = new Point(_x, _y); Capture = true;
                _group = !TaskbarMode && Suite != null ? Suite.Cluster(this, (ModifierKeys & Keys.Alt) != 0) : new List<WidgetWindow> { this };
                _groupStart = _group.Select(g => g.Pos).ToList();
            }
        }

        bool _reorderMoved; Point _reorderStart;
        protected override void OnMouseMove(MouseEventArgs e)
        {
            e = Content(e);
            if (_reorderKey != null && (e.Button & MouseButtons.Left) != 0)
            {
                if (!_reorderMoved && Math.Abs(Cursor.Position.X - _reorderStart.X) + Math.Abs(Cursor.Position.Y - _reorderStart.Y) < 5) return;
                _reorderMoved = true; Cursor = Cursors.SizeAll;
                var items = Cells.Where(c => c.Kind != "__scan__").ToList();
                var target = items.OrderBy(c => Math.Pow(c.R.X + c.R.Width / 2 - e.X, 2) + Math.Pow(c.R.Y + c.R.Height / 2 - e.Y, 2)).FirstOrDefault();
                if (target != null && target.Key != _reorderKey) OnReorder(_reorderKey, items.FindIndex(c => c.Key == target.Key));   // takes its place, live
                return;
            }
            bool hm = MoonRect.HasValue && MoonRect.Value.Contains(e.Location), hg = GripRect.HasValue && GripRect.Value.Contains(e.Location);
            if (hm != HoverMoon || hg != HoverGrip) { HoverMoon = hm; HoverGrip = hg; Render(); }
            if (_dragScreen == null && _reorderKey == null)
                Cursor = hm ? Cursors.Hand : hg ? (TaskbarMode && Tb != null && !Tb.Horizontal ? Cursors.SizeNS : Cursors.SizeWE) : C.Bool("locked") ? Cursors.Default : Cursors.SizeAll;
            if (_dragScreen.HasValue && (e.Button & MouseButtons.Left) != 0)
            {
                var d = new Point(Cursor.Position.X - _dragScreen.Value.X, Cursor.Position.Y - _dragScreen.Value.Y);
                if (!_dragMoved && Math.Abs(d.X) + Math.Abs(d.Y) < 3) return;
                if (!_dragMoved) { _dragMoved = true; Render(); }
                if (TaskbarMode && Tb != null)
                {
                    int lo = Embedded ? (Tb.Horizontal ? Tb.Rect.Left - (WinUtil.WindowRect(_tray) ?? Tb.Rect).Left : Tb.Rect.Top - (WinUtil.WindowRect(_tray) ?? Tb.Rect).Top)
                                      : (Tb.Horizontal ? Tb.Rect.Left : Tb.Rect.Top);
                    int L = Tb.Horizontal ? Tb.Rect.Width : Tb.Rect.Height, size = Tb.Horizontal ? _size.Width : _size.Height;
                    var cur = Cursor.Position; float far = 30 * Dpi;
                    _tearOff = Tb.Edge == "bottom" ? cur.Y < Tb.Rect.Top - far : Tb.Edge == "top" ? cur.Y > Tb.Rect.Bottom + far
                             : Tb.Edge == "left" ? cur.X > Tb.Rect.Right + far : cur.X < Tb.Rect.Left - far;
                    Cursor = _tearOff ? Cursors.Hand : Cursor;
                    if (Tb.Horizontal) MoveWindow(Math.Max(lo, Math.Min(_dragFrom.X + d.X, lo + L - size)), _dragFrom.Y);
                    else MoveWindow(_dragFrom.X, Math.Max(lo, Math.Min(_dragFrom.Y + d.Y, lo + L - size)));
                    if (_pressGrip && !_tearOff)
                    {
                        float lim = Tb.Thickness * 0.3f / Dpi, nc = _crossStart + (Tb.Horizontal ? d.Y : d.X) / Dpi;
                        C["tb_cross"] = Math.Max(-lim, Math.Min(lim, nc)); Render();
                    }
                }
                else
                {
                    if (Suite != null) d = Suite.Snap(this, _group, _dragFrom, d);
                    for (int i = 0; i < _group.Count; i++) _group[i].MoveWindow(_groupStart[i].X + d.X, _groupStart[i].Y + d.Y);
                }
                return;
            }
            var cell = CellAt(e.Location);
            string tip = hm ? MoonTip() : cell?.Tip;
            if (tip != _tipText)
            {
                _tipText = tip;
                if (tip == null) _tip.Hide(this); else _tip.Show(tip, this, e.X + (int)(14 * Dpi), e.Y + (int)(20 * Dpi), 8000);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (HoverMoon || HoverGrip) { HoverMoon = HoverGrip = false; Render(); }
            _tipText = null; _tip.Hide(this);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            e = Content(e);
            Capture = false;
            if (e.Button == MouseButtons.Right) { MenuCell = CellAt(e.Location); ShowMenu(); MenuCell = null; return; }
            if (e.Button != MouseButtons.Left) return;
            if (_reorderKey != null)
            {
                bool moved = _reorderMoved; _reorderKey = null; _reorderMoved = false; Cursor = Cursors.Default;
                if (!moved && _pressCell != null && CellAt(e.Location) == _pressCell) { var pc = _pressCell; _pressCell = null; OnCellClick(pc); }
                return;
            }
            if (_pressMoon && MoonRect.HasValue && MoonRect.Value.Contains(e.Location)) { _pressMoon = false; ShowMoonMenu(); return; }
            if (_pressScan && !_dragMoved) { _pressScan = false; _dragScreen = null; OnScanClicked(); return; }
            if (_pressCell != null && !_dragMoved && !_pressMoon && CellAt(e.Location) == _pressCell)
            { var c = _pressCell; _pressCell = null; _dragScreen = null; OnCellClick(c); return; }
            if (_dragScreen.HasValue)
            {
                _dragScreen = null;
                if (_dragMoved && TaskbarMode && _tearOff)            // pulled out of the taskbar: becomes a desktop widget right there
                {
                    var cur = Cursor.Position;
                    C["mode"] = "desktop"; C["tb_cross"] = 0; C["pos"] = new List<object> { cur.X - (int)(40 * Dpi), cur.Y - (int)(30 * Dpi) };
                    Save(); Suite?.RestartAll(); return;
                }
                if (_dragMoved && !TaskbarMode && Suite != null && WinUtil.Taskbar().Rect.Contains(Cursor.Position))   // dropped on the taskbar
                {
                    C["mode"] = "taskbar"; C["tb_offset"] = null; Save(); Suite.RestartAll(); return;
                }
                if (_dragMoved) { if (TaskbarMode) StorePosition(); else foreach (var g in _group) g.StorePosition(); }
                Render();
            }
        }

        public void HookMouse(string phase, MouseButtons b, POINT screen)
        {
            var p = new Point(screen.X - _x, screen.Y - _y);
            var e = new MouseEventArgs(b, phase == "double" ? 2 : 1, p.X, p.Y, 0);
            if (phase == "down") OnMouseDown(e);
            else if (phase == "up") OnMouseUp(e);
            else if (phase == "double") OnMouseDoubleClick(e);
            else OnMouseMove(new MouseEventArgs(Control.MouseButtons, 0, p.X, p.Y, 0));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            e = Content(e);
            if (MoonRect.HasValue && MoonRect.Value.Contains(e.Location)) return;
            RefreshNow();
        }

        // ---- menus -----------------------------------------------------------------------------------
        public ContextMenuStrip NewMenu() { var m = new ContextMenuStrip(); Menus.Style(m, MenuTheme); return m; }
        public Theme MenuTheme
        {
            get
            {
                if (TaskbarMode && !C.Bool("tb_background"))     // menus always get a solid surface
                {
                    string n = C.Str("theme", "Dark");
                    var t = Themes.Resolve(n == "Auto" ? ((Themes.WindowsLight(true) ?? false) ? "Light" : "Dark") : n, C.Str("accent", ""), false, true);
                    return t;
                }
                return Pal;
            }
        }

        void ShowMenu()
        {
            var m = NewMenu();
            FillMenu(m, true);
            m.Show(Cursor.Position);
        }

        string MoonTip()
        {
            var cur = WinUtil.ScreenTimeout();
            return (cur.HasValue ? "Screen turns off after " + WinUtil.ScreenLabel(cur.Value).ToLowerInvariant() : "Screen-off timer") + "\nClick to change";
        }

        public void FillScreenMenu(ToolStripItemCollection items)
        {
            var cur = WinUtil.ScreenTimeout();
            items.Add(new ToolStripMenuItem("Turn off my screen after") { Enabled = false });
            foreach (int s in WinUtil.ScreenSeconds)
            {
                int v = s;
                var it = new ToolStripMenuItem(WinUtil.ScreenLabel(s)) { Checked = cur == s, Enabled = Native.OnWindows };
                it.Click += (o, e) => SetScreenTimeout(v);
                items.Add(it);
            }
            items.Add(new ToolStripSeparator());
            var off = new ToolStripMenuItem("Turn screen off now") { Enabled = Native.OnWindows };
            off.Click += (o, e) => Later(600, WinUtil.ScreenOffNow);
            items.Add(off);
        }

        void SetScreenTimeout(int s)
        {
            bool ok = WinUtil.SetScreenTimeout(s);
            if (ok) Toast.Show("moon", s == 0 ? "Screen stays on" : "Screen turns off after " + WinUtil.ScreenLabel(s).ToLowerInvariant(),
                               s == 0 ? "The monitor won't go idle by itself." : "When the PC is idle. Change it any time with the moon button.", MenuTheme);
            else Toast.Show("moon", "Windows didn't accept the change", "A company policy may control the screen-off timer.", MenuTheme, 5000);
        }

        void ShowMoonMenu()
        {
            var m = NewMenu();
            FillScreenMenu(m.Items);
            var box = MoonRect ?? new RectangleF(0, 0, Width, Height);
            var top = PointToScreen(new Point((int)box.Left, (int)box.Top));
            var bottom = PointToScreen(new Point((int)box.Left, (int)box.Bottom));
            var pref = m.GetPreferredSize(Size.Empty);
            var scr = Screen.FromPoint(top).Bounds;
            var pt = bottom.Y + pref.Height + 4 > scr.Bottom ? new Point(top.X, top.Y - pref.Height - 4) : new Point(bottom.X, bottom.Y + 4);
            m.Show(pt);
        }

        // ---- painting helpers both widgets use ----------------------------------------------------------
        protected void PaintGrip(Graphics g, RectangleF box)
        {
            bool dark = Key.GetBrightness() < 0.5f;
            Color pad = dark ? Draw.Lighter(Key, 1.35) : Draw.Lighter(Key, 0.91);
            if (Key.R + Key.G + Key.B < 36) pad = Color.FromArgb(30, 30, 30);
            bool active = HoverGrip || Dragging;
            if (active) pad = dark ? Draw.Lighter(pad, 1.5) : Draw.Lighter(pad, 0.89);
            if (!Embedded && !(TaskbarMode && SimLight.HasValue)) pad = Draw.A(Pal.Sub, 70);
            using (var b = new SolidBrush(pad)) using (var path = Draw.Round(box, Math.Min(box.Width, box.Height) / 2)) g.FillPath(b, path);
            using (var p = new Pen(active ? Pal.Accent : Pal.Sub, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
                if (box.Height >= box.Width) g.DrawLine(p, cx, box.Top + box.Height * 0.22f, cx, box.Bottom - box.Height * 0.22f);
                else g.DrawLine(p, box.Left + box.Width * 0.22f, cy, box.Right - box.Width * 0.22f, cy);
            }
        }

        protected void PaintMoon(Graphics g, RectangleF box)
        {
            if (HoverMoon) using (var b = new SolidBrush(Pal.Track)) g.FillEllipse(b, box);
            var inner = RectangleF.Inflate(box, -box.Width * 0.24f, -box.Height * 0.24f);
            Draw.Moon(g, inner, HoverMoon ? Pal.Text : Pal.Sub);
        }

        /// <summary>Taskbar background: the color key (cut out) plus the optional panel and the handle.</summary>
        protected void PaintTaskbarBackground(Graphics g)
        {
            var rect = new RectangleF(0.5f, 0.5f, _size.Width - 1, _size.Height - 1);
            if (!Embedded && !(TaskbarMode && SimLight.HasValue))
                using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, 0, 0, _size.Width, _size.Height);   // keeps the mouse working
            if (C.Bool("tb_background"))
            {
                var r = Tb != null && Tb.Horizontal ? RectangleF.Inflate(rect, -1, -4) : RectangleF.Inflate(rect, -4, -1);
                using (var b = new SolidBrush(Color.FromArgb(255, Pal.Bg))) using (var p = Draw.Round(r, 8 * Dpi)) g.FillPath(b, p);
            }
            if (GripRect.HasValue) PaintGrip(g, GripRect.Value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tick.Dispose(); _z.Dispose(); _tip.Dispose(); _frame?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Menus in the widget's theme.</summary>
    public static class Menus
    {
        /// <summary>A theme color as it looks on the theme's background – see-through colors (Glass) become solid.</summary>
        public static Color Solid(Theme t, Color c)
        {
            var bg = Color.FromArgb(255, t.Bg.R, t.Bg.G, t.Bg.B);
            if (t.Bg.A < 200) bg = Draw.Mix(Color.FromArgb(255, 20, 22, 29), bg, t.Bg.A / 255.0);
            return Draw.Mix(bg, Color.FromArgb(255, c.R, c.G, c.B), c.A / 255.0);
        }

        class Colors : ProfessionalColorTable
        {
            readonly Theme t;
            public Colors(Theme th) { t = th; UseSystemColors = false; }
            Color Solid(Color c) => Menus.Solid(t, c);
            public override Color ToolStripDropDownBackground => Solid(t.Card);
            public override Color ImageMarginGradientBegin => Solid(t.Card);
            public override Color ImageMarginGradientMiddle => Solid(t.Card);
            public override Color ImageMarginGradientEnd => Solid(t.Card);
            public override Color MenuBorder => Solid(t.Border);
            public override Color MenuItemBorder => Solid(t.Accent);
            public override Color MenuItemSelected => Solid(t.Accent);
            public override Color MenuItemSelectedGradientBegin => Solid(t.Accent);
            public override Color MenuItemSelectedGradientEnd => Solid(t.Accent);
            public override Color SeparatorDark => Solid(t.Border);
            public override Color SeparatorLight => Solid(t.Border);
            public override Color CheckBackground => Draw.A(t.Accent, 60);
            public override Color CheckSelectedBackground => Draw.A(t.Accent, 90);
            public override Color CheckPressedBackground => Draw.A(t.Accent, 90);
        }
        class Renderer : ToolStripProfessionalRenderer
        {
            readonly Theme t;
            public Renderer(Theme th) : base(new Colors(th)) { t = th; RoundedEdges = false; }
            // Acrylic: everything drawn with real transparency so the blur shows through (GDI text would vanish)
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                if (!t.Glass) { base.OnRenderToolStripBackground(e); return; }
                e.Graphics.Clear(Color.FromArgb(0, 0, 0, 0));
                using (var b = new SolidBrush(Color.FromArgb(70, 28, 28, 30))) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { if (!t.Glass) base.OnRenderImageMargin(e); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { if (!t.Glass) base.OnRenderToolStripBorder(e); }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!t.Glass) { base.OnRenderMenuItemBackground(e); return; }
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Color.FromArgb(38, 255, 255, 255))) using (var p = Draw.Round(r, 4)) e.Graphics.FillPath(b, p);
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                if (!t.Glass) { base.OnRenderSeparator(e); return; }
                using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) e.Graphics.DrawLine(p, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (t.Glass)
                {
                    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var col = !e.Item.Enabled ? Color.FromArgb(150, 255, 255, 255) : Color.White;
                    using (var b = new SolidBrush(col)) using (var f = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                        e.Graphics.DrawString(e.Text, e.TextFont, b, e.TextRectangle, f);
                    return;
                }
                bool sel = e.Item.Selected && e.Item.Enabled;
                Color on = t.Accent.GetBrightness() > 0.6f ? Color.Black : Color.White;
                e.TextColor = !e.Item.Enabled ? Menus.Solid(t, t.Sub) : sel ? on : Color.FromArgb(255, t.Text);
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Menus.Solid(t, t.Sub); base.OnRenderArrow(e); }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                var r = e.ImageRectangle;
                using (var p = new Pen(Color.FromArgb(255, t.Accent), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(p, new[] { new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f), new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                        new PointF(r.Left + r.Width * 0.82f, r.Top + r.Height * 0.28f) });
                }
            }
        }
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State, Flags, Color, Anim; }
        [StructLayout(LayoutKind.Sequential)] struct CompData { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h, ref CompData d);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        /// <summary>Windows 11 look for a menu: rounded corners; in the Acrylic theme, real acrylic behind it.</summary>
        static void Win11(IntPtr h, Theme t)
        {
            if (!Native.OnWindows || h == IntPtr.Zero) return;
            try { int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4); } catch { }
            if (!t.Glass) return;
            try
            {
                var ap = new AccentPolicy { State = 4, Flags = 2, Color = unchecked((int)0x99231F1F) };   // acrylic blur, dark tint (ABGR)
                int size = Marshal.SizeOf(ap); IntPtr p = Marshal.AllocHGlobal(size);
                try { Marshal.StructureToPtr(ap, p, false); var d = new CompData { Attr = 19, Data = p, Size = size }; SetWindowCompositionAttribute(h, ref d); }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        public static void Style(ToolStrip m, Theme t)
        {
            m.HandleCreated += (s, e) => Win11(m.Handle, t);
            if (m is ToolStripDropDown dd) dd.Opened += (s, e) => Win11(dd.Handle, t);
            m.Renderer = new Renderer(t);
            m.BackColor = Solid(t, t.Card);
            m.ForeColor = Color.FromArgb(255, t.Text);
            m.Font = new Font(Fonts.Ui, 9.5f);
            m.Padding = new Padding(4);
        }
        public static ToolStripMenuItem Item(ToolStripItemCollection parent, string text, Action fn, bool? check = null, bool enabled = true, Image image = null)
        {
            var it = new ToolStripMenuItem(text) { Enabled = enabled, Image = image };
            if (check.HasValue) it.Checked = check.Value;
            if (fn != null) it.Click += (s, e) => fn();
            parent.Add(it);
            return it;
        }
        public static ToolStripMenuItem Sub(ToolStripItemCollection parent, string text, Theme t)
        {
            var it = new ToolStripMenuItem(text);
            Style(it.DropDown, t);
            parent.Add(it);
            return it;
        }
        public static void Sep(ToolStripItemCollection parent) => parent.Add(new ToolStripSeparator());
    }
}
