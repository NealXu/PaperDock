param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArguments
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'app\dist\PaperDock.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "WPF application not found: $exe"
}

if ($AppArguments -and $AppArguments.Count -gt 0) {
    Start-Process -FilePath $exe -ArgumentList $AppArguments
}
else {
    Start-Process -FilePath $exe
}
