using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PaperDock
{
    internal sealed class InstalledOcrPack
    {
        public string Directory { get; set; }
        public OcrPack Manifest { get; set; }

        public string DisplayName
        {
            get { return Manifest.Name + " · " + Manifest.Version; }
        }
    }

    internal static class OcrPackManager
    {
        private const long MaxPackBytes = 256L * 1024 * 1024;
        private const int MaxPackFiles = 10000;
        private const string ManifestName = "ocrpack.json";

        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(
            new[]
            {
                ".exe", ".dll", ".traineddata", ".json", ".txt", ".csv",
                ".pfm", ".unicharset", ".lstm", ".lstm-punc-dawg",
                ".lstm-word-dawg", ".lstm-number-dawg", ".lstm-unicharset"
            },
            StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<InstalledOcrPack> List()
        {
            var result = new List<InstalledOcrPack>();
            foreach (var directory in Directory.GetDirectories(AppPaths.OcrPackDirectory))
            {
                var manifestPath = Path.Combine(directory, "ocrpack.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                try
                {
                    var pack = new InstalledOcrPack
                    {
                        Directory = directory,
                        Manifest = ReadManifest(manifestPath)
                    };
                    ValidateInstalledPack(pack);
                    result.Add(pack);
                }
                catch
                {
                    // Invalid packs are ignored; they remain on disk for diagnosis.
                }
            }
            return result.OrderBy(item => item.Manifest.Name).ToArray();
        }

        public static InstalledOcrPack Import(string zipPath)
        {
            if (!File.Exists(zipPath))
            {
                throw new FileNotFoundException("找不到 OCR 包。", zipPath);
            }

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var manifestEntry = archive.GetEntry(ManifestName);
                if (manifestEntry == null)
                {
                    throw new InvalidDataException("OCR 包缺少 ocrpack.json。");
                }

                OcrPack manifest;
                using (var stream = manifestEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    manifest = Json.Deserialize<OcrPack>(reader.ReadToEnd());
                }
                ValidateManifest(manifest);

                var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
                if (files.Count > MaxPackFiles)
                {
                    throw new InvalidDataException("OCR 包文件数量超过限制。");
                }
                var totalBytes = files.Aggregate(
                    0L,
                    (total, entry) => total > MaxPackBytes - entry.Length
                        ? MaxPackBytes + 1
                        : total + entry.Length);
                if (totalBytes > MaxPackBytes)
                {
                    throw new InvalidDataException("OCR 包解压后超过 256 MB 上限。");
                }
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    ValidateEntry(entry);
                    if (!string.IsNullOrEmpty(entry.Name) && !names.Add(NormalizePath(entry.FullName)))
                    {
                        throw new InvalidDataException("OCR 包包含重复文件：" + entry.FullName);
                    }
                }
                ValidateFileList(manifest, files.Select(entry => NormalizePath(entry.FullName)));

                var packDirectory = Path.Combine(
                    AppPaths.OcrPackDirectory,
                    manifest.Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(packDirectory);
                try
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }
                        var destination = Path.GetFullPath(Path.Combine(packDirectory, entry.FullName));
                        if (!destination.StartsWith(
                            Path.GetFullPath(packDirectory) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("OCR 包包含非法路径。");
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination, true);
                    }

                    var installed = new InstalledOcrPack
                    {
                        Directory = packDirectory,
                        Manifest = ReadManifest(Path.Combine(packDirectory, ManifestName))
                    };
                    ValidateInstalledPack(installed);
                    return installed;
                }
                catch
                {
                    try { Directory.Delete(packDirectory, true); } catch { }
                    throw;
                }
            }
        }

        public static void Delete(InstalledOcrPack pack)
        {
            var root = Path.GetFullPath(AppPaths.OcrPackDirectory) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(pack.Directory);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝删除 OCR 包目录之外的路径。");
            }
            Directory.Delete(target, true);
        }

        private static OcrPack ReadManifest(string path)
        {
            return Json.Deserialize<OcrPack>(File.ReadAllText(path, Encoding.UTF8));
        }

        private static void ValidateManifest(OcrPack manifest)
        {
            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.Name) ||
                string.IsNullOrWhiteSpace(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.Language))
            {
                throw new InvalidDataException("OCR 包清单缺少必要字段。");
            }
            if (!string.Equals(manifest.Engine, "tesseract", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("当前只支持 tesseract OCR 包。");
            }
            if (!string.Equals(manifest.Entry, "tesseract.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("OCR 包入口必须是 tesseract.exe。");
            }
            if (!IsSha256(manifest.EntrySha256))
            {
                throw new InvalidDataException("OCR 包清单缺少 tesseract.exe 的 SHA-256。");
            }
            if (!manifest.Id.All(character =>
                char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_'))
            {
                throw new InvalidDataException("OCR 包 id 只能包含字母、数字、点、横线和下划线。");
            }
            if (string.IsNullOrWhiteSpace(manifest.DataDirectory) ||
                !IsSafeRelativePath(manifest.DataDirectory))
            {
                throw new InvalidDataException("OCR 包数据目录不合法。");
            }
            if (manifest.Files == null || manifest.Files.Count == 0)
            {
                throw new InvalidDataException("OCR 包缺少完整文件清单。");
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path) || !IsSafeRelativePath(file.Path))
                {
                    throw new InvalidDataException("OCR 包文件路径不合法。");
                }
                var path = NormalizePath(file.Path);
                if (string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase) ||
                    !paths.Add(path))
                {
                    throw new InvalidDataException("OCR 包文件清单包含重复项或清单文件本身。");
                }
                if (!IsSha256(file.Sha256))
                {
                    throw new InvalidDataException("OCR 包文件缺少合法 SHA-256：" + path);
                }
            }

            var entryFile = manifest.Files.FirstOrDefault(file =>
                string.Equals(NormalizePath(file.Path), manifest.Entry, StringComparison.OrdinalIgnoreCase));
            if (entryFile == null ||
                !string.Equals(entryFile.Sha256, manifest.EntrySha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("OCR 包入口程序的 SHA-256 与文件清单不一致。");
            }
        }

        private static void ValidateEntry(ZipArchiveEntry entry)
        {
            if (entry.FullName.StartsWith("/") ||
                entry.FullName.Contains("..") ||
                Path.IsPathRooted(entry.FullName))
            {
                throw new InvalidDataException("OCR 包包含非法路径：" + entry.FullName);
            }
            if (string.IsNullOrEmpty(entry.Name))
            {
                return;
            }
            var extension = Path.GetExtension(entry.Name);
            if (!AllowedExtensions.Contains(extension))
            {
                throw new InvalidDataException("OCR 包包含不允许的文件类型：" + entry.FullName);
            }
        }

        internal static void ValidateInstalledPack(InstalledOcrPack pack)
        {
            ValidateManifest(pack.Manifest);
            var executable = ResolveInside(pack.Directory, pack.Manifest.Entry);
            var dataDirectory = ResolveInside(pack.Directory, pack.Manifest.DataDirectory);
            if (!File.Exists(executable) || !Directory.Exists(dataDirectory))
            {
                throw new InvalidDataException("OCR 包缺少入口程序或语言数据目录。");
            }

            var installedFiles = Directory.GetFiles(pack.Directory, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(
                    Path.GetFileName(path),
                    ManifestName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (installedFiles.Count != pack.Manifest.Files.Count)
            {
                throw new InvalidDataException("OCR 包文件数量与清单不一致。");
            }
            foreach (var file in pack.Manifest.Files)
            {
                var path = ResolveInside(pack.Directory, file.Path);
                if (!File.Exists(path) ||
                    !string.Equals(Sha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("OCR 包文件校验失败：" + file.Path);
                }
            }
        }

        private static void ValidateFileList(OcrPack manifest, IEnumerable<string> archiveFiles)
        {
            var actual = new HashSet<string>(
                archiveFiles.Where(path => !string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            var expected = new HashSet<string>(
                manifest.Files.Select(file => NormalizePath(file.Path)),
                StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(expected))
            {
                throw new InvalidDataException("OCR 包实际文件与清单不一致。");
            }
        }

        private static bool IsSafeRelativePath(string path)
        {
            var normalized = NormalizePath(path);
            return !string.IsNullOrWhiteSpace(normalized) &&
                !Path.IsPathRooted(normalized) &&
                normalized.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Length == 64 &&
                value.All(character =>
                    (character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'));
        }

        private static string ResolveInside(string root, string relativePath)
        {
            var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(
                root,
                NormalizePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("OCR 包文件路径越界：" + relativePath);
            }
            return target;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        public static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("X2")));
            }
        }
    }

    internal static class OcrService
    {
        public static OcrDocument Recognize(
            string imageDirectory,
            AppSettings settings,
            IReadOnlyList<InstalledOcrPack> installedPacks,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(settings.DefaultOcr) ||
                settings.DefaultOcr == "system")
            {
                return SystemPdfService.RecognizeDirectory(imageDirectory, "zh-Hans-CN", cancellationToken);
            }

            var pack = installedPacks.FirstOrDefault(item =>
                string.Equals(item.Manifest.Id, settings.ActiveOcrPackId, StringComparison.OrdinalIgnoreCase));
            if (pack == null)
            {
                throw new InvalidOperationException("已选择的离线 OCR 包不存在，请在设置中重新选择。");
            }
            OcrPackManager.ValidateInstalledPack(pack);
            return RecognizeWithTesseract(pack, imageDirectory, cancellationToken);
        }

        private static OcrDocument RecognizeWithTesseract(
            InstalledOcrPack pack,
            string imageDirectory,
            CancellationToken cancellationToken)
        {
            var result = new OcrDocument { language = pack.Manifest.Language };
            var executable = Path.Combine(pack.Directory, pack.Manifest.Entry);
            var data = Path.Combine(pack.Directory, pack.Manifest.DataDirectory);
            foreach (var image in Directory.GetFiles(imageDirectory)
                .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = Quote(image) + " stdout -l " + pack.Manifest.Language +
                        " --psm 6 -c tessedit_create_tsv=1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                start.EnvironmentVariables["TESSDATA_PREFIX"] = data;
                using (var process = new Process { StartInfo = start })
                {
                    process.Start();
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    while (!process.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        Thread.Sleep(80);
                    }
                    var output = outputTask.Result;
                    var error = errorTask.Result;
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("离线 OCR 失败：" + error.Trim());
                    }

                    int width;
                    int height;
                    using (var bitmap = Image.FromFile(image))
                    {
                        width = bitmap.Width;
                        height = bitmap.Height;
                    }
                    result.pages.Add(ParseTsv(Path.GetFileName(image), width, height, output));
                }
            }
            return result;
        }

        private static OcrPage ParseTsv(string fileName, int width, int height, string tsv)
        {
            var page = new OcrPage { file = fileName, width = width, height = height };
            var lineMap = new Dictionary<string, OcrLine>();
            foreach (var rawLine in tsv.Replace("\r", string.Empty).Split('\n').Skip(1))
            {
                var fields = rawLine.Split('\t');
                if (fields.Length < 12 || fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11]))
                {
                    continue;
                }
                var key = fields[1] + ":" + fields[2] + ":" + fields[3] + ":" + fields[4];
                OcrLine line;
                if (!lineMap.TryGetValue(key, out line))
                {
                    line = new OcrLine();
                    lineMap[key] = line;
                    page.lines.Add(line);
                }
                var word = new OcrWord
                {
                    text = fields[11],
                    x = ParseDouble(fields[6]),
                    y = ParseDouble(fields[7]),
                    w = ParseDouble(fields[8]),
                    h = ParseDouble(fields[9])
                };
                line.words.Add(word);
                line.text = string.IsNullOrWhiteSpace(line.text)
                    ? word.text
                    : line.text + " " + word.text;
            }
            return page;
        }

        private static double ParseDouble(string value)
        {
            double result;
            return double.TryParse(value, out result) ? result : 0;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
