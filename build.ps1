# Builds MouseBatteryTray.exe from the source files in src\ (uses the compiler that ships with Windows, nothing to install).
# With -Setup, also builds the installer into dist\ (requires Inno Setup 6: https://jrsoftware.org/isinfo.php).
#
# Code signing (optional, removes SmartScreen's "Unknown publisher" warning): set the environment variable
# MBT_SIGN_CERT to the SHA-1 thumbprint of an Authenticode code-signing certificate installed in the
# current user's store (Cert:\CurrentUser\My). The exe, the installer and its uninstaller are then signed
# and timestamped with signtool.exe (Windows SDK). Without it, everything is built unsigned as before.
param([switch]$Setup)

$signtool = $null
$certThumb = $env:MBT_SIGN_CERT
$timestampUrl = "http://timestamp.digicert.com"
if ($certThumb) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) { Write-Error "MBT_SIGN_CERT is set but signtool.exe (Windows SDK) was not found."; exit 1 }
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /out:"$PSScriptRoot\MouseBatteryTray.exe" `
    /win32icon:"$PSScriptRoot\app.ico" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    "$PSScriptRoot\src\*.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
"OK: MouseBatteryTray.exe"

if ($signtool) {
    & $signtool sign /sha1 $certThumb /fd sha256 /tr $timestampUrl /td sha256 "$PSScriptRoot\MouseBatteryTray.exe"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    "OK: MouseBatteryTray.exe signed"
}

if ($Setup) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { Write-Error "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php"; exit 1 }
    if ($signtool) {
        # $f is replaced by Inno Setup with the file to sign (installer, uninstaller).
        $signCmd = "signcmd=`"$signtool`" sign /sha1 $certThumb /fd sha256 /tr $timestampUrl /td sha256 `$f"
        & $iscc /Q /DSign=1 "/S$signCmd" "$PSScriptRoot\installer\setup.iss"
    } else {
        & $iscc /Q "$PSScriptRoot\installer\setup.iss"
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($signtool) { "OK: signed installer written to dist\" } else { "OK: installer written to dist\" }
}
