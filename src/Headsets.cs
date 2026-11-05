// Headsets.cs – wireless headsets read directly (no extra software): SteelSeries Arctis / Arctis Nova,
// HyperX Cloud II Wireless / Cloud Alpha Wireless / Cloud Flight, Corsair Void / Virtuoso.
// Protocol facts as documented by the HeadsetControl project (github.com/Sapd/HeadsetControl).
using System;
using System.Collections.Generic;
using System.Linq;

namespace BatteryWidget
{
    public class HeadsetProvider : Provider
    {
        public override string Id => "headsets";
        public override string Label => "Wireless headsets (SteelSeries, HyperX, Corsair)";
        public override string Hint => "Turn the headset on, then scan again.";

        public class Model
        {
            public int[] Vids; public int[] Pids; public string Name;
            public int Interface = -1, Page = -1;               // which HID interface to talk to (-1 = any vendor one)
            public Func<IHidDevice, int, Result> Read;           // (device, pid) -> result
        }
        public class Result { public int? Pct; public bool Charging, Offline, Approx; }

        static Result Ask(IHidDevice dev, byte[] req, int pad, Func<byte[], bool> want, Func<byte[], Result> parse, double timeout = 1.0)
        {
            var b = new byte[Math.Max(req.Length, dev.Info != null && dev.Info.OutLen > 0 ? dev.Info.OutLen : pad)];   // the report length the device wants
            Array.Copy(req, b, req.Length);
            if (!Prov.Send(dev, b)) return null;
            Result res = null;
            Prov.ReadUntil(dev, timeout, d => { if (!want(d)) return false; res = parse(d); return true; });
            return res;
        }

        static int Map(int v, int lo, int hi) => Math.Max(0, Math.Min(100, (int)Math.Round((v - lo) * 100.0 / (hi - lo))));
        static readonly HashSet<int> NovaDiscrete = new HashSet<int> { 0x2202, 0x2206, 0x220a, 0x223a, 0x227a, 0x22a4 };
        const int SS = 0x1038, HP = 0x03F0, KINGSTON = 0x0951, CORSAIR = 0x1B1C;

