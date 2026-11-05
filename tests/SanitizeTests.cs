using System; using System.Collections.Generic; using System.Linq; using BatteryWidget;
static class Z { static int fail; static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static List<Metric> Cpu(double temp, double eff, double vid) => new List<Metric> {
    new Metric("lhm:/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "AMD Ryzen 9 5900X", "temp", temp, "°C", source: "lhm", device: "CPU"),
    new Metric("lhm:/amdcpu/0/clock/13", "Core #12 (Effective)", "AMD Ryzen 9 5900X", "clock", eff, "MHz", source: "lhm", device: "CPU"),
    new Metric("lhm:/amdcpu/0/voltage/12", "Core #12 VID", "AMD Ryzen 9 5900X", "voltage", vid, "V", source: "lhm", device: "CPU"),
    new Metric("lhm:/amdcpu/0/load/0", "CPU Total", "AMD Ryzen 9 5900X", "usage", 7, "%", source: "lhm", device: "CPU"),
    new Metric("lhm:/gpu-nvidia/0/temperature/0", "GPU Core", "RTX 4070", "temp", 41, "°C", source: "lhm", device: "GPU") };
  static int Main() {
    var ms = Cpu(0, 0, 1.55); var st = LhmSource.Sanitize(ms, false, false);
    Check("your picture: zeros without admin -> 'no admin rights'", st.Contains("no admin"), st);
    Check("0 °C / 0 MHz / 1.55 V are hidden, not shown as values", ms.Where(m => m.Device == "CPU" && m.Kind != "usage").All(m => m.Value == null));
    Check("CPU load (from Windows) and GPU values stay", ms.First(m => m.Kind == "usage").Value == 7 && ms.Last().Value == 41);
    ms = Cpu(0, 0, 1.55); st = LhmSource.Sanitize(ms, true, false);
    Check("admin but no PawnIO -> 'PawnIO driver missing'", st.Contains("PawnIO"), st);
    ms = Cpu(0, 0, 1.55); st = LhmSource.Sanitize(ms, true, true);
    Check("admin + PawnIO but zeros -> 'driver not responding' (restart Windows)", st.Contains("not responding"), st);
    ms = Cpu(52.4, 4380, 1.21); st = LhmSource.Sanitize(ms, true, true);
    Check("working driver -> real values kept", st == "on · 5 sensors" && ms[0].Value == 52.4 && ms[1].Value == 4380, st);
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
