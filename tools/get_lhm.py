"""Puts the official LibreHardwareMonitor 0.9.6 library (checksum-checked) into ./lhm for the build."""
import hashlib, io, os, urllib.request, zipfile
URL = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip"
SHA = "086d9f1b5a99e643edc2cfaaac16051685b551e4c5ac0b32a57c58c0e529c001"
data = urllib.request.urlopen(urllib.request.Request(URL, headers={"User-Agent": "build"}), timeout=120).read()
assert hashlib.sha256(data).hexdigest() == SHA, "LibreHardwareMonitor.zip isn't the expected file"
os.makedirs("lhm", exist_ok=True)
with zipfile.ZipFile(io.BytesIO(data)) as z:
    for n in z.namelist():
        if "/" not in n and n.lower().endswith(".dll") and not n.lower().startswith(("aga.controls", "oxyplot")):
            open(os.path.join("lhm", n), "wb").write(z.read(n))
print("lhm:", len(os.listdir("lhm")), "files")
