param(
    [Parameter(Mandatory = $true)][string]$InputPdf,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [string]$Pages = '',
    [int]$Dpi = 144
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfPage, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]

$asTaskOperation = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq 'AsTask' -and
        $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1)

$asTaskAction = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq 'AsTask' -and
        -not $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
    } |
    Select-Object -First 1)

function Await($Operation, [Type]$ResultType) {
    $method = $script:asTaskOperation.MakeGenericMethod($ResultType)
    $task = $method.Invoke($null, @($Operation))
    $task.Wait(-1) | Out-Null
    return $task.Result
}

function AwaitAction($Operation) {
    if (-not $script:asTaskAction) {
        throw 'IAsyncAction AsTask bridge not found.'
    }
    $task = $script:asTaskAction.Invoke($null, @($Operation))
    $task.Wait(-1) | Out-Null
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    [IO.Directory]::CreateDirectory($OutputDir) | Out-Null
}

$source = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($InputPdf)) ([Windows.Storage.StorageFile])
$document = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($source)) ([Windows.Data.Pdf.PdfDocument])
$total = [int]$document.PageCount
if ($total -lt 1) {
    throw 'PDF has no pages.'
}

if ([string]::IsNullOrWhiteSpace($Pages)) {
    $indexes = 0..($total - 1)
}
else {
    $indexes = @($Pages.Split(',') |
        ForEach-Object { [int]$_ - 1 } |
        Where-Object { $_ -ge 0 -and $_ -lt $total } |
        Select-Object -Unique)
}

$rendered = New-Object System.Collections.ArrayList
foreach ($index in $indexes) {
    $page = $document.GetPage([uint32]$index)
    $target = Join-Path $OutputDir ('page-{0:D4}.png' -f ($index + 1))
    [IO.File]::WriteAllBytes($target, [byte[]]@())
    $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($target)) ([Windows.Storage.StorageFile])
    $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::ReadWrite)) ([Windows.Storage.Streams.IRandomAccessStream])
    try {
        $options = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $options.DestinationWidth = [uint32][Math]::Max(1, [Math]::Round(($page.Size.Width / 72.0) * $Dpi))
        AwaitAction ($page.RenderToStreamAsync($stream, $options))
        Await ($stream.FlushAsync()) ([bool]) | Out-Null
    }
    finally {
        if ($stream) { $stream.Dispose() }
    }
    [void]$rendered.Add([ordered]@{
        page = $index + 1
        path = $target
        width = $options.DestinationWidth
    })
}

[ordered]@{
    pageCount = $total
    pages = $rendered
} | ConvertTo-Json -Depth 5 -Compress
