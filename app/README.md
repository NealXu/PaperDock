# PaperDock 应用源码

WPF 桌面版使用 C# 和 .NET Framework 4.8。

## 构建

```powershell
.\build.ps1
```

## 打包

```powershell
.\package.ps1
```

打包结果写入仓库根目录的 `release/wpf/`。

可选 Tesseract OCR 包位于 `release/ocr/`，基础安装包不包含 OCR 模型。
