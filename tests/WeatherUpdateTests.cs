using System; using System.Linq; using BatteryWidget;
static class W { static int fail; static void Check(string n, bool c, string x = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); if (!c) fail++; }
  static int Main() {
    var d = WxData.Parse(Demo.WeatherJson());
    Check("forecast: now", d.Temp == 7.3 && d.Code == 61 && d.Feels == 4.1 && d.Humidity == 81 && d.Day, $"{d.Temp} {d.Code}");
    Check("forecast: 168 hours, 7 days", d.Hours.Count == 168 && d.Days.Count == 7);
    Check("forecast: today/tomorrow highs and lows", d.Days[0].Max == 8.4 && d.Days[1].Min == 1.2 && d.Days[0].Pop == 70);
    Check("forecast: sunrise/sunset", d.Days[0].Rise.Value.ToString("HH:mm") == "07:24" && d.Days[0].Set.Value.ToString("HH:mm") == "15:54");
    Check("weather codes", WxData.Describe(0) == "Clear" && WxData.Describe(95) == "Thunderstorm" && WxIcons.Kind(82) == "heavyrain" && WxIcons.Kind(73) == "snow");
    var places = WxApi.ParsePlaces("{\"results\":[{\"name\":\"Huddinge\",\"latitude\":59.237,\"longitude\":17.982,\"country\":\"Sweden\",\"admin1\":\"Stockholm\"}]}");
    Check("place search results", places.Count == 1 && places[0].Name == "Huddinge, Stockholm, Sweden" && places[0].Lat == 59.237, places.Count > 0 ? places[0].Name : "");
    Check("url asks for °C and m/s", WxApi.ForecastUrl(59.33, 18.07, false, "ms").Contains("temperature_unit=celsius") && WxApi.ForecastUrl(59.33, 18.07, false, "ms").Contains("latitude=59.3300"));
    Check("versions: 3.1.0 > 3.0.1", Updater.Newer("v3.1.0", "3.0.1") && !Updater.Newer("3.0.1", "3.0.1") && !Updater.Newer("v3.0.9", "3.1.0") && Updater.Newer("v3.10.0", "3.9.9"));
    var rel = Updater.FromJson("{\"tag_name\":\"v3.2.0\",\"html_url\":\"https://github.com/o/r/releases/tag/v3.2.0\",\"body\":\"Weather + updates\",\"draft\":false,\"assets\":[{\"name\":\"BatteryWidget.exe\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v3.2.0/BatteryWidget.exe\",\"digest\":\"sha256:ABC123\"}]}");
    Check("release: tag, exe and its checksum", rel.Version == "3.2.0" && rel.Url.EndsWith("/BatteryWidget.exe") && rel.Sha256 == "abc123", rel.Version + " " + rel.Sha256);
    Check("draft releases are ignored", Updater.FromJson("{\"tag_name\":\"v9.0.0\",\"draft\":true,\"assets\":[]}") == null);
    var noSum = Updater.FromJson("{\"tag_name\":\"v3.2.0\",\"assets\":[{\"name\":\"BatteryWidget.exe\",\"browser_download_url\":\"x\"}]}");
    try { Updater.Install(noSum, null); Check("no checksum -> refused", false); } catch (Exception ex) { Check("no checksum -> not installed", ex.Message.Contains("checksum"), ex.Message); }
    Console.WriteLine(fail == 0 ? "\nALL PASSED" : $"\n{fail} FAILED"); return fail; } }
