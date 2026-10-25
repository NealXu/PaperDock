$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$roslyn = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn'
$run = Join-Path $PSScriptRoot '.build\csi-full'
if (-not (Test-Path -LiteralPath (Join-Path $run 'csi.exe'))) {
    New-Item -ItemType Directory -Force -Path $run | Out-Null
    Copy-Item -Path (Join-Path $roslyn '*') -Destination $run -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $root 'app\PaperDock.exe.config') -Destination (Join-Path $run 'csi.exe.config') -Force
foreach ($name in 'System.Buffers.dll','System.Memory.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll') {
    Copy-Item -LiteralPath (Join-Path $root "app\lib\$name") -Destination $run -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $run 'assets') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $run 'scripts') | Out-Null
Copy-Item -Path (Join-Path $root 'app\assets\*') -Destination (Join-Path $run 'assets') -Force
Copy-Item -Path (Join-Path $root 'app\scripts\*') -Destination (Join-Path $run 'scripts') -Force

$sources = @(
    (Join-Path $root 'app\src\Models.cs'),
    (Join-Path $root 'app\src\Infrastructure.cs'),
    (Join-Path $root 'app\src\PdfEngine.cs'),
    (Join-Path $root 'app\src\OcrService.cs'),
    (Join-Path $root 'app\src\SystemPdfService.cs'),
    (Join-Path $root 'app\src\OfficeEngine.cs'),
    (Join-Path $PSScriptRoot 'PaperDock.SelfCheck.cs')
)
$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Xml.dll'),
    (Join-Path $framework 'System.Xml.Linq.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.IO.Compression.dll'),
    (Join-Path $framework 'System.IO.Compression.FileSystem.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'WPF\WindowsBase.dll'),
    (Join-Path $framework 'WPF\PresentationCore.dll'),
    (Join-Path $run 'System.Buffers.dll'),
    (Join-Path $run 'System.Memory.dll'),
    (Join-Path $run 'System.Numerics.Vectors.dll'),
    (Join-Path $run 'System.Runtime.CompilerServices.Unsafe.dll')
) + @(Get-ChildItem -LiteralPath (Join-Path $root 'app\lib') -File -Filter '*.dll' |
    Where-Object Name -notin 'System.Buffers.dll','System.Memory.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll' |
    Select-Object -ExpandProperty FullName)
$usings = @'
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
using System.Web.Script.Serialization;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using PdfPage = PdfSharp.Pdf.PdfPage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
'@
$body = ($sources | ForEach-Object {
    $text = [IO.File]::ReadAllText($_, [Text.Encoding]::UTF8)
    $text = $text -replace '(?m)^using [^\r\n]+;\s*', ''
    $text = $text -replace 'namespace PaperDock\s*\{', ''
    $text = $text -replace 'new PdfDocument\(\)', 'new PdfSharp.Pdf.PdfDocument()'
    $text = $text.TrimEnd()
    if ($text.EndsWith('}')) {
        $text = $text.Substring(0, $text.Length - 1)
    }
    $text
}) -join [Environment]::NewLine
$script = (($references | ForEach-Object { '#r "' + $_ + '"' }) -join [Environment]::NewLine) +
    [Environment]::NewLine + $usings + [Environment]::NewLine + $body +
    [Environment]::NewLine + 'Environment.ExitCode = SelfCheck.Main();'
$scriptPath = Join-Path $run 'paperdock-self-check.csx'
[IO.File]::WriteAllText($scriptPath, $script, (New-Object Text.UTF8Encoding($false)))

$env:PAPERDOCK_REPO_ROOT = $root
$env:PAPERDOCK_RELEASE_OCR_PACK = Join-Path $root 'release\ocr\Tesseract-5.5-zh-en.ocrpack.zip'
try {
    & (Join-Path $run 'csi.exe') $scriptPath
    if ($LASTEXITCODE -ne 0) {
        throw "Self-check failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item Env:\PAPERDOCK_REPO_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:\PAPERDOCK_RELEASE_OCR_PACK -ErrorAction SilentlyContinue
}
