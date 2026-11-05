// Updater.cs – a small confirmation pop-up (toast) and the self-updater (GitHub releases, tags vX.Y.Z).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    /// <summary>A pop-up in the app's look that fades out by itself (shows even when Windows hides notifications).</summary>
    public class Toast : Form
    {
        static Toast _current;
        readonly Bitmap _frame;
        readonly System.Windows.Forms.Timer _t = new System.Windows.Forms.Timer { Interval = 30 };
        int _ms; readonly int _life;
        double _alpha;

        Toast(string icon, string title, string text, Theme pal, int lifeMs)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            _life = lifeMs;
            float s = WidgetWindow.Dpi, M = 12 * s;
            Font ft = Fonts.Get(14 * s, FontStyle.Regular, false, true), fb = Fonts.Get(12.5f * s);
            float tw, bw;
            using (var b = new Bitmap(1, 1)) using (var g = Graphics.FromImage(b)) { tw = Fonts.Width(g, title, ft); bw = text == "" ? 0 : Fonts.Width(g, text, fb); }
            float W = Math.Max(tw, bw) + 64 * s + 2 * M, H = (text == "" ? 52 : 70) * s + 2 * M;
            _frame = new Bitmap((int)W, (int)H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(_frame))
            {
                Draw.Hq(g);
                var card = new RectangleF(M, M, W - 2 * M, H - 2 * M);
                for (int i = 0; i < 8; i++) { float gr = (8 - i) * 1.3f * s; using (var b = new SolidBrush(Color.FromArgb(8, 0, 0, 0))) using (var p = Draw.Round(new RectangleF(card.X - gr, card.Y - gr + 3 * s, card.Width + 2 * gr, card.Height + 2 * gr), 14 * s + gr)) g.FillPath(b, p); }
                using (var p = Draw.Round(card, 14 * s))
                {
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max((int)pal.Bg.A, 235), pal.Bg))) g.FillPath(b, p);
                    using (var pen = new Pen(pal.Metal ? Draw.A(pal.Accent, 150) : pal.Border, 1.2f * s)) g.DrawPath(pen, p);
                }
                var ib = new RectangleF(card.X + 14 * s, card.Y + card.Height / 2 - 15 * s, 30 * s, 30 * s);
                using (var b = new SolidBrush(Draw.A(pal.Accent, 46))) g.FillEllipse(b, ib);
                var ii = RectangleF.Inflate(ib, -7 * s, -7 * s);
                if (icon == "moon") Draw.Moon(g, ii, pal.Accent);
                else if (icon.StartsWith("wx:")) WxIcons.Paint(g, int.Parse(icon.Substring(3)), true, ii, pal);
                else Draw.SysIcon(g, icon, ii, pal.Accent);
                float tx = ib.Right + 12 * s;
                float ty = text == "" ? card.Y + card.Height / 2 + Fonts.Ascent(ft) / 2 - 2 * s : card.Y + card.Height / 2 - 2 * s;
                using (var b = new SolidBrush(pal.Text)) Fonts.Text(g, title, ft, b, tx, ty);
                if (text != "") using (var b = new SolidBrush(pal.Sub)) Fonts.Text(g, text, fb, b, tx, ty + 20 * s);
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.Right - _frame.Width - (int)(12 * s), wa.Bottom - _frame.Height - (int)(12 * s), _frame.Width, _frame.Height);
            _t.Tick += (o, e) => Step();
            Click += (o, e) => { _ms = Math.Max(_ms, _life); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST); return cp; }
        }
        protected override bool ShowWithoutActivation => true;

        void Push()
        {
            if (!Native.OnWindows || !IsHandleCreated) return;
            IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
            IntPtr hb = _frame.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hb);
            try
            {
                var size = new SIZE(_frame.Width, _frame.Height); var src = new POINT(0, 0); var pos = new POINT(Left, Top);
                var blend = new BLENDFUNCTION { SourceConstantAlpha = (byte)Math.Round(255 * _alpha), AlphaFormat = 1 };
                Native.UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally { Native.SelectObject(mem, old); Native.DeleteObject(hb); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        void Step()
        {
            _ms += _t.Interval;
            _alpha = _ms < 160 ? _ms / 160.0 : _ms > _life ? Math.Max(0, 1 - (_ms - _life) / 400.0) : 1;
            Push();
            if (_ms > _life + 420) { _t.Stop(); Close(); }
        }

        /// <summary>icon: a system icon name, "moon", or "wx:CODE" for a weather icon.</summary>
        public static void Show(string icon, string title, string text, Theme pal, int lifeMs = 3200)
        {
            try
            {
                if (_current != null && !_current.IsDisposed) _current.Close();
                var t = new Toast(icon, title, text ?? "", pal, lifeMs);
                _current = t;
                t.Show(); t._alpha = 0; t.Push(); t._t.Start();
            }
            catch { }
        }

        public static Bitmap Preview(string icon, string title, string text, Theme pal) { using (var t = new Toast(icon, title, text, pal, 1000)) return (Bitmap)t._frame.Clone(); }

        protected override void Dispose(bool d) { if (d) { _t.Dispose(); _frame.Dispose(); } base.Dispose(d); }
    }

    /// <summary>Updates from GitHub releases. A release is tagged vX.Y.Z and carries BatteryWidget.exe
    /// (GitHub records its SHA-256; a BatteryWidget.exe.sha256 file works too). Nothing is installed unless it matches.</summary>
    public static class Updater
    {
        public const string DefaultRepo = "";            // "owner/repo" – filled in by the GitHub build from the repository it runs in
        public class Release { public string Tag, Version, Url, Sha256, Page, Notes; }

        public static string Repo(Cfg c) { var r = c.Str("update_repo", ""); return r != "" ? r : DefaultRepo; }

        public static int[] Parse(string v)
        {
            var parts = (v ?? "").TrimStart('v', 'V').Split('-')[0].Split('.');
            var n = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
            return n;
        }
        public static bool Newer(string candidate, string current)
        {
            int[] a = Parse(candidate), b = Parse(current);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        static WebClient Web() { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; var w = new WebClient(); w.Headers["User-Agent"] = "BatteryWidget-updater"; return w; }

        public static Release FromJson(string json)
        {
            var rel = Cfg.FromJson(json) as Dictionary<string, object>;
            if (rel == null || (rel.TryGetValue("draft", out var d) && d is bool db && db)) return null;
            var r = new Release { Tag = Cfg.S(rel, "tag_name", ""), Page = Cfg.S(rel, "html_url", ""), Notes = Cfg.S(rel, "body", "") };
            r.Version = r.Tag.TrimStart('v', 'V');
            var assets = (rel.TryGetValue("assets", out var a) ? a as List<object> : null) ?? new List<object>();
            foreach (var o in assets.OfType<Dictionary<string, object>>())
            {
                string name = Cfg.S(o, "name", "");
                if (name.Equals("BatteryWidget.exe", StringComparison.OrdinalIgnoreCase))
                {
                    r.Url = Cfg.S(o, "browser_download_url");
                    string dg = Cfg.S(o, "digest", "");
                    if (dg.StartsWith("sha256:")) r.Sha256 = dg.Substring(7).ToLowerInvariant();
                }
            }
            if (r.Sha256 == null)
            {
                var sha = assets.OfType<Dictionary<string, object>>().FirstOrDefault(o => Cfg.S(o, "name", "").Equals("BatteryWidget.exe.sha256", StringComparison.OrdinalIgnoreCase));
                if (sha != null) try { using (var w = Web()) r.Sha256 = w.DownloadString(Cfg.S(sha, "browser_download_url")).Trim().Split(' ', '\t')[0].ToLowerInvariant(); } catch { }
            }
            return r;
        }

        public static Release Latest(string repo)
        {
            if (string.IsNullOrEmpty(repo)) return null;
            using (var w = Web()) return FromJson(w.DownloadString($"https://api.github.com/repos/{repo}/releases/latest"));
        }

        /// <summary>Downloads, checks the SHA-256, swaps the exe (a running exe can be renamed) and starts the new one.</summary>
        public static void Install(Release r, Action<string> say)
        {
            if (r.Url == null) throw new Exception("this release has no BatteryWidget.exe");
            if (string.IsNullOrEmpty(r.Sha256)) throw new Exception("this release has no checksum – not installed");
            string exe = WinUtil.ExePath, dir = Path.GetDirectoryName(exe), fresh = exe + ".new", old = exe + ".old";
            say?.Invoke("Downloading " + r.Tag + "…");
            using (var w = Web()) w.DownloadFile(r.Url, fresh);
            string got;
            using (var sha = SHA256.Create()) using (var f = File.OpenRead(fresh)) got = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            if (got != r.Sha256) { File.Delete(fresh); throw new Exception("the download doesn't match its checksum – not installed"); }
            say?.Invoke("Installing…");
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
        }

        public static void CleanUp()
        {
            try { var old = WinUtil.ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { var fresh = WinUtil.ExePath + ".new"; if (File.Exists(fresh)) File.Delete(fresh); } catch { }
        }
    }

    public class UpdateForm : ThemedForm
    {
        public UpdateForm(Suite suite, Updater.Release r, Theme t) : base(t)
        {
            Text = "Update – Battery Widget";
            ClientSize = new Size(520, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; TopMost = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl($"Battery Widget {r.Version} is available", false, 14, true));
            var status = Lbl($"You have {Program.Version}. Updating takes a few seconds and restarts the app.", true);
            root.Controls.Add(status);
            var notes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                BackColor = CardSolid, ForeColor = ForeColor, Text = string.IsNullOrWhiteSpace(r.Notes) ? "(no release notes)" : r.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n") };
            root.Controls.Add(notes);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var now = Btn("Update now", true); var later = Btn("Later"); var skip = Btn("Skip this version");
            bar.Controls.AddRange(new Control[] { now, later, skip });
            root.Controls.Add(bar);
            Controls.Add(root);
            later.Click += (s, e) => Close();
            skip.Click += (s, e) => { suite.Cfg["update_skip"] = r.Version; suite.Cfg.Save(); Close(); };
            now.Click += (s, e) =>
            {
                now.Enabled = later.Enabled = skip.Enabled = false;
                new Thread(() =>
                {
                    string err = null;
                    try { Updater.Install(r, m => BeginInvoke((Action)(() => status.Text = m))); } catch (Exception ex) { err = ex.Message; }
                    BeginInvoke((Action)(() =>
                    {
                        if (err == null) { status.Text = "Restarting…"; suite.RestartAll(); return; }
                        status.Text = "Couldn't update: " + err + ". Opening the download page instead.";
                        try { Process.Start(r.Page); } catch { }
                        later.Enabled = true; later.Text = "Close";
                    }));
                }) { IsBackground = true }.Start();
            };
        }
    }
}
