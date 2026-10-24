using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaperDock
{
    internal enum SplitMode
    {
        Fixed,
        Ranges,
        Count
    }

    internal enum PdfOutputKind
    {
        Normal,
        ImageOnly,
        Searchable
    }

    internal enum OfficeTarget
    {
        Word,
        Excel,
        PowerPoint
    }

    internal sealed class SplitRequest
    {
        public string SourcePath { get; set; }
        public string OutputDirectory { get; set; }
        public SplitMode Mode { get; set; }
        public int PagesPerPart { get; set; } = 1;
        public int PartCount { get; set; } = 1;
        public string Ranges { get; set; } = string.Empty;
        public PdfOutputKind OutputKind { get; set; }
        public string Watermark { get; set; } = string.Empty;
        public int Dpi { get; set; } = 200;
    }

    internal sealed class MergeItem
    {
        public string Path { get; set; }
        public int PageCount { get; set; }
        public string Range { get; set; } = string.Empty;

        public string DisplayName
        {
            get { return System.IO.Path.GetFileName(Path); }
        }

        public IReadOnlyList<int> SelectedPages()
        {
            return PageRange.Parse(Range, PageCount);
        }
    }

    internal sealed class MergeRequest
    {
        public string OutputPath { get; set; }
        public List<MergeItem> Items { get; set; } = new List<MergeItem>();
        public string Watermark { get; set; } = string.Empty;
    }

    internal sealed class ConvertRequest
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public OfficeTarget Target { get; set; }
        public int Dpi { get; set; } = 150;
        public bool OcrScannedPages { get; set; } = true;
    }

    internal static class PageRange
    {
        public static IReadOnlyList<int> Parse(string value, int pageCount)
        {
            if (pageCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount), "PDF 没有页面。");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return Enumerable.Range(0, pageCount).ToArray();
            }

            var result = new List<int>();
            foreach (var rawPart in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                var dash = part.IndexOf('-');
                int start;
                int end;
                if (dash < 0)
                {
                    if (!int.TryParse(part, out start))
                    {
                        throw new ArgumentException("页码范围格式不正确：" + part);
                    }
                    end = start;
                }
                else
                {
                    if (!int.TryParse(part.Substring(0, dash), out start) ||
                        !int.TryParse(part.Substring(dash + 1), out end))
                    {
                        throw new ArgumentException("页码范围格式不正确：" + part);
                    }
                }

                if (start < 1 || end < start || end > pageCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        "页码范围 " + part + " 超出 1-" + pageCount + "。");
                }

                for (var page = start; page <= end; page++)
                {
                    result.Add(page - 1);
                }
            }

            if (result.Count == 0)
            {
                throw new ArgumentException("请提供至少一页。");
            }
            return result;
        }

        public static List<List<int>> BuildGroups(SplitRequest request, int pageCount)
        {
            if (pageCount < 1)
            {
                throw new ArgumentException("PDF 没有页面。");
            }

            var groups = new List<List<int>>();
            if (request.Mode == SplitMode.Fixed)
            {
                if (request.PagesPerPart < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.PagesPerPart), "每份页数必须大于 0。");
                }
                for (var index = 0; index < pageCount; index += request.PagesPerPart)
                {
                    groups.Add(Enumerable.Range(index, Math.Min(request.PagesPerPart, pageCount - index)).ToList());
                }
            }
            else if (request.Mode == SplitMode.Count)
            {
                if (request.PartCount < 1 || request.PartCount > pageCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(request.PartCount), "份数必须在 1 到总页数之间。");
                }

                var baseSize = pageCount / request.PartCount;
                var remainder = pageCount % request.PartCount;
                var offset = 0;
                for (var part = 0; part < request.PartCount; part++)
                {
                    var size = baseSize + (part < remainder ? 1 : 0);
                    groups.Add(Enumerable.Range(offset, size).ToList());
                    offset += size;
                }
            }
            else
            {
                foreach (var rawGroup in request.Ranges.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    groups.Add(PageRange.Parse(rawGroup, pageCount).ToList());
                }
                if (groups.Count == 0)
                {
                    throw new ArgumentException("请填写至少一个页码范围。");
                }
            }
            return groups;
        }
    }

    internal sealed class OcrWord
    {
        public string text { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public double w { get; set; }
        public double h { get; set; }
    }

    internal sealed class OcrLine
    {
        public string text { get; set; }
        public List<OcrWord> words { get; set; } = new List<OcrWord>();
    }

    internal sealed class OcrPage
    {
        public string file { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public List<OcrLine> lines { get; set; } = new List<OcrLine>();
    }

    internal sealed class OcrDocument
    {
        public string language { get; set; }
        public List<OcrPage> pages { get; set; } = new List<OcrPage>();
    }

    internal sealed class OcrPack
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Engine { get; set; }
        public string Entry { get; set; }
        public string EntrySha256 { get; set; }
        public string Language { get; set; }
        public string DataDirectory { get; set; }
        public List<OcrPackFile> Files { get; set; } = new List<OcrPackFile>();
    }

    internal sealed class OcrPackFile
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class AppSettings
    {
        public string OutputDirectory { get; set; } =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PDF输出");
        public string DefaultOcr { get; set; } = "system";
        public string ActiveOcrPackId { get; set; } = string.Empty;
    }
}
