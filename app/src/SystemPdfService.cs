using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PaperDock
{
    internal sealed class SystemOcrProbe
    {
        public bool available { get; set; }
        public List<string> languages { get; set; } = new List<string>();
    }

    internal static class SystemPdfService
    {
        public static IReadOnlyDictionary<int, string> RenderPages(
            string sourcePath,
            string outputDirectory,
            IEnumerable<int> pageIndexes,
            int dpi,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到 PDF。", sourcePath);
            }

            Directory.CreateDirectory(outputDirectory);
            var pages = string.Join(",", pageIndexes.Select(index => (index + 1).ToString()));
            ProcessRunner.RunPowerShell("render_pdf_pages.ps1", new Dictionary<string, string>
            {
                { "InputPdf", sourcePath },
                { "OutputDir", outputDirectory },
                { "Pages", pages },
                { "Dpi", Math.Max(48, Math.Min(600, dpi)).ToString() }
            }, cancellationToken);

            var result = new Dictionary<int, string>();
            foreach (var index in pageIndexes)
            {
                var path = Path.Combine(outputDirectory, "page-" + (index + 1).ToString("D4") + ".png");
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException("系统未能渲染第 " + (index + 1) + " 页。");
                }
                result[index] = path;
            }
            return result;
        }

        public static SystemOcrProbe ProbeOcr()
        {
            var text = ProcessRunner.RunPowerShell("probe_ocr.ps1", null);
            return Json.Deserialize<SystemOcrProbe>(text);
        }

        public static OcrDocument RecognizeDirectory(
            string imageDirectory,
            string language,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var outputFile = Path.Combine(imageDirectory, "ocr.json");
            ProcessRunner.RunPowerShell("ocr_pages.ps1", new Dictionary<string, string>
            {
                { "InputDir", imageDirectory },
                { "OutFile", outputFile },
                { "Language", language }
            }, cancellationToken);
            return Json.Deserialize<OcrDocument>(File.ReadAllText(outputFile, System.Text.Encoding.UTF8));
        }
    }
}
