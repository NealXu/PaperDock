from __future__ import annotations

import time
from pathlib import Path

from pywinauto.keyboard import send_keys


class AppHelpers:
    @staticmethod
    def wait_status(window, text: str, timeout: int = 120) -> str:
        status = window.child_window(auto_id="StatusText", control_type="Text")
        deadline = time.time() + timeout
        while time.time() < deadline:
            value = status.window_text()
            if text in value:
                return value
            time.sleep(0.25)
        raise TimeoutError(f"status did not contain {text!r}; last={status.window_text()!r}")

    @staticmethod
    def finish_operation(window, start_id: str, timeout: int = 240) -> str:
        start = window.child_window(auto_id=start_id, control_type="Button")
        start.wait("enabled", timeout=10)
        start.invoke()
        deadline = time.time() + timeout
        status = ""
        while time.time() < deadline:
            status = window.child_window(auto_id="StatusText", control_type="Text").window_text()
            if "完成" in status or "失败" in status:
                break
            time.sleep(0.25)
        else:
            raise TimeoutError(f"operation did not finish: {start_id}")

        time.sleep(0.8)
        for _ in range(3):
            AppHelpers.dismiss_dialog(window)
            deadline = time.time() + 4
            while time.time() < deadline and not start.is_enabled():
                time.sleep(0.2)
            if start.is_enabled():
                break
        if not start.is_enabled():
            raise TimeoutError(f"start button remained disabled after message: {start_id}")
        if "失败" in status:
            raise AssertionError(status)
        return status

    @staticmethod
    def dismiss_dialog(window):
        try:
            window.set_focus()
        except Exception:
            pass
        try:
            dialogs = [
                item
                for item in window.descendants(control_type="Window")
                if item.is_visible()
            ]
            for dialog in dialogs:
                for button in dialog.descendants(control_type="Button"):
                    if button.is_visible() and button.window_text() in ("确定", "OK", "是"):
                        button.click_input()
                        return
        except Exception:
            pass
        send_keys("{ENTER}")

    @staticmethod
    def choose_file(window, button_id: str, dialog_title: str, path: Path, timeout: int = 15):
        window.child_window(auto_id=button_id, control_type="Button").invoke()
        dialog = None
        deadline = time.time() + timeout
        while time.time() < deadline:
            matches = [
                item
                for item in window.descendants(control_type="Window")
                if item.window_text() == dialog_title and item.is_visible()
            ]
            if matches:
                dialog = matches[0]
                break
            time.sleep(0.2)
        if dialog is None:
            raise TimeoutError(f"file dialog did not appear: {dialog_title}")
        filenames = [
            item
            for item in dialog.descendants(control_type="Edit")
            if item.automation_id() == "1148"
        ]
        if not filenames:
            raise RuntimeError("file name edit not found in dialog")
        filename = filenames[0]
        filename.set_edit_text(str(path))
        dialog.type_keys("{ENTER}")
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                visible = [
                    item
                    for item in window.descendants(control_type="Window")
                    if item.window_text() == dialog_title and item.is_visible()
                ]
                if not visible:
                    return
            except Exception:
                return
            time.sleep(0.2)
        raise TimeoutError(f"file dialog did not close: {dialog_title}")

    @staticmethod
    def select_radio(window, auto_id: str):
        control = window.child_window(auto_id=auto_id, control_type="RadioButton")
        control.wait("visible", timeout=10)
        control.click_input()

    @staticmethod
    def set_text(window, auto_id: str, value: str):
        control = window.child_window(auto_id=auto_id, control_type="Edit")
        control.wait("visible", timeout=10)
        control.set_edit_text(value)

    @staticmethod
    def screenshot(window, path: Path):
        path.parent.mkdir(parents=True, exist_ok=True)
        try:
            window.capture_as_image().save(path)
        except OSError:
            # Some remote or locked desktop sessions cannot capture the screen.
            pass