        public static readonly List<Model> Models = new List<Model> {
            new Model { Vids = new[] { SS }, Pids = new[] { 0x1260, 0x12ad, 0x1252, 0x1280 }, Name = "SteelSeries Arctis 7", Interface = 5,
                Read = (d, p) => Ask(d, new byte[] { 0x06, 0x18 }, 31, r => r.Length >= 3 && r[0] == 0x06 && r[1] == 0x18, r => new Result { Pct = Math.Min(100, (int)r[2]) }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x12b3, 0x12b6, 0x12d7, 0x12d5 }, Name = "SteelSeries Arctis 1 Wireless", Interface = 3,
                Read = (d, p) => Ask(d, new byte[] { 0x06, 0x12 }, 31, r => r.Length >= 4 && r[0] == 0x06 && r[1] == 0x12,
                    r => r[2] == 0x01 ? new Result { Offline = true } : new Result { Pct = Math.Min(100, (int)r[3]) }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x12c2, 0x12c4 }, Name = "SteelSeries Arctis 9", Interface = 0,
                Read = (d, p) => Ask(d, new byte[] { 0x00, 0x20 }, 31, r => r.Length >= 5, r => new Result { Pct = Map(r[3], 0x64, 0x9A), Charging = r[4] == 0x01 }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x1290 }, Name = "SteelSeries Arctis Pro Wireless", Interface = 0,
                Read = (d, p) => Ask(d, new byte[] { 0x40, 0xAA }, 31, r => r.Length >= 1 && r[0] <= 4, r => new Result { Pct = Map(r[0], 0, 4), Approx = true }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x12e0, 0x12e5 }, Name = "SteelSeries Arctis Nova Pro Wireless", Interface = 4,
                Read = (d, p) => Ask(d, new byte[] { 0x06, 0xb0 }, 31, r => r.Length >= 16 && r[0] == 0x06 && r[1] == 0xb0,
                    r => r[15] == 0x01 ? new Result { Offline = true } : new Result { Pct = Map(Math.Min((int)r[6], 8), 0, 8), Charging = r[15] == 0x02, Approx = true }) },
            // Arctis Nova family: ask 00 B0, read the status report
            new Model { Vids = new[] { SS }, Pids = new[] { 0x2202, 0x22A1, 0x227e, 0x2206, 0x2258, 0x229e, 0x22ad, 0x223a, 0x22a9, 0x227a, 0x22a4, 0x22a5, 0x220a, 0x22a7, 0x2298 },
                Name = "SteelSeries Arctis Nova 7", Interface = 3, Page = 0xFFC0,
                Read = (d, p) => Ask(d, new byte[] { 0x00, 0xb0 }, 31, r => r.Length >= 4, r =>
                    r[3] == 0x00 ? new Result { Offline = true }
                    : new Result { Pct = NovaDiscrete.Contains(p) ? Map(Math.Min((int)r[2], 4), 0, 4) : Math.Min(100, (int)r[2]), Charging = r[3] == 1 || r[3] == 2, Approx = NovaDiscrete.Contains(p) }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x220e, 0x2212, 0x2216, 0x2236 }, Name = "SteelSeries Arctis 7+", Interface = 3, Page = 0xFFC0,
                Read = (d, p) => Ask(d, new byte[] { 0x00, 0xb0 }, 31, r => r.Length >= 4, r =>
                    r[1] == 0x01 ? new Result { Offline = true } : new Result { Pct = Map(Math.Min((int)r[2], 4), 0, 4), Charging = r[3] == 1, Approx = true }) },
            new Model { Vids = new[] { SS }, Pids = new[] { 0x2232, 0x2253 }, Name = "SteelSeries Arctis Nova 5", Interface = 3, Page = 0xFFC0,
                Read = (d, p) => Ask(d, new byte[] { 0x00, 0xb0 }, 31, r => r.Length >= 5, r =>
                    r[1] == 0x02 ? new Result { Offline = true } : new Result { Pct = Math.Min(100, (int)r[3]), Charging = r[4] == 1 }) },
            new Model { Vids = new[] { HP, KINGSTON }, Pids = new[] { 0x0696, 0x018b, 0x1718 }, Name = "HyperX Cloud II Wireless", Interface = 0, Page = 0xFF90,
                Read = (d, p) =>
                {
                    var lvl = Ask(d, new byte[] { 0x06, 0xff, 0xbb, 0x02 }, 52, r => r.Length >= 8 && r[3] == 0x02, r => new Result { Pct = Math.Min(100, (int)r[7]) });
                    if (lvl == null) return null;
                    var chg = Ask(d, new byte[] { 0x06, 0xff, 0xbb, 0x03 }, 52, r => r.Length >= 5 && r[3] == 0x03, r => new Result { Charging = r[4] == 1 });
                    lvl.Charging = chg != null && chg.Charging;
                    return lvl;
                } },
            new Model { Vids = new[] { HP }, Pids = new[] { 0x098d }, Name = "HyperX Cloud Alpha Wireless",
                Read = (d, p) =>
                {
                    var conn = Ask(d, new byte[] { 0x21, 0xbb, 0x03 }, 31, r => r.Length >= 4 && r[0] == 0x21 && r[2] == 0x03, r => new Result { Offline = r[3] == 0x01 });
                    if (conn == null || conn.Offline) return conn;
                    var chg = Ask(d, new byte[] { 0x21, 0xbb, 0x0c }, 31, r => r.Length >= 4 && r[0] == 0x21 && r[2] == 0x0c, r => new Result { Charging = r[3] == 0x01 });
                    var lvl = Ask(d, new byte[] { 0x21, 0xbb, 0x0b }, 31, r => r.Length >= 4 && r[0] == 0x21 && r[2] == 0x0b, r => new Result { Pct = Math.Min(100, (int)r[3]) });
                    if (lvl != null) lvl.Charging = chg != null && chg.Charging;
                    return lvl ?? (chg != null && chg.Charging ? chg : null);
                } },
            new Model { Vids = new[] { KINGSTON }, Pids = new[] { 0x16C4, 0x1723 }, Name = "HyperX Cloud Flight",
                Read = (d, p) => Ask(d, new byte[] { 0x21, 0xff, 0x05 }, 20, r => r.Length >= 5 && (r.Length == 15 || r.Length == 20 || r[0] == 0x21),
                    r =>
                    {
                        int mv = (r[3] << 8) | r[4];
                        if (mv < 3000) return null;
                        if (mv >= 4300) return new Result { Charging = true };
                        return new Result { Pct = Prov.CurvePercent(mv, Prov.Lipo), Approx = true };
                    }) },
            new Model { Vids = new[] { CORSAIR }, Pids = new[] { 0x1b27, 0x0a14, 0x0a16, 0x0a17, 0x0a1d, 0x0a1a, 0x1b2a, 0x1b23, 0x1b29, 0x0a55, 0x0a51, 0x0a52, 0x0a38, 0x0a4f, 0x0a2b, 0x0a75, 0x0a56 },
                Name = "Corsair headset", Page = 0xFF00,
                Read = (d, p) => Ask(d, new byte[] { 0xC9, 0x64 }, 2, r => r.Length >= 5,
                    r => r[4] == 0 ? new Result { Offline = true } : new Result { Pct = Math.Min(100, r[2] & 0x7F), Charging = r[4] == 4 || r[4] == 5 }) },
        };

