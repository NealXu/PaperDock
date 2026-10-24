from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

from pywinauto import Application

from helpers import AppHelpers

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "app" / "dist" / "PaperDock.exe"
SAMPLE = ROOT / "samples" / "sample.pdf"
OUT = ROOT / "docs" / "evidence"
TEMP = ROOT / "tmp" / "evidence-run"

if TEMP.exists():
    shutil.rmtree(TEMP)
data = TEMP / "data"
output = TEMP / "output"
shots = TEMP / "shots"
for path in (data, output, shots):
    path.mkdir(parents=True, exist_ok=True)
(data / "settings.json").write_text(
    json.dumps(
        {
            "OutputDirectory": str(output),
            "DefaultOcr": "system",
            "ActiveOcrPackId": "",
        },
        ensure_ascii=False,
    ),
    encoding="utf-8",
)
env = os.environ.copy()
env["PAPERDOCK_DATA_DIR"] = str(data)

process = subprocess.Popen([str(APP), str(SAMPLE)], env=env)
application = Application(backend="uia").connect(process=process.pid, timeout=20)
window = application.window(title="纸坞")
window.wait("visible", timeout=20)
window.set_focus()
AppHelpers.wait_status(window, "就绪 · 3 页", 30)
time.sleep(5)

OUT.mkdir(parents=True, exist_ok=True)
window.capture_as_image().save(OUT / "01-split.png")

window.child_window(auto_id="Navmerge", control_type="Button").invoke()
second = TEMP / "附录 与 补充材料.pdf"
shutil.copy2(SAMPLE, second)
AppHelpers.choose_file(window, "AddMergeFiles", "选择要合并的 PDF", SAMPLE)
window.child_window(auto_id="MergeRange0", control_type="Edit").wait("visible", timeout=20)
AppHelpers.choose_file(window, "AddMergeFiles", "选择要合并的 PDF", second)
window.child_window(auto_id="MergeRange1", control_type="Edit").wait("visible", timeout=20)
time.sleep(8)
window.capture_as_image().save(OUT / "02-merge.png")

window.child_window(auto_id="Navconvert", control_type="Button").invoke()
AppHelpers.wait_status(window, "就绪 · 3 页", 30)
time.sleep(1)
window.capture_as_image().save(OUT / "03-convert.png")

window.child_window(auto_id="Navsettings", control_type="Button").invoke()
time.sleep(2)
window.capture_as_image().save(OUT / "04-settings.png")

window.close()
try:
    process.wait(timeout=5)
except subprocess.TimeoutExpired:
    process.kill()

print(OUT)
