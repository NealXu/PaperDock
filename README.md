# PaperDock · 纸坞

一个完全本地运行的 Windows PDF 工具。

PaperDock 提供拆分、合并、扫描型 PDF、可搜索 PDF、水印和 Office 格式转换；所有文件均在本机处理。

## 使用

双击 `run_gui.bat`，或直接运行：

`app\dist\PaperDock.exe`

也可以把 PDF 路径作为启动参数，直接打开拆分页。

安装包和便携版生成在：

- `release\wpf\PaperDock-Setup-0.2.6.exe`
- `release\wpf\PaperDock-Portable-0.2.6.zip`

可选离线 OCR 包：

- `release\ocr\Tesseract-5.5-zh-en.ocrpack.zip`

在应用“设置 → 导入 OCR 包”中导入。

## 功能

- PDF 拆分：固定页数、自定义范围、按数量均分。
- PDF 合并：多文件排序和每份文件独立页范围。
- 扫描型 PDF、可搜索 PDF。
- 文字水印。
- PDF → Word：可编辑对齐（默认，PDF 整页背景 + 原坐标文字框）与版式保真（纯整页图像，不可编辑）。
- Word 保真文档强制使用打印布局，避免整页图片在草稿视图中显示为空白。
- PDF → Excel：仅导出检测到规则表格的页面。
- PDF → PPT：每页整页图像，视觉优先。
- Windows 系统 OCR。
- Tesseract 离线 OCR 包。
- 进度状态、任务取消、输出文件防覆盖。
- OCR 包全文件校验、解压体积限制、取消后清理本次拆分输出。

不包含 PDF→CAD、CAD→PDF、CAJ→PDF、PDF 加密和批量队列。

## 构建

环境：

- Windows 10/11 x64
- .NET Framework 4.8
- Visual Studio 2022 Build Tools 中的 Roslyn C# 编译器
- Inno Setup 6

```powershell
.\app\build.ps1
.\app\package.ps1
```

## 验证

```powershell
cd .\tests\e2e
python -m pip install -r requirements.txt
python -m pytest -s
```

测试入口包含 10 个 GUI 端到端测试函数和 1 组核心逻辑自检。测试进展与已知环境限制见测试报告：

- `docs/TEST-REPORT.md`
- `tests/e2e/test-output.txt`
- `docs/evidence/`

## 目录

```text
PaperDock/
├─ app/             WPF 源码、依赖、构建和安装脚本
├─ tests/e2e/       UI Automation 端到端测试
├─ samples/         PDF 测试样例
├─ design/v5-fluent 当前 UI 设计稿
├─ docs/            研究、验证和交付文档
└─ release/         安装包、便携版和可选 OCR 包
```

## 运行条件与边界

- 应用依赖 Windows 自带的 .NET Framework 4.8、PowerShell 5.1、PDF 渲染和 OCR 组件。
- Word 保留文字、图片和基本段落，复杂版式不保证完全还原。
- Excel 只对规则表格可靠。
- PPT 使用整页图片，文字不可编辑。
- 安装包尚未代码签名；启用应用控制或严格 SmartScreen 策略的机器需要签名或企业放行。
