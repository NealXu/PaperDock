param(
    [string]$CertificateThumbprint = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1')

$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) {
    throw "Inno Setup compiler not found: $iscc"
}
$iss = Get-Content -LiteralPath (Join-Path $root 'PaperDock.iss') -Raw
$versionMatch = [regex]::Match($iss, '#define MyAppVersion "([^"]+)"')
if (-not $versionMatch.Success) {
    throw 'App version was not found in PaperDock.iss'
}
$version = $versionMatch.Groups[1].Value
& $iscc (Join-Path $root 'PaperDock.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed with exit code $LASTEXITCODE"
}

$installer = Join-Path $root "..\release\wpf\PaperDock-Setup-$version.exe"
$portable = Join-Path $root "..\release\wpf\PaperDock-Portable-$version.zip"
Compress-Archive -Path (Join-Path $root 'dist\*') -DestinationPath $portable -CompressionLevel Optimal -Force
if ($CertificateThumbprint) {
    $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Select-Object -First 1
    if (-not $signTool) {
        throw 'signtool.exe not found.'
    }
    & $signTool.FullName sign /sha1 $CertificateThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $installer
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed with exit code $LASTEXITCODE"
    }
}

$hashes = Get-FileHash -Algorithm SHA256 -LiteralPath $installer, $portable
$hashes | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath (Join-Path $root '..\release\wpf\SHA256.txt') -Encoding ASCII
$hashes
