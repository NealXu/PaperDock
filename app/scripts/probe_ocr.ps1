$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime]

$languages = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages |
    ForEach-Object { $_.LanguageTag })
$simplifiedChinese = @($languages | Where-Object {
    $_ -like 'zh-Hans*' -or $_ -eq 'zh-CN'
})

[ordered]@{
    available = $simplifiedChinese.Count -gt 0
    languages = $languages
} | ConvertTo-Json -Compress
