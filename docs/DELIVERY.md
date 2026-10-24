# 纸坞 WPF 版交付说明

## 安装

- 安装包：`release/wpf/PaperDock-Setup-0.2.1.exe`
- 大小：3.59 MiB
- 架构：Windows x64
- 系统：Windows 10/11，需 .NET Framework 4.8
- 安装位置：`%LocalAppData%\Programs\PaperDock`
- 权限：普通用户，不需要管理员

如果安装器被本机应用控制策略拦截，可解压便携版：

- `release/wpf/PaperDock-Portable-0.2.1.zip`
- 大小：2.72 MiB
- 解压后运行 `PaperDock.exe`

## 可选离线 OCR

系统没有 Windows OCR 时，在“设置 → 导入 OCR 包”导入：

`release/ocr/Tesseract-5.5-zh-en.ocrpack.zip`

该包大小 6.60 MiB，包含简体中文、英文模型，不随基础安装包分发。

## SHA-256

```text
FE952D40B8738260AB442E9C3564128CEAA3D8B05C48DFCD6ABAA3E3273C6940  PaperDock-Setup-0.2.1.exe
B108010313CCDB9B8F16349DECDCAB53C6C1451457812F6E5E6A9B4B238CAF9D  PaperDock-Portable-0.2.1.zip
9A92566F10D7BC24D20BDEF973C5DD981A09A1FBAF623A23BA077910C293011C  Tesseract-5.5-zh-en.ocrpack.zip
```

## 状态

核心功能和 WPF GUI 自动化测试已通过。安装包未做数字签名；在启用应用控制或严格 SmartScreen 策略的机器上，需要代码签名、企业白名单或使用已放行的便携版目录。
