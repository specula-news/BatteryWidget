using System; using System.Collections.Generic; using System.Linq; using BatteryWidget;
class FakeDev : IHidDevice {
  public HidInfo Info { get; set; } public Queue<byte[]> Q = new Queue<byte[]>();
  public virtual bool Write(byte[] d) => false;
  public byte[] Read(int ms) { if (Q.Count > 0) return Q.Dequeue(); System.Threading.Thread.Sleep(Math.Min(ms, 20)); return null; }
  public virtual bool SendFeature(byte[] d) => false;
  public virtual byte[] GetFeature(byte rid, int n) => null;
  public byte[] GetInputReport(byte rid, int n) => null;
  public int? StandardBattery(out bool c) { c = false; return Battery; }
  public int? Battery; public bool HasStandardBattery => Battery != null;
  public void Dispose() { }
}
class RongYuan : FakeDev { public int B = 73, C = 1, P = 1;
  public override bool SendFeature(byte[] d) { if (d[1] != 0xF7 || d[8] != 0x08 || d.Length != 65) throw new Exception("bad F7"); return true; }
  public override byte[] GetFeature(byte rid, int n) { var r = new byte[65]; r[2] = (byte)B; r[4] = (byte)C; r[5] = 1; r[6] = 1; r[7] = 1; r[9] = (byte)P; return r; } }
class Compx : FakeDev { public override bool Write(byte[] d) {
  int s = 0; for (int i = 0; i < 16; i++) s += d[i]; if (((s + d[16]) & 0xFF) != 0x55) throw new Exception("checksum");
  var r = new byte[17]; r[0] = 8; r[1] = 4; r[5] = 4; r[6] = 88; r[7] = 1; Q.Enqueue(r); return true; } }
class Razer : FakeDev { byte[] resp; public int Tid = 0x3F;
  public override bool SendFeature(byte[] d) { var o = (byte[])d.Clone(); byte crc = 0; for (int i = 3; i < 89; i++) crc ^= d[i]; if (crc != d[89]) throw new Exception("crc");
    if (d[2] != Tid) o[1] = 5; else { o[1] = 2; if (d[7] == 7 && d[8] == 0x80) o[10] = 200; if (d[7] == 7 && d[8] == 0x84) o[10] = 1; } resp = o; return true; }
  public override byte[] GetFeature(byte rid, int n) => resp; }
class LogiHeadset : FakeDev { public bool On = true;
  public override bool Write(byte[] d) { if (d[0] != 0x11 || d.Length != 20) return false; var r = new byte[20]; r[0] = 0x11; r[1] = d[1]; r[2] = d[2]; r[3] = d[3];
    if (!On) { r[2] = 0xFF; r[3] = d[2]; } else if (d[2] == 0 && (d[3] >> 4) == 1) { r[4] = 4; r[5] = 2; }
    else if (d[2] == 0 && (d[3] >> 4) == 0) { int fid = (d[4] << 8) | d[5]; r[4] = (byte)(fid == 0x1F20 ? 5 : fid == 1 ? 1 : 0); }
    else if (d[2] == 5) { r[4] = 3800 >> 8; r[5] = 3800 & 0xFF; r[6] = 1; }
    Q.Enqueue(r); return true; } }
class HyperX66 : FakeDev { public int Lvl = 80, Chg = 1, Conn = 1, FlakyConnected = 0;
  public override bool Write(byte[] d) { if (d[0] != 0x66 || d.Length != 62) throw new Exception("bad 66"); var r = new byte[62]; r[0] = 0x66;
    if (d[1] == 0x82) { r[1] = 0x0B; r[2] = (byte)(FlakyConnected == 1 ? 0 : Conn); } else if (d[1] == 0x89) { r[1] = 0x0D; if (Conn == 1) { r[2] = 1; r[3] = 1; r[4] = (byte)Lvl; } }
    else { r[1] = 0x0C; r[2] = (byte)Chg; } Q.Enqueue(new byte[] { 0x66, 0x99, 1, 2, 3 }); Q.Enqueue(r); return true; } }
