using System; using System.Collections.Generic; using System.Linq; using BatteryWidget;
static class A { static int fail; static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static int Main() {
    Func<string, string, string, string> v = (src, det, name) => Apps.Vendor(new Reading("k", name, "mouse", 50, source: src, detail: det));
    Check("maker from source / USB id", v("ghub", "G HUB · dev1", "G502 X") == "logitech" && v("razer", "1532:007c · Razer", "") == "razer"
      && v("headsets", "1038:12ad · built-in", "SteelSeries Arctis 7") == "steelseries" && v("headsets", "1b1c:0a14 · x", "CORSAIR VOID") == "corsair"
      && v("hyperx", "03f0:05b7 · HyperX 66", "") == "hyperx" && v("rongyuan", "3151:5038 · RongYuan F7", "") == "monsgeek" && v("compx", "373b:1085 · Compx", "") == "atk"
      && v("mchose", "3837:1018 · MCHOSE", "") == "mchose" && v("keychron", "3434:d030 · Keychron", "") == "keychron" && v("asus", "0b05:1a55 · ROG", "") == "asus"
      && v("bluetooth", "Bluetooth 705A6F66996B", "EarFun") == "bluetooth" && v("hidbattery", "248a:8367 · HID battery", "2.4G Mouse") == "");
    var installed = new List<AppTarget> { new AppTarget { Name = "HyperX NGENUITY", Kind = "app", Target = "KingstonTechnology.HyperXNGENUITY_x!App" },
      new AppTarget { Name = "ARMOURY CRATE", Kind = "app", Target = "B9ECED6F.ArmouryCrate_x!App" }, new AppTarget { Name = "Spotify", Kind = "app", Target = "s" } };
    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    Apps.Exists = p => p == pf + @"\LGHUB\lghub.exe" || p == pf + @"\LogiOptionsPlus\logioptionsplus.exe";
    var c = Apps.Candidates("logitech", "G502 X LIGHTSPEED", () => installed);
    Check("Logitech gaming mouse -> G HUB first (standard folder)", c.Count == 2 && c[0].Name == "Logitech G HUB" && c[0].Kind == "exe" && c[0].Target.EndsWith(@"LGHUB\lghub.exe"), string.Join(",", c.Select(x => x.Name)));
    c = Apps.Candidates("logitech", "MX Master 3S", () => installed);
    Check("Logitech MX -> Logi Options+ first", c[0].Name == "Logi Options+", c[0].Name);
    c = Apps.Candidates("hyperx", "HyperX Cloud III Wireless", () => installed);
    Check("Store app found by name (NGENUITY)", c.Count == 1 && c[0].Kind == "app" && c[0].Target.Contains("NGENUITY"), c.Count > 0 ? c[0].Target : "");
    c = Apps.Candidates("asus", "ROG Strix Scope II 96", () => installed);
    Check("Armoury Crate found by name", c.Count == 1 && c[0].Name == "ARMOURY CRATE");
    Apps.Exists = p => false;
    c = Apps.Candidates("razer", "Razer DeathAdder", () => installed);
    Check("Synapse not installed -> nothing found (you get to choose)", c.Count == 0);
    c = Apps.Candidates("bluetooth", "EarFun OpenJump", () => installed);
    Check("Bluetooth -> Windows Bluetooth settings", c.Count == 1 && c[0].Kind == "uri" && c[0].Target == "ms-settings:bluetooth");
    c = Apps.Candidates("keychron", "Keychron M6", () => installed);
    Check("Keychron -> its web launcher", c.Count == 1 && c[0].Target.StartsWith("https://launcher.keychron.com"));
    var saved = AppTarget.From(new AppTarget { Name = "My tool", Kind = "exe", Target = @"C:\x\tool.exe" }.ToDict());
    Check("a chosen app is remembered", saved.Name == "My tool" && saved.Target == @"C:\x\tool.exe");
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
