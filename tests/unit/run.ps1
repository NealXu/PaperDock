$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $PSScriptRoot '.build'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Roslyn C# compiler not found: $compiler"
}
New-Item -ItemType Directory -Force -Path $build | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'app\lib') -File -Filter '*.dll' |
    Copy-Item -Destination $build -Force

$references = Get-ChildItem -LiteralPath (Join-Path $root 'app\lib') -File -Filter '*.dll' |
    ForEach-Object { "/reference:$($_.FullName)" }
$frameworkReferences = @(
    "/reference:$(Join-Path $framework 'System.dll')",
    "/reference:$(Join-Path $framework 'System.Core.dll')",
    "/reference:$(Join-Path $framework 'System.Xml.dll')",
    "/reference:$(Join-Path $framework 'System.Xml.Linq.dll')",
    "/reference:$(Join-Path $framework 'System.Drawing.dll')",
    "/reference:$(Join-Path $framework 'System.IO.Compression.dll')",
    "/reference:$(Join-Path $framework 'System.IO.Compression.FileSystem.dll')",
    "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')",
    "/reference:$(Join-Path $framework 'WPF\WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'WPF\PresentationCore.dll')"
)
$sources = @(
    (Join-Path $root 'app\src\Models.cs'),
    (Join-Path $root 'app\src\Infrastructure.cs'),
    (Join-Path $root 'app\src\PdfEngine.cs'),
    (Join-Path $root 'app\src\OcrService.cs'),
    (Join-Path $root 'app\src\SystemPdfService.cs'),
    (Join-Path $PSScriptRoot 'PaperDock.SelfCheck.cs')
)
$output = Join-Path $build 'PaperDock.SelfCheck.exe'
Copy-Item -LiteralPath (Join-Path $root 'app\PaperDock.exe.config') `
    -Destination ($output + '.config') -Force

& $compiler /nologo /langversion:latest /platform:x64 /target:exe `
    "/out:$output" $frameworkReferences $references $sources
if ($LASTEXITCODE -ne 0) {
    throw "Self-check compilation failed with exit code $LASTEXITCODE"
}

$env:PAPERDOCK_REPO_ROOT = $root
$env:PAPERDOCK_RELEASE_OCR_PACK = Join-Path $root 'release\ocr\Tesseract-5.5-zh-en.ocrpack.zip'
try {
    & $output
    if ($LASTEXITCODE -ne 0) {
        throw "Self-check failed with exit code $LASTEXITCODE"
    }
}
finally {
    Remove-Item Env:\PAPERDOCK_REPO_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:\PAPERDOCK_RELEASE_OCR_PACK -ErrorAction SilentlyContinue
}
