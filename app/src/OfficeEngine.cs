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
                    WriteWord(pages, tempPath, cancellationToken);
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
                    var content = new PageContent
                    {
                        Number = index + 1,
                        Width = (double)page.Width,
                        Height = (double)page.Height,
                        Text = ContentOrderTextExtractor.GetText(page).Trim(),
                        Words = page.GetWords().Select(word => new PositionedWord
                        {
                            Text = word.Text,
                            Left = (double)word.BoundingBox.Left,
                            Right = (double)word.BoundingBox.Right,
                            Top = (double)word.BoundingBox.Top,
                            Bottom = (double)word.BoundingBox.Bottom
                        }).ToList()
                    };

                    foreach (var image in page.GetImages())
                    {
                        byte[] png;
                        if (image.TryGetPng(out png) && png != null && png.Length > 0)
                        {
                            content.Images.Add(png);
                        }
                    }
                    result.Add(content);
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
                        page.Text = string.Join(Environment.NewLine, ocrPage.lines.Select(line => line.text));
                    }
                }
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static void WriteWord(
            IReadOnlyList<PageContent> pages,
            string outputPath,
            CancellationToken cancellationToken)
        {
            File.Copy(Path.Combine(AppPaths.Assets, "blank.docx"), outputPath, true);
            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update))
            {
                var documentXml = ReadXml(archive, "word/document.xml");
                var body = documentXml.Root.Element(W + "body");
                var sectionProperties = body.Elements(W + "sectPr").LastOrDefault();
                body.RemoveNodes();

                var relationships = ReadXml(archive, "word/_rels/document.xml.rels");
                var nextRelationship = NextRelationshipNumber(relationships);
                var imageNumber = 0;
                var drawingId = 1;

                foreach (var page in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var heading = new XElement(W + "p",
                        new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1"))),
                        Run("第 " + page.Number + " 页"));
                    body.Add(heading);

                    if (!string.IsNullOrWhiteSpace(page.Text))
                    {
                        foreach (var line in page.Text.Replace("\r", string.Empty).Split('\n'))
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                body.Add(new XElement(W + "p", Run(line)));
                            }
                        }
                    }

                    if (page.Images.Count == 0 && string.IsNullOrWhiteSpace(page.Text))
                    {
                        body.Add(new XElement(W + "p", Run("[本页没有可提取文字或图片]")));
                    }

                    foreach (var imageBytes in page.Images)
                    {
                        imageNumber++;
                        var relationshipId = "rIdImg" + nextRelationship++;
                        var target = "media/pdf-page-" + imageNumber + ".png";
                        relationships.Root.Add(new XElement(Relationships + "Relationship",
                            new XAttribute("Id", relationshipId),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                            new XAttribute("Target", target)));
                        AddEntry(archive, "word/" + target, imageBytes);
                        body.Add(CreateWordImageParagraph(imageBytes, relationshipId, drawingId++));
                    }

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
                AddContentTypeDefault(archive, "png", "image/png");
            }
        }

        private static XElement Run(string text)
        {
            return new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "rFonts",
                        new XAttribute(W + "ascii", "Microsoft YaHei"),
                        new XAttribute(W + "eastAsia", "Microsoft YaHei"))),
                new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? string.Empty));
        }

        private static XElement CreateWordImageParagraph(byte[] imageBytes, string relationshipId, int id)
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
            var width = maxWidthEmu;
            var height = Math.Max(1, (long)(maxWidthEmu * (heightPixels / (double)Math.Max(1, widthPixels))));

            return new XElement(W + "p",
                new XElement(W + "r",
                    new XElement(W + "drawing",
                        new XElement(WP + "inline",
                            new XAttribute("distT", "0"),
                            new XAttribute("distB", "0"),
                            new XAttribute("distL", "0"),
                            new XAttribute("distR", "0"),
                            new XElement(WP + "extent", new XAttribute("cx", width), new XAttribute("cy", height)),
                            new XElement(WP + "docPr", new XAttribute("id", id), new XAttribute("name", "PDF image " + id)),
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

        private static void WriteExcel(
            IReadOnlyList<PageContent> pages,
            string outputPath,
            CancellationToken cancellationToken)
        {
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

                for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = pages[pageIndex];
                    var sheetNumber = pageIndex + 1;
                    var relationId = "rId" + nextRelationship++;
                    sheets.Add(new XElement(workbookNs + "sheet",
                        new XAttribute("name", "P" + page.Number),
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
                        new XElement(S + "is", new XElement(S + "t", rows[rowIndex][columnIndex] ?? string.Empty))));
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
            public string Text { get; set; }
            public List<byte[]> Images { get; } = new List<byte[]>();
            public List<PositionedWord> Words { get; set; }
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
