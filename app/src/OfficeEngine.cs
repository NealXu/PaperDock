using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PaperDock
{
    internal static class OfficeEngine
    {
        private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        private static readonly XNamespace WPS = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

        public static string Convert(
            ConvertRequest request,
            AppSettings settings,
            IReadOnlyList<InstalledOcrPack> ocrPacks,
            IProgress<string> progress,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var pages = ReadPages(request.SourcePath, cancellationToken);
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("PDF 没有页面。");
            }

            if (request.OcrScannedPages && pages.Any(page => string.IsNullOrWhiteSpace(page.Text)))
            {
                ApplyOcr(request.SourcePath, pages, settings, ocrPacks, progress, cancellationToken);
            }

            var extension = request.Target == OfficeTarget.Word ? ".docx" :
                request.Target == OfficeTarget.Excel ? ".xlsx" : ".pptx";
            var desiredPath = request.OutputPath;
            if (!string.Equals(Path.GetExtension(desiredPath), extension, StringComparison.OrdinalIgnoreCase))
            {
                desiredPath = Path.ChangeExtension(desiredPath, extension);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(desiredPath)));
            var finalPath = FileSafety.UniquePath(desiredPath);
            var tempPath = FileSafety.TempPathFor(finalPath);

            try
            {
                if (request.Target == OfficeTarget.Word)
                {
                    if (request.WordMode == WordOutputMode.Fidelity)
                    {
                        WriteWordFidelity(request, pages, tempPath, progress, cancellationToken);
                    }
                    else
                    {
                        WriteWord(request, pages, tempPath, progress, cancellationToken);
                    }
                }
                else if (request.Target == OfficeTarget.Excel)
                {
                    WriteExcel(pages, tempPath, cancellationToken);
                }
                else
                {
                    WritePowerPoint(request, pages, tempPath, progress, cancellationToken);
                }
                FileSafety.Commit(tempPath, finalPath);
                return finalPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                throw;
            }
        }

        private static List<PageContent> ReadPages(string path, CancellationToken cancellationToken)
        {
            var result = new List<PageContent>();
            using (var document = PdfDocument.Open(path))
            {
                for (var index = 0; index < document.NumberOfPages; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = document.GetPage(index + 1);
                    var words = page.GetWords().ToList();
                    var content = new PageContent
                    {
                        Number = index + 1,
                        Width = (double)page.Width,
                        Height = (double)page.Height,
                        HasTableLines = HasTableLines(page),
                        Text = XmlSafe(ContentOrderTextExtractor.GetText(page).Trim()),
                        Words = words.Select(word => new PositionedWord
                        {
                            Text = XmlSafe(word.Text),
                            Left = (double)word.BoundingBox.Left,
                            Right = (double)word.BoundingBox.Right,
                            Top = (double)word.BoundingBox.Top,
                            Bottom = (double)word.BoundingBox.Bottom
                        }).ToList(),
                        Lines = ReadLines(page, words)
                    };

                    foreach (var image in page.GetImages())
                    {
                        byte[] png;
                        if (image.TryGetPng(out png) && png != null && png.Length > 0)
                        {
                            var bounds = image.BoundingBox;
                            content.Images.Add(new PdfImageContent
                            {
                                Bytes = png,
                                X = Math.Max(0, (double)bounds.Left),
                                Y = Math.Max(0, (double)page.Height - (double)bounds.Top),
                                Width = (double)bounds.Width,
                                Height = (double)bounds.Height
                            });
                        }
                    }
                    result.Add(content);
                }
            }
            return result;
        }

        private static List<PdfLineContent> ReadLines(Page page, IReadOnlyList<Word> words)
        {
            var result = new List<PdfLineContent>();
            foreach (var block in DocstrumBoundingBoxes.Instance.GetBlocks(words))
            {
                foreach (var line in block.TextLines)
                {
                    if (line.TextOrientation.ToString() != "Horizontal" ||
                        string.IsNullOrWhiteSpace(line.Text))
                    {
                        continue;
                    }
                    var letters = line.Words.SelectMany(word => word.Letters).ToList();
                    var fontSize = letters.Count == 0
                        ? 10
                        : letters.Select(letter => letter.PointSize).DefaultIfEmpty(10).Max();
                    var bold = letters.Count > 0 &&
                        letters.Count(letter => letter.FontDetails.IsBold) > letters.Count / 2;
                    var italic = letters.Count > 0 &&
                        letters.Count(letter => letter.FontDetails.IsItalic) > letters.Count / 2;
                    var bounds = line.BoundingBox;
                    result.Add(new PdfLineContent
                    {
                        Text = XmlSafe(line.Text),
                        X = Math.Max(0, (double)bounds.Left),
                        Y = Math.Max(0, (double)page.Height - (double)bounds.Top),
                        Width = Math.Max(1, (double)bounds.Width),
                        Height = Math.Max((double)bounds.Height, fontSize * 1.25),
                        FontName = ContainsCjk(line.Text) ? "Microsoft YaHei" : "Times New Roman",
                        FontSize = fontSize,
                        Bold = bold,
                        Italic = italic
                    });
                }
            }
            return result;
        }

        private static void ApplyOcr(
            string sourcePath,
            IReadOnlyList<PageContent> pages,
            AppSettings settings,
            IReadOnlyList<InstalledOcrPack> ocrPacks,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var scanned = pages.Where(page => string.IsNullOrWhiteSpace(page.Text)).Select(page => page.Number - 1).ToArray();
            if (scanned.Length == 0)
            {
                return;
            }

            var temp = AppPaths.CreateTempDirectory();
            try
            {
                progress.Report("正在识别扫描页…");
                SystemPdfService.RenderPages(sourcePath, temp, scanned, 200, cancellationToken);
                var ocr = OcrService.Recognize(temp, settings, ocrPacks, cancellationToken);
                foreach (var page in pages.Where(page => string.IsNullOrWhiteSpace(page.Text)))
                {
                    var name = "page-" + page.Number.ToString("D4") + ".png";
                    var ocrPage = ocr.pages.FirstOrDefault(item =>
                        string.Equals(item.file, name, StringComparison.OrdinalIgnoreCase));
                    if (ocrPage != null)
                    {
                        page.Text = XmlSafe(string.Join(
                            Environment.NewLine,
                            ocrPage.lines.Select(line => line.text)));
                        page.Lines = ReadOcrLines(page, ocrPage);
                    }
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static List<PdfLineContent> ReadOcrLines(PageContent page, OcrPage ocrPage)
        {
            var result = new List<PdfLineContent>();
            if (ocrPage.width <= 0 || ocrPage.height <= 0)
            {
                return result;
            }
            var scaleX = page.Width / ocrPage.width;
            var scaleY = page.Height / ocrPage.height;
            foreach (var line in ocrPage.lines.Where(item => !string.IsNullOrWhiteSpace(item.text)))
            {
                var words = line.words.Where(item => item.w > 0 && item.h > 0).ToList();
                if (words.Count == 0)
                {
                    continue;
                }
                var left = words.Min(word => word.x);
                var top = words.Min(word => word.y);
                var right = words.Max(word => word.x + word.w);
                var bottom = words.Max(word => word.y + word.h);
                result.Add(new PdfLineContent
                {
                    Text = XmlSafe(line.text),
                    X = left * scaleX,
                    Y = top * scaleY,
                    Width = Math.Max(1, (right - left) * scaleX),
                    Height = Math.Max(6, (bottom - top) * scaleY),
                    FontName = ContainsCjk(line.text) ? "Microsoft YaHei" : "Times New Roman",
                    FontSize = Math.Max(6, (bottom - top) * scaleY * 0.8)
                });
            }
            return result;
        }

        private static void WriteWord(
            ConvertRequest request,
            IReadOnlyList<PageContent> pages,
            string outputPath,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var backgroundDirectory = request.RenderEditablePageBackground
                ? AppPaths.CreateTempDirectory()
                : null;
            IReadOnlyDictionary<int, string> backgrounds = null;
            try
            {
                if (backgroundDirectory != null)
                {
                    progress.Report("正在渲染可编辑页面背景…");
                    backgrounds = SystemPdfService.RenderPages(
                        request.SourcePath,
                        backgroundDirectory,
                        Enumerable.Range(0, pages.Count),
                        request.Dpi,
                        cancellationToken);
                }

                File.Copy(Path.Combine(AppPaths.Assets, "blank.docx"), outputPath, true);
                using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update))
                {
                    var documentXml = ReadXml(archive, "word/document.xml");
                    var settingsXml = ReadXml(archive, "word/settings.xml");
                    ConfigurePrintView(settingsXml);
                    var body = documentXml.Root.Element(W + "body");
                    var sectionProperties = body.Elements(W + "sectPr").LastOrDefault();
                    body.RemoveNodes();
                    ConfigureWordPage(sectionProperties, pages[0].Width, pages[0].Height, false);

                    var relationships = ReadXml(archive, "word/_rels/document.xml.rels");
                    var nextRelationship = NextRelationshipNumber(relationships);
                    var imageNumber = 0;
                    var drawingId = 1;

                    foreach (var page in pages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var pageFlow = new XElement(W + "p",
                            new XElement(W + "pPr",
                                new XElement(W + "spacing",
                                    new XAttribute(W + "before", 0),
                                    new XAttribute(W + "after", 0),
                                    new XAttribute(W + "line", 1),
                                    new XAttribute(W + "lineRule", "exact"))));

                        if (backgrounds != null)
                        {
                            var background = File.ReadAllBytes(backgrounds[page.Number - 1]);
                            var relationshipId = "rIdImg" + nextRelationship++;
                            var target = "media/pdf-background-" + page.Number + ".png";
                            relationships.Root.Add(new XElement(Relationships + "Relationship",
                                new XAttribute("Id", relationshipId),
                                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                                new XAttribute("Target", target)));
                            AddEntry(archive, "word/" + target, background);
                            pageFlow.Add(CreateAnchoredImageRun(
                                background,
                                relationshipId,
                                0,
                                0,
                                page.Width,
                                page.Height,
                                drawingId++));
                        }
                        else
                        {
                            foreach (var image in page.Images)
                            {
                                imageNumber++;
                                var relationshipId = "rIdImg" + nextRelationship++;
                                var target = "media/pdf-page-" + imageNumber + ".png";
                                relationships.Root.Add(new XElement(Relationships + "Relationship",
                                    new XAttribute("Id", relationshipId),
                                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                                    new XAttribute("Target", target)));
                                AddEntry(archive, "word/" + target, image.Bytes);
                                pageFlow.Add(CreateAnchoredImageRun(
                                    image.Bytes,
                                    relationshipId,
                                    image.X,
                                    image.Y,
                                    image.Width,
                                    image.Height,
                                    drawingId++));
                            }
                        }

                        foreach (var line in page.Lines)
                        {
                            pageFlow.Add(CreateAnchoredTextRun(
                                line.Text,
                                line.X,
                                line.Y,
                                line.Width,
                                line.Height,
                                line.FontName,
                                line.FontSize,
                                line.Bold,
                                line.Italic,
                                drawingId++,
                                backgrounds != null));
                        }
                        body.Add(pageFlow);

                        if (page.Number < pages.Count)
                        {
                            body.Add(new XElement(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page")))));
                        }
                    }

                    if (sectionProperties != null)
                    {
                        body.Add(sectionProperties);
                    }
                    WriteXml(archive, "word/document.xml", documentXml);
                    WriteXml(archive, "word/_rels/document.xml.rels", relationships);
                    WriteXml(archive, "word/settings.xml", settingsXml);
                    AddContentTypeDefault(archive, "png", "image/png");
                }
            }
            finally
            {
                if (backgroundDirectory != null)
                {
                    try { Directory.Delete(backgroundDirectory, true); } catch { }
                }
            }
        }

        private static void WriteWordFidelity(
            ConvertRequest request,
            IReadOnlyList<PageContent> pages,
            string outputPath,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var temp = AppPaths.CreateTempDirectory();
            try
            {
                progress.Report("正在渲染 Word 页面…");
                var rendered = SystemPdfService.RenderPages(
                    request.SourcePath,
                    temp,
                    Enumerable.Range(0, pages.Count),
                    request.Dpi,
                    cancellationToken);

                File.Copy(Path.Combine(AppPaths.Assets, "blank.docx"), outputPath, true);
                using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update))
                {
                    var documentXml = ReadXml(archive, "word/document.xml");
                    var settingsXml = ReadXml(archive, "word/settings.xml");
                    ConfigurePrintView(settingsXml);
                    var body = documentXml.Root.Element(W + "body");
                    var sectionProperties = body.Elements(W + "sectPr").LastOrDefault();
                    body.RemoveNodes();

                    // shortcut: one Word section assumes uniform pages; add per-page sections for mixed sizes.
                    ConfigureWordPage(sectionProperties, pages[0].Width, pages[0].Height, true);

                    var relationships = ReadXml(archive, "word/_rels/document.xml.rels");
                    var nextRelationship = NextRelationshipNumber(relationships);
                    for (var index = 0; index < pages.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress.Report("正在生成 Word 第 " + (index + 1) + " / " + pages.Count + " 页…");
                        var imageBytes = File.ReadAllBytes(rendered[index]);
                        var relationshipId = "rIdImg" + nextRelationship++;
                        var target = "media/pdf-page-" + (index + 1) + ".png";
                        relationships.Root.Add(new XElement(Relationships + "Relationship",
                            new XAttribute("Id", relationshipId),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                            new XAttribute("Target", target)));
                        AddEntry(archive, "word/" + target, imageBytes);
                        var paragraph = CreateWordImageParagraph(
                            imageBytes,
                            relationshipId,
                            index + 1,
                            (long)Math.Round(pages[index].Width * 12700),
                            (long)Math.Round(pages[index].Height * 12700));
                        if (index < pages.Count - 1)
                        {
                            paragraph.Add(new XElement(W + "r",
                                new XElement(W + "br", new XAttribute(W + "type", "page"))));
                        }
                        body.Add(paragraph);
                    }

                    if (sectionProperties != null)
                    {
                        body.Add(sectionProperties);
                    }
                    WriteXml(archive, "word/document.xml", documentXml);
                    WriteXml(archive, "word/_rels/document.xml.rels", relationships);
                    WriteXml(archive, "word/settings.xml", settingsXml);
                    AddContentTypeDefault(archive, "png", "image/png");
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static void ConfigureWordPage(
            XElement sectionProperties,
            double width,
            double height,
            bool zeroMargins)
        {
            if (sectionProperties == null)
            {
                return;
            }
            var pageSize = sectionProperties.Element(W + "pgSz");
            if (pageSize == null)
            {
                pageSize = new XElement(W + "pgSz");
                sectionProperties.AddFirst(pageSize);
            }
            pageSize.SetAttributeValue(W + "w", (int)Math.Round(width * 20));
            pageSize.SetAttributeValue(W + "h", (int)Math.Round(height * 20));
            pageSize.SetAttributeValue(W + "orient", width > height ? "landscape" : "portrait");

            var margins = sectionProperties.Element(W + "pgMar");
            if (margins == null)
            {
                margins = new XElement(W + "pgMar");
                pageSize.AddAfterSelf(margins);
            }
            var margin = zeroMargins ? 0 : 1134;
            margins.SetAttributeValue(W + "top", margin);
            margins.SetAttributeValue(W + "right", margin);
            margins.SetAttributeValue(W + "bottom", margin);
            margins.SetAttributeValue(W + "left", margin);
            margins.SetAttributeValue(W + "header", 0);
            margins.SetAttributeValue(W + "footer", 0);
            margins.SetAttributeValue(W + "gutter", 0);
        }

        private static void ConfigurePrintView(XDocument settings)
        {
            var root = settings.Root;
            if (root == null)
            {
                return;
            }
            var view = root.Element(W + "view");
            if (view == null)
            {
                view = new XElement(W + "view", new XAttribute(W + "val", "print"));
                root.AddFirst(view);
            }
            else
            {
                view.SetAttributeValue(W + "val", "print");
            }

            var zoom = root.Element(W + "zoom");
            if (zoom == null)
            {
                zoom = new XElement(W + "zoom");
                view.AddAfterSelf(zoom);
            }
            zoom.SetAttributeValue(W + "percent", "100");
            zoom.SetAttributeValue(W + "val", null);
        }

        private static XElement Run(string text)
        {
            return new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "sz", new XAttribute(W + "val", 20)),
                    new XElement(W + "szCs", new XAttribute(W + "val", 20)),
                    new XElement(W + "rFonts",
                        new XAttribute(W + "ascii", "Microsoft YaHei"),
                        new XAttribute(W + "eastAsia", "Microsoft YaHei"))),
                new XElement(W + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    XmlSafe(text)));
        }

        private static string XmlSafe(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character) &&
                    index + 1 < value.Length &&
                    char.IsLowSurrogate(value[index + 1]))
                {
                    result.Append(character);
                    result.Append(value[++index]);
                }
                else if (character == '\t' || character == '\n' || character == '\r' ||
                    (character >= 0x20 && character <= 0xD7FF) ||
                    (character >= 0xE000 && character <= 0xFFFD))
                {
                    result.Append(character);
                }
            }
            return result.ToString();
        }

        private static bool IsCjk(char value)
        {
            return (value >= 0x3400 && value <= 0x9FFF) ||
                (value >= 0xF900 && value <= 0xFAFF);
        }

        private static XElement CreateWordImageParagraph(
            byte[] imageBytes,
            string relationshipId,
            int id,
            long? widthEmu = null,
            long? heightEmu = null)
        {
            long widthPixels;
            long heightPixels;
            using (var stream = new MemoryStream(imageBytes))
            using (var image = Image.FromStream(stream))
            {
                widthPixels = image.Width;
                heightPixels = image.Height;
            }
            const long maxWidthEmu = 5943600;
            var width = widthEmu ?? maxWidthEmu;
            var height = heightEmu ??
                Math.Max(1, (long)(maxWidthEmu * (heightPixels / (double)Math.Max(1, widthPixels))));

            return new XElement(W + "p",
                new XElement(W + "r",
                    new XElement(W + "drawing",
                        // WPS ignores DrawingML that relies on default namespace serialization.
                        new XAttribute(XNamespace.Xmlns + "a", A),
                        new XAttribute(XNamespace.Xmlns + "pic", Pic),
                        new XAttribute(XNamespace.Xmlns + "r", R),
                        new XAttribute(XNamespace.Xmlns + "wp", WP),
                        new XElement(WP + "inline",
                            new XAttribute("distT", "0"),
                            new XAttribute("distB", "0"),
                            new XAttribute("distL", "0"),
                            new XAttribute("distR", "0"),
                            new XElement(WP + "extent", new XAttribute("cx", width), new XAttribute("cy", height)),
                            new XElement(WP + "effectExtent",
                                new XAttribute("l", "0"),
                                new XAttribute("t", "0"),
                                new XAttribute("r", "0"),
                                new XAttribute("b", "0")),
                            new XElement(WP + "docPr", new XAttribute("id", id), new XAttribute("name", "PDF image " + id)),
                            new XElement(WP + "cNvGraphicFramePr"),
                            new XElement(A + "graphic",
                                new XElement(A + "graphicData",
                                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                    new XElement(Pic + "pic",
                                        new XElement(Pic + "nvPicPr",
                                            new XElement(Pic + "cNvPr", new XAttribute("id", id), new XAttribute("name", "PDF image " + id)),
                                            new XElement(Pic + "cNvPicPr")),
                                        new XElement(Pic + "blipFill",
                                            new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)),
                                            new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                        new XElement(Pic + "spPr",
                                            new XElement(A + "xfrm",
                                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                                new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                                            new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                                                new XElement(A + "avLst"))))))))));
        }

        private static XElement CreateAnchoredImageRun(
            byte[] imageBytes,
            string relationshipId,
            double left,
            double top,
            double width,
            double height,
            int id)
        {
            if (width <= 0 || height <= 0)
            {
                using (var stream = new MemoryStream(imageBytes))
                using (var image = Image.FromStream(stream))
                {
                    width = image.Width * 72.0 / 96.0;
                    height = image.Height * 72.0 / 96.0;
                }
            }
            var cx = Emu(width);
            var cy = Emu(height);
            return new XElement(W + "r",
                CreateAnchoredDrawing(
                    id,
                    true,
                    left,
                    top,
                    cx,
                    cy,
                    CreatePictureGraphic(relationshipId, id, cx, cy)));
        }

        private static XElement CreateAnchoredTextRun(
            string text,
            double left,
            double top,
            double width,
            double height,
            string fontName,
            double fontSize,
            bool bold,
            bool italic,
            int id,
            bool opaqueBackground)
        {
            var cx = Emu(Math.Max(1, width));
            var cy = Emu(Math.Max(1, height));
            var halfPoints = (int)Math.Round(Math.Max(4, Math.Min(72, fontSize)) * 2);
            var textContent = new XElement(W + "txbxContent",
                new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "spacing",
                            new XAttribute(W + "before", 0),
                            new XAttribute(W + "after", 0),
                            new XAttribute(W + "line", 240),
                            new XAttribute(W + "lineRule", "auto")),
                        new XElement(W + "jc", new XAttribute(W + "val", "left"))),
                    new XElement(W + "r",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", fontName)),
                            bold ? new XElement(W + "b") : null,
                            italic ? new XElement(W + "i") : null,
                            new XElement(W + "sz", new XAttribute(W + "val", halfPoints)),
                            new XElement(W + "szCs", new XAttribute(W + "val", halfPoints))),
                        new XElement(W + "t",
                            new XAttribute(XNamespace.Xml + "space", "preserve"),
                            XmlSafe(text)))));
            var shape = new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", "http://schemas.microsoft.com/office/word/2010/wordprocessingShape"),
                    new XElement(WPS + "wsp",
                        new XElement(WPS + "cNvSpPr", new XAttribute("txBox", "1")),
                        new XElement(WPS + "spPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                            new XElement(A + "prstGeom",
                                new XAttribute("prst", "rect"),
                                new XElement(A + "avLst")),
                            opaqueBackground
                                ? new XElement(A + "solidFill",
                                    new XElement(A + "srgbClr", new XAttribute("val", "FFFFFF")))
                                : new XElement(A + "noFill"),
                            new XElement(A + "ln", new XElement(A + "noFill"))),
                        new XElement(WPS + "txbx", textContent),
                        new XElement(WPS + "bodyPr",
                            new XAttribute("wrap", "none"),
                            new XAttribute("lIns", "0"),
                            new XAttribute("tIns", "0"),
                            new XAttribute("rIns", "0"),
                            new XAttribute("bIns", "0"),
                            new XAttribute("anchor", "t"),
                            new XAttribute("anchorCtr", "0"),
                            new XElement(A + "noAutofit")))));
            return new XElement(W + "r",
                CreateAnchoredDrawing(id, false, left, top, cx, cy, shape));
        }

        private static XElement CreateAnchoredDrawing(
            int id,
            bool behindDocument,
            double left,
            double top,
            long width,
            long height,
            XElement graphic)
        {
            return new XElement(W + "drawing",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "a", A),
                new XAttribute(XNamespace.Xmlns + "pic", Pic),
                new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", WP),
                new XAttribute(XNamespace.Xmlns + "wps", WPS),
                new XElement(WP + "anchor",
                    new XAttribute("distT", "0"),
                    new XAttribute("distB", "0"),
                    new XAttribute("distL", "0"),
                    new XAttribute("distR", "0"),
                    new XAttribute("simplePos", "0"),
                    new XAttribute("relativeHeight", 251659264L + id),
                    new XAttribute("behindDoc", behindDocument ? "1" : "0"),
                    new XAttribute("locked", "0"),
                    new XAttribute("layoutInCell", "1"),
                    new XAttribute("allowOverlap", "1"),
                    new XElement(WP + "simplePos", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(WP + "positionH",
                        new XAttribute("relativeFrom", "page"),
                        new XElement(WP + "posOffset", Emu(left))),
                    new XElement(WP + "positionV",
                        new XAttribute("relativeFrom", "page"),
                        new XElement(WP + "posOffset", Emu(top))),
                    new XElement(WP + "extent", new XAttribute("cx", width), new XAttribute("cy", height)),
                    new XElement(WP + "effectExtent",
                        new XAttribute("l", "0"),
                        new XAttribute("t", "0"),
                        new XAttribute("r", "0"),
                        new XAttribute("b", "0")),
                    new XElement(WP + "wrapNone"),
                    new XElement(WP + "docPr",
                        new XAttribute("id", id),
                        new XAttribute("name", "PDF object " + id)),
                    new XElement(WP + "cNvGraphicFramePr"),
                    graphic));
        }

        private static XElement CreatePictureGraphic(
            string relationshipId,
            int id,
            long width,
            long height)
        {
            return new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                    new XElement(Pic + "pic",
                        new XElement(Pic + "nvPicPr",
                            new XElement(Pic + "cNvPr",
                                new XAttribute("id", id),
                                new XAttribute("name", "PDF image " + id)),
                            new XElement(Pic + "cNvPicPr")),
                        new XElement(Pic + "blipFill",
                            new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)),
                            new XElement(A + "stretch", new XElement(A + "fillRect"))),
                        new XElement(Pic + "spPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                            new XElement(A + "prstGeom",
                                new XAttribute("prst", "rect"),
                                new XElement(A + "avLst"))))));
        }

        private static long Emu(double points)
        {
            return (long)Math.Round(points * 12700);
        }

        private static bool ContainsCjk(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Any(IsCjk);
        }

        private static void WriteExcel(
            IReadOnlyList<PageContent> pages,
            string outputPath,
            CancellationToken cancellationToken)
        {
            var tablePages = pages.Where(page => page.HasTableLines).ToList();
            if (tablePages.Count == 0)
            {
                throw new InvalidOperationException(
                    "未检测到规则表格。可选择 Word 版式保真或 PPT 进行视觉转换。");
            }

            File.Copy(Path.Combine(AppPaths.Assets, "blank.xlsx"), outputPath, true);
            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update))
            {
                var workbook = ReadXml(archive, "xl/workbook.xml");
                var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
                var contentTypes = ReadXml(archive, "[Content_Types].xml");
                var workbookNs = workbook.Root.Name.Namespace;
                var sheets = workbook.Root.Element(workbookNs + "sheets");
                sheets.RemoveNodes();
                var initialSheetRelationship = relationships.Root.Elements().FirstOrDefault(item =>
                    (string)item.Attribute("Id") == "rId1" &&
                    ((string)item.Attribute("Type") ?? string.Empty).EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase));
                if (initialSheetRelationship != null)
                {
                    initialSheetRelationship.Remove();
                }
                var nextRelationship = NextRelationshipNumber(relationships);

                for (var pageIndex = 0; pageIndex < tablePages.Count; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = tablePages[pageIndex];
                    var sheetNumber = pageIndex + 1;
                    var relationId = "rId" + nextRelationship++;
                    sheets.Add(new XElement(workbookNs + "sheet",
                        new XAttribute("name", "表" + sheetNumber),
                        new XAttribute("sheetId", sheetNumber),
                        new XAttribute("state", "visible"),
                        new XAttribute(R + "id", relationId)));

                    var target = "worksheets/sheet" + sheetNumber + ".xml";
                    relationships.Root.Add(new XElement(Relationships + "Relationship",
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", target),
                        new XAttribute("Id", relationId)));

                    var sheetDoc = CreateWorksheet(page);
                    WriteXml(archive, "xl/" + target, sheetDoc);
                    AddContentTypeOverride(
                        archive,
                        "/xl/" + target,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
                        contentTypes);
                }

                WriteXml(archive, "xl/workbook.xml", workbook);
                WriteXml(archive, "xl/_rels/workbook.xml.rels", relationships);
                WriteXml(archive, "[Content_Types].xml", contentTypes);
            }
        }

        private static bool HasTableLines(Page page)
        {
            // shortcut: only axis-aligned ruled tables are detected; add layout-model fallback for borderless tables.
            var horizontal = 0;
            var vertical = 0;
            foreach (var path in page.Paths)
            {
                var bounds = path.GetBoundingRectangle();
                if (!bounds.HasValue)
                {
                    continue;
                }
                var rectangle = bounds.Value;
                if (rectangle.Width >= 30 && rectangle.Height <= 2)
                {
                    horizontal++;
                }
                else if (rectangle.Height >= 20 && rectangle.Width <= 2)
                {
                    vertical++;
                }
            }
            return horizontal >= 2 && vertical >= 2;
        }

        private static XDocument CreateWorksheet(PageContent page)
        {
            var rows = ExtractRows(page);
            var sheetData = new XElement(S + "sheetData");
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = new XElement(S + "row", new XAttribute("r", rowIndex + 1));
                for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
                {
                    var cellReference = ColumnName(columnIndex + 1) + (rowIndex + 1);
                    row.Add(new XElement(S + "c",
                        new XAttribute("r", cellReference),
                        new XAttribute("t", "inlineStr"),
                        new XElement(S + "is", new XElement(S + "t", XmlSafe(rows[rowIndex][columnIndex])))));
                }
                sheetData.Add(row);
            }

            return new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(S + "worksheet",
                    new XElement(S + "sheetData", sheetData)));
        }

        private static List<List<string>> ExtractRows(PageContent page)
        {
            var rows = new List<List<string>>();
            var words = page.Words.OrderByDescending(word => word.Top).ToList();
            if (words.Count == 0)
            {
                rows.AddRange(page.Text.Replace("\r", string.Empty)
                    .Split('\n')
                    .Where(line => line.Length > 0)
                    .Select(line => new List<string> { line }));
                return rows;
            }

            var groups = new List<List<PositionedWord>>();
            foreach (var word in words)
            {
                var group = groups.FirstOrDefault(item =>
                {
                    var center = item.Average(value => (value.Top + value.Bottom) / 2);
                    var wordCenter = (word.Top + word.Bottom) / 2;
                    return Math.Abs(center - wordCenter) <= Math.Max(3, word.Bottom - word.Top) * 0.6;
                });
                if (group == null)
                {
                    group = new List<PositionedWord>();
                    groups.Add(group);
                }
                group.Add(word);
            }

            foreach (var group in groups.OrderByDescending(item => item.Max(word => word.Top)))
            {
                var cells = new List<string>();
                var current = new StringBuilder();
                PositionedWord previous = null;
                foreach (var word in group.OrderBy(item => item.Left))
                {
                    if (previous != null && word.Left - previous.Right > 14)
                    {
                        cells.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    if (current.Length > 0)
                    {
                        current.Append(' ');
                    }
                    current.Append(word.Text);
                    previous = word;
                }
                cells.Add(current.ToString().Trim());
                if (cells.Any(value => value.Length > 0))
                {
                    rows.Add(cells);
                }
            }
            return rows;
        }

        private static string ColumnName(int number)
        {
            var name = string.Empty;
            while (number > 0)
            {
                number--;
                name = (char)('A' + number % 26) + name;
                number /= 26;
            }
            return name;
        }

        private static void WritePowerPoint(
            ConvertRequest request,
            IReadOnlyList<PageContent> pages,
            string outputPath,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var temp = AppPaths.CreateTempDirectory();
            try
            {
                progress.Report("正在渲染幻灯片图片…");
                var rendered = SystemPdfService.RenderPages(
                    request.SourcePath,
                    temp,
                    Enumerable.Range(0, pages.Count),
                    request.Dpi,
                    cancellationToken);

                File.Copy(Path.Combine(AppPaths.Assets, "blank.pptx"), outputPath, true);
                using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update))
                {
                    var presentation = ReadXml(archive, "ppt/presentation.xml");
                    var relationships = ReadXml(archive, "ppt/_rels/presentation.xml.rels");
                    var contentTypes = ReadXml(archive, "[Content_Types].xml");
                    var presentationNs = presentation.Root.Name.Namespace;

                    var slideWidth = (long)Math.Round(pages[0].Width * 12700);
                    var slideHeight = (long)Math.Round(pages[0].Height * 12700);
                    var size = presentation.Root.Element(presentationNs + "sldSz");
                    if (size != null)
                    {
                        size.SetAttributeValue("cx", slideWidth);
                        size.SetAttributeValue("cy", slideHeight);
                        size.SetAttributeValue("type", "custom");
                    }

                    var slideList = presentation.Root.Element(presentationNs + "sldIdLst");
                    if (slideList == null)
                    {
                        slideList = new XElement(presentationNs + "sldIdLst");
                        var masterList = presentation.Root.Element(presentationNs + "sldMasterIdLst");
                        if (masterList != null)
                        {
                            masterList.AddAfterSelf(slideList);
                        }
                        else
                        {
                            presentation.Root.AddFirst(slideList);
                        }
                    }
                    slideList.RemoveNodes();

                    var nextRel = NextRelationshipNumber(relationships);
                    for (var index = 0; index < pages.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var slideNumber = index + 1;
                        var relationId = "rId" + nextRel++;
                        var slidePath = "slides/slide" + slideNumber + ".xml";
                        relationships.Root.Add(new XElement(Relationships + "Relationship",
                            new XAttribute("Id", relationId),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                            new XAttribute("Target", slidePath)));
                        slideList.Add(new XElement(presentationNs + "sldId",
                            new XAttribute("id", 255 + slideNumber),
                            new XAttribute(R + "id", relationId)));

                        var mediaPath = "media/pdf-page-" + slideNumber + ".png";
                        AddEntry(archive, "ppt/" + mediaPath, File.ReadAllBytes(rendered[index]));
                        AddEntry(
                            archive,
                            "ppt/" + slidePath,
                            Encoding.UTF8.GetBytes(CreateSlideXml(slideWidth, slideHeight)));
                        AddEntry(
                            archive,
                            "ppt/slides/_rels/slide" + slideNumber + ".xml.rels",
                            Encoding.UTF8.GetBytes(CreateSlideRelationships(mediaPath)));
                        AddContentTypeOverride(
                            archive,
                            "/ppt/" + slidePath,
                            "application/vnd.openxmlformats-officedocument.presentationml.slide+xml",
                            contentTypes);
                    }

                    AddContentTypeDefault(archive, "png", "image/png", contentTypes);
                    WriteXml(archive, "ppt/presentation.xml", presentation);
                    WriteXml(archive, "ppt/_rels/presentation.xml.rels", relationships);
                    WriteXml(archive, "[Content_Types].xml", contentTypes);
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static string CreateSlideXml(long width, long height)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(P + "sld",
                    new XAttribute(XNamespace.Xmlns + "a", A),
                    new XAttribute(XNamespace.Xmlns + "r", R),
                    new XAttribute(XNamespace.Xmlns + "p", P),
                    new XElement(P + "cSld",
                        new XElement(P + "spTree",
                            new XElement(P + "nvGrpSpPr",
                                new XElement(P + "cNvPr", new XAttribute("id", 1), new XAttribute("name", string.Empty)),
                                new XElement(P + "cNvGrpSpPr"),
                                new XElement(P + "nvPr")),
                            new XElement(P + "grpSpPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                    new XElement(A + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0)),
                                    new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                                    new XElement(A + "chExt", new XAttribute("cx", 0), new XAttribute("cy", 0)))),
                            new XElement(P + "pic",
                                new XElement(P + "nvPicPr",
                                    new XElement(P + "cNvPr", new XAttribute("id", 2), new XAttribute("name", "PDF page")),
                                    new XElement(P + "cNvPicPr"),
                                    new XElement(P + "nvPr")),
                                new XElement(P + "blipFill",
                                    new XElement(A + "blip", new XAttribute(R + "embed", "rId1")),
                                    new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                new XElement(P + "spPr",
                                    new XElement(A + "xfrm",
                                        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                        new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")))))),
                    new XElement(P + "clrMapOvr", new XElement(A + "masterClrMapping")))).ToString(SaveOptions.DisableFormatting);
        }

        private static string CreateSlideRelationships(string mediaPath)
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(Relationships + "Relationships",
                    new XElement(Relationships + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target", "../" + mediaPath)),
                    new XElement(Relationships + "Relationship",
                        new XAttribute("Id", "rId2"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout"),
                        new XAttribute("Target", "../slideLayouts/slideLayout7.xml"))))
                .ToString(SaveOptions.DisableFormatting);
        }

        private static XDocument ReadXml(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path);
            if (entry == null)
            {
                throw new InvalidDataException("Office 模板缺少部件：" + path);
            }
            using (var stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static void WriteXml(ZipArchive archive, string path, XDocument document)
        {
            var entry = archive.GetEntry(path);
            if (entry != null)
            {
                entry.Delete();
            }
            entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                document.Save(writer, SaveOptions.DisableFormatting);
            }
        }

        private static void AddEntry(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.GetEntry(path);
            if (entry != null)
            {
                entry.Delete();
            }
            entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        private static int NextRelationshipNumber(XDocument relationships)
        {
            var maximum = 0;
            foreach (var element in relationships.Root.Elements())
            {
                var value = (string)element.Attribute("Id");
                int number;
                if (value != null && value.StartsWith("rId", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value.Substring(3), out number))
                {
                    maximum = Math.Max(maximum, number);
                }
            }
            return maximum + 1;
        }

        private static void AddContentTypeDefault(
            ZipArchive archive,
            string extension,
            string contentType,
            XDocument existing = null)
        {
            var document = existing ?? ReadXml(archive, "[Content_Types].xml");
            var root = document.Root;
            if (!root.Elements(ContentTypes + "Default").Any(item =>
                string.Equals((string)item.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase)))
            {
                root.AddFirst(new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", extension),
                    new XAttribute("ContentType", contentType)));
            }
            if (existing == null)
            {
                WriteXml(archive, "[Content_Types].xml", document);
            }
        }

        private static void AddContentTypeOverride(
            ZipArchive archive,
            string partName,
            string contentType,
            XDocument existing)
        {
            var root = existing.Root;
            var item = root.Elements(ContentTypes + "Override").FirstOrDefault(element =>
                string.Equals((string)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                root.Add(new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", partName),
                    new XAttribute("ContentType", contentType)));
            }
            else
            {
                item.SetAttributeValue("ContentType", contentType);
            }
        }

        private sealed class PageContent
        {
            public int Number { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool HasTableLines { get; set; }
            public string Text { get; set; }
            public List<PdfLineContent> Lines { get; set; } = new List<PdfLineContent>();
            public List<PdfImageContent> Images { get; } = new List<PdfImageContent>();
            public List<PositionedWord> Words { get; set; }
        }

        private sealed class PdfLineContent
        {
            public string Text { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public string FontName { get; set; }
            public double FontSize { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
        }

        private sealed class PdfImageContent
        {
            public byte[] Bytes { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private sealed class PositionedWord
        {
            public string Text { get; set; }
            public double Left { get; set; }
            public double Right { get; set; }
            public double Top { get; set; }
            public double Bottom { get; set; }
        }
    }
}