class HyperX0C : FakeDev { public override bool Write(byte[] d) { if (d[0] != 0x0C || d[5] != 6) throw new Exception("bad 0C");
    Q.Enqueue(new byte[] { 0x0D, 2, 3, 0, 0x0A, 1, 0, 0 }); Q.Enqueue(new byte[] { 0x0C, 2, 3, 1, 0, 6, 64, 0 }); return true; } }
class KeyB3 : FakeDev { public byte B20 = 0x80 | 77; public bool Asleep;
  public override bool Write(byte[] d) { if (d[0] != 0xB3 || d[1] != 6) throw new Exception("bad B3"); Q.Enqueue(new byte[64].Select((x, i) => i == 0 ? (byte)0xE2 : x).ToArray());
    if (!Asleep) { var r = new byte[64]; r[0] = 0xB4; r[1] = 6; r[20] = B20; Q.Enqueue(r); } return true; } }
class KeyLink : FakeDev { bool armed; public override bool SendFeature(byte[] d) { if (d[0] != 0x51 || d[1] != 6) throw new Exception("bad 51"); armed = true; return true; }
  public override byte[] GetFeature(byte rid, int n) { if (!armed || rid != 0x51) return null; var r = new byte[21]; r[0] = 0x51; r[1] = 6; r[11] = 63; r[12] = 1; return r; } }
class MchoseNew : FakeDev { bool busy = true;
  public static byte[] Frame(int pct) { var r = new byte[64]; r[0] = 0x4D; r[1] = 1; r[2] = 1; r[3] = 16; r[5] = 9; r[8] = 0x37; r[9] = 0x38; r[10] = 16432 & 0xFF; r[11] = 16432 >> 8;
    r[18] = 1; r[20] = (byte)pct; byte x = 0; for (int i = 2; i < 24; i++) x ^= r[i]; r[24] = x; return r; }
  public override bool Write(byte[] d) { if (d[0] != 0x4D || d[8] != 8) throw new Exception("bad 4D"); Q.Enqueue(new byte[] { 1, 0, 0, 5, 0 });
    if (busy) { busy = false; var f = Frame(73); f[8] = 0xFF; Q.Enqueue(f); } else Q.Enqueue(Frame(73)); return true; } }
class MchoseClassic : FakeDev { public override bool SendFeature(byte[] d) => d[0] == 0x11 && d[1] == 0xF9 && d.Skip(2).All(b => b == 0xFF);
  public override byte[] GetFeature(byte rid, int n) { var p = new byte[] { 0x37, 0x38, 0x18, 0x40, 1, 0, 0, 0, 9, 66, 0 }.Concat(new byte[8]);
    return new byte[] { 0x11, 0xF9 }.Concat(p.Select(b => (byte)(b ^ 0xFF))).ToArray(); } }

