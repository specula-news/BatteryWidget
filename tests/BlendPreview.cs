using System; using System.Collections.Generic; using System.Drawing; using System.Linq; using System.Windows.Forms; using BatteryWidget;
static class B { [STAThread] static void Main() {
  var wall = new Bitmap("/mnt/user-data/uploads/1793481872505_image.png"); Wallpaper.Override = wall;
  Func<Dictionary<string, object>, string, bool, Cfg> mk = (def, th, blend) => { var d = Cfg.DeepCopy(def); d["theme"] = th; d["blend"] = blend; return new Cfg(d, "/tmp/cs/x.json") { Persist = false }; };
  var outImg = new Bitmap(wall);
  using (var g = Graphics.FromImage(outImg)) {
    foreach (var spec in new[] { new object[] { "Gold", true, 1590 }, new object[] { "Twilight", true, 2080 } }) {
      string th = (string)spec[0]; int x0 = (int)spec[2];
      var sys = new SystemView(mk(SystemView.Defaults(), th, true), null); var hub = new Demo.Hub();
      var ms = hub.ReadAll(); sys.C["items"] = Fmt.Suggest(ms).Select(k => (object)new Dictionary<string, object> { { "key", k } }).ToList();
      for (int i = 0; i < 60; i++) { hub.Offset = i * 0.7; sys.OnMetrics(hub.ReadAll()); }
      var wx = new WeatherView(mk(WeatherView.Defaults(), th, true)); wx.Arrived(Demo.WeatherJson(), new Place { Name = "Stockholm, Sweden" }, null);
      var bat = new BatteryView(mk(BatteryView.Defaults(), th, true), null); bat.OnReadings(Demo.Readings());
      wx.MoveWindow(x0, 140); var wv = wx.ScreenVisual;
      sys.MoveWindow(x0, 0); sys.MoveWindow(x0, wv.Bottom + 10 - (sys.ScreenVisual.Top - 0));
      bat.MoveWindow(sys.ScreenVisual.Left - 0, sys.ScreenVisual.Bottom + 10);
      foreach (var w in new WidgetWindow[] { wx, sys, bat }) { w.Relayout(); using (var b = w.RenderToBitmap()) g.DrawImage(b, w.Pos.X, w.Pos.Y); }
    } }
  outImg.Save("/home/claude/blend_full.png"); } }
