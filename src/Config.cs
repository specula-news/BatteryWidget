// Config.cs – settings as JSON. Portable: kept next to the exe when that folder is writable.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace BatteryWidget
{
    public class Cfg
    {
        public Dictionary<string, object> D;
        readonly string _path;
        public Cfg(Dictionary<string, object> d, string path) { D = d; _path = path; }

        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static string AppDir => Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        public static string RoamingDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryWidget");

        /// <summary>Where settings live: next to the exe (portable), or %APPDATA%\BatteryWidget if that folder is read-only.</summary>
        public static string DataDir
        {
            get
            {
                if (_dataDir != null) return _dataDir;
                try
                {
                    string probe = Path.Combine(AppDir, ".write_test");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    _dataDir = AppDir;
                }
                catch { _dataDir = RoamingDir; Directory.CreateDirectory(_dataDir); }
                return _dataDir;
            }
        }
        static string _dataDir;

        public static Cfg Load(string fileName, Dictionary<string, object> defaults, params string[] importFrom)
        {
            string path = Path.Combine(DataDir, fileName);
            var d = DeepCopy(defaults);
            string src = File.Exists(path) ? path : importFrom.FirstOrDefault(File.Exists);
            if (src != null)
            {
                try
                {
                    var saved = Js.DeserializeObject(File.ReadAllText(src)) as Dictionary<string, object>;
                    if (saved != null)
                        foreach (var kv in saved)
                        {
                            var dv = d.ContainsKey(kv.Key) ? d[kv.Key] as Dictionary<string, object> : null;
                            var sv = kv.Value as Dictionary<string, object>;
                            if (dv != null && sv != null) foreach (var x in sv) dv[x.Key] = x.Value;
                            else d[kv.Key] = Normalize(kv.Value);
                        }
                }
                catch { }
            }
            return new Cfg(d, path);
        }

        // JSON arrays come back as object[] – turn them into List<object> so they can grow
        static object Normalize(object v)
        {
            if (v is object[] arr) return arr.Select(Normalize).ToList();
            if (v is ArrayList al) return al.Cast<object>().Select(Normalize).ToList();
            if (v is Dictionary<string, object> dd) return dd.ToDictionary(k => k.Key, k => Normalize(k.Value));
            return v;
        }

        public static Dictionary<string, object> DeepCopy(Dictionary<string, object> d) =>
            (Dictionary<string, object>)Normalize(Js.DeserializeObject(Js.Serialize(d)));

        public bool Persist = true;

        public void Save()
        {
            if (!Persist) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, Js.Serialize(D));
                if (File.Exists(_path)) File.Replace(tmp, _path, null); else File.Move(tmp, _path);
            }
            catch { }
        }

        // --- typed access ------------------------------------------------------------
        public object this[string k] { get { object v; return D.TryGetValue(k, out v) ? v : null; } set { D[k] = value; } }
        public string Str(string k, string def = "") { var v = this[k]; return v == null ? def : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); }
        public bool Bool(string k, bool def = false) { var v = this[k]; return v is bool b ? b : def; }
        public double Num(string k, double def = 0) { var v = this[k]; try { return v == null ? def : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return def; } }
        public int Int(string k, int def = 0) => (int)Math.Round(Num(k, def));
        public double? NumOrNull(string k) { var v = this[k]; if (v == null) return null; try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; } }
        public Dictionary<string, object> Dict(string k)
        {
            var d = this[k] as Dictionary<string, object>;
            if (d == null) { d = new Dictionary<string, object>(); D[k] = d; }
            return d;
        }
        public List<object> List(string k)
        {
            var l = this[k] as List<object>;
            if (l == null) { l = (this[k] as IEnumerable<object>)?.ToList() ?? new List<object>(); D[k] = l; }
            return l;
        }
        public List<string> Strings(string k) => List(k).Select(x => Convert.ToString(x)).ToList();
        public void SetStrings(string k, IEnumerable<string> v) => D[k] = v.Cast<object>().ToList();

        public static Dictionary<string, object> SubDict(Dictionary<string, object> d, string k)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is Dictionary<string, object> dd) return dd;
            dd = new Dictionary<string, object>(); d[k] = dd; return dd;
        }
        public static string S(Dictionary<string, object> d, string k, string def = null) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : def; }
        public static double? N(Dictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return null;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
        }

        public static string ToJson(object o) => Js.Serialize(o);
        public static object FromJson(string s) => Normalize(Js.DeserializeObject(s));
    }
}
