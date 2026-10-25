using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PaperDock
{
    public sealed class SelfCheckHost : MarshalByRefObject
    {
        public int Run()
        {
            return SelfCheck.Main();
        }
    }

    internal static class SelfCheck
    {
        internal static int Main()
        {
            var root = Environment.GetEnvironmentVariable("PAPERDOCK_REPO_ROOT");
            var work = Path.Combine(Path.GetTempPath(), "PaperDockSelfCheck", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                Console.WriteLine("check: page ranges");
                CheckPageRanges();
                Console.WriteLine("check: file safety");
                CheckFileSafety(work);
                Console.WriteLine("check: settings fallback");
                CheckDamagedSettings(work);
                Console.WriteLine("check: OCR pack integrity");
                CheckOcrPackIntegrity(work);
                Console.WriteLine("check: cancellation cleanup");
                CheckCancelCleanup(work, Path.Combine(root, "samples", "sample.pdf"));
                Console.WriteLine("check: editable Word layout");
                CheckEditableWord(work, Path.Combine(root, "samples", "sample.pdf"));
                Console.WriteLine("check: editable Word default");
                CheckEditableWordDefault(work, Path.Combine(root, "samples", "sample.pdf"));
                Console.WriteLine("check: Word XML text sanitization");
                CheckWordTextSanitization();
                Console.WriteLine("check: editable image bounds");
                CheckEditableImageBounds(work);
                Console.WriteLine("check: editable layout text box");
                CheckEditableLayoutTextBox();
                Console.WriteLine("check: editable background contract");
                CheckEditableBackgroundContract();
                Console.WriteLine("check: Excel table only");
                CheckExcelTableOnly(work);
                Console.WriteLine("check: fidelity print view");
                CheckFidelityPrintView();
                Console.WriteLine("check: WPS image compatibility");
                CheckWpsImageCompatibility();
                var regressionPdf = Environment.GetEnvironmentVariable("PAPERDOCK_REGRESSION_PDF");
                if (File.Exists(regressionPdf))
                {
                    Console.WriteLine("check: real standard regression");
                    CheckRegressionDocument(work, regressionPdf);
                }
                Console.WriteLine("SELF-CHECK OK");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        private static void CheckPageRanges()
        {
            Equal("0,1,2", string.Join(",", PageRange.Parse("1,2-3", 3)), "range pages");

            var fixedGroups = PageRange.BuildGroups(
                new SplitRequest { Mode = SplitMode.Fixed, PagesPerPart = 2 },
                5);
            Equal(3, fixedGroups.Count, "fixed groups");
            Equal("2,2,1", string.Join(",", fixedGroups.Select(group => group.Count)), "fixed sizes");

            var countGroups = PageRange.BuildGroups(
                new SplitRequest { Mode = SplitMode.Count, PartCount = 2 },
                5);
            Equal("3,2", string.Join(",", countGroups.Select(group => group.Count)), "count sizes");
        }

        private static void CheckFileSafety(string work)
        {
            var folder = Path.Combine(work, "file-safety");
            Directory.CreateDirectory(folder);
            var first = Path.Combine(folder, "result.pdf");
            File.WriteAllText(first, "existing");
            var unique = FileSafety.UniquePath(first);
            Equal("result (2).pdf", Path.GetFileName(unique), "unique name");
            Equal(false, File.Exists(unique), "unique name must not exist");
        }

        private static void CheckDamagedSettings(string work)
        {
            var data = Path.Combine(work, "settings-data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{broken", Encoding.UTF8);
            var previous = Environment.GetEnvironmentVariable("PAPERDOCK_DATA_DIR");
            Environment.SetEnvironmentVariable("PAPERDOCK_DATA_DIR", data);
            try
            {
                var settings = AppSettingsStore.Load();
                Equal(false, string.IsNullOrWhiteSpace(settings.OutputDirectory), "settings fallback");
                Equal("system", settings.DefaultOcr, "settings OCR fallback");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PAPERDOCK_DATA_DIR", previous);
            }
        }

        private static void CheckOcrPackIntegrity(string work)
        {
            var data = Path.Combine(work, "ocr-data");
            Directory.CreateDirectory(data);
            var previous = Environment.GetEnvironmentVariable("PAPERDOCK_DATA_DIR");
            Environment.SetEnvironmentVariable("PAPERDOCK_DATA_DIR", data);
            try
            {
                var source = Path.Combine(work, "ocr-source");
                Directory.CreateDirectory(Path.Combine(source, "tessdata"));
                File.WriteAllBytes(Path.Combine(source, "tesseract.exe"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(source, "tesseract55.dll"), new byte[] { 4, 5, 6 });
                File.WriteAllBytes(Path.Combine(source, "tessdata", "eng.traineddata"), new byte[] { 7, 8, 9 });

                var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                    .Select(path => new OcrPackFile
                    {
                        Path = RelativePath(source, path),
                        Sha256 = OcrPackManager.Sha256(path)
                    })
                    .OrderBy(file => file.Path)
                    .ToList();
                var manifest = new OcrPack
                {
                    Id = "self-check",
                    Name = "Self check",
                    Version = "1.0",
                    Engine = "tesseract",
                    Entry = "tesseract.exe",
                    EntrySha256 = files.Single(file => file.Path == "tesseract.exe").Sha256,
                    Language = "eng",
                    DataDirectory = "tessdata",
                    Files = files
                };
                File.WriteAllText(
                    Path.Combine(source, "ocrpack.json"),
                    Json.Serialize(manifest),
                    new UTF8Encoding(false));

                var archivePath = Path.Combine(work, "valid.ocrpack.zip");
                ZipFile.CreateFromDirectory(source, archivePath, CompressionLevel.Optimal, false);
                var installed = OcrPackManager.Import(archivePath);
                Equal(true, Directory.Exists(installed.Directory), "OCR pack imported");

                File.WriteAllBytes(Path.Combine(installed.Directory, "tesseract55.dll"), new byte[] { 9, 9, 9 });
                Equal(0, OcrPackManager.List().Count, "tampered OCR pack rejected");
                OcrPackManager.Delete(installed);

                var releasePack = Environment.GetEnvironmentVariable("PAPERDOCK_RELEASE_OCR_PACK");
                if (File.Exists(releasePack))
                {
                    var release = OcrPackManager.Import(releasePack);
                    Equal("tesseract-zh-eng", release.Manifest.Id, "release OCR pack id");
                    Equal(5, release.Manifest.Files.Count, "release OCR pack file count");
                    OcrPackManager.Delete(release);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PAPERDOCK_DATA_DIR", previous);
            }
        }

        private static void CheckCancelCleanup(string work, string sample)
        {
            if (!File.Exists(sample))
            {
                throw new InvalidOperationException("Missing test PDF: " + sample);
            }
            var output = Path.Combine(work, "cancel-output");
            Directory.CreateDirectory(output);
            using (var cancellation = new CancellationTokenSource())
            {
                var progress = new CancelOnSecondGroup(cancellation);
                var cancelled = false;
                try
                {
                    PdfEngine.Split(
                        new SplitRequest
                        {
                            SourcePath = sample,
                            OutputDirectory = output,
                            Mode = SplitMode.Fixed,
                            PagesPerPart = 1,
                            OutputKind = PdfOutputKind.Normal
                        },
                        new AppSettings { OutputDirectory = output },
                        new InstalledOcrPack[0],
                        progress,
                        cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                Equal(true, cancelled, "split cancellation");
                Equal(0, Directory.GetFiles(output, "*.pdf").Length, "cancelled split cleanup");
            }
        }

        private static void CheckEditableWord(string work, string sample)
        {
            var output = Path.Combine(work, "word-editable.docx");
            OfficeEngine.Convert(
                new ConvertRequest
                {
                    SourcePath = sample,
                    OutputPath = output,
                    Target = OfficeTarget.Word,
                    Dpi = 150,
                    OcrScannedPages = false,
                    WordMode = WordOutputMode.Editable,
                    RenderEditablePageBackground = false
                },
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            using (var archive = ZipFile.OpenRead(output))
            {
                var document = ReadXml(archive, "word/document.xml");
                var pageSize = document.Descendants(w + "pgSz").First();
                var width = (int)pageSize.Attribute(w + "w");
                var height = (int)pageSize.Attribute(w + "h");
                Equal(11900, width, "editable page width");
                Equal(16840, height, "editable page height");

                var text = string.Concat(document.Descendants(w + "t").Select(item => item.Value));
                Equal(true, text.Contains("PAGE 1"), "editable source text");
                Equal(false, text.Contains("第 1 页"), "editable page label");
            }
        }

        private static XDocument ReadXml(ZipArchive archive, string path)
        {
            using (var stream = archive.GetEntry(path).Open())
            {
                return XDocument.Load(stream);
            }
        }

        private static void CheckEditableWordDefault(string work, string sample)
        {
            var output = Path.Combine(work, "word-default.docx");
            var request = new ConvertRequest
            {
                SourcePath = sample,
                OutputPath = output,
                Target = OfficeTarget.Word,
                OcrScannedPages = false,
                RenderEditablePageBackground = false
            };
            Equal(WordOutputMode.Editable, request.WordMode, "default Word mode");
            OfficeEngine.Convert(
                request,
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            using (var archive = ZipFile.OpenRead(output))
            {
                var text = string.Concat(ReadXml(archive, "word/document.xml")
                    .Descendants(w + "t")
                    .Select(item => item.Value));
                Equal(true, text.Contains("PAGE 1"), "default Word source text");
            }
        }

        private static void CheckExcelTableOnly(string work)
        {
            var source = Path.Combine(work, "table-source.pdf");
            PdfEngine.InitializeFonts();
            using (var document = new PdfDocument())
            {
                var prose = document.AddPage();
                using (var graphics = XGraphics.FromPdfPage(prose))
                {
                    graphics.DrawString(
                        "PROSE ONLY",
                        new XFont("Microsoft YaHei", 12),
                        XBrushes.Black,
                        new XPoint(50, 80));
                }

                var tablePage = document.AddPage();
                using (var graphics = XGraphics.FromPdfPage(tablePage))
                {
                    var pen = new XPen(XColors.Black, 1);
                    var font = new XFont("Microsoft YaHei", 9);
                    var rows = new[]
                    {
                        new[] { "NAME", "COUNT", "UNIT" },
                        new[] { "Alpha", "12", "kg" },
                        new[] { "Beta", "7", "m" }
                    };
                    for (var row = 0; row < rows.Length; row++)
                    {
                        var y = 80 + row * 28;
                        graphics.DrawLine(pen, 50, y, 400, y);
                        for (var column = 0; column < 3; column++)
                        {
                            var x = 50 + column * 120;
                            graphics.DrawLine(pen, x, y, x, y + 28);
                            graphics.DrawString(rows[row][column], font, XBrushes.Black, x + 5, y + 18);
                        }
                    }
                    graphics.DrawLine(pen, 50, 80 + rows.Length * 28, 400, 80 + rows.Length * 28);
                    graphics.DrawLine(pen, 410, 80, 410, 80 + rows.Length * 28);
                }
                document.Save(source);
            }

            var output = Path.Combine(work, "table-only.xlsx");
            OfficeEngine.Convert(
                new ConvertRequest
                {
                    SourcePath = source,
                    OutputPath = output,
                    Target = OfficeTarget.Excel,
                    OcrScannedPages = false
                },
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using (var archive = ZipFile.OpenRead(output))
            {
                var workbook = ReadXml(archive, "xl/workbook.xml");
                var sheets = workbook.Descendants(spreadsheet + "sheet").ToList();
                Equal(1, sheets.Count, "Excel table sheet count");
                Equal("表1", (string)sheets[0].Attribute("name"), "Excel table sheet name");
            }
        }

        private static void CheckWordTextSanitization()
        {
            var method = typeof(OfficeEngine).GetMethod(
                "XmlSafe",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("Word text is not sanitized for XML.");
            }
            var cleaned = (string)method.Invoke(null, new object[] { "A\0B\u0001C😀D" });
            Equal("ABC😀D", cleaned, "Word XML safe text");
        }

        private static void CheckEditableImageBounds(string work)
        {
            var source = Path.Combine(work, "sized-image.pdf");
            PdfEngine.InitializeFonts();
            using (var bitmap = new Bitmap(20, 20))
            using (var imageStream = new MemoryStream())
            {
                bitmap.Save(imageStream, System.Drawing.Imaging.ImageFormat.Png);
                imageStream.Position = 0;
                using (var document = new PdfDocument())
                using (var image = XImage.FromStream(imageStream))
                {
                    var page = document.AddPage();
                    using (var graphics = XGraphics.FromPdfPage(page))
                    {
                        graphics.DrawImage(image, new XRect(50, 70, 120, 80));
                    }
                    document.Save(source);
                }
            }

            var output = Path.Combine(work, "sized-image.docx");
            OfficeEngine.Convert(
                new ConvertRequest
                {
                    SourcePath = source,
                    OutputPath = output,
                    Target = OfficeTarget.Word,
                    WordMode = WordOutputMode.Editable,
                    OcrScannedPages = false,
                    RenderEditablePageBackground = false
                },
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            using (var archive = ZipFile.OpenRead(output))
            {
                var extent = ReadXml(archive, "word/document.xml")
                    .Descendants(wp + "extent")
                    .First();
                Equal(1524000L, (long)extent.Attribute("cx"), "editable image width");
                Equal(1016000L, (long)extent.Attribute("cy"), "editable image height");
            }
        }

        private static void CheckEditableLayoutTextBox()
        {
            var method = typeof(OfficeEngine).GetMethod(
                "CreateAnchoredTextRun",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("Editable Word does not create anchored text boxes.");
            }
            var element = (XElement)method.Invoke(
                null,
                new object[] { "Aligned line", 72d, 90d, 230d, 20d, "Times New Roman", 10d, false, false, 1, false });
            var xml = element.ToString(SaveOptions.DisableFormatting);
            Equal(true, xml.Contains("<wp:anchor"), "editable text anchor");
            Equal(true, xml.Contains("relativeFrom=\"page\""), "editable page positioning");
            Equal(true, xml.Contains("<w:txbxContent>"), "editable text box");
            Equal(true, xml.Contains("Aligned line"), "editable text");
            Equal(true, xml.Contains("914400"), "editable x position");
            Equal(true, xml.Contains("1143000"), "editable y position");
        }

        private static void CheckEditableBackgroundContract()
        {
            var property = typeof(ConvertRequest).GetProperty("RenderEditablePageBackground");
            if (property == null)
            {
                throw new InvalidOperationException("Editable Word does not enable page backgrounds.");
            }
            var request = new ConvertRequest();
            Equal(true, (bool)property.GetValue(request), "editable background default");

            byte[] png;
            using (var bitmap = new Bitmap(2, 2))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                png = stream.ToArray();
            }
            var method = typeof(OfficeEngine).GetMethod(
                "CreateAnchoredImageRun",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var element = (XElement)method.Invoke(
                null,
                new object[] { png, "rId1", 0d, 0d, 612d, 792d, 1 });
            var xml = element.ToString(SaveOptions.DisableFormatting);
            Equal(true, xml.Contains("behindDoc=\"1\""), "editable background z-order");
            Equal(true, xml.Contains("relativeFrom=\"page\""), "editable background page anchor");
        }

        private static void CheckFidelityPrintView()
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var settings = new XDocument(new XElement(w + "settings"));
            var method = typeof(OfficeEngine).GetMethod(
                "ConfigurePrintView",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("Fidelity DOCX does not configure print view.");
            }
            method.Invoke(null, new object[] { settings });
            Equal("print", (string)settings.Root.Element(w + "view").Attribute(w + "val"), "Word print view");
            Equal("100", (string)settings.Root.Element(w + "zoom").Attribute(w + "percent"), "Word zoom");
        }

        private static void CheckWpsImageCompatibility()
        {
            byte[] png;
            using (var bitmap = new Bitmap(2, 2))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                png = stream.ToArray();
            }

            var method = typeof(OfficeEngine)
                .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(item => item.Name == "CreateWordImageParagraph");
            var paragraph = (XElement)method.Invoke(
                null,
                new object[] { png, "rId1", 1, 100L, 100L });
            var xml = paragraph.ToString(SaveOptions.DisableFormatting);
            Equal(true, xml.Contains("xmlns:a="), "WPS DrawingML namespace");
            Equal(true, xml.Contains("<a:graphic"), "WPS graphic node");
            Equal(true, xml.Contains("<pic:pic"), "WPS picture node");
            Equal(true, xml.Contains("<wp:effectExtent"), "WPS effect extent");
            Equal(true, xml.Contains("<wp:cNvGraphicFramePr"), "WPS graphic frame");
        }

        private static void CheckRegressionDocument(string work, string source)
        {
            var wordOutput = Path.Combine(work, "regression-editable.docx");
            OfficeEngine.Convert(
                new ConvertRequest
                {
                    SourcePath = source,
                    OutputPath = wordOutput,
                    Target = OfficeTarget.Word,
                    WordMode = WordOutputMode.Editable,
                    OcrScannedPages = false,
                    RenderEditablePageBackground = false
                },
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            var exportDirectory = Environment.GetEnvironmentVariable("PAPERDOCK_REGRESSION_OUTPUT");
            if (!string.IsNullOrWhiteSpace(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
                File.Copy(
                    wordOutput,
                    Path.Combine(exportDirectory, Path.GetFileNameWithoutExtension(source) + "-editable.docx"),
                    true);
            }

            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            int expectedWidth;
            int expectedHeight;
            using (var sourceDocument = UglyToad.PdfPig.PdfDocument.Open(source))
            {
                var sourcePage = sourceDocument.GetPage(1);
                expectedWidth = (int)Math.Round((double)sourcePage.Width * 20);
                expectedHeight = (int)Math.Round((double)sourcePage.Height * 20);
            }
            using (var archive = ZipFile.OpenRead(wordOutput))
            {
                var document = ReadXml(archive, "word/document.xml");
                var pageSize = document.Descendants(w + "pgSz").First();
                Near(expectedWidth, (int)pageSize.Attribute(w + "w"), 2, "standard Word width");
                Near(expectedHeight, (int)pageSize.Attribute(w + "h"), 2, "standard Word height");
                var text = string.Concat(document.Descendants(w + "t").Select(item => item.Value));
                Equal(false, text.Contains("第 1 页"), "standard Word labels");
            }

            var excelOutput = Path.Combine(work, "regression-tables.xlsx");
            OfficeEngine.Convert(
                new ConvertRequest
                {
                    SourcePath = source,
                    OutputPath = excelOutput,
                    Target = OfficeTarget.Excel,
                    OcrScannedPages = false
                },
                new AppSettings { OutputDirectory = work },
                new InstalledOcrPack[0],
                new Progress<string>());

            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using (var archive = ZipFile.OpenRead(excelOutput))
            {
                var workbook = ReadXml(archive, "xl/workbook.xml");
                var sheets = workbook.Descendants(spreadsheet + "sheet").ToList();
                Equal(true, sheets.Count > 0, "standard Excel tables");
                Equal(true, sheets.All(sheet => ((string)sheet.Attribute("name")).StartsWith("表")),
                    "standard Excel sheet names");
                Console.WriteLine("standard Excel sheets: " +
                    string.Join(",", sheets.Select(sheet => (string)sheet.Attribute("name"))));
            }
        }

        private static string RelativePath(string root, string path)
        {
            return path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " failed: expected " + expected + ", actual " + actual);
            }
        }

        private static void Near(int expected, int actual, int tolerance, string name)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    name + " failed: expected " + expected + " +/- " + tolerance + ", actual " + actual);
            }
        }

        private sealed class CancelOnSecondGroup : IProgress<string>
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelOnSecondGroup(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public void Report(string value)
            {
                if (value.Contains("第 2"))
                {
                    _cancellation.Cancel();
                }
            }
        }
    }
}
