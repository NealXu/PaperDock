# 纸坞 WPF 版交付说明

## 安装

- 安装包：`release/wpf/PaperDock-Setup-0.2.0.exe`
- 大小：3.58 MiB
- 架构：Windows x64
- 系统：Windows 10/11，需 .NET Framework 4.8
- 安装位置：`%LocalAppData%\Programs\PaperDock`
- 权限：普通用户，不需要管理员

如果安装器被本机应用控制策略拦截，可解压便携版：

- `release/wpf/PaperDock-Portable-0.2.0.zip`
- 大小：2.72 MiB
- 解压后运行 `PaperDock.exe`

## 可选离线 OCR

系统没有 Windows OCR 时，在“设置 → 导入 OCR 包”导入：

`release/ocr/Tesseract-5.5-zh-en.ocrpack.zip`

该包大小 6.60 MiB，包含简体中文、英文模型，不随基础安装包分发。

## SHA-256

```text
78060FE48D651F435D80A6D96635E4D19C1D64650E287FE9BD5CED060C5C68CD  PaperDock-Setup-0.2.0.exe
DC3FCEB506C8DEB1976D4EC56B2A26BB02483E0FCF4E5F6282BD466A76F34A68  PaperDock-Portable-0.2.0.zip
9D09E83DC2F394207106E739F7832EDE9308388288D2315396D5B95FBB98A015  Tesseract-5.5-zh-en.ocrpack.zip
```

## 状态

核心功能和 WPF GUI 自动化测试已通过。安装包未做数字签名；在启用应用控制或严格 SmartScreen 策略的机器上，需要代码签名、企业白名单或使用已放行的便携版目录。
