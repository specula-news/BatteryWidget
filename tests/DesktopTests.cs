using System; using System.Collections.Generic; using System.Drawing; using System.Linq; using System.Windows.Forms; using BatteryWidget;
static class K { static int fail; static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static Cfg Mk(Dictionary<string, object> def, string theme) { var d = Cfg.DeepCopy(def); d["theme"] = theme; return new Cfg(d, "/tmp/cs/x.json") { Persist = false }; }
  [STAThread] static int Main() {
    var bcfg = Mk(BatteryView.Defaults(), "Dark");
    var suite = new Suite(bcfg, true);
    var bat = new BatteryView(bcfg, null) { Suite = suite }; var sys = new SystemView(Mk(SystemView.Defaults(), "Dark"), null) { Suite = suite }; var wx = new WeatherView(Mk(WeatherView.Defaults(), "Dark")) { Suite = suite };
    suite.Battery = bat; suite.System = sys; suite.Weather = wx;
    bat.OnReadings(Demo.Readings()); sys.OnMetrics(new Demo.Hub().ReadAll()); wx.Arrived(Demo.WeatherJson(), new Place { Name = "Stockholm" }, null);
    foreach (var w in new WidgetWindow[] { bat, sys, wx }) w.Show();
    Application.DoEvents();
    // theme: one change, all follow
    suite.ThemeChanged(bat, "theme", "Gold"); bat.C["theme"] = "Gold";
    Check("same theme for all: system + weather follow", sys.C.Str("theme") == "Gold" && wx.C.Str("theme") == "Gold");
    bcfg["link_themes"] = false; suite.ThemeChanged(bat, "theme", "Nord");
    Check("each its own when unlinked", sys.C.Str("theme") == "Gold");
    bcfg["link_themes"] = true;
    // snapping
    sys.MoveWindow(400, 100); var v = sys.ScreenVisual;
    wx.MoveWindow(400, 600); var wv = wx.ScreenVisual;
    var from = wx.Pos; int gapPx = (int)(10 * WidgetWindow.Dpi);
    var target = new Point(0, v.Bottom + 25 - wv.Top);                          // dropped 25 px below the system card's bottom
    var d = suite.Snap(wx, new List<WidgetWindow> { wx }, from, target);
    wx.MoveWindow(from.X + d.X, from.Y + d.Y);
    Check("weather snaps under the system card with an even gap", wx.ScreenVisual.Top - sys.ScreenVisual.Bottom == gapPx && wx.ScreenVisual.Left == sys.ScreenVisual.Left,
      $"gap {wx.ScreenVisual.Top - sys.ScreenVisual.Bottom}, left {wx.ScreenVisual.Left} vs {sys.ScreenVisual.Left}");
    var group = suite.Cluster(sys, false);
    Check("snapped widgets move together", group.Contains(wx) && group.Contains(sys), string.Join(",", group.Select(x => x.Text)));
    Check("Alt = only this one", suite.Cluster(sys, true).Count == 1);
    var far = suite.Snap(wx, new List<WidgetWindow> { wx }, wx.Pos, new Point(300, 0));
    Check("no pull when far away", far == new Point(300, 0) || Math.Abs(far.X - 300) <= 18 * WidgetWindow.Dpi, far.ToString());
    // the update source window at 150 %
    WidgetWindow.Dpi = 1.5f;
    var f = new SourceForm(bat.MenuTheme, ""); f.Show(); Application.DoEvents();
    var btn = f.Controls[0].Controls.OfType<FlowLayoutPanel>().First().Controls.OfType<Button>().First();
    var r = f.RectangleToClient(btn.RectangleToScreen(btn.ClientRectangle));
    Check("update source: Save button fits inside the window at 150 %", f.ClientRectangle.Contains(r), $"{r} in {f.ClientRectangle}");
    f.Close();
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
