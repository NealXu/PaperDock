# 小体积 WPF 交付前置验证

日期：2026-10-24  
结论：**继续 C# + .NET Framework 4.8 + WPF 路线。核心 PDF、系统预览、系统 OCR 和安装包体积均已验证通过；Office 转换质量和 Windows 10 实机仍待验收。**

## 一、环境

| 项目 | 实测结果 |
| --- | --- |
| 操作系统 | Windows 11，版本 10.0.26200 |
| .NET Framework | 4.8.09221，Release 533509 |
| WPF | 使用系统 WPF 程序集编译并启动自检成功 |
| 构建工具 | MSBuild 17.14.51；另有 Windows Framework C# 编译器 4.8.9221 |
| 系统 PDF | `Windows.Data.Pdf.dll` 10.0.26100.8875 |
| 系统 OCR | `Windows.Media.Ocr.dll` 10.0.26100.9278 |

WPF 自检程序只有 4,608 字节，证明应用本身不需要携带 WPF 运行时；.NET Framework 4.8 由 Windows 系统提供。

## 二、系统 PDF 与 OCR

### 系统 PDF 渲染

使用 `Windows.Data.Pdf` 打开 `samples/sample.pdf` 并渲染第 1 页：

- 识别页数：3
- 输出尺寸：1190 × 1684
- PNG 大小：31,160 字节
- 结论：预览和扫描型 PDF 的页面栅格化可以不引入第三方渲染引擎。

### 系统 OCR

使用现有 `app/scripts/ocr_pages.ps1` 识别上一步图片：

- 英文 `PAGE 1 SEARCHABLE PDF DEMO` 全部识别正确。
- 中文样本 `可搜索 PDF 测试` 被识别为 `可 搜 索 P D F 刂 试`，合成字体出现了一个形近字误识别。
- 结论：系统 OCR 可用，但不能承诺 100% 字符准确；界面必须允许用户检查结果，不能把低置信度文本当作已验证内容。

## 三、C# PDF 核心

候选组件：

- `PDFsharp-wpf 6.2.4`：MIT，`net462` 程序集。
- `PdfPig 0.1.16`：Apache-2.0，`net471` 程序集，用于文字和版式提取。
- `.NET Framework 4.8` 的 `System.IO.Compression`：用于后续生成 Office 文件，不额外引入 Open XML SDK。

实测：

| 项目 | 结果 |
| --- | --- |
| 拆分 | 3 页样例拆出 1 页，输出文本和尺寸保持 |
| 合并 | 合并样例前 2 页，输出 2 页，尺寸 595 × 842 pt |
| 文字提取 | 得到 `PAGE 1 SEARCHABLE PDF DEMO 可搜索 PDF 测试` |
| Unicode 路径 | 含中文和空格的输入、输出路径均成功 |
| 系统回读 | 拆分/合并结果均可由 `Windows.Data.Pdf` 重新打开和渲染 |

注意：`PDFsharp-wpf 6.2.4` 需要 `app.config` 程序集绑定重定向。当前验证已确认日志抽象和 `System.Runtime.CompilerServices.Unsafe` 的重定向必须进入正式项目。

## 四、许可边界

| 组件 | 可用版本 | 许可 | 判断 |
| --- | --- | --- | --- |
| PDFsharp-wpf | 6.2.4 | MIT | 可固定使用 |
| PDFsharp / PDFsharp-wpf | 6.5.0、7 预览 | AGPL-3.0-only | 不进入默认交付，升级需重新审核 |
| PdfPig | 0.1.16 | Apache-2.0 | 可固定使用 |
| DocumentFormat.OpenXml | 3.5.1 | MIT | 单个 `netstandard2.0` 程序集约 7.99 MB，暂不作为默认依赖 |
| Windows.Data.Pdf / Windows.Media.Ocr | 系统组件 | Windows 系统条款 | 不随安装包分发 |

`PDFsharp-wpf 6.2.4` NuGet 包 SHA-256：

```text
138109DDEEEA62EEB7844B0C8EBF5EB05EC5ACD51F464F4DCD8D653B6BD4F807
```

`PdfPig 0.1.16` NuGet 包 SHA-256：

```text
D67171846EA8C28F50359137065FEC4514266D7A32B23EAE6C5F2EBED8FFCFC4
```

## 五、体积验证

| 产物 | 实测大小 |
| --- | --- |
| 现有 Python/PySide6 安装包 | 217.26 MB |
| C# 最小运行目录，25 个文件 | 7.16 MB |
| 同目录普通 ZIP | 2.61 MB |
| Inno Setup LZMA2 安装包样机 | 3.49 MB |

安装包样机包含 WPF 自检程序、PDF 拆分/合并、PDF 文字提取及全部依赖，**不含**正式四页 UI、Office 写出模块、图标、签名和更新逻辑。按当前余量，正式基础安装包控制在 10 MB 以内可行。

安装包样机当前状态：**未数字签名**，因此不能把“不会触发 SmartScreen/应用控制策略警告”作为已验证结论。

## 六、后续完成情况

1. 正式 WPF 界面、Office 输出、系统/离线 OCR、取消任务、损坏与加密 PDF 已由后续端到端测试覆盖。
2. 150% 显示缩放已验证；Windows 10、100%/125% 缩放仍未实测。
3. 安装、启动和卸载流程曾通过；最终重建的未签名安装包被本机应用控制策略阻止。
4. 代码签名、磁盘满、超长路径和极端内存压力仍待验证。

## 七、当前复现入口

- WPF 源码与构建：`app/`
- GUI 端到端测试：`tests/e2e/`
- 最终测试报告：`docs/TEST-REPORT.md`
- 界面证据：`docs/evidence/`

前期 spike 和临时下载物已在交付清理中移除，本报告保留当时的验证结论。

