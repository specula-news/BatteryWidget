using System; using System.Collections.Generic; using System.Linq; using BatteryWidget;
class Echo : FakeDev { readonly Func<byte[], byte[][]> f; public Echo(Func<byte[], byte[][]> fn) { f = fn; }
  public override bool Write(byte[] d) { foreach (var r in f(d)) if (r != null) Q.Enqueue(r); return true; } }
static class H {
  static int fail;
  static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static byte[] B(params int[] v) => v.Select(x => (byte)x).ToArray();
  static string Show(List<Reading> r) => string.Join("; ", r.Select(x => $"{x.Name}|{x.Percent}|{(x.Charging ? "chg" : "")}|{(x.Online ? "on" : "off")}"));
  static List<Reading> Run(int vid, int pid, int page, int iface, string prod, Func<byte[], byte[][]> dev) {
    var f = new Fake(); f.Add(vid, pid, page, 1, prod, () => new Echo(dev), iface, 0, 64); HidBackend.Current = f;
    var p = new HeadsetProvider(); var r = p.Run(true); if (p.Error != null) Console.WriteLine("  error " + p.Error); return r; }
  static int Main() {
    var r = Run(0x1038, 0x12ad, 0xFFC0, 5, "SteelSeries Arctis 7", d => d[0] == 6 && d[1] == 0x18 ? new[] { B(0x06, 0x18, 72, 0, 0, 0, 0, 0) } : new byte[0][]);
    Check("Arctis 7", Show(r) == "SteelSeries Arctis 7|72||on", Show(r));
    r = Run(0x1038, 0x22A1, 0xFFC0, 3, "Arctis Nova 7", d => d[1] == 0xb0 ? new[] { B(0xb0, 0, 80, 1, 0, 0) } : new byte[0][]);
    Check("Arctis Nova 7 (percent, charging)", Show(r) == "SteelSeries Arctis Nova 7|80|chg|on", Show(r));
    r = Run(0x1038, 0x2202, 0xFFC0, 3, "Arctis Nova 7", d => d[1] == 0xb0 ? new[] { B(0xb0, 0, 3, 3, 0, 0) } : new byte[0][]);
    Check("Arctis Nova 7 (first models: 0-4 steps)", Show(r) == "SteelSeries Arctis Nova 7|75||on", Show(r));
    r = Run(0x1038, 0x22A1, 0xFFC0, 3, "Arctis Nova 7", d => d[1] == 0xb0 ? new[] { B(0xb0, 0, 0, 0, 0, 0) } : new byte[0][]);
    Check("Arctis Nova 7 headset off -> offline", r.Count == 1 && !r[0].Online, Show(r));
    r = Run(0x03F0, 0x018b, 0xFF90, 0, "HyperX Cloud II Wireless", d => d[3] == 2 ? new[] { B(6, 0xff, 0xbb, 2, 0, 0x0f, 0xa0, 67) } : d[3] == 3 ? new[] { B(6, 0xff, 0xbb, 3, 1, 0, 0, 0) } : new byte[0][]);
    Check("HyperX Cloud II Wireless", Show(r) == "HyperX Cloud II Wireless|67|chg|on", Show(r));
    r = Run(0x03F0, 0x098d, 0xFF00, 0, "HyperX Cloud Alpha Wireless", d => d[0] != 0x21 ? new byte[0][] : d[2] == 3 ? new[] { B(0x21, 0xbb, 3, 2) } : d[2] == 0x0c ? new[] { B(0x21, 0xbb, 0x0c, 0) } : new[] { B(0x21, 0xbb, 0x0b, 91) });
    Check("HyperX Cloud Alpha Wireless", Show(r) == "HyperX Cloud Alpha Wireless|91||on", Show(r));
    r = Run(0x1B1C, 0x0a14, 0xFF00, 0, "CORSAIR VOID PRO Wireless Gaming Headset", d => d[0] == 0xC9 ? new[] { B(0x64, 0, 0x80 | 55, 0, 1) } : new byte[0][]);
    Check("Corsair Void (mic-up bit ignored)", Show(r) == "CORSAIR VOID PRO Gaming Headset|55||on", Show(r));
    var f = new Fake();
    f.Add(0x1B1C, 0x1b49, 0xFFC2, 1, "CORSAIR K70 RGB MK.2 Mechanical Gaming Keyboard", () => new FakeDev(), 1);
    f.Add(0x046D, 0xc232, 1, 6, "", () => new FakeDev());
    f.Add(0x046D, 0xc54d, 0xFF00, 2, "USB Receiver", () => new FakeDev(), 2);
    HidBackend.Current = f;
    var res = Scan.All(new List<Provider>(), false, null);
    var shown = res.Unclaimed.Select(g => g.VidPid).ToList();
    Check("wired K70 and the unnamed device are no longer 'needs a look'", shown.SequenceEqual(new[] { "046d:c54d" }), string.Join(",", shown));
    Check("tip no longer tells you to install software", !res.Unclaimed[0].Hint.Contains("G HUB") && !res.Unclaimed[0].Hint.Contains("HeadsetControl"), res.Unclaimed[0].Hint);
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
