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
class Fake : HidBackend {
  public List<KeyValuePair<HidInfo, Func<FakeDev>>> Devs = new List<KeyValuePair<HidInfo, Func<FakeDev>>>();
  public void Add(int vid, int pid, int up, int u, string prod, Func<FakeDev> mk, int iface = 0, int feat = 65, int outl = 64) =>
    Devs.Add(new KeyValuePair<HidInfo, Func<FakeDev>>(new HidInfo { Path = "p" + Devs.Count, Vid = vid, Pid = pid, UsagePage = up, Usage = u, Product = prod, Interface = iface,
      InLen = 64, OutLen = outl, FeatLen = feat }, mk));
  public override List<HidInfo> Enumerate() => Devs.Select(d => d.Key).ToList();
  public override IHidDevice Open(HidInfo i) { var d = Devs.First(x => x.Key.Path == i.Path).Value(); d.Info = i; return d; }
}