class Fake : HidBackend {
  public List<KeyValuePair<HidInfo, Func<FakeDev>>> Devs = new List<KeyValuePair<HidInfo, Func<FakeDev>>>();
  public void Add(int vid, int pid, int up, int u, string prod, Func<FakeDev> mk, int iface = 0, int feat = 65, int outl = 64) =>
    Devs.Add(new KeyValuePair<HidInfo, Func<FakeDev>>(new HidInfo { Path = "p" + Devs.Count, Vid = vid, Pid = pid, UsagePage = up, Usage = u, Product = prod, Interface = iface,
      InLen = 64, OutLen = outl, FeatLen = feat }, mk));
  public override List<HidInfo> Enumerate() => Devs.Select(d => d.Key).ToList();
  public override IHidDevice Open(HidInfo i) { var d = Devs.First(x => x.Key.Path == i.Path).Value(); d.Info = i; return d; }
}
static class T {
  static int fail;
  static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static List<Reading> Run(Provider p, Action<Fake> setup, bool deep = false) { var f = new Fake(); setup(f); HidBackend.Current = f; p.Deep = deep; var r = p.Run(true);
    if (p.Error != null) Console.WriteLine("   error: " + p.Error); return r; }
  static string Show(List<Reading> r) => string.Join("; ", r.Select(x => $"{x.Name}|{x.Kind}|{x.Percent}|{(x.Charging ? "chg" : "")}|{(x.Online ? "on" : "off")}"));
  static int Main() {
    var r = Run(new RongYuanProvider(), f => { f.Add(0x3151, 0x5038, 1, 6, "MonsGeek 2.4G Wireless Keyboard", () => new FakeDev()); f.Add(0x3151, 0x5038, 0xFFFF, 2, "MonsGeek 2.4G Wireless Keyboard", () => new RongYuan(), 2); });
    Check("MonsGeek dongle", Show(r) == "MonsGeek Keyboard|keyboard|73|chg|on", Show(r));
    r = Run(new RongYuanProvider(), f => f.Add(0x1234, 1, 0xFFFF, 2, "Other Keyboard", () => new RongYuan()));
    Check("unknown brand only in deep scan", r.Count == 0 && Run(new RongYuanProvider(), f => f.Add(0x1234, 1, 0xFFFF, 2, "Other Keyboard", () => new RongYuan()), true).Count == 1);
    r = Run(new CompxProvider(), f => f.Add(0x373B, 0x1085, 0xFF02, 2, "ATK Wireless mouse -1k dongle", () => new Compx(), 0, 65, 17));
    Check("ATK/VXE mouse", Show(r) == "ATK mouse|mouse|88|chg|on", Show(r));
    r = Run(new RazerProvider(), f => f.Add(0x1532, 0x7C, 1, 2, "Razer DeathAdder V2 Pro", () => new Razer(), 0, 91));
    Check("Razer (finds the right transaction id)", Show(r) == "Razer DeathAdder V2 Pro|mouse|78|chg|on", Show(r));
    r = Run(new LogitechProvider(), f => f.Add(0x046D, 0x0AC4, 0x0C, 1, "G535 Wireless Gaming Headset", () => new LogiHeadset(), 3, 0, 20));
    Check("Logitech G535 via HID++ discovery", r.Count == 1 && r[0].Name == "G535" && r[0].Kind == "headset" && r[0].Percent >= 40 && r[0].Percent <= 46, Show(r));
    r = Run(new LogitechProvider(), f => f.Add(0x046D, 0x0AC4, 0x0C, 1, "G535 Wireless Gaming Headset", () => new LogiHeadset { On = false }, 3, 0, 20));
    Check("G535 switched off -> offline via known table", Show(r) == "G535|headset||| off".Replace("| off", "|off"), Show(r));
    r = Run(new HyperXProvider(), f => { f.Add(0x03F0, 0x05B7, 0x0C, 1, "HyperX Cloud III Wireless", () => new FakeDev(), 3); f.Add(0x03F0, 0x05B7, 0xFF13, 1, "HyperX Cloud III Wireless", () => new HyperX66(), 3); });
    Check("HyperX Cloud III Wireless", Show(r) == "HyperX Cloud III Wireless|headset|80|chg|on", Show(r));
    r = Run(new HyperXProvider(), f => f.Add(0x03F0, 0x05B7, 0xFF13, 1, "HyperX Cloud III Wireless", () => new HyperX66 { FlakyConnected = 1, Lvl = 79 }));
    Check("HyperX: real battery answer wins over 'not connected'", r.Count == 1 && r[0].Online && r[0].Percent == 79, Show(r));
    r = Run(new HyperXProvider(), f => f.Add(0x03F0, 0x05B7, 0xFF13, 1, "HyperX Cloud III Wireless", () => new HyperX66 { Conn = 0 }));
    Check("HyperX off -> offline", r.Count == 1 && !r[0].Online, Show(r));
    r = Run(new HyperXProvider(), f => f.Add(0x03F0, 0x06BE, 0x01C0, 1, "HyperX Cloud III S Wireless", () => new HyperX0C()));
    Check("HyperX Cloud III S", Show(r) == "HyperX Cloud III S Wireless|headset|64|chg|on", Show(r));
    r = Run(new KeychronProvider(), f => f.Add(0x3434, 0xD028, 0xFFC1, 1, "Keychron Ultra-Link 8K", () => new KeyB3(), 4));
    Check("Keychron Ultra-Link (E2 pushes skipped)", Show(r) == "Keychron mouse|mouse|77|chg|on", Show(r));
    r = Run(new KeychronProvider(), f => { f.Add(0x3434, 0xD030, 1, 2, "Keychron Link ", () => new FakeDev(), 0, 0); f.Add(0x3434, 0xD030, 0x8C, 1, "Keychron Link ", () => new KeyLink(), 1, 21); });
    Check("Keychron Link (feature 0x51)", Show(r) == "Keychron mouse|mouse|63|chg|on", Show(r));
    r = Run(new MchoseProvider(), f => f.Add(0x3837, 0x1018, 0xFF0B, 0x104, "MCHOSE A7 V3", () => new MchoseNew(), 2));
    Check("MCHOSE A7 V3 after a busy answer", Show(r) == "MCHOSE A7 V3|mouse|73||on", Show(r));
    var bad = MchoseNew.Frame(50); bad[24] ^= 1;
    Check("MCHOSE bad checksum refused", MchoseProvider.ParseNew(bad) == null);
    r = Run(new MchoseProvider(), f => f.Add(0x3837, 0x100B, 0xFF01, 1, "2.4G Receiver", () => new MchoseClassic()));
    Check("MCHOSE A7 V2 Pro (classic)", Show(r) == "MCHOSE A7 V2 Pro|mouse|66||on", Show(r));
    r = Run(new HidBatteryProvider(), f => { f.Add(0x248A, 0x8367, 6, 0x20, "2.4G Mouse", () => new FakeDev { Battery = 75 }); f.Add(0x248A, 0x8367, 1, 2, "2.4G Mouse", () => new FakeDev()); });
    Check("standard HID battery", Show(r) == "Mouse|mouse|75||on", Show(r));
    var nodes = new List<BluetoothProvider.Node> {
      new BluetoothProvider.Node { Id = @"BTHENUM\DEV_705A6F66996B\7&1&0&BLUETOOTHDEVICE_705A6F66996B", Name = "EarFun OpenJump" },
      new BluetoothProvider.Node { Id = @"BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\7&2&0&705A6F66996B_C00000000", Name = "EarFun OpenJump Hands-Free AG", Level = 100 },
      new BluetoothProvider.Node { Id = @"BTHLE\DEV_F4EE25AF3A38\7&3&0&F4EE25AF3A38", Name = "MX Master 3S", Level = 55 } };
    var bt = BluetoothProvider.Parse(nodes).Select(x => x.Item2 + "=" + x.Item3).OrderBy(x => x).ToList();
    Check("Bluetooth names + levels", string.Join(",", bt) == "EarFun OpenJump=100,MX Master 3S=55", string.Join(",", bt));
    foreach (var kv in new[] { "G502 X LIGHTSPEED=mouse", "G535=headset", "MonsGeek Keyboard=keyboard", "Xbox Wireless Controller=controller", "EarFun OpenJump=headset", "MCHOSE A7 V3=mouse" }) {
      var p = kv.Split('='); Check("kind " + p[0], Prov.GuessKind(p[0]) == p[1], Prov.GuessKind(p[0])); }
    var d = Prov.Dedupe(new[] { new Reading("logitech:G535", "G535", "headset", 42, source: "ghub"), new Reading("logitech:G535", "G535", "headset", 44), new Reading("headset:Logitech G535", "Logitech G535", "headset", 41) });
    Check("dedupe keeps the first source", d.Count == 1 && d[0].Source == "ghub");
    GHubProvider.Port = 19010;
    r = new GHubProvider().Run(true);
    Check("G HUB websocket", Show(r) == "G502 X LIGHTSPEED|mouse|40||on; G535|headset|42|chg|on", Show(r));
    GHubProvider.Port = 19999; var g = new GHubProvider(); r = g.Run(true);
    Check("G HUB not running is quiet", r.Count == 0 && g.Error == null && g.Note.Contains("not running"), g.Note);
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED");
    return fail;
  }
}
