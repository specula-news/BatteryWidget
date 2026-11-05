# Battery Widget 3.3.0 (C#)

<img width="771" height="785" alt="image" src="https://github.com/user-attachments/assets/8790fe57-0e12-4c8d-be66-015bd4fd6132" />
<img width="443" height="759" alt="image" src="https://github.com/user-attachments/assets/27319cba-6cff-4f73-bbb3-2e867299684c" />
<img width="1174" height="1549" alt="image" src="https://github.com/user-attachments/assets/1b40907e-7da1-442d-8edc-aa191e493d78" />


One portable file: `BatteryWidget.exe`. Runs on Windows 10 and 11 as it is – no installation, no other software.
Settings are kept next to the exe (`BatteryWidget.json`, `SystemMonitor.json`); the first start imports the settings of the
earlier (Python) version.

- **Battery widget** – mice, keyboards and headsets read directly: Logitech (HID++, incl. G535/G733/G935/PRO X headsets),
  Razer, ASUS ROG, MonsGeek/Akko/Epomaker, ATK/VXE, HyperX Cloud II / III / III S / Alpha / Flight / II Core,
  SteelSeries Arctis 1/7/9/Pro Wireless and Arctis Nova 5/7/7+/Pro Wireless, Corsair Void/Virtuoso/HS headsets,
  Keychron mice, MCHOSE mice, any device using the standard HID battery report, and Bluetooth devices Windows knows the level of.
  G HUB and HeadsetControl are used if they happen to be there, but are never needed.
- **System monitor** – CPU/GPU temperature, clock, load and power, fans, memory, drives and network (built-in
  LibreHardwareMonitor 0.9.6, MPL-2.0). CPU and motherboard sensors need administrator rights (right-click →
  *Run as administrator*) and the free PawnIO driver once (*Choose what to show…* → *Install PawnIO…*).

- **Weather** – now, today and tomorrow; click the icon or the temperature for the next 24 hours and 7 days.
  Location: automatic (Windows' location service, else the IP address) or right-click → *Location → Set location…*.
  °C/°F, wind in m/s, km/h or mph. Forecast from Open-Meteo (free, no account).
- **Screen-off timer** – the moon button; a pop-up confirms the new time.

## Device apps
Click a device in the battery widget to open its maker's app: Logitech G HUB (Logi Options+ for MX devices), Razer Synapse,
SteelSeries GG, Corsair iCUE, ASUS Armoury Crate, HyperX NGENUITY, MCHOSE M HUB, MonsGeek/Akko, Keychron Launcher (web),
Windows Bluetooth settings for Bluetooth devices. Standard install folders first, then installed apps (desktop and Store)
by name; if nothing fits, a window lets you pick an app or browse for a program – remembered for that device and its maker.
Right-click a device → *Open …* / *Choose app for …*.

## Device apps
Click a device to open its maker's app: G HUB (Logi Options+ for MX devices), Razer Synapse, SteelSeries GG, Corsair iCUE,
ASUS Armoury Crate, HyperX NGENUITY, MCHOSE M HUB, MonsGeek/Akko, the Keychron web launcher, or Windows' Bluetooth settings.
The usual install folder is checked first, then every installed app (Store apps too); if none is found you pick one, and the
choice is remembered for that device and its maker. Right-click a device → *Choose app…* to change it.

## Desktop
- One theme for all widgets (Theme → *Same theme for all widgets* to switch it off and style each one on its own).
- Drag anywhere on a widget; right-click anywhere on it for its menu. Widgets snap together (and to the screen edge)
  and then move as one; **Alt + drag** moves a single widget away. **Shift + drag** reorders the values inside one.
- Look → *Blend with the wallpaper (frosted glass)*: the wallpaper behind the widget, blurred and tinted.
- Position → *Part of the wallpaper (behind desktop icons)*: the widget lives in the wallpaper layer, so desktop icons
  and the selection box go over it; it still answers clicks, drags and right-clicks.

## Updates
Every copy checks quietly at start whether this repository has a newer release, and offers **Update now / Later /
Skip this version**. Update now downloads BatteryWidget.exe, checks its SHA-256, swaps the file and restarts.

**Publishing a new version** (no compiler needed – GitHub builds it):
1. Upload the changed files to this repository.
2. *Releases → Draft a new release* → *Choose a tag* → type a higher version, e.g. `v3.6.2` → *Create new tag* →
   write what's new → **Publish release**.
3. GitHub Actions builds `BatteryWidget.exe` (version from the tag, update source = this repository), checks the sensor
   library's checksum and attaches the exe and its `.sha256` to the release (about 2 minutes, see the *Actions* tab).

The repository must be **public** so the apps can read its releases.

Not supported yet: Corsair wireless mice/keyboards, Sony INZONE, Audeze, Roccat headsets.

Build by hand (Linux/Mono): `python3 tools/get_lhm.py && python3 res/make_res.py res/icon.ico res/app.res 3.6.1 && ./build.sh out/BatteryWidget.exe`
