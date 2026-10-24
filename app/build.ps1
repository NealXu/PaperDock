$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Roslyn C# compiler not found: $compiler"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'lib') -File |
    Copy-Item -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination $dist -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'scripts') -Destination $dist -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'PaperDock.exe.config') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.txt') -Destination $dist -Force

$references = Get-ChildItem -LiteralPath $dist -File -Filter '*.dll' |
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
    "/reference:$(Join-Path $framework 'System.Windows.Forms.dll')",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    "/reference:$(Join-Path $framework 'WPF\WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'WPF\PresentationCore.dll')",
    "/reference:$(Join-Path $framework 'WPF\PresentationFramework.dll')"
)
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -File -Filter '*.cs' |
    Select-Object -ExpandProperty FullName

& $compiler /nologo /langversion:latest /platform:x64 /target:winexe `
    "/out:$(Join-Path $dist 'PaperDock.exe')" `
    "/win32manifest:$(Join-Path $root 'app.manifest')" `
    $frameworkReferences $references $sources

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $(Join-Path $dist 'PaperDock.exe')"
