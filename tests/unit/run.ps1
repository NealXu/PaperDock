$ErrorActionPreference = 'Stop'
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -STA -ExecutionPolicy Bypass `
    -File (Join-Path $PSScriptRoot 'run-csi.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Self-check failed with exit code $LASTEXITCODE"
}
