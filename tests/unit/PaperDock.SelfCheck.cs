using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace PaperDock
{
    internal static class SelfCheck
    {
        private static int Main()
        {
            var root = Environment.GetEnvironmentVariable("PAPERDOCK_REPO_ROOT");
            var work = Path.Combine(Path.GetTempPath(), "PaperDockSelfCheck", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                CheckPageRanges();
                CheckFileSafety(work);
                CheckDamagedSettings(work);
                CheckOcrPackIntegrity(work);
                CheckCancelCleanup(work, Path.Combine(root, "samples", "sample.pdf"));
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
