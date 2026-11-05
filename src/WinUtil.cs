// WinUtil.cs – the taskbar, the screen-off timer, autostart, administrator rights and ending the process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryWidget
{
    public class TaskbarInfo
    {
        public string Edge = "bottom";       // bottom | top | left | right
        public Rectangle Rect;               // screen pixels
        public bool AutoHide;
        public bool Horizontal => Edge == "bottom" || Edge == "top";
        public int Thickness => Horizontal ? Rect.Height : Rect.Width;
    }

    public static class WinUtil
    {
        static readonly string[] Edges = { "left", "top", "right", "bottom" };
        public static TaskbarInfo SimTaskbar;          // for previews

        public static TaskbarInfo Taskbar()
        {
            if (SimTaskbar != null) return SimTaskbar;
            var t = new TaskbarInfo();
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            if (Native.OnWindows)
            {
                var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                if (Native.SHAppBarMessage(5, ref abd) != IntPtr.Zero)
                {
                    t.Edge = abd.uEdge < 4 ? Edges[abd.uEdge] : "bottom";
                    t.Rect = Rectangle.FromLTRB(abd.rc.Left, abd.rc.Top, abd.rc.Right, abd.rc.Bottom);
                    var st = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                    t.AutoHide = (Native.SHAppBarMessage(4, ref st).ToInt64() & 1) != 0;
                    t.Rect.Intersect(scr);
                    if (t.Rect.Width > 0 && t.Rect.Height > 0) return t;
                }
            }
            t.Rect = new Rectangle(scr.Left, scr.Bottom - 48, scr.Width, 48);
            return t;
        }

        public static IntPtr TrayHwnd => Native.OnWindows ? Native.FindWindow("Shell_TrayWnd", null) : IntPtr.Zero;

        public static Rectangle? WindowRect(IntPtr h)
        {
            RECT r;
            if (h == IntPtr.Zero || !Native.GetWindowRect(h, out r) || r.Width <= 0 || r.Height <= 0) return null;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        public static Rectangle? TrayArea()
        {
            var tray = TrayHwnd;
            return tray == IntPtr.Zero ? null : WindowRect(Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null));
        }

        public static bool TaskbarVisible()
        {
            var r = WindowRect(TrayHwnd);
            if (r == null) return true;
            var scr = System.Windows.Forms.Screen.FromRectangle(r.Value).Bounds;
            var vis = Rectangle.Intersect(r.Value, scr);
            return vis.Width > 8 && vis.Height > 8;
        }

        // --- the color key: our background is painted in the taskbar's own color and cut out ---------
        public static Color? SampleTaskbarColor()
        {
            var r = WindowRect(TrayHwnd);
            if (r == null) return null;
            var rc = r.Value;
            bool horiz = rc.Width >= rc.Height;
            var cols = new List<Color>();
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                foreach (var f in new[] { 0.02, 0.12, 0.25, 0.38, 0.62, 0.75, 0.88, 0.97 })
                {
                    int x = horiz ? (int)(rc.Left + f * rc.Width) : rc.Left + Math.Max(3, rc.Width / 12);
                    int y = horiz ? rc.Top + Math.Max(3, rc.Height / 12) : (int)(rc.Top + f * rc.Height);
                    uint c = Native.GetPixel(dc, x, y);
                    if (c != 0xFFFFFFFF) cols.Add(Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF)));
                }
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
            if (cols.Count == 0) return null;
            return cols.OrderBy(c => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B).ElementAt(cols.Count / 2);
        }

        public static void SetColorKey(IntPtr h, Color key, byte alpha)
        {
            long ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_LAYERED) == 0) Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
            Native.SetLayeredWindowAttributes(h, (uint)(key.R | (key.G << 8) | (key.B << 16)), Math.Max((byte)30, alpha),
                Native.LWA_COLORKEY | (alpha < 255 ? Native.LWA_ALPHA : 0));
        }

        public static bool IsLayered(IntPtr h) => (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_LAYERED) != 0;

        // --- z-order ------------------------------------------------------------------------------
        public static void SetTopmost(IntPtr h) =>
            Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

        public static bool CoveredByTaskbar(IntPtr h)
        {
            var tray = TrayHwnd;
            if (tray == IntPtr.Zero) return false;
            IntPtr w = Native.GetWindow(h, 3);                         // GW_HWNDPREV
            for (int i = 0; i < 600 && w != IntPtr.Zero; i++)
            {
                if (w == tray && Native.IsWindowVisible(w)) return true;
                w = Native.GetWindow(w, 3);
            }
            return false;
        }

        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "progman", "workerw",
            "shell_traywnd", "shell_secondarytraywnd", "windows.ui.core.corewindow", "xamlexplorerhostislandwindow",
            "toplevelwindowforoverflowxamlisland", "notifyiconoverflowwindow", "multitaskingviewframe", "tasklistthumbnailwnd",
            "foregroundstaging", "taskswitcherwnd" };
        static readonly HashSet<string> ShellExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "explorer", "startmenuexperiencehost",
            "searchhost", "searchapp", "shellexperiencehost", "textinputhost", "lockapp", "widgets" };

        /// <summary>A game or video covers the whole taskbar screen (Start menu and search never count).</summary>
        public static bool FullscreenApp()
        {
            if (!Native.OnWindows) return false;
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                var sb = new StringBuilder(256);
                Native.GetClassName(h, sb, 256);
                if (ShellClasses.Contains(sb.ToString())) return false;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                try { if (ShellExes.Contains(Process.GetProcessById((int)pid).ProcessName)) return false; } catch { }
                var r = WindowRect(h);
                if (r == null) return false;
                var scr = System.Windows.Forms.Screen.FromRectangle(r.Value);
                if (scr != System.Windows.Forms.Screen.PrimaryScreen) return false;
                return r.Value.Left <= scr.Bounds.Left && r.Value.Top <= scr.Bounds.Top && r.Value.Right >= scr.Bounds.Right && r.Value.Bottom >= scr.Bounds.Bottom;
            }
            catch { return false; }
        }

        // --- the screen-off timer (Settings › System › Power › "Turn off my screen after") ------------
        public static readonly int[] ScreenSeconds = { 60, 120, 180, 300, 600, 900, 1200, 1500, 1800, 2700, 3600, 7200, 10800, 14400, 18000, 0 };
        public static string ScreenLabel(int s) => s == 0 ? "Never" : s < 3600 ? $"{s / 60} minute{(s == 60 ? "" : "s")}" : $"{s / 3600} hour{(s == 3600 ? "" : "s")}";
        static Guid SubVideo = new Guid("7516b95f-f776-4464-8c53-06167f40cc99"), VideoIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

        public static int? ScreenTimeout()
        {
            if (!Native.OnWindows) return null;
            IntPtr scheme;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out scheme) != 0) return null;
            try { uint v; return Native.PowerReadACValueIndex(IntPtr.Zero, scheme, ref SubVideo, ref VideoIdle, out v) == 0 ? (int)v : (int?)null; }
            finally { Native.LocalFree(scheme); }
        }

        public static bool SetScreenTimeout(int seconds)
        {
            if (!Native.OnWindows) return false;
            IntPtr scheme;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out scheme) != 0) return false;
            try
            {
                if (Native.PowerWriteACValueIndex(IntPtr.Zero, scheme, ref SubVideo, ref VideoIdle, (uint)seconds) != 0) return false;
                Native.PowerWriteDCValueIndex(IntPtr.Zero, scheme, ref SubVideo, ref VideoIdle, (uint)seconds);
                return Native.PowerSetActiveScheme(IntPtr.Zero, scheme) == 0;
            }
            finally { Native.LocalFree(scheme); }
        }

        public static void ScreenOffNow() { if (Native.OnWindows) Native.PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2)); }

        // --- start with Windows: Run key normally, a logon task with highest privileges as administrator -----
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

        public static string RunKeyGet(string name)
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(name) as string; } catch { return null; }
        }
        public static void RunKeySet(string name, string cmd)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (cmd != null) k.SetValue(name, cmd); else if (k.GetValue(name) != null) k.DeleteValue(name);
                }
            }
            catch { }
        }

        static Tuple<int, string> Schtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return Tuple.Create(p.ExitCode, o);
            }
        }

        public static string TaskGet(string name)
        {
            if (!Native.OnWindows) return null;
            try { var r = Schtasks($"/Query /TN \"{name}\" /XML"); return r.Item1 == 0 ? r.Item2 : null; } catch { return null; }
        }

        public static void TaskCreate(string name, string command, string description)
        {
            string user = string.Join("\\", new[] { Environment.UserDomainName, Environment.UserName }.Where(s => !string.IsNullOrEmpty(s)));
            Func<string, string> e = System.Security.SecurityElement.Escape;
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                $"<RegistrationInfo><Description>{e(description)}</Description></RegistrationInfo>\n" +
                $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{e(user)}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\n" +
                $"<Principals><Principal id=\"Author\"><UserId>{e(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable>" +
                "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle>" +
                "<WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\n" +
                $"<Actions Context=\"Author\"><Exec><Command>{e(command)}</Command></Exec></Actions>\n</Task>\n";
            string path = Path.Combine(Path.GetTempPath(), name + "_task.xml");
            File.WriteAllText(path, xml, Encoding.Unicode);
            try
            {
                var r = Schtasks($"/Create /TN \"{name}\" /XML \"{path}\" /F");
                if (r.Item1 != 0) throw new Exception(r.Item2.Trim());
            }
            finally { try { File.Delete(path); } catch { } }
        }

        public static void TaskDelete(string name) { try { Schtasks($"/Delete /TN \"{name}\" /F"); } catch { } }

        public static bool AutostartOn(string name) => TaskGet(name) != null || RunKeyGet(name) != null;

        public static void SetAutostart(string name, bool on)
        {
            string cmd = "\"" + ExePath + "\"";
            if (on && Native.IsAdmin) { TaskCreate(name, ExePath, "Starts Battery Widget when you log on."); RunKeySet(name, null); }
            else if (on) RunKeySet(name, cmd);
            else { RunKeySet(name, null); if (Native.IsAdmin) TaskDelete(name); }
        }

        /// <summary>"Start with Windows" follows the copy you actually run, using the right mechanism for it.</summary>
        public static void HealAutostart(string name)
        {
            try
            {
                string task = TaskGet(name), reg = RunKeyGet(name), cmd = "\"" + ExePath + "\"";
                if ((task != null && task.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) < 0) || (reg != null && reg != cmd) || (reg != null && Native.IsAdmin))
                    SetAutostart(name, true);
            }
            catch { }
        }

        public static bool RelaunchAsAdmin(string args)
        {
            try { Process.Start(new ProcessStartInfo(ExePath, args) { UseShellExecute = true, Verb = "runas" }); return true; }
            catch { return false; }                                         // the user said no
        }

        /// <summary>Ends the process at once – nothing in a driver library can keep it alive.</summary>
        public static void HardExit(int code = 0)
        {
            try { if (Native.OnWindows) Native.TerminateProcess(Native.GetCurrentProcess(), (uint)code); } catch { }
            Environment.Exit(code);
        }
    }
}
