using System; using System.Collections.Generic; using System.Drawing; using System.Linq; using System.Windows.Forms; using BatteryWidget;
static class V { static int fail; static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  [STAThread] static int Main() {
    var hid = new Reading("logitech:PRO 2 LIGHTSPEED", "PRO 2 LIGHTSPEED", "mouse", 86, source: "logitech", detail: "046d:c54d · HID++ 0x1004");
    var gh = new Reading("logitech:PRO X SUPERLIGHT 2", "PRO X SUPERLIGHT 2", "mouse", 87, source: "ghub", detail: "G HUB · dev1");
    var kb = new Reading("usb:3151:5038", "MonsGeek Keyboard", "keyboard", 96, source: "rongyuan");
    var d = Prov.Dedupe(new[] { gh, hid, kb });
    Check("your case: one mouse, not two (G HUB + HID++)", d.Count(r => r.Kind == "mouse") == 1 && d.First(r => r.Kind == "mouse").Key == hid.Key && d.First(r => r.Kind == "mouse").Percent == 87,
      string.Join(", ", d.Select(r => r.Key + "=" + r.Percent)));
    Check("the G HUB name is remembered as the same device", Prov.Aliases.TryGetValue(gh.Key, out var to) && to == hid.Key);
    var two = Prov.Dedupe(new[] { new Reading("logitech:G502 X", "G502 X LIGHTSPEED", "mouse", 40, source: "ghub"), new Reading("logitech:G502 X PLUS", "G502 X PLUS", "mouse", 40, source: "logitech"),
                                  new Reading("logitech:G305", "G305", "mouse", 70, source: "logitech"), new Reading("logitech:G305 LIGHTSPEED", "G305 LIGHTSPEED Wireless", "mouse", 70, source: "ghub") });
    Check("two different Logitech mice stay two", two.Count == 2, string.Join(", ", two.Select(r => r.Name)));
    // the widget drops the extra entry it had already saved
    var cfg = new Cfg(Cfg.DeepCopy(BatteryView.Defaults()), "/tmp/cs/x.json") { Persist = false };
    var w = new BatteryView(cfg, null);
    w.OnReadings(new List<Reading> { gh.Copy() });                        // an earlier session where only G HUB's name was seen
    Prov.Aliases.Clear(); Prov.Aliases[gh.Key] = hid.Key;
    w.OnReadings(Prov.Dedupe(new[] { gh.Copy(), hid.Copy() }));
    Check("only one mouse icon in the widget", w.Items.Count(r => r.Kind == "mouse") == 1, string.Join(", ", w.Items.Select(r => r.Key)));
    // start in the middle, always on screen
    var wa = Screen.PrimaryScreen.WorkingArea;
    var off = w.KeepOnScreen(new Point(wa.Right + 500, wa.Bottom + 300));
    Check("a widget saved off-screen comes back on screen", wa.Contains(new Rectangle(off, w.LayoutSize)), off.ToString());
    var suite = new Suite(cfg, true); suite.Battery = w;
    var spot = suite.DefaultSpot(w);
    Check("first start: near the middle of the screen", Math.Abs(spot.X + w.LayoutSize.Width / 2 - (wa.Left + wa.Width / 2)) < 5 && Math.Abs(spot.Y - (wa.Top + wa.Height / 2)) < 300 * WidgetWindow.Dpi, spot.ToString());
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
