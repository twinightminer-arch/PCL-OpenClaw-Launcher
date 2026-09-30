import pathlib
import sys

VER = sys.argv[1] if len(sys.argv) > 1 else "0.8.0"
ROOT = pathlib.Path(r"E:\openclaw\ocl")
TM = ROOT / "installer" / "ocl.iss.tmpl"
text = TM.read_text(encoding="utf-8")
text = text.replace("__VER__", VER)
text = text.replace("__SRC__", str(ROOT / ("app-v" + VER)))
text = text.replace("__OUT__", str(ROOT / "installers" / VER))
out = ROOT / "installer" / ("ocl-" + VER + ".iss")
out.write_text(text, encoding="utf-8-sig")
print("written:", out, out.stat().st_size, "bytes")
for line in text.splitlines():
    if line.startswith(("OutputDir", "OutputBaseFilename", "DefaultDirName", "AppVersion")):
        print("  ", line)
