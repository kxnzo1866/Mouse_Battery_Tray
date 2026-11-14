# Builds MouseBatteryTray.exe from MouseBatteryTray.cs (uses the compiler that ships with Windows, nothing to install).
# With -Setup, also builds the installer into dist\ (requires Inno Setup 6: https://jrsoftware.org/isinfo.php).
param([switch]$Setup)

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /out:"$PSScriptRoot\MouseBatteryTray.exe" `
    /win32icon:"$PSScriptRoot\app.ico" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    "$PSScriptRoot\MouseBatteryTray.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
"OK: MouseBatteryTray.exe"

if ($Setup) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { Write-Error "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php"; exit 1 }
    & $iscc /Q "$PSScriptRoot\installer\setup.iss"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    "OK: installer written to dist\"
}
