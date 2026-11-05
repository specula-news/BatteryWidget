"""Builds app.res: the app icon (all sizes), a Windows 10/11 manifest and version info – for mcs -win32res."""
import struct, sys

def u16z(s): return s.encode("utf-16-le") + b"\0\0"
def pad4(b): return b + b"\0" * ((4 - len(b) % 4) % 4)

def resource(rtype, name, data, lang=0x0409):
    def ident(v): return struct.pack("<HH", 0xFFFF, v) if isinstance(v, int) else u16z(v)
    hdr = ident(rtype) + ident(name)
    hdr = pad4(struct.pack("<II", len(data), 0) + hdr)          # header size patched below
    hdr += struct.pack("<IHHII", 0, 0x1030, lang, 0, 0)          # DataVersion, MemoryFlags, LanguageId, Version, Characteristics
    hdr = struct.pack("<II", len(data), len(hdr)) + hdr[8:]
    return pad4(hdr + data)

def read_ico(path):
    d = open(path, "rb").read()
    _, _, n = struct.unpack_from("<HHH", d, 0)
    out = []
    for i in range(n):
        w, h, cc, _r, planes, bpp, size, off = struct.unpack_from("<BBBBHHII", d, 6 + 16 * i)
        out.append((w, h, cc, planes, bpp, d[off:off + size]))
    return out

def node(key, value=b"", wtype=1, value_len=None, children=()):
    body = pad4(struct.pack("<HHH", 0, 0, 0) + u16z(key)) + value
    if children:
        body = pad4(body)
        for i, c in enumerate(children):
            body += pad4(c) if i < len(children) - 1 else c
    vl = value_len if value_len is not None else len(value)
    return struct.pack("<HHH", len(body), vl, wtype) + body[6:]

def version_info(ver, strings):
    a, b, c, d = (list(map(int, ver.split("."))) + [0, 0, 0, 0])[:4]
    ms, ls = (a << 16) | b, (c << 16) | d
    fixed = struct.pack("<13I", 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x40004, 1, 0, 0, 0)
    strs = [node(k, u16z(v), 1, len(v) + 1) for k, v in strings.items()]
    table = node("040904B0", b"", 1, 0, strs)
    sfi = node("StringFileInfo", b"", 1, 0, [table])
    var = node("Translation", struct.pack("<HH", 0x0409, 1200), 0, 4)
    vfi = node("VarFileInfo", b"", 1, 0, [var])
    return node("VS_VERSION_INFO", fixed, 0, len(fixed), [sfi, vfi])

MANIFEST = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity version="3.0.0.0" name="BatteryWidget.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2"><security><requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
    <requestedExecutionLevel level="asInvoker" uiAccess="false"/></requestedPrivileges></security></trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1"><application>
    <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}"/>
    <supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}"/>
    <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}"/>
  </application></compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
  </windowsSettings></application>
  <dependency><dependentAssembly><assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0"
    processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/></dependentAssembly></dependency>
</assembly>
"""

def main(ico, out, ver):
    res = resource(0, 0, b"", 0)                                  # the empty first entry every .res starts with
    res = struct.pack("<II", 0, 32) + struct.pack("<HHHH", 0xFFFF, 0, 0xFFFF, 0) + struct.pack("<IHHII", 0, 0, 0, 0, 0)
    group = struct.pack("<HHH", 0, 1, 0)
    imgs = read_ico(ico)
    for i, (w, h, cc, planes, bpp, data) in enumerate(imgs, 1):
        res += resource(3, i, data)                               # RT_ICON
        group += struct.pack("<BBBBHHIH", w, h, cc, 0, planes or 1, bpp, len(data), i)
    group = struct.pack("<HHH", 0, 1, len(imgs)) + group[6:]
    res += resource(14, 32512, group)                             # RT_GROUP_ICON (IDI_APPLICATION – the exe's icon)
    res += resource(24, 1, MANIFEST.encode("utf-8"))              # RT_MANIFEST
    res += resource(16, 1, version_info(ver, {
        "CompanyName": "", "FileDescription": "Battery Widget", "FileVersion": ver, "InternalName": "BatteryWidget",
        "OriginalFilename": "BatteryWidget.exe", "ProductName": "Battery Widget", "ProductVersion": ver,
        "LegalCopyright": "Free. Sensor library: LibreHardwareMonitor (MPL-2.0)."}))
    open(out, "wb").write(res)
    print(f"{out}: {len(imgs)} icon sizes, manifest, version {ver} ({len(res)} bytes)")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "3.0.0")
