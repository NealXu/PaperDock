param(
    [Parameter(Mandatory = $true)][string]$PackPath
)

$ErrorActionPreference = 'Stop'
$pack = (Resolve-Path -LiteralPath $PackPath).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $tempRoot ('paperdock-ocrpack-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$updated = Join-Path $work 'updated.ocrpack.zip'

New-Item -ItemType Directory -Path $source | Out-Null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($pack, $source)
    $manifestPath = Join-Path $source 'ocrpack.json'
    $existing = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @(Get-ChildItem -LiteralPath $source -Recurse -File |
        Where-Object Name -ne 'ocrpack.json' |
        Sort-Object FullName |
        ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($source, $_.FullName).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    $entryHash = ($files | Where-Object path -eq $existing.entry).sha256
    if (-not $entryHash) {
        throw "Pack entry was not found: $($existing.entry)"
    }

    $manifest = [ordered]@{
        id = $existing.id
        name = $existing.name
        version = $existing.version
        engine = $existing.engine
        entry = $existing.entry
        entrySha256 = $entryHash
        language = $existing.language
        dataDirectory = $existing.dataDirectory
        files = $files
    }
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 5),
        (New-Object Text.UTF8Encoding($false)))
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $source,
        $updated,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
    Copy-Item -LiteralPath $updated -Destination $pack -Force
}
finally {
    $resolved = [IO.Path]::GetFullPath($work)
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}
