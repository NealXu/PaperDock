param(
    [Parameter(Mandatory = $true)][string]$InputDir,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$Language = 'zh-Hans-CN'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]

$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq 'AsTask' -and
        $_.IsGenericMethod -and
        $_.GetParameters().Count -eq 1
    } |
    Select-Object -First 1)

function Await($Operation, [Type]$ResultType) {
    $method = $script:asTask.MakeGenericMethod($ResultType)
    $task = $method.Invoke($null, @($Operation))
    $task.Wait(-1) | Out-Null
    return $task.Result
}

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage(
    [Windows.Globalization.Language]::new($Language))
if (-not $engine) {
    throw "OCR language is not available: $Language"
}

$pages = New-Object System.Collections.ArrayList
Get-ChildItem -LiteralPath $InputDir -File |
    Where-Object { $_.Extension -in '.png', '.jpg', '.jpeg' } |
    Sort-Object Name |
    ForEach-Object {
        $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($_.FullName)) ([Windows.Storage.StorageFile])
        $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
        try {
            $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
            $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
            $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])

            $lines = New-Object System.Collections.ArrayList
            foreach ($line in $result.Lines) {
                $words = New-Object System.Collections.ArrayList
                foreach ($word in $line.Words) {
                    $rect = $word.BoundingRect
                    [void]$words.Add([ordered]@{
                        text = $word.Text
                        x = [double]$rect.X
                        y = [double]$rect.Y
                        w = [double]$rect.Width
                        h = [double]$rect.Height
                    })
                }
                [void]$lines.Add([ordered]@{
                    text = $line.Text
                    words = $words
                })
            }
            [void]$pages.Add([ordered]@{
                file = $_.Name
                width = $bitmap.PixelWidth
                height = $bitmap.PixelHeight
                lines = $lines
            })
        }
        finally {
            if ($stream) { $stream.Dispose() }
        }
    }

[ordered]@{
    language = $Language
    pages = $pages
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8
