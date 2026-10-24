$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]

$languages = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages |
    ForEach-Object { $_.LanguageTag })

[ordered]@{
    available = $languages.Count -gt 0
    languages = $languages
} | ConvertTo-Json -Compress
