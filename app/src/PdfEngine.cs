using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PaperDock
{
    internal sealed class ChineseFontResolver : IFontResolver
    {
        private readonly byte[] _fontBytes;

        public ChineseFontResolver()
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var candidates = new[]
            {
                Path.Combine(fonts, "simhei.ttf"),
                Path.Combine(fonts, "arial.ttf")
            };
            var path = candidates.FirstOrDefault(File.Exists);
            if (path != null)
            {
                _fontBytes = File.ReadAllBytes(path);
            }
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (_fontBytes == null)
            {
                return null;
            }
            return new FontResolverInfo("PaperDockFont");
        }

        public byte[] GetFont(string faceName)
        {
            return _fontBytes;
        }
    }

    internal static class PdfEngine
    {
        private const string FontFamily = "Microsoft YaHei";
        private static XFont _watermarkFont;

        public static void InitializeFonts()
        {
            GlobalFontSettings.FontResolver = new ChineseFontResolver();
        }

        public static int GetPageCount(string path)
        {
            try
            {
                using (var document = UglyToad.PdfPig.PdfDocument.Open(path))
                {
                    return document.NumberOfPages;
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "无法打开 PDF。文件可能已损坏、加密或格式不受支持。",
                    exception);
            }
        }

        public static IReadOnlyList<string> Split(
            SplitRequest request,
            AppSettings settings,
            IReadOnlyList<InstalledOcrPack> ocrPacks,
            IProgress<string> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var totalPages = GetPageCount(request.SourcePath);
            var groups = PageRange.BuildGroups(request, totalPages);
            Directory.CreateDirectory(request.OutputDirectory);

            var outputPaths = new List<string>();
            var tempRoot = AppPaths.CreateTempDirectory();
            try
            {
                Dictionary<int, string> images = null;
                OcrDocument ocr = null;
                if (request.OutputKind != PdfOutputKind.Normal)
                {
                    progress.Report("正在用 Windows 渲染页面…");
                    images = SystemPdfService
                        .RenderPages(
                            request.SourcePath,
                            Path.Combine(tempRoot, "images"),
                            groups.SelectMany(x => x),
                            request.Dpi,
                            cancellationToken)
                        .ToDictionary(x => x.Key, x => x.Value);

                    if (request.OutputKind == PdfOutputKind.Searchable)
                    {
                        progress.Report("正在执行离线 OCR…");
                        ocr = OcrService.Recognize(
                            Path.Combine(tempRoot, "images"),
                            settings,
                            ocrPacks,
                            cancellationToken);
                    }
                }

                var sourcePages = ReadPageSizes(request.SourcePath);
                for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var group = groups[groupIndex];
                    progress.Report("正在生成第 " + (groupIndex + 1) + " / " + groups.Count + " 份…");
                    var fileName = Path.GetFileNameWithoutExtension(request.SourcePath) +
                        "_part" + (groupIndex + 1).ToString("D2") +
                        "_p" + (group[0] + 1) + "-" + (group[group.Count - 1] + 1) + ".pdf";
                    var finalPath = FileSafety.UniquePath(Path.Combine(request.OutputDirectory, fileName));

                    if (request.OutputKind == PdfOutputKind.Normal)
                    {
                        SaveImportedPages(request.SourcePath, group, request.Watermark, finalPath);
                    }
                    else
                    {
                        SaveRenderedPages(
                            group,
                            sourcePages,
                            images,
                            ocr,
                            request.Watermark,
                            finalPath);
                    }
                    outputPaths.Add(finalPath);
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
            return outputPaths;
        }

        public static string Merge(
            MergeRequest request,
            IProgress<string> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("请先添加至少一个 PDF。");
            }

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));
            Directory.CreateDirectory(outputDirectory);
            var finalPath = FileSafety.UniquePath(request.OutputPath);
            var tempPath = FileSafety.TempPathFor(finalPath);
            var output = new PdfDocument();

            try
            {
                var itemNumber = 0;
                foreach (var item in request.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    itemNumber++;
                    progress.Report("正在合并第 " + itemNumber + " / " + request.Items.Count + " 份文件…");
                    if (!File.Exists(item.Path))
                    {
                        throw new FileNotFoundException("找不到待合并文件。", item.Path);
                    }

                    var input = PdfReader.Open(item.Path, PdfDocumentOpenMode.Import);
                    var selected = item.SelectedPages();
                    foreach (var pageIndex in selected)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var page = output.AddPage(input.Pages[pageIndex]);
                        DrawWatermark(page, request.Watermark);
                    }
                }

                if (output.PageCount == 0)
                {
                    throw new InvalidOperationException("合并结果为空。");
                }
                output.Save(tempPath);
                FileSafety.Commit(tempPath, finalPath);
                return finalPath;
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        public static IReadOnlyList<string> RenderPreview(
            string sourcePath,
            int maximumPages,
            int dpi)
        {
            var total = GetPageCount(sourcePath);
            var count = Math.Min(Math.Max(1, maximumPages), total);
            var folder = AppPaths.CreateTempDirectory();
            var pages = Enumerable.Range(0, count).ToArray();
            var files = SystemPdfService.RenderPages(sourcePath, folder, pages, dpi);
            return pages.Select(index => files[index]).ToArray();
        }

        public static string BuildOutputName(string sourcePath, string extension)
        {
            return Path.Combine(
                Path.GetDirectoryName(sourcePath),
                Path.GetFileNameWithoutExtension(sourcePath) + extension);
        }

        private static Dictionary<int, PdfPageSize> ReadPageSizes(string sourcePath)
        {
            var result = new Dictionary<int, PdfPageSize>();
            using (var document = UglyToad.PdfPig.PdfDocument.Open(sourcePath))
            {
                for (var index = 0; index < document.NumberOfPages; index++)
                {
                    var page = document.GetPage(index + 1);
                    result[index] = new PdfPageSize((double)page.Width, (double)page.Height);
                }
            }
            return result;
        }

        private static void SaveImportedPages(
            string sourcePath,
            IReadOnlyList<int> pageIndexes,
            string watermark,
            string finalPath)
        {
            var tempPath = FileSafety.TempPathFor(finalPath);
            try
            {
                var input = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
                var output = new PdfDocument();
                foreach (var pageIndex in pageIndexes)
                {
                    var page = output.AddPage(input.Pages[pageIndex]);
                    DrawWatermark(page, watermark);
                }
                output.Save(tempPath);
                FileSafety.Commit(tempPath, finalPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        private static void SaveRenderedPages(
            IReadOnlyList<int> pageIndexes,
            IReadOnlyDictionary<int, PdfPageSize> sourcePages,
            IReadOnlyDictionary<int, string> images,
            OcrDocument ocr,
            string watermark,
            string finalPath)
        {
            var tempPath = FileSafety.TempPathFor(finalPath);
            try
            {
                var output = new PdfDocument();
                foreach (var pageIndex in pageIndexes)
                {
                    var size = sourcePages[pageIndex];
                    var page = output.AddPage();
                    page.Width = XUnit.FromPoint(size.Width);
                    page.Height = XUnit.FromPoint(size.Height);
                    using (var graphics = XGraphics.FromPdfPage(page))
                    using (var image = XImage.FromFile(images[pageIndex]))
                    {
                        graphics.DrawImage(image, 0, 0, size.Width, size.Height);
                        if (ocr != null)
                        {
                            var ocrPage = FindOcrPage(ocr, pageIndex);
                            if (ocrPage != null)
                            {
                                DrawInvisibleText(graphics, ocrPage, size);
                            }
                        }
                        DrawWatermark(graphics, page, watermark);
                    }
                }
                output.Save(tempPath);
                FileSafety.Commit(tempPath, finalPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }
        }

        private static OcrPage FindOcrPage(OcrDocument document, int pageIndex)
        {
            if (document == null || document.pages == null)
            {
                return null;
            }
            var fileName = "page-" + (pageIndex + 1).ToString("D4") + ".png";
            return document.pages.FirstOrDefault(page =>
                string.Equals(page.file, fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static void DrawInvisibleText(XGraphics graphics, OcrPage page, PdfPageSize target)
        {
            if (page.width <= 0 || page.height <= 0)
            {
                return;
            }
            var scaleX = target.Width / page.width;
            var scaleY = target.Height / page.height;
            var brush = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

            foreach (var word in page.lines.SelectMany(line => line.words))
            {
                if (string.IsNullOrWhiteSpace(word.text))
                {
                    continue;
                }
                var fontSize = Math.Max(4, Math.Min(72, word.h * scaleY));
                var font = GetSearchableFont(fontSize);
                graphics.DrawString(word.text, font, brush, word.x * scaleX, word.y * scaleY + fontSize);
            }
        }

        private static XFont GetSearchableFont(double size)
        {
            return new XFont(FontFamily, size, XFontStyleEx.Regular);
        }

        private static void DrawWatermark(PdfPage page, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            using (var graphics = XGraphics.FromPdfPage(page))
            {
                DrawWatermark(graphics, page, text);
            }
        }

        private static void DrawWatermark(XGraphics graphics, PdfPage page, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            if (_watermarkFont == null)
            {
                _watermarkFont = new XFont(FontFamily, 30, XFontStyleEx.Bold);
            }

            var width = page.Width.Point;
            var height = page.Height.Point;
            var brush = new XSolidBrush(XColor.FromArgb(42, 170, 50, 40));
            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(width / 2, height / 2);
                graphics.RotateTransform(-32);
                for (var y = -height * 0.55; y <= height * 0.55; y += 125)
                {
                    for (var x = -width * 0.65; x <= width * 0.65; x += 260)
                    {
                        graphics.DrawString(text, _watermarkFont, brush, x, y);
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Temporary files are best-effort cleanup only.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Temporary files are best-effort cleanup only.
            }
        }

        private sealed class PdfPageSize
        {
            public PdfPageSize(double width, double height)
            {
                Width = width;
                Height = height;
            }

            public double Width { get; private set; }
            public double Height { get; private set; }
        }
    }
}