        public static Model Find(int vid, int pid) => Models.FirstOrDefault(m => m.Vids.Contains(vid) && m.Pids.Contains(pid));

        protected override List<Reading> Poll(bool force)
        {
            var outp = new List<Reading>();
            var groups = Hid.Enumerate().Where(e => Find(e.Vid, e.Pid) != null).GroupBy(e => e.VidPid).ToList();
            foreach (var g in groups)
            {
                var first = g.First();
                var model = Find(first.Vid, first.Pid);
                string product = g.Select(e => e.Product).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";
                string name = model.Name == "Corsair headset" && product != "" ? Prov.CleanName(product, "Corsair headset") : model.Name;
                string key = "usb:" + g.Key, det = g.Key + " · built-in headset protocol";
                var ifaces = g.Where(e => e.UsagePage >= 0xFF00 || e.UsagePage == model.Page)
                              .OrderBy(e => (model.Interface >= 0 && e.Interface == model.Interface ? 0 : 2) + (model.Page >= 0 && e.UsagePage == model.Page ? 0 : 1)).ToList();
                if (ifaces.Count == 0) ifaces = g.ToList();
                Result res = null;
                foreach (var e in ifaces)
                {
                    var dev = Prov.Open(e);
                    if (dev == null) continue;
                    using (dev) { try { res = model.Read(dev, first.Pid); } catch { res = null; } }
                    if (res != null) break;
                }
                if (res == null || res.Offline || (res.Pct == null && !res.Charging))
                {
                    var off = OfflineIfSeen(key, name, "headset", det);
                    if (off.Count == 0 && res != null) { Seen.Add(key); off.Add(new Reading(key, name, "headset", null, false, false, false, Id, det)); }
                    outp.AddRange(off);
                    continue;
                }
                Seen.Add(key);
                outp.Add(new Reading(key, name, "headset", res.Pct, res.Charging, true, res.Approx, Id, det));
            }
            Note = groups.Count > 0 ? $"{outp.Count(r => r.Online)} headset(s)" : "no supported headset receivers";
            return outp;
        }
    }
}
