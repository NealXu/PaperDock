# WPF GUI 端到端测试

测试使用 `pywinauto` 的 Windows UI Automation 后端驱动真实 WPF 窗口。

## 前置

- Windows 10/11
- Python 3.12
- 先运行 `app/build.ps1`

## 安装测试依赖

```powershell
python -m pip install -r requirements.txt
```

## 运行

```powershell
python -m pytest -s
```

测试会在临时目录中隔离应用设置和输出，不会覆盖原始 PDF。每次用例都会启动新的应用进程。
