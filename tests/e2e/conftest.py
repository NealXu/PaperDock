from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path

import pytest
from pywinauto import Application

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "app" / "dist" / "PaperDock.exe"
SAMPLE = ROOT / "samples" / "sample.pdf"
OCR_PACK = ROOT / "release" / "ocr" / "Tesseract-5.5-zh-en.ocrpack.zip"


class AppSession:
    def __init__(self, process, application, window, root: Path, output: Path, shots: Path):
        self.process = process
        self.application = application
        self.window = window
        self.root = root
        self.output = output
        self.shots = shots

    def close(self):
        try:
            self.window.close()
            self.process.wait(timeout=5)
        except Exception:
            try:
                self.process.kill()
            except Exception:
                pass


@pytest.fixture
def session(tmp_path: Path):
    if not APP.exists():
        pytest.skip(f"missing app build: {APP}")
    if not SAMPLE.exists():
        pytest.skip(f"missing sample PDF: {SAMPLE}")

    data = tmp_path / "data"
    output = tmp_path / "output"
    shots = tmp_path / "shots"
    local = tmp_path / "local"
    temp = tmp_path / "temp"
    for path in (data, output, shots, local, temp):
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
    env["LOCALAPPDATA"] = str(local)
    env["APPDATA"] = str(tmp_path / "roaming")
    env["TEMP"] = str(temp)
    env["TMP"] = str(temp)
    env["PYTHONIOENCODING"] = "utf-8"

    process = subprocess.Popen([str(APP), str(SAMPLE)], env=env)
    application = Application(backend="uia").connect(process=process.pid, timeout=20)
    window = application.window(title="纸坞")
    window.wait("visible", timeout=20)
    window.set_focus()
    result = AppSession(process, application, window, tmp_path, output, shots)
    _wait_status(window, "就绪", 30)
    yield result
    result.close()


def _wait_status(window, text: str, timeout: int = 90):
    status = window.child_window(auto_id="StatusText", control_type="Text")
    status.wait("visible", timeout=10)
    deadline = __import__("time").time() + timeout
    while __import__("time").time() < deadline:
        value = status.window_text()
        if text in value:
            return value
        __import__("time").sleep(0.25)
    raise TimeoutError(f"status did not contain {text!r}; last={status.window_text()!r}")


@pytest.fixture
def app_helpers():
    from helpers import AppHelpers

    return AppHelpers
