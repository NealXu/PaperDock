using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PaperDock
{
    internal static class AppPaths
    {
        public static string Root
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string Scripts
        {
            get { return Path.Combine(Root, "scripts"); }
        }

        public static string Assets
        {
            get { return Path.Combine(Root, "assets"); }
        }

        public static string Data
        {
            get
            {
                var overridePath = Environment.GetEnvironmentVariable("PAPERDOCK_DATA_DIR");
                var path = !string.IsNullOrWhiteSpace(overridePath)
                    ? Path.GetFullPath(overridePath)
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PaperDock");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(Data, "settings.json"); }
        }

        public static string OcrPackDirectory
        {
            get
            {
                var path = Path.Combine(Data, "ocr-packs");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "PaperDock", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 100
        };

        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }

        public static T Deserialize<T>(string value)
        {
            return Serializer.Deserialize<T>(value);
        }
    }

    internal static class ProcessRunner
    {
        public static string RunPowerShell(
            string scriptName,
            IDictionary<string, string> arguments,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var script = Path.Combine(AppPaths.Scripts, scriptName);
            if (!File.Exists(script))
            {
                throw new FileNotFoundException("缺少系统集成脚本。", script);
            }

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " +
                    Quote(script) + " " + BuildArguments(arguments),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };

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
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "系统组件调用失败。" : error.Trim());
                }
                return output.Trim();
            }
        }

        private static string BuildArguments(IDictionary<string, string> arguments)
        {
            if (arguments == null)
            {
                return string.Empty;
            }
            return string.Join(" ", arguments.Select(pair => "-" + pair.Key + " " + Quote(pair.Value ?? string.Empty)));
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class FileSafety
    {
        public static string UniquePath(string desiredPath)
        {
            if (!File.Exists(desiredPath))
            {
                return desiredPath;
            }

            var directory = Path.GetDirectoryName(desiredPath);
            var stem = Path.GetFileNameWithoutExtension(desiredPath);
            var extension = Path.GetExtension(desiredPath);
            for (var index = 2; index < 10000; index++)
            {
                var candidate = Path.Combine(directory, stem + " (" + index + ")" + extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new IOException("无法生成不重复的输出文件名。");
        }

        public static string TempPathFor(string finalPath)
        {
            return Path.Combine(
                Path.GetDirectoryName(finalPath),
                "." + Path.GetFileName(finalPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        }

        public static void Commit(string tempPath, string finalPath)
        {
            if (File.Exists(finalPath))
            {
                throw new IOException("输出文件已存在：" + finalPath);
            }
            File.Move(tempPath, finalPath);
        }
    }

    internal static class AppSettingsStore
    {
        public static AppSettings Load()
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                return new AppSettings();
            }
            return Json.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8));
        }

        public static void Save(AppSettings settings)
        {
            File.WriteAllText(AppPaths.SettingsFile, Json.Serialize(settings), new UTF8Encoding(false));
        }
    }
}
