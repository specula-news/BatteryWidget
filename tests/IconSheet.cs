using System; using System.Drawing; using BatteryWidget;
static class S { static void Main() {
  var bmp = new Bitmap(1060, 260); using (var g = Graphics.FromImage(bmp)) { Draw.Hq(g); g.Clear(Color.FromArgb(21, 22, 28));
    int x = 10; foreach (var d in Draw.DeviceIcons) { Draw.DeviceIcon(g, d[0], new RectangleF(x, 10, 40, 40), Color.FromArgb(237, 239, 245), int.Parse(d[1])); x += 62; }
    x = 10; foreach (var d in Draw.SysIcons) { Draw.SysIcon(g, d[0], new RectangleF(x, 70, 40, 40), Color.FromArgb(217, 180, 95)); x += 62; }
    Draw.Moon(g, new RectangleF(10, 130, 40, 40), Color.FromArgb(200, 200, 210)); Draw.Moon(g, new RectangleF(70, 140, 14, 14), Color.White);
    Draw.Bolt(g, new RectangleF(110, 130, 24, 40), Color.FromArgb(52, 211, 153));
    using (var a = Draw.AppIcon(96)) g.DrawImage(a, 160, 125); x = 270; foreach (var s in new[] { 16, 24, 32, 48 }) { using (var a = Draw.AppIcon(s)) g.DrawImage(a, x, 150); x += s + 14; }
    using (var f = Fonts.Get(16, FontStyle.Regular, true, true)) { }
    Fonts.Text(g, "82%  CPU: 5.4 GHz  58°C", Fonts.Get(18, FontStyle.Regular, true, true), Brushes.White, 420, 160);
    using (var gb = Draw.Gold(new RectangleF(420, 180, 200, 24))) Fonts.Text(g, "52°C gold text", Fonts.Get(20, FontStyle.Regular, true, true), gb, 420, 205);
  } bmp.Save("/home/claude/cs_icons.png"); Console.WriteLine("ui font: " + Fonts.Ui); } }
