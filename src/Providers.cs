// Providers.cs – where battery levels come from. Each source returns Readings; the widget only sees Readings.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BatteryWidget
{
    public class Reading
    {
        public string Key, Name, Kind = "other", Source = "", Detail = "";
        public int? Percent;
        public bool Charging, Online = true, Approx;
        public Reading(string key, string name, string kind, int? pct = null, bool charging = false, bool online = true,
                       bool approx = false, string source = "", string detail = "")
        { Key = key; Name = name; Kind = kind; Percent = pct; Charging = charging; Online = online; Approx = approx; Source = source; Detail = detail; }
        public Reading Copy() => (Reading)MemberwiseClone();
        public override string ToString() => $"{Name} {Kind} {(Percent?.ToString() ?? "-")}% {(Online ? "" : "offline")}";
    }

    public abstract class Provider
    {
        public static readonly object IoLock = new object();     // all HID talk is serialized
        public virtual string Id => "base";
        public virtual string Label => "Base";
        public virtual string Hint => "";
        public virtual double MinInterval => 0;
        public virtual bool UsesHid => true;
        public string Error, Note = "";
        public bool Deep;
        protected readonly HashSet<string> Seen = new HashSet<string>();
        readonly object _runLock = new object();
        DateTime _last = DateTime.MinValue;
        List<Reading> _cache = new List<Reading>();

        protected abstract List<Reading> Poll(bool force);

        public List<Reading> Run(bool force)
        {
            lock (_runLock)
            {
                if (!force && MinInterval > 0 && (DateTime.UtcNow - _last).TotalSeconds < MinInterval) return _cache;
                try
                {
                    if (UsesHid) { lock (IoLock) _cache = Poll(force); }
                    else _cache = Poll(force);
                    Error = null;
                }
                catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; _cache = new List<Reading>(); }
                _last = DateTime.UtcNow;
                return _cache;
            }
        }

        protected List<Reading> OfflineIfSeen(string key, string name, string kind, string detail = "") =>
            Seen.Contains(key) ? new List<Reading> { new Reading(key, name, kind, null, false, false, false, Id, detail) } : new List<Reading>();

        protected static HidBackend Hid => HidBackend.Current;
    }

    public static class Prov
    {
        public static readonly int[][] Lipo = { new[] { 4186, 100 }, new[] { 4067, 90 }, new[] { 3989, 80 }, new[] { 3922, 70 },
            new[] { 3859, 60 }, new[] { 3811, 50 }, new[] { 3778, 40 }, new[] { 3750, 30 }, new[] { 3722, 20 }, new[] { 3700, 10 },
            new[] { 3600, 5 }, new[] { 3300, 0 } };

        public static int CurvePercent(int mv, int[][] curve)
        {
            if (mv >= curve[0][0]) return 100;
            for (int i = 0; i + 1 < curve.Length; i++)
            {
                int v1 = curve[i][0], p1 = curve[i][1], v2 = curve[i + 1][0], p2 = curve[i + 1][1];
                if (mv >= v2) return (int)Math.Round(p2 + (p1 - p2) * (mv - v2) / (double)(v1 - v2));
            }
            return 0;
        }

        static readonly KeyValuePair<string, Regex>[] KindRules = {
            new KeyValuePair<string, Regex>("controller", new Regex(@"controller|gamepad|xbox|dualsense|dualshock|joy-?con|8bitdo", RegexOptions.IgnoreCase)),
            new KeyValuePair<string, Regex>("headset", new Regex(@"head(set|phone)|buds|airpods|earbud|ear ?phone|\bwh-|\bwf-|arctis|kraken|\bcloud\b|virtuoso|\bvoid\b|\bhs\d|barracuda|blackshark|astro|inzone|maxwell|audio|hands-free|\bpods\b|g(33|43|53|63|73|93)\d|pro x( 2)?( wireless)? headset|elo|nari|stealth|openjump|speaker", RegexOptions.IgnoreCase)),
            new KeyValuePair<string, Regex>("keyboard", new Regex(@"keyboard|\bkeys\b|\bkb\b|\btkl\b|blackwidow|huntsman|deathstalker|ornata|cynosa|apex|strix scope|azoth|falchion|monsgeek|akko|keychron k|epomaker|nuphy|wooting|ducky|royal ?kludge|\brk\d|aula|ajazz|womier|feker|m1 ?v5|fun60|g(213|413|513|613|715|815|915|pro k)", RegexOptions.IgnoreCase)),
            new KeyValuePair<string, Regex>("mouse", new Regex(@"mouse|mice|viper|deathadder|basilisk|naga|orochi|cobra|mamba|lancehead|atheris|superlight|mx master|mx anywhere|harpoon|ironclaw|dark ?core|\bm65\b|sabre|nightsword|aerox|rival|\bprime\b|pulsefire|model [odi]\b|xlite|lamzu|\bvxe\b|\br1\b|\batk\b|zowie|trackball|trackpad|mchose|keychron m|g(30[345]|40[23]|50[0-9]|60[0-9]|70[0-9]|90[0-9])\b|g502", RegexOptions.IgnoreCase)),
        };

        public static string GuessKind(string name)
        {
            foreach (var r in KindRules) if (r.Value.IsMatch(name ?? "")) return r.Key;
            return "other";
        }

        static readonly Regex Noise = new Regex(@"\b(2\.4\s?g(hz)?|usb|receiver|dongle|wireless|transceiver|nano|rf)\b|[-–]?\s*\d+\s?k\s*hz\b|[-–]\s*\d+k\b|\(\s*\)", RegexOptions.IgnoreCase);
        public static string CleanName(string s, string fallback = "Device")
        {
            var o = Regex.Replace(Noise.Replace(s ?? "", " "), @"\s{2,}", " ").Trim(' ', '-', '–', '_', '·');
            return string.IsNullOrEmpty(o) ? fallback : o;
        }

        static readonly string[] WirelessWords = { "receiver", "dongle", "wireless", "2.4g", "2.4 g", "lightspeed", "unifying", "bolt",
            "hyperspeed", "slipstream", "headset", "rf ", "nano", "transceiver", "link" };
        public static bool LooksWireless(params string[] texts)
        {
            var t = string.Join(" ", texts.Where(x => x != null)).ToLowerInvariant();
            return WirelessWords.Any(w => t.Contains(w));
        }

        /// <summary>Output report first, feature report as fallback (some dongles only take one).</summary>
        public static bool Send(IHidDevice dev, byte[] pkt)
        {
            try { if (dev.Write(pkt)) return true; } catch { }
            try { return dev.SendFeature(pkt); } catch { return false; }
        }

        /// <summary>Reads input reports until handle(report) returns true or the time is up.</summary>
        public static void ReadUntil(IHidDevice dev, double seconds, Func<byte[], bool> handle)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) return;
                byte[] d;
                try { d = dev.Read(Math.Max(1, left)); } catch { return; }
                if (d != null && handle(d)) return;
            }
        }

        public static IHidDevice Open(HidInfo i) { try { return HidBackend.Current.Open(i); } catch { return null; } }
        public static string Hex(byte[] d, int n = 16) => d == null ? "" : string.Join(" ", d.Take(n).Select(b => b.ToString("x2")));
        public static byte[] Pad(int len, params int[] head) { var b = new byte[len]; for (int i = 0; i < head.Length && i < len; i++) b[i] = (byte)head[i]; return b; }

        /// <summary>Keys that turned out to be another source's name for the same device (old key -> kept key).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Aliases = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        static readonly HashSet<string> Filler = new HashSet<string> { "LOGITECH", "WIRELESS", "GAMING", "MOUSE", "LIGHTSPEED", "KEYBOARD", "HEADSET", "RECEIVER", "USB" };
        static HashSet<string> Tokens(string s) => new HashSet<string>(Regex.Split((s ?? "").ToUpperInvariant(), "[^A-Z0-9]+").Where(t => t.Length >= 2 && !Filler.Contains(t)));

        public static List<Reading> Dedupe(IEnumerable<Reading> readings)
        {
            var list = readings.ToList();
            // G HUB and HID++ name the same Logitech device differently: pair them (shared model word, or the only one of its kind)
            foreach (var gh in list.Where(r => r.Source == "ghub").ToList())
            {
                var mates = list.Where(r => r.Source == "logitech" && r.Kind == gh.Kind).ToList();
                var mate = mates.FirstOrDefault(r => Tokens(r.Name).Overlaps(Tokens(gh.Name)))
                        ?? (mates.Count == 1 && list.Count(r => r.Source == "ghub" && r.Kind == gh.Kind) == 1 ? mates[0] : null);
                if (mate == null) continue;
                if (gh.Online && gh.Percent.HasValue) { mate.Percent = gh.Percent; mate.Charging = gh.Charging; mate.Online = true; mate.Approx = false; }
                Aliases[gh.Key] = mate.Key;
                list.Remove(gh);
            }
            Func<string, string> norm = s => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
            var outp = new List<Reading>();
            foreach (var r in list)
            {
                string n = norm(r.Name);
                Reading dup = null;
                foreach (var o in outp)
                {
                    string m = norm(o.Name);
                    bool sameKind = o.Kind == r.Kind || o.Kind == "other" || r.Kind == "other";
                    if (o.Key == r.Key || (sameKind && n.Length > 2 && m.Length > 2 && (n.Contains(m) || m.Contains(n)))) { dup = o; break; }
                }
                if (dup == null) outp.Add(r);
                else
                {
                    if (dup.Key != r.Key) Aliases[r.Key] = dup.Key;
                    if (r.Online && !dup.Online) outp[outp.IndexOf(dup)] = r;
                }
            }
            return outp;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logitech G HUB – its local websocket API (the same numbers G HUB shows)
    // ──────────────────────────────────────────────────────────────────────────
    class MiniWs : IDisposable
    {
        readonly TcpClient _tcp;
        readonly NetworkStream _s;
        readonly List<byte> _buf = new List<byte>();

        public MiniWs(string host, int port, int timeoutMs)
        {
            _tcp = new TcpClient();
            var ar = _tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !_tcp.Connected) { _tcp.Close(); throw new IOException("not running"); }
            _tcp.EndConnect(ar);
            _s = _tcp.GetStream();
            string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string req = $"GET / HTTP/1.1\r\nHost: {host}:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n" +
                         "Sec-WebSocket-Version: 13\r\nOrigin: file://\r\nPragma: no-cache\r\nCache-Control: no-cache\r\nSec-WebSocket-Protocol: json\r\n\r\n";
            var rb = Encoding.ASCII.GetBytes(req);
            _s.Write(rb, 0, rb.Length);
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var head = new List<byte>();
            var one = new byte[4096];
            while (true)
            {
                int idx = IndexOf(head, new byte[] { 13, 10, 13, 10 });
                if (idx >= 0)
                {
                    string status = Encoding.ASCII.GetString(head.Take(idx).ToArray()).Split('\n')[0];
                    if (!status.Contains(" 101")) throw new IOException("websocket refused: " + status);
                    _buf.AddRange(head.Skip(idx + 4));
                    break;
                }
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0 || head.Count > 65536) throw new IOException("handshake failed");
                _tcp.ReceiveTimeout = left;
                int n = _s.Read(one, 0, one.Length);
                if (n <= 0) throw new IOException("closed");
                head.AddRange(one.Take(n));
            }
        }

        static int IndexOf(List<byte> h, byte[] pat)
        {
            for (int i = 0; i + pat.Length <= h.Count; i++)
            {
                bool ok = true;
                for (int j = 0; j < pat.Length; j++) if (h[i + j] != pat[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        public void Send(string text, int opcode = 1) => SendBytes(Encoding.UTF8.GetBytes(text), opcode);

        void SendBytes(byte[] p, int opcode)
        {
            var f = new List<byte> { (byte)(0x80 | opcode) };
            int n = p.Length;
            if (n < 126) f.Add((byte)(0x80 | n));
            else if (n < 65536) { f.Add(0x80 | 126); f.Add((byte)(n >> 8)); f.Add((byte)n); }
            else { f.Add(0x80 | 127); for (int i = 7; i >= 0; i--) f.Add((byte)((long)n >> (8 * i))); }
            var mask = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(mask);
            f.AddRange(mask);
            for (int i = 0; i < n; i++) f.Add((byte)(p[i] ^ mask[i & 3]));
            var arr = f.ToArray();
            _s.Write(arr, 0, arr.Length);
        }

        byte[] Exact(int n, DateTime deadline)
        {
            var one = new byte[65536];
            while (_buf.Count < n)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) throw new TimeoutException();
                _tcp.ReceiveTimeout = left;
                int got;
                try { got = _s.Read(one, 0, one.Length); }
                catch (IOException) { throw new TimeoutException(); }
                if (got <= 0) throw new IOException("closed");
                _buf.AddRange(one.Take(got));
            }
            var r = _buf.Take(n).ToArray();
            _buf.RemoveRange(0, n);
            return r;
        }

        /// <summary>Next text message, or null on timeout.</summary>
        public string Recv(double seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0.01, seconds));
            var parts = new List<byte>();
            try
            {
                while (true)
                {
                    var h = Exact(2, deadline);
                    long n = h[1] & 0x7F;
                    if (n == 126) { var e = Exact(2, deadline); n = (e[0] << 8) | e[1]; }
                    else if (n == 127) { var e = Exact(8, deadline); n = 0; foreach (var b in e) n = (n << 8) | b; }
                    byte[] mask = (h[1] & 0x80) != 0 ? Exact(4, deadline) : null;
                    var payload = Exact((int)n, deadline);
                    if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
                    int op = h[0] & 0x0F;
                    if (op == 8) throw new IOException("closed by server");
                    if (op == 9) { SendBytes(payload, 0xA); continue; }
                    if (op == 0xA) continue;
                    parts.AddRange(payload);
                    if ((h[0] & 0x80) != 0) return Encoding.UTF8.GetString(parts.ToArray());
                }
            }
            catch (TimeoutException) { return null; }
        }

        public void Dispose()
        {
            try { SendBytes(new byte[0], 8); } catch { }
            try { _tcp.Close(); } catch { }
        }
    }

    public class GHubProvider : Provider
    {
        public override string Id => "ghub";
        public override string Label => "Logitech G HUB";
        public override string Hint => "Optional – Logitech devices are read directly. If G HUB happens to run, its numbers are used.";
        public override bool UsesHid => false;
        public static int Port = 9010;
        static readonly Dictionary<string, string> Kinds = new Dictionary<string, string> {
            { "mouse", "mouse" }, { "keyboard", "keyboard" }, { "headset", "headset" }, { "headphones", "headset" },
            { "gamepad", "controller" }, { "controller", "controller" } };

        protected override List<Reading> Poll(bool force)
        {
            MiniWs ws;
            try { ws = new MiniWs("127.0.0.1", Port, 1500); }
            catch { Note = "G HUB is not running"; return new List<Reading>(); }
            var want = new Dictionary<string, string[]>();
            var got = new Dictionary<string, Dictionary<string, object>>();
            using (ws)
            {
                ws.Send("{\"msgId\":\"\",\"verb\":\"GET\",\"path\":\"/devices/list\"}");
                List<object> infos = null;
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (infos == null && DateTime.UtcNow < deadline)
                {
                    var msg = ws.Recv((deadline - DateTime.UtcNow).TotalSeconds);
                    if (msg == null) break;
                    var m = Cfg.FromJson(msg) as Dictionary<string, object>;
                    if (m != null && Cfg.S(m, "path") == "/devices/list")
                        infos = (Cfg.SubDict(m, "payload").TryGetValue("deviceInfos", out var di) ? di as List<object> : null) ?? new List<object>();
                }
                if (infos == null) { Note = "G HUB is starting or didn't answer"; return new List<Reading>(); }
                foreach (var o in infos)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var caps = Cfg.SubDict(d, "capabilities");
                    if (!(caps.TryGetValue("hasBatteryStatus", out var hb) && hb is bool b && b)) continue;
                    string did = Cfg.S(d, "id", "");
                    string name = Cfg.S(d, "extendedDisplayName") ?? Cfg.S(d, "displayName") ?? Cfg.S(d, "name") ?? did;
                    string kind;
                    if (!Kinds.TryGetValue(Cfg.S(d, "deviceType", "").ToLowerInvariant(), out kind)) kind = Prov.GuessKind(name);
                    want[did] = new[] { name, kind };
                    ws.Send("{\"msgId\":\"\",\"verb\":\"GET\",\"path\":\"/battery/" + did + "/state\"}");
                }
                deadline = DateTime.UtcNow.AddSeconds(3);
                var rx = new Regex("^/battery/([^/]+)/state$");
                while (got.Count < want.Count && DateTime.UtcNow < deadline)
                {
                    var msg = ws.Recv((deadline - DateTime.UtcNow).TotalSeconds);
                    if (msg == null) break;
                    var m = Cfg.FromJson(msg) as Dictionary<string, object>;
                    var mm = m == null ? null : rx.Match(Cfg.S(m, "path", ""));
                    if (mm != null && mm.Success && want.ContainsKey(mm.Groups[1].Value)) got[mm.Groups[1].Value] = m;
                }
            }
            var outp = new List<Reading>();
            foreach (var kv in want)
            {
                string name = kv.Value[0], kind = kv.Value[1], key = "logitech:" + name, det = "G HUB · " + kv.Key;
                Dictionary<string, object> m;
                got.TryGetValue(kv.Key, out m);
                var payload = m == null ? null : Cfg.SubDict(m, "payload");
                string code = m == null ? "" : Cfg.S(Cfg.SubDict(m, "result"), "code", "SUCCESS").ToUpperInvariant();
                double? pct = Cfg.N(payload, "percentage");
                if (m != null && code == "SUCCESS" && pct.HasValue)
                {
                    Seen.Add(key);
                    bool chg = payload.TryGetValue("charging", out var c) && c is bool cb && cb;
                    outp.Add(new Reading(key, name, kind, Math.Max(0, Math.Min(100, (int)pct.Value)), chg, true, false, Id, det));
                }
                else outp.AddRange(OfflineIfSeen(key, name, kind, det));
            }
            Note = $"connected · {want.Count} device(s) with battery";
            return outp;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Logitech HID++ 2.0 – receivers, cable, Bluetooth and headsets (G535, G533, G733, G935, PRO X …)
    // ──────────────────────────────────────────────────────────────────────────
    public class LogitechProvider : Provider
    {
        public override string Id => "logitech";
        public override string Label => "Logitech (HID++)";
        public override string Hint => "Turn the device on and move it / put the headset on, then scan again.";
        const int Vid = 0x046D;
        const byte SwId = 0x0A;
        static readonly int[] BatteryFeatures = { 0x1004, 0x1000, 0x1001, 0x1F20 };
        static readonly Dictionary<int, string> TypeKind = new Dictionary<int, string> { { 0, "keyboard" }, { 2, "keyboard" }, { 3, "mouse" }, { 4, "mouse" }, { 5, "mouse" }, { 8, "headset" } };
        static readonly int[][] G535 = { new[] { 4175, 100 }, new[] { 3817, 50 }, new[] { 3766, 30 }, new[] { 3730, 20 }, new[] { 3664, 5 }, new[] { 3310, 0 } };
        static readonly int[][] G533 = { new[] { 4200, 100 }, new[] { 3850, 50 }, new[] { 3790, 30 }, new[] { 3750, 20 }, new[] { 3680, 5 }, new[] { 3330, 0 } };
        static readonly int[][] G633 = { new[] { 4100, 100 }, new[] { 3950, 80 }, new[] { 3850, 60 }, new[] { 3750, 40 }, new[] { 3650, 20 }, new[] { 3500, 10 }, new[] { 3300, 5 }, new[] { 3150, 0 } };
        static readonly int[][] GPRO = { new[] { 4150, 100 }, new[] { 3830, 50 }, new[] { 3780, 30 }, new[] { 3740, 20 }, new[] { 3670, 5 }, new[] { 3320, 0 } };

        class Known { public int FIdx, FByte, Page, Usage; public string Name; public int[][] Curve; }
        static Known K(int fidx, int fbyte, string name, int[][] c, int page = 0xFF43, int usage = 0x0202) =>
            new Known { FIdx = fidx, FByte = fbyte, Name = name, Curve = c, Page = page, Usage = usage };
        static readonly Dictionary<int, Known> Headsets = new Dictionary<int, Known> {
            { 0x0AC4, K(0x05, 0x0D, "G535", G535, 0x000C, 0x0001) }, { 0x0A66, K(0x07, 0x01, "G533", G533) },
            { 0x0A5C, K(0x08, 0x0A, "G633", G633) }, { 0x0A89, K(0x08, 0x0A, "G635", G633) }, { 0x0A5B, K(0x08, 0x0A, "G933", G633) },
            { 0x0A87, K(0x08, 0x0A, "G935", G633) }, { 0x0AB5, K(0x08, 0x0A, "G733", G633) }, { 0x0AFE, K(0x08, 0x0A, "G733", G633) },
            { 0x0B1F, K(0x08, 0x0A, "G733", G633) }, { 0x0AA7, K(0x06, 0x0D, "G PRO", GPRO) }, { 0x0AAA, K(0x06, 0x0D, "G PRO X", GPRO) },
            { 0x0ABA, K(0x06, 0x0D, "G PRO X", GPRO) }, { 0x0AFB, K(0x06, 0x0D, "G PRO X 2", GPRO) }, { 0x0AFC, K(0x06, 0x0D, "G PRO X 2", GPRO) } };

        static bool Audio(int pid) => pid >= 0x0A00 && pid <= 0x0BFF;

        public class Target { public string Path; public int Idx, Pid, Fid, FIdx; public int? FByte; public string Name, Kind; public int[][] Curve;
            public string Key => "logitech:" + Name; public string Detail => $"{Vid:x4}:{Pid:x4} · HID++ 0x{Fid:x4}"; }

        public List<Target> Targets = new List<Target>();
        DateTime _scanned = DateTime.MinValue;
        string _paths = "";
        bool _missing;

        byte[] Xfer(IHidDevice dev, int idx, int feat, int func, byte[] prm = null, double timeout = 0.6, int? fbyte = null)
        {
            var req = new byte[20];
            req[0] = 0x11; req[1] = (byte)idx; req[2] = (byte)feat;
            req[3] = (byte)(fbyte ?? (((func & 0xF) << 4) | SwId));
            if (prm != null) Array.Copy(prm, 0, req, 4, Math.Min(prm.Length, 16));
            if (!dev.Write(req)) return null;
            byte want = req[3];
            byte[] result = null;
            bool done = false;
            Prov.ReadUntil(dev, timeout, d =>
            {
                if (d.Length < 5) return false;
                if (d[1] != idx && idx != 0xFF) return false;
                if (d[2] == 0xFF && d[3] == feat) { done = true; return true; }
                if (d[2] == 0x8F) { done = true; return true; }
                if (d[2] == feat && d[3] == want) { result = d.Skip(4).ToArray(); done = true; return true; }
                return false;
            });
            return done ? result : null;
        }

        int FeatureIndex(IHidDevice dev, int idx, int fid)
        {
            var r = Xfer(dev, idx, 0, 0, new[] { (byte)(fid >> 8), (byte)fid });
            return r != null && r.Length > 0 ? r[0] : 0;
        }

        void NameKind(IHidDevice dev, int idx, string fallback, out string name, out string kind)
        {
            name = fallback; kind = "other";
            int fi = FeatureIndex(dev, idx, 0x0005);
            if (fi == 0) return;
            var r = Xfer(dev, idx, fi, 0);
            int count = r != null && r.Length > 0 ? r[0] : 0;
            var buf = new List<byte>();
            while (count > 0 && buf.Count < count)
            {
                var chunk = Xfer(dev, idx, fi, 1, new[] { (byte)buf.Count });
                if (chunk == null) break;
                buf.AddRange(chunk.Take(Math.Min(16, count - buf.Count)));
            }
            var s = Encoding.UTF8.GetString(buf.ToArray()).Trim('\0', ' ');
            if (s.Length > 0) name = s;
            var t = Xfer(dev, idx, fi, 2);
            int type = t != null && t.Length > 0 ? t[0] : -1;
            if (type == 7) { kind = "receiver"; return; }
            if (!TypeKind.TryGetValue(type, out kind)) kind = "other";
        }

        Target Probe(IHidDevice dev, HidInfo e, int idx)
        {
            var r = Xfer(dev, idx, 0, 1, null, 0.35);                       // ping
            if (r == null || r.Length < 1 || r[0] < 2) return null;         // HID++ 2.0+
            int fid = 0, fidx = 0;
            foreach (var f in BatteryFeatures) { int fi = FeatureIndex(dev, idx, f); if (fi != 0) { fid = f; fidx = fi; break; } }
            if (fid == 0) return null;
            Known known; Headsets.TryGetValue(e.Pid, out known);
            string fallback = known != null ? known.Name : Prov.CleanName(e.Product, "Logitech device");
            string name, kind;
            NameKind(dev, idx, fallback, out name, out kind);
            if (kind == "receiver") return null;
            if (Audio(e.Pid) && kind == "other") kind = "headset";
            var curve = known != null ? known.Curve : (fid == 0x1F20 ? G535 : null);
            return new Target { Path = e.Path, Idx = idx, Pid = e.Pid, Name = name, Kind = kind, Fid = fid, FIdx = fidx, Curve = curve };
        }

        List<HidInfo> Candidates()
        {
            var outp = Hid.Enumerate(Vid).Where(e =>
                ((e.UsagePage & 0xFF00) == 0xFF00 && (e.Usage == 1 || e.Usage == 2 || e.Usage == 0x0202)) ||
                (Audio(e.Pid) && e.UsagePage == 0x000C)).ToList();
            return outp.OrderBy(e => e.Usage == 2 || e.Usage == 0x0202 ? 0 : e.Usage == 1 ? 1 : 2).ToList();
        }

        void Scan(List<HidInfo> entries)
        {
            _scanned = DateTime.UtcNow;
            var found = new List<Target>();
            var seen = new HashSet<string>();
            foreach (var e in entries)
            {
                var dev = Prov.Open(e);
                if (dev == null) continue;
                using (dev)
                {
                    int[] idxs = Audio(e.Pid) ? new[] { 0xFF } : new[] { 1, 2, 3, 4, 5, 6, 0xFF };
                    foreach (var idx in idxs)
                    {
                        if (found.Any(x => x.Path == e.Path && x.Idx == idx)) continue;
                        var t = Probe(dev, e, idx);
                        if (t != null && seen.Add($"{t.Pid}/{t.Idx}/{t.Name}")) found.Add(t);
                    }
                }
            }
            var have = new HashSet<int>(found.Select(t => t.Pid));
            bool missing = false;
            foreach (int pid in entries.Select(x => x.Pid).Distinct().Where(p => !have.Contains(p)).OrderBy(p => p))
            {
                if (!Audio(pid)) continue;
                missing = true;
                Known k;
                if (!Headsets.TryGetValue(pid, out k)) continue;
                var mine = entries.Where(x => x.Pid == pid).ToList();
                var e = mine.FirstOrDefault(x => x.UsagePage == k.Page && x.Usage == k.Usage) ?? mine[0];
                found.Add(new Target { Path = e.Path, Idx = 0xFF, Pid = pid, Name = k.Name, Kind = "headset", Fid = 0x1F20, FIdx = k.FIdx, FByte = k.FByte, Curve = k.Curve });
            }
            _missing = missing;
            var paths = new HashSet<string>(entries.Select(x => x.Path));
            foreach (var old in Targets)                 // asleep right now: keep it known (shown offline)
                if (paths.Contains(old.Path) && !found.Any(t => t.Pid == old.Pid && t.Idx == old.Idx)) found.Add(old);
            Targets = found;
        }

        bool ReadBattery(IHidDevice dev, Target t, out int pct, out bool charging, out bool approx)
        {
            pct = 0; charging = false; approx = false;
            byte[] r;
            switch (t.Fid)
            {
                case 0x1004:
                    r = Xfer(dev, t.Idx, t.FIdx, 1);
                    if (r == null || r.Length < 3) return false;
                    pct = r[0];
                    if (pct == 0 && r[1] != 0) { pct = (r[1] & 1) != 0 ? 5 : (r[1] & 2) != 0 ? 20 : (r[1] & 4) != 0 ? 60 : 95; approx = true; }
                    charging = r[2] >= 1 && r[2] <= 3;
                    return true;
                case 0x1000:
                    r = Xfer(dev, t.Idx, t.FIdx, 0);
                    if (r == null || r.Length < 3) return false;
                    pct = r[0]; charging = r[2] >= 1 && r[2] <= 4;
                    return true;
                case 0x1001:
                    r = Xfer(dev, t.Idx, t.FIdx, 0);
                    if (r == null || r.Length < 3) return false;
                    pct = Prov.CurvePercent((r[0] << 8) | r[1], t.Curve ?? Prov.Lipo); charging = (r[2] & 0x80) != 0; approx = true;
                    return true;
                case 0x1F20:
                    r = Xfer(dev, t.Idx, t.FIdx, 0, null, 0.8, t.FByte);
                    if (r == null || r.Length < 3) return false;
                    int mv = (r[0] << 8) | r[1];
                    if (mv < 3000) return false;
                    pct = Prov.CurvePercent(mv, t.Curve ?? G535); charging = (r[2] & 0x02) != 0; approx = true;
                    return true;
            }
            return false;
        }

        protected override List<Reading> Poll(bool force)
        {
            var entries = Candidates();
            string paths = string.Join("|", entries.Select(e => e.Path).OrderBy(p => p));
            double age = (DateTime.UtcNow - _scanned).TotalSeconds;
            if (force || Targets.Count == 0 || paths != _paths || age > (_missing ? 90 : 600)) { _paths = paths; Scan(entries); }
            var outp = new List<Reading>();
            foreach (var grp in Targets.GroupBy(t => t.Path))
            {
                var info = entries.FirstOrDefault(e => e.Path == grp.Key);
                var dev = info == null ? null : Prov.Open(info);
                if (dev == null)
                {
                    foreach (var t in grp) outp.Add(new Reading(t.Key, t.Name, t.Kind, null, false, false, false, Id, t.Detail));
                    Targets = new List<Target>();
                    continue;
                }
                using (dev)
                    foreach (var t in grp)
                    {
                        int pct; bool chg, approx;
                        if (ReadBattery(dev, t, out pct, out chg, out approx))
                            outp.Add(new Reading(t.Key, t.Name, t.Kind, Math.Max(0, Math.Min(100, pct)), chg, true, approx, Id, t.Detail));
                        else outp.Add(new Reading(t.Key, t.Name, t.Kind, null, false, false, false, Id, t.Detail));
                    }
            }
            Note = entries.Count > 0 ? $"{entries.Count} interface(s), {Targets.Count} device(s)" : "no Logitech receivers";
            return outp;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Razer – the control protocol OpenRazer documents (90-byte feature reports)
    // ──────────────────────────────────────────────────────────────────────────
    public class RazerProvider : Provider
    {
        public override string Id => "razer";
        public override string Label => "Razer";
        public override string Hint => "Wake the device (move the mouse / press a key) and scan again.";
        static readonly int[] Tids = { 0x1F, 0x3F, 0xFF, 0x9F };
        readonly Dictionary<string, KeyValuePair<string, int>> _good = new Dictionary<string, KeyValuePair<string, int>>();
        readonly Dictionary<string, DateTime> _neg = new Dictionary<string, DateTime>();

        static byte[] Packet(int tid, int cls, int cmd, int size)
        {
            var rep = new byte[91];          // report id 0 + 90 bytes
            rep[2] = (byte)tid; rep[6] = (byte)size; rep[7] = (byte)cls; rep[8] = (byte)cmd;
            byte crc = 0;
            for (int i = 3; i < 89; i++) crc ^= rep[i];
            rep[89] = crc;
            return rep;
        }

        static byte[] Ask(IHidDevice dev, int tid, int cls, int cmd)
        {
            try { if (!dev.SendFeature(Packet(tid, cls, cmd, 2))) return null; } catch { return null; }
            foreach (int wait in new[] { 50, 80, 150 })
            {
                Thread.Sleep(wait);
                byte[] r;
                try { r = dev.GetFeature(0, 91); } catch { return null; }
                if (r == null || r.Length < 11) return null;
                if (r[1] == 0x01) continue;                            // busy
                if (r[1] == 0x02 && r[7] == cls && r[8] == cmd) return r;
                return null;
            }
            return null;
        }

        protected override List<Reading> Poll(bool force)
        {
            var groups = Hid.Enumerate(0x1532).GroupBy(e => e.VidPid).ToList();
            var outp = new List<Reading>();
            foreach (var g in groups)
            {
                string vp = g.Key;
                var entries = g.OrderBy(e => e.Interface).ThenBy(e => e.UsagePage).ToList();
                string product = entries.Select(e => e.Product).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "Razer device";
                string name = Prov.CleanName(product.StartsWith("Razer", StringComparison.OrdinalIgnoreCase) ? product : "Razer " + product, "Razer device");
                string kind = Prov.GuessKind(product), key = "usb:" + vp, det = vp + " · Razer";
                DateTime negAt;
                if (!force && !_good.ContainsKey(vp) && _neg.TryGetValue(vp, out negAt) && (DateTime.UtcNow - negAt).TotalSeconds < 600) continue;
                var tries = new List<KeyValuePair<string, int>>();
                if (_good.ContainsKey(vp)) tries.Add(_good[vp]);
                foreach (var e in entries) foreach (var t in Tids) tries.Add(new KeyValuePair<string, int>(e.Path, t));
                int? pct = null; bool chg = false;
                foreach (var tr in tries)
                {
                    var info = entries.FirstOrDefault(e => e.Path == tr.Key);
                    var dev = info == null ? null : Prov.Open(info);
                    if (dev == null) continue;
                    using (dev)
                    {
                        var r = Ask(dev, tr.Value, 0x07, 0x80);
                        if (r != null)
                        {
                            var c = Ask(dev, tr.Value, 0x07, 0x84);
                            pct = (int)Math.Round(r[10] * 100 / 255.0); chg = c != null && c[10] != 0;
                            _good[vp] = tr;
                            break;
                        }
                    }
                }
                if (pct == null) { if (!_good.ContainsKey(vp)) _neg[vp] = DateTime.UtcNow; outp.AddRange(OfflineIfSeen(key, name, kind, det)); continue; }
                _neg.Remove(vp);
                if (pct == 0 && !chg) { outp.AddRange(OfflineIfSeen(key, name, kind, det)); continue; }
                Seen.Add(key);
                outp.Add(new Reading(key, name, kind, Math.Max(0, Math.Min(100, pct.Value)), chg, true, false, Id, det));
            }
            Note = groups.Count > 0 ? $"{groups.Count} Razer device(s) connected" : "no Razer devices";
            return outp;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ASUS ROG keyboards through the ROG Omni receiver (report 0x02, 12 01)
    // ──────────────────────────────────────────────────────────────────────────
    public class AsusRogProvider : Provider
    {
        public override string Id => "asus";
        public override string Label => "ASUS ROG keyboard (Omni receiver)";
        public override string Hint => "Press a key on the keyboard to wake it, then scan again.";
        public static string KeyboardName = "ROG Strix Scope II 96";
        string _goodPath;

        static int? Query(HidInfo e, out bool charging)
        {
            charging = false;
            var dev = Prov.Open(e);
            if (dev == null) return null;
            using (dev)
            {
                var req = Prov.Pad(64, 0x02, 0x12, 0x01);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!dev.Write(req)) return null;
                    int? pct = null; bool chg = false;
                    Prov.ReadUntil(dev, 0.8, d =>
                    {
                        if (d.Length < 10 || d[0] != 0x02 || d[1] != 0x12 || d[2] != 0x01) return false;
                        if (d[6] <= 100) { pct = d[6]; chg = d[9] == 1; }
                        return true;
                    });
                    if (pct != null) { charging = chg; return pct; }
                }
                return null;
            }
        }

        protected override List<Reading> Poll(bool force)
        {
            var entries = Hid.Enumerate(0x0B05).Where(e => e.UsagePage >= 0xFF00)
                .OrderBy(e => e.Path == _goodPath ? 0 : 1).ThenBy(e => e.UsagePage == 0xFF00 ? 0 : 1)
                .ThenBy(e => e.Path.ToLowerInvariant().Contains("col02") ? 0 : 1).ToList();
            string key = "asus:" + KeyboardName;
            foreach (var e in entries)
            {
                bool chg;
                var pct = Query(e, out chg);
                if (pct == null) continue;
                _goodPath = e.Path;
                Seen.Add(key);
                return new List<Reading> { new Reading(key, KeyboardName, "keyboard", pct, chg, true, false, Id, e.VidPid + " · ROG") };
            }
            Note = entries.Count > 0 ? "no ROG keyboard answered" : "no ASUS receivers";
            return OfflineIfSeen(key, KeyboardName, "keyboard");
        }
    }
}
