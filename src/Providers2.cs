// Providers2.cs – more battery sources: Chinese 2.4 GHz dongles, HyperX, Keychron, MCHOSE, standard HID, HeadsetControl, Bluetooth.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BatteryWidget
{
    // MonsGeek / Akko / Epomaker … – RongYuan 2.4 GHz dongles (feature report 0: F7 = GET_DONGLE_STATUS)
    public class RongYuanProvider : Provider
    {
        public override string Id => "rongyuan";
        public override string Label => "MonsGeek / Akko / Epomaker (2.4 GHz)";
        public override string Hint => "Press a key on the keyboard (the dongle only knows the level after the keyboard reports it).";
        public static readonly HashSet<int> Vids = new HashSet<int> { 0x3151, 0x145F, 0x342D, 0x347A, 0x374A, 0x379A };
        public static HashSet<string> Extra = new HashSet<string>();

        bool Candidate(HidInfo e) => e.UsagePage == 0xFFFF && e.Usage == 0x02 && (Deep || Extra.Contains(e.VidPid) || Vids.Contains(e.Vid));

        public static bool Query(IHidDevice dev, out int pct, out bool charging, out bool paired)
        {
            pct = 0; charging = false; paired = false;
            var cmd = new byte[65];
            cmd[1] = 0xF7;
            int sum = 0; for (int i = 1; i < 8; i++) sum += cmd[i];
            cmd[8] = (byte)(255 - (sum & 0xFF));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] r;
                try
                {
                    if (!dev.SendFeature(cmd)) return false;
                    Thread.Sleep(30 + 50 * attempt);
                    r = dev.GetFeature(0, 65);
                }
                catch { return false; }
                if (r != null && r.Length >= 10 && r[2] <= 100 && r[5] == 1 && r[7] == 1 && r[1] <= 1 && r[4] <= 1 && r[6] <= 1)
                { pct = r[2]; charging = r[4] == 1; paired = r[9] == 1; return true; }
            }
            return false;
        }

        protected override List<Reading> Poll(bool force)
        {
            var outp = new List<Reading>();
            var asleep = new List<string>();
            int tried = 0;
            foreach (var e in Hid.Enumerate().Where(Candidate))
            {
                tried++;
                string name = Prov.CleanName(e.Product, "Wireless keyboard");
                if (name.Equals("keyboard", StringComparison.OrdinalIgnoreCase) || name.Equals("wireless keyboard", StringComparison.OrdinalIgnoreCase))
                    name = Prov.CleanName(e.Manufacturer + " keyboard", "Wireless keyboard");
                string key = "usb:" + e.VidPid, det = e.VidPid + " · RongYuan F7";
                var dev = Prov.Open(e);
                if (dev == null) continue;
                int pct; bool chg, paired, ok;
                using (dev) ok = Query(dev, out pct, out chg, out paired);
                if (!ok) continue;
                if (pct == 0 || !paired) { asleep.Add(name); outp.AddRange(OfflineIfSeen(key, name, "keyboard", det)); continue; }
                Seen.Add(key);
                outp.Add(new Reading(key, name, "keyboard", pct, chg, true, false, Id, det));
            }
            Note = asleep.Count > 0 ? string.Join(", ", asleep) + ": dongle answers, keyboard asleep – press a key"
                 : tried > 0 ? $"{outp.Count} keyboard(s)" : "no MonsGeek/Akko-type dongles";
            return outp;
        }
    }

    // ATK / VXE / Compx mice – vendor report 0x08, command 0x04 (checksum 0x55)
    public class CompxProvider : Provider
    {
        public override string Id => "compx";
        public override string Label => "ATK / VXE / Compx mice";
        public override string Hint => "Move the mouse to wake it, then scan again (a sleeping 2.4 GHz mouse doesn't answer).";
        public static readonly HashSet<int> Vids = new HashSet<int> { 0x373B, 0x3554 };
        public static HashSet<string> Extra = new HashSet<string>();

        bool Candidate(HidInfo e) => e.UsagePage == 0xFF02 && e.Usage == 0x02 && (Deep || Extra.Contains(e.VidPid) || Vids.Contains(e.Vid));

        public static int? Query(IHidDevice dev, out bool charging)
        {
            charging = false;
            var pkt = new byte[17];
            pkt[0] = 0x08; pkt[1] = 0x04;
            int s = 0; for (int i = 0; i < 16; i++) s += pkt[i];
            pkt[16] = (byte)((0x55 - s) & 0xFF);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!dev.Write(pkt)) return null;
                int? pct = null; bool chg = false;
                Prov.ReadUntil(dev, 0.5, d =>
                {
                    if (d.Length < 8 || d[0] != 0x08 || d[1] != 0x04) return false;
                    int n = d[5];
                    if (n >= 1 && n <= 10 && d[6] <= 100) { pct = d[6]; chg = n >= 2 && d[7] != 0; return true; }
                    return false;
                });
                if (pct != null) { charging = chg; return pct; }
            }
            return null;
        }

        protected override List<Reading> Poll(bool force)
        {
            var outp = new List<Reading>();
            int tried = 0;
            foreach (var e in Hid.Enumerate().Where(Candidate))
            {
                tried++;
                string name = Prov.CleanName(e.Product, "Wireless mouse");
                if (name.Equals("mouse", StringComparison.OrdinalIgnoreCase) || name.Equals("wireless mouse", StringComparison.OrdinalIgnoreCase))
                    name = Prov.CleanName(e.Manufacturer + " mouse", "Wireless mouse");
                string key = "usb:" + e.VidPid, det = e.VidPid + " · Compx 0x04";
                var dev = Prov.Open(e);
                if (dev == null) continue;
                bool chg; int? pct;
                using (dev) pct = Query(dev, out chg);
                if (pct == null) { outp.AddRange(OfflineIfSeen(key, name, "mouse", det)); continue; }
                Seen.Add(key);
                outp.Add(new Reading(key, name, "mouse", pct, chg, true, false, Id, det));
            }
            Note = tried > 0 ? $"{outp.Count} mouse/mice" : "no ATK/VXE-type receivers";
            return outp;
        }
    }

    // HyperX (HP) – Cloud III Wireless / Cloud II Core Wireless ("66") and Cloud III S Wireless ("0C")
    public class HyperXProvider : Provider
    {
        public override string Id => "hyperx";
        public override string Label => "HyperX Cloud III / III S / II Core";
        public override string Hint => "Turn the headset on. NGENUITY can stay open.";
        const int Vid = 0x03F0;
        static readonly Dictionary<int, string> F66 = new Dictionary<int, string> { { 0x05B7, "HyperX Cloud III Wireless" }, { 0x0C9D, "HyperX Cloud III Wireless" },
            { 0x0995, "HyperX Cloud II Core Wireless" }, { 0x0795, "HyperX Cloud II Core Wireless" } };
        static readonly Dictionary<int, string> F0C = new Dictionary<int, string> { { 0x06BE, "HyperX Cloud III S Wireless" }, { 0x02CC, "HyperX Cloud III S Wireless" } };

        public static void Query66(IHidDevice dev, out bool? connected, out int? level, out bool charging)
        {
            bool? conn = null; int? lvl = null; bool chg = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (var pair in new[] { new[] { 0x82, 0x82, 0x0B }, new[] { 0x89, 0x89, 0x0D }, new[] { 0x8A, 0x8A, 0x0C } })
                {
                    var pkt = new byte[62]; pkt[0] = 0x66; pkt[1] = (byte)pair[0];
                    if (!Prov.Send(dev, pkt)) { connected = null; level = null; charging = false; return; }
                    Prov.ReadUntil(dev, 0.6, d =>
                    {
                        if (d.Length < 5 || d[0] != 0x66) return false;
                        int c = d[1];
                        if (c == 0x82 || c == 0x0B) conn = d[2] == 1;
                        else if (c == 0x89 || c == 0x0D) { if ((d[2] != 0 || d[3] != 0) && d[4] <= 100) lvl = d[4]; }
                        else if (c == 0x8A || c == 0x0C) chg = d[2] == 1;
                        return c == pair[1] || c == pair[2];
                    });
                }
                if (lvl != null) { conn = true; break; }       // a real battery answer wins over "not connected"
                Thread.Sleep(150);
            }
            connected = conn; level = lvl; charging = chg;
        }

        public static int? Query0C(IHidDevice dev, out bool charging)
        {
            int? lvl = null; bool chg = false;
            if (!Prov.Send(dev, Prov.Pad(64, 0x0C, 0x02, 0x03, 0x01, 0x00, 0x06))) { charging = false; return null; }
            Prov.ReadUntil(dev, 1.5, d =>
            {
                if (d.Length >= 6 && d[0] == 0x0D && d[4] == 0x0A) chg = d[5] == 1;
                if (d.Length >= 7 && d[0] == 0x0C && d[5] == 0x06) { lvl = d[6]; return true; }
                return false;
            });
            charging = chg;
            return lvl;
        }

        protected override List<Reading> Poll(bool force)
        {
            var groups = Hid.Enumerate(Vid).Where(e => e.UsagePage == 0xFF13 || e.UsagePage == 0x01C0 ||
                ((F66.ContainsKey(e.Pid) || F0C.ContainsKey(e.Pid)) && e.UsagePage >= 0xFF00)).GroupBy(e => e.VidPid).ToList();
            var outp = new List<Reading>();
            foreach (var g in groups)
            {
                var entries = g.ToList();
                int pid = entries[0].Pid;
                bool fam0c = F0C.ContainsKey(pid) || (entries.Any(e => e.UsagePage == 0x01C0) && !F66.ContainsKey(pid));
                string product = entries.Select(e => e.Product).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
                string name = F66.ContainsKey(pid) ? F66[pid] : F0C.ContainsKey(pid) ? F0C[pid] : Prov.CleanName(product, "HyperX headset");
                if (!name.StartsWith("HyperX", StringComparison.OrdinalIgnoreCase)) name = "HyperX " + name;
                string key = "usb:" + g.Key, det = g.Key + " · HyperX " + (fam0c ? "0C" : "66");
                int best = fam0c ? 0x01C0 : 0xFF13;
                bool answered = false, online = false; int? pct = null; bool chg = false;
                foreach (var e in entries.OrderBy(x => x.UsagePage == best ? 0 : 1))
                {
                    var dev = Prov.Open(e);
                    if (dev == null) continue;
                    using (dev)
                    {
                        if (fam0c)
                        {
                            var l = Query0C(dev, out chg);
                            if (l != null) { answered = true; online = l <= 100; pct = l <= 100 ? l : null; }
                        }
                        else
                        {
                            bool? conn; int? l;
                            Query66(dev, out conn, out l, out chg);
                            if (conn != null || l != null) { answered = true; online = conn != false && l != null; pct = l; }
                        }
                    }
                    if (answered) break;
                }
                if (!answered || !online)
                {
                    var off = OfflineIfSeen(key, name, "headset", det);
                    if (off.Count == 0 && answered) { off.Add(new Reading(key, name, "headset", null, false, false, false, Id, det)); Seen.Add(key); }
                    outp.AddRange(off);
                    continue;
                }
                Seen.Add(key);
                outp.Add(new Reading(key, name, "headset", pct, chg, true, false, Id, det));
            }
            Note = groups.Count > 0 ? $"{outp.Count} headset(s)" : "no HyperX Cloud III-type dongles";
            return outp;
        }
    }

    // Keychron mice – Ultra-Link 8K / cable (B3 06 → B4 06, byte 20) and Keychron Link 1K (feature 0x51)
    public class KeychronProvider : Provider
    {
        public override string Id => "keychron";
        public override string Label => "Keychron mice";
        public override string Hint => "Move the mouse to wake it (and switch it to 2.4 GHz mode), then scan again.";
        const int Vid = 0x3434, Link = 0xD030;
        static readonly Dictionary<int, string> Models = new Dictionary<int, string> { { 0xD049, "Keychron M6" }, { 0xD048, "Keychron M5" } };

        public static int? QueryB3(IHidDevice dev, out bool charging)
        {
            int? pct = null; bool chg = false;
            for (int attempt = 0; attempt < 2 && pct == null; attempt++)
            {
                if (!Prov.Send(dev, Prov.Pad(64, 0xB3, 0x06))) break;
                Prov.ReadUntil(dev, 1.0, d =>
                {
                    if (d.Length > 20 && d[0] == 0xB4 && d[1] == 0x06)
                    {
                        if ((d[20] & 0x7F) <= 100) { pct = d[20] & 0x7F; chg = (d[20] & 0x80) != 0; }
                        return true;
                    }
                    return false;
                });
            }
            charging = chg;
            return pct;
        }

        public static int? QueryLink(IHidDevice dev, out bool charging)
        {
            charging = false;
            int n = Math.Max(20, dev.Info.FeatLen > 0 ? dev.Info.FeatLen - 1 : 20);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] r;
                try
                {
                    var req = new byte[n + 1]; req[0] = 0x51; req[1] = 0x06;
                    if (!dev.SendFeature(req)) return null;
                    Thread.Sleep(50 + 100 * attempt);
                    r = dev.GetFeature(0x51, n + 1);
                }
                catch { return null; }
                if (r == null || r.Length < 13 || r[0] != 0x51) continue;
                if (r[11] > 0 && r[11] <= 100) { charging = (r[12] & 3) == 1 || (r[12] & 3) == 2; return r[11]; }
                if (r[11] == 0) return null;                       // mouse asleep / not linked
            }
            return null;
        }

        protected override List<Reading> Poll(bool force)
        {
            var groups = Hid.Enumerate(Vid).Where(e => e.UsagePage == 0xFFC1 || e.Pid == Link ||
                (Deep && e.UsagePage >= 0xFF00 && e.UsagePage != 0xFF60)).GroupBy(e => e.VidPid).ToList();
            var outp = new List<Reading>();
            foreach (var g in groups)
            {
                var entries = g.ToList();
                int pid = entries[0].Pid;
                string product = entries.Select(e => e.Product).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "";
                string name = Models.ContainsKey(pid) ? Models[pid]
                    : Regex.IsMatch(product, "link|receiver|dongle", RegexOptions.IgnoreCase) || product == "" ? "Keychron mouse"
                    : Prov.CleanName(product, "Keychron mouse");
                bool link = pid == Link;
                string key = "usb:" + g.Key, det = g.Key + " · Keychron " + (link ? "0x51" : "B3/06");
                int? pct = null; bool chg = false;
                foreach (var e in entries.OrderBy(x => x.UsagePage == 0xFFC1 || x.UsagePage == 0x008C ? 0 : 1).ThenBy(x => x.Interface))
                {
                    if (link && e.FeatLen < 13) continue;            // only collections that can carry feature report 0x51
                    var dev = Prov.Open(e);
                    if (dev == null) continue;
                    using (dev) pct = link ? QueryLink(dev, out chg) : QueryB3(dev, out chg);
                    if (pct != null) break;
                }
                if (pct == null) { outp.AddRange(OfflineIfSeen(key, name, "mouse", det)); continue; }
                Seen.Add(key);
                outp.Add(new Reading(key, name, "mouse", pct, chg, true, false, Id, det));
            }
            Note = groups.Count > 0 ? $"{outp.Count} mouse/mice" : "no Keychron receivers";
            return outp;
        }
    }

    // MCHOSE – A7 V3 family (new "0x4D" protocol) and A7 / A7 V2 / L7 / M7 (classic feature 0x11)
    public class MchoseProvider : Provider
    {
        public override string Id => "mchose";
        public override string Label => "MCHOSE mice";
        public override string Hint => "Move the mouse to wake it, then scan again. M HUB can stay closed.";
        static readonly HashSet<int> Vids = new HashSet<int> { 0x3837, 0x5253 };
        public static readonly Dictionary<int, string> New = new Dictionary<int, string> { { 16422, "A5 V3 Ultra+" }, { 16423, "K7 V2 Pro+" },
            { 16424, "K7 V2 Ultra+" }, { 16425, "G3 V3" }, { 16426, "V7" }, { 16432, "A7 V3" }, { 16433, "A7 V3 Pro" }, { 16434, "A7 V3 Pro+" },
            { 16435, "A7 V3 Ultra+" }, { 16436, "A5 V3 Ultra+" }, { 16437, "A5 V3 Pro" }, { 16438, "R7 Ultra" }, { 16439, "K5 Pro" },
            { 16440, "K5 Ultra" }, { 16442, "A7 V3-X" }, { 16443, "K7 V2 Pro" }, { 16444, "K7 V2 Ultra" }, { 16445, "A7 V3 Ultra" },
            { 16446, "V7 Pro" }, { 16447, "A7 V3 Magnetic" }, { 16448, "K5 V2" } };
        static readonly HashSet<int> NewReceivers = new HashSet<int> { 0x1014, 0x1016, 0x1018, 0x101C, 0x101D };
        public static readonly Dictionary<int, string> Classic = new Dictionary<int, string> { { 16, "A7 Pro" }, { 17, "A7 Ultra" }, { 32, "M7" },
            { 48, "M7 Pro" }, { 49, "M7 Ultra" }, { 112, "A7" }, { 161, "AX5 V2" }, { 176, "L7 Pro" }, { 177, "L7 Ultra" }, { 192, "L7" },
            { 4353, "A5 V2 Ultra" }, { 16400, "AX5 V2" }, { 16401, "A7X Ultra" }, { 16405, "L7 Pro+" }, { 16406, "L7 Ultra+" },
            { 16408, "A7 V2 Pro" }, { 16409, "A7 V2 Ultra" }, { 16417, "A7 V2 Ultra+" }, { 16419, "A7 V2 Pro+" }, { 16656, "A7 Ultra(RE)" }, { 16720, "K7 Ultra" } };
        static readonly HashSet<int> ClassicReceivers = new HashSet<int> { 0x1000, 0x1001, 0x1009, 0x100A, 0x100B, 0x100C, 0x1020, 0x1021 };

        public static byte[] NewRequest()
        {
            var b = new byte[64];
            b[0] = 0x4D; b[1] = 1; b[2] = 1; b[5] = 9;
            byte x = 0; for (int i = 2; i < 8; i++) x ^= b[i];
            b[8] = x;
            return b;
        }

        /// <summary>(model, percent, charging, linked) or null.</summary>
        public static Tuple<string, int, bool, bool> ParseNew(byte[] r)
        {
            if (r == null || r.Length < 22 || r[0] != 0x4D || r[1] != 1 || r[4] != 0 || r[5] != 9 || r[6] != 0 || r[7] != 0) return null;
            int n = r[3];
            if (n < 14 || n > r.Length - 8) return null;
            if ((r[2] & 1) != 0)
            {
                if (r.Length <= 8 + n) return null;
                byte x = 0; for (int i = 2; i < 8 + n; i++) x ^= r[i];
                if (x != r[8 + n]) return null;
            }
            int vid = r[8] | (r[9] << 8), pid = r[10] | (r[11] << 8);
            if (!Vids.Contains(vid) || !New.ContainsKey(pid) || r[20] > 100) return null;
            bool linked = r[18] != 0 || r[20] > 0 || r[19] == 1 || r[19] == 2;
            return Tuple.Create(New[pid], (int)r[20], r[19] == 1, linked);
        }

        public static Tuple<string, int, bool, bool> ParseClassic(byte[] r)
        {
            if (r == null || r.Length < 13 || r[0] != 0x11 || r[1] != 0xF9) return null;
            var d = r.Skip(2).Select(b => (byte)(b ^ 0xFF)).ToArray();
            int vid = d[0] | (d[1] << 8), pid = d[2] | (d[3] << 8);
            if (!Vids.Contains(vid) || pid == 0 || pid == 0xFFFF || d[9] > 100) return null;
            return Tuple.Create(Classic.ContainsKey(pid) ? Classic[pid] : "mouse " + pid, (int)d[9], d[10] == 1, (d[8] & 8) != 0);
        }

        static Tuple<string, int, bool, bool> QueryNew(IHidDevice dev)
        {
            var req = NewRequest();
            if (!Prov.Send(dev, req)) return null;
            Tuple<string, int, bool, bool> res = null;
            Prov.ReadUntil(dev, 1.5, d =>
            {
                if (d.Length > 8 && d[0] == 0x4D && d[4] == 0 && d[5] == 9)
                {
                    if (d[8] == 0xFF) { Thread.Sleep(30); Prov.Send(dev, req); return false; }   // busy – ask again
                    res = ParseNew(d);
                    return true;
                }
                return false;
            });
            return res;
        }

        static Tuple<string, int, bool, bool> QueryClassic(IHidDevice dev)
        {
            var req = new byte[21]; req[0] = 0x11; req[1] = 0xF9;
            for (int i = 2; i < 21; i++) req[i] = 0xFF;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!dev.SendFeature(req)) { Thread.Sleep(100); continue; }
                    Thread.Sleep(100);
                    var got = ParseClassic(dev.GetFeature(0x11, 21));
                    if (got != null) return got;
                }
                catch { return null; }
            }
            return null;
        }

        protected override List<Reading> Poll(bool force)
        {
            var groups = new Dictionary<string, List<HidInfo>>();
            var isNew = new Dictionary<string, bool>();
            foreach (var e in Hid.Enumerate())
            {
                if (!Vids.Contains(e.Vid) || (e.UsagePage != 0xFF01 && e.UsagePage != 0xFF0B)) continue;
                bool nw = e.Vid == 0x3837 && (New.ContainsKey(e.Pid) || NewReceivers.Contains(e.Pid) || Deep);
                bool cl = !nw && (Classic.ContainsKey(e.Pid) || ClassicReceivers.Contains(e.Pid) || Deep);
                if (!nw && !cl) continue;
                if (!groups.ContainsKey(e.VidPid)) { groups[e.VidPid] = new List<HidInfo>(); isNew[e.VidPid] = nw; }
                groups[e.VidPid].Add(e);
            }
            var outp = new List<Reading>();
            foreach (var g in groups)
            {
                bool nw = isNew[g.Key];
                int pid = g.Value[0].Pid;
                string fb = New.ContainsKey(pid) ? New[pid] : Classic.ContainsKey(pid) ? Classic[pid]
                    : Prov.CleanName(g.Value.Select(x => x.Product).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "", "mouse");
                if (!fb.StartsWith("MCHOSE", StringComparison.OrdinalIgnoreCase)) fb = "MCHOSE " + fb;
                string key = "usb:" + g.Key, det = g.Key + " · MCHOSE " + (nw ? "0x4D" : "0x11");
                Tuple<string, int, bool, bool> got = null;
                foreach (var e in g.Value)
                {
                    var dev = Prov.Open(e);
                    if (dev == null) continue;
                    using (dev) got = nw ? QueryNew(dev) : QueryClassic(dev);
                    if (got != null) break;
                }
                if (got == null || !got.Item4) { outp.AddRange(OfflineIfSeen(key, fb, "mouse", det)); continue; }
                string name = got.Item1.StartsWith("MCHOSE", StringComparison.OrdinalIgnoreCase) ? got.Item1 : "MCHOSE " + got.Item1;
                Seen.Add(key);
                outp.Add(new Reading(key, name, "mouse", got.Item2, got.Item3, true, false, Id, det));
            }
            Note = groups.Count > 0 ? $"{outp.Count} mouse/mice" : "no MCHOSE receivers";
            return outp;
        }
    }

    // Any device that reports battery the standard HID way
    public class HidBatteryProvider : Provider
    {
        public override string Id => "hidbattery";
        public override string Label => "Standard HID battery";
        public override string Hint => "Only devices that follow the HID battery standard show up here.";
        static readonly HashSet<int> Skip = new HashSet<int> { 0x046D, 0x1532, 0x1B1C, 0x0B05 };
        List<HidInfo> _targets = new List<HidInfo>();
        string _paths = "";
        DateTime _scanned = DateTime.MinValue;

        protected override List<Reading> Poll(bool force)
        {
            var entries = Hid.Enumerate().Where(e => !Skip.Contains(e.Vid)).ToList();
            string paths = string.Join("|", entries.Select(e => e.Path).OrderBy(p => p));
            if (force || paths != _paths || (DateTime.UtcNow - _scanned).TotalSeconds > 600)
            {
                _paths = paths; _scanned = DateTime.UtcNow;
                _targets = new List<HidInfo>();
                foreach (var e in entries)
                {
                    var dev = Prov.Open(e);
                    if (dev == null) continue;
                    using (dev) if (dev.HasStandardBattery) _targets.Add(e);
                }
            }
            var outp = new List<Reading>();
            var done = new HashSet<string>();
            foreach (var e in _targets)
            {
                if (done.Contains(e.VidPid)) continue;
                string product = !string.IsNullOrEmpty(e.Product) ? e.Product : !string.IsNullOrEmpty(e.Manufacturer) ? e.Manufacturer : "HID device";
                string name = Prov.CleanName(product, "Wireless device");
                string kind = Prov.GuessKind(product + " " + e.Manufacturer);
                if (kind == "other")
                {
                    var tops = entries.Where(x => x.VidPid == e.VidPid).Select(x => (x.UsagePage << 16) | x.Usage).ToList();
                    kind = tops.Contains(0x10002) ? "mouse" : tops.Contains(0x10006) ? "keyboard"
                         : tops.Contains(0x10005) || tops.Contains(0x10004) ? "controller" : tops.Contains(0xB0005) ? "headset" : "other";
                }
                string key = "usb:" + e.VidPid, det = e.VidPid + " · HID battery";
                var dev = Prov.Open(e);
                int? pct = null; bool chg = false;
                if (dev != null) using (dev) pct = dev.StandardBattery(out chg);
                if (pct == null) { outp.AddRange(OfflineIfSeen(key, name, kind, det)); continue; }
                done.Add(e.VidPid); Seen.Add(key);
                outp.Add(new Reading(key, name, kind, Math.Max(0, Math.Min(100, pct.Value)), chg, true, false, Id, det));
            }
            Note = _targets.Count > 0 ? $"{_targets.Count} interface(s) report battery" : "no standard HID batteries";
            return outp;
        }
    }

    // HeadsetControl (github.com/Sapd/HeadsetControl) – ~100 headsets
    public class HeadsetControlProvider : Provider
    {
        public override string Id => "headsetcontrol";
        public override string Label => "Headsets (HeadsetControl)";
        public override string Hint => "Optional – only used if headsetcontrol.exe happens to be on this PC.";
        public override bool UsesHid => false;
        public const string Releases = "https://github.com/Sapd/HeadsetControl/releases";
        public static string ToolsDir => Path.Combine(Cfg.DataDir, "tools");

        public static string Find()
        {
            var cands = new List<string> { Path.Combine(Cfg.AppDir, "headsetcontrol.exe"), Path.Combine(ToolsDir, "headsetcontrol.exe"),
                Path.Combine(Cfg.RoamingDir, "tools", "headsetcontrol.exe") };
            foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (p.Length > 0) cands.Add(Path.Combine(p.Trim(), "headsetcontrol.exe"));
            return cands.FirstOrDefault(File.Exists);
        }

        protected override List<Reading> Poll(bool force)
        {
            var outp = new List<Reading>();
            string exe = Find();
            if (exe == null) { Note = "not installed"; return outp; }
            var psi = new ProcessStartInfo(exe, "-b -o json") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            string raw;
            using (var p = Process.Start(psi))
            {
                var task = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } Note = "HeadsetControl didn't answer"; return outp; }
                raw = task.Result.Trim();
            }
            int brace = raw.IndexOf('{');
            if (brace < 0) { Note = "installed · no supported headset connected"; return outp; }
            var data = Cfg.FromJson(raw.Substring(brace)) as Dictionary<string, object>;
            var devs = data != null && data.TryGetValue("devices", out var dv) ? dv as List<object> : null;
            foreach (var o in devs ?? new List<object>())
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                string status = Cfg.S(d, "status");
                if (status != null && status != "success") continue;
                var bat = Cfg.SubDict(d, "battery");
                string st = Cfg.S(bat, "status", "").ToUpperInvariant();
                double? lvl = Cfg.N(bat, "level");
                bool valid = lvl.HasValue && lvl >= 0 && st != "BATTERY_UNAVAILABLE";
                string name = Cfg.S(d, "device") ?? Cfg.S(d, "product") ?? "Headset", vendor = Cfg.S(d, "vendor", "");
                if (vendor != "" && !name.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)) name = vendor + " " + name;
                string vid = Cfg.S(d, "id_vendor", "").Replace("0x", ""), pid = Cfg.S(d, "id_product", "").Replace("0x", "");
                string det = vid != "" && pid != "" ? $"{vid}:{pid} · HeadsetControl" : "HeadsetControl";
                outp.Add(new Reading("headset:" + name, name, "headset", valid ? (int?)(int)lvl.Value : null, st == "BATTERY_CHARGING",
                    valid || st == "BATTERY_CHARGING", false, Id, det));
            }
            Note = $"installed · {outp.Count} headset(s)";
            return outp;
        }

        /// <summary>Latest Windows build from GitHub into the portable tools folder.</summary>
        public static string Download(Action<string> say)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;     // TLS 1.2
            say?.Invoke("Looking up the latest HeadsetControl release…");
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "BatteryWidget";
                json = wc.DownloadString("https://api.github.com/repos/Sapd/HeadsetControl/releases/latest");
            }
            var rel = Cfg.FromJson(json) as Dictionary<string, object>;
            var assets = (rel != null && rel.TryGetValue("assets", out var a) ? a as List<object> : null) ?? new List<object>();
            Func<string, int> score = n =>
            {
                n = n.ToLowerInvariant();
                if (!(n.EndsWith(".exe") || n.EndsWith(".zip"))) return -1;
                if (new[] { "darwin", "mac", "linux", "appimage", "setup", "install", "arm" }.Any(n.Contains)) return -1;
                if (!n.Contains("win") && !n.EndsWith(".exe")) return -1;
                return (n.Contains("portable") ? 4 : 0) + (n.EndsWith(".exe") ? 3 : 2) + (n.Contains("64") ? 1 : 0);
            };
            var best = assets.OfType<Dictionary<string, object>>().Where(x => score(Cfg.S(x, "name", "")) >= 0)
                .OrderByDescending(x => score(Cfg.S(x, "name", ""))).FirstOrDefault();
            if (best == null) throw new Exception("No Windows download in the latest release – get it from " + Releases);
            Directory.CreateDirectory(ToolsDir);
            string name = Cfg.S(best, "name"), tmp = Path.Combine(ToolsDir, "_download_" + name), target = Path.Combine(ToolsDir, "headsetcontrol.exe");
            say?.Invoke("Downloading " + name + "…");
            using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "BatteryWidget"; wc.DownloadFile(Cfg.S(best, "browser_download_url"), tmp); }
            try
            {
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var z = ZipFile.OpenRead(tmp))
                    {
                        var exe = z.Entries.FirstOrDefault(x => x.Name.ToLowerInvariant().StartsWith("headsetcontrol") && x.Name.ToLowerInvariant().EndsWith(".exe"));
                        if (exe == null) throw new Exception("headsetcontrol.exe was not in the zip");
                        string folder = Path.GetDirectoryName(exe.FullName.Replace('/', '\\')) ?? "";
                        foreach (var x in z.Entries)
                        {
                            if (x.Name == "" || (Path.GetDirectoryName(x.FullName.Replace('/', '\\')) ?? "") != folder) continue;
                            x.ExtractToFile(x == exe ? target : Path.Combine(ToolsDir, x.Name), true);
                        }
                    }
                }
                else File.Copy(tmp, target, true);
            }
            finally { try { File.Delete(tmp); } catch { } }
            say?.Invoke("HeadsetControl is installed.");
            return target;
        }
    }

    // Bluetooth + Bluetooth LE: Windows' own battery value on the device nodes
    public class BluetoothProvider : Provider
    {
        public override string Id => "bluetooth";
        public override string Label => "Bluetooth (Windows)";
        public override string Hint => "Pair the device with Windows. Only devices that share their battery with Windows show up.";
        public override bool UsesHid => false;
        public override double MinInterval => 60;
        static readonly Regex Addr = new Regex("(?<![0-9A-F])([0-9A-F]{12})(?![0-9A-F])");
        static readonly Regex Suffix = new Regex(@"\s+(hands-free( ag)?( audio)?|avrcp transport|stereo|a2dp( snk)?|le)$", RegexOptions.IgnoreCase);

        public class Node { public string Id, Name; public int? Level; }

        public static List<Node> ReadNodes()
        {
            var nodes = new List<Node>();
            if (!Native.OnWindows) return nodes;
            var batKey = new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
            var nameKey = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 14);     // FriendlyName
            var descKey = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 2);      // DeviceDesc
            foreach (var en in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
            {
                IntPtr set = Native.SetupDiGetClassDevsAll(IntPtr.Zero, en, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_ALLCLASSES);
                if (set == Native.INVALID_HANDLE) continue;
                try
                {
                    for (int i = 0; ; i++)
                    {
                        var d = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                        if (!Native.SetupDiEnumDeviceInfo(set, i, ref d)) break;
                        var sb = new StringBuilder(512);
                        int need;
                        if (!Native.SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out need)) continue;
                        var n = new Node { Id = sb.ToString(), Name = Prop(set, ref d, nameKey) as string ?? Prop(set, ref d, descKey) as string ?? "" };
                        var b = Prop(set, ref d, batKey);
                        if (b is byte by) n.Level = by;
                        nodes.Add(n);
                    }
                }
                finally { Native.SetupDiDestroyDeviceInfoList(set); }
            }
            return nodes;
        }

        static object Prop(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key)
        {
            var buf = new byte[1024];
            uint type; int need;
            if (!Native.SetupDiGetDeviceProperty(set, ref d, ref key, out type, buf, buf.Length, out need, 0)) return null;
            if (type == 3 && need >= 1) return buf[0];                                     // DEVPROP_TYPE_BYTE
            if (type == 0x12) { var s = Encoding.Unicode.GetString(buf, 0, Math.Max(0, need - 2)); return s.TrimEnd('\0'); }   // STRING
            return null;
        }

        /// <summary>Device nodes -> (address, name, level). Pure – testable anywhere.</summary>
        public static List<Tuple<string, string, int>> Parse(IEnumerable<Node> nodes)
        {
            var names = new Dictionary<string, string>();
            var fallback = new Dictionary<string, string>();
            var list = nodes.ToList();
            foreach (var x in list)
            {
                var m = Addr.Matches((x.Id ?? "").ToUpperInvariant());
                if (m.Count == 0 || string.IsNullOrEmpty(x.Name)) continue;
                string a = m[m.Count - 1].Value;
                if (Regex.IsMatch(x.Id, @"^BTH(ENUM|LE)\\DEV_", RegexOptions.IgnoreCase)) names[a] = x.Name;
                if (!fallback.ContainsKey(a)) fallback[a] = x.Name;
            }
            var outp = new Dictionary<string, Tuple<string, string, int>>();
            foreach (var x in list.Where(n => n.Level.HasValue))
            {
                var m = Addr.Matches((x.Id ?? "").ToUpperInvariant());
                string a = m.Count > 0 ? m[m.Count - 1].Value : x.Id;
                string name = names.ContainsKey(a) ? names[a] : Suffix.Replace(fallback.ContainsKey(a) ? fallback[a] : "", "");
                if (string.IsNullOrEmpty(name)) name = "Bluetooth device";
                outp[a] = Tuple.Create(a, name, Math.Max(0, Math.Min(100, x.Level.Value)));
            }
            return outp.Values.ToList();
        }

        protected override List<Reading> Poll(bool force)
        {
            if (!Native.OnWindows) { Note = "Windows only"; return new List<Reading>(); }
            var outp = Parse(ReadNodes()).Select(t => new Reading("bt:" + t.Item2, t.Item2, Prov.GuessKind(t.Item2), t.Item3, false, true, false, Id,
                "Bluetooth " + t.Item1)).ToList();
            Note = outp.Count > 0 ? $"{outp.Count} device(s) with battery info" : "no Bluetooth devices with battery info";
            return outp;
        }
    }
}
