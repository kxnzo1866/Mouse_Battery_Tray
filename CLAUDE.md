# Mouse Battery Tray (ex-Pulsar Battery Tray)

Windows tray app (C# / WinForms, .NET Framework 4.x) showing the battery of Pulsar and Logitech (HID++ 2.0) wireless mice.

## Build & run
- Build exe: `.\build.ps1` (uses Windows' built-in `csc.exe`, C# 5 max: no `?.`, `$""`, `nameof`, expression-bodied members, etc.)
- Build installer: `.\build.ps1 -Setup` (Inno Setup 6, output in `dist\`)
- Diagnostic reading: `.\MouseBatteryTray.exe --once diag.txt`
- Visual check without a mouse: `.\MouseBatteryTray.exe --preview preview.png` (draws icons + windows into a PNG, plus `preview-styles.png` = details window in every style and `preview-settings.png` = settings window, both tabs, every style)
- No test suite: verify by building, then `--once` / `--preview`.
- `git` is not on PATH in the PowerShell used by Claude.

## Code map (`src/`)
| File | Role |
| --- | --- |
| `MouseBatteryTray.cs` | Device detection, Pulsar + Logitech HID++ protocols, `AssemblyVersion` |
| `TrayContext.cs` | Tray icon, right-click menu, reading loop |
| `DeviceWatcher.cs` | Plug/unplug notifications, reading pace (backoff) |
| `DetailsForm.cs` / `SettingsForm.cs` | Details and settings windows |
| `Settings.cs` | Storage: `HKCU\Software\MouseBatteryTray` or `portable.ini` |
| `Updater.cs` | GitHub Releases check + SHA-256 verified install |
| `Lang.cs` | ALL user-facing strings (EN + FR) |
| `Theme.cs` / `Ui.cs` | Light/dark theme, title bar, menu renderer, icon & gear drawing |
| `Styles.cs` | Visual styles (`Palette` per style × light/dark), DWM/Acrylic calls, `StyledButton`, `TabStrip`, `StyleCard` |
| `Logger.cs` | Optional rotating diagnostic log |
| `Program.cs` | Entry point, single instance, `--once` / `--preview` modes |

## Rules
- Every new user-facing text goes in `Lang.cs`, in both English and French.
- Only send read requests to the mouse: never write mouse settings.
- Theme changes must cover the details window, settings window (title bar too) and context menu.
- Colours come from `Theme.P` (current `Palette`), never hard-coded per window. While Acrylic is on, the details window paints alpha 0 + GDI+ only (no GDI/TextRenderer text).
- Portable mode must never write to the registry.
- Release: bump version in BOTH `src/MouseBatteryTray.cs` (`AssemblyVersion`) and `installer/setup.iss` (`AppVersion`); tag `vX.Y.Z`; attach `MouseBatteryTray-Setup-X.Y.Z.exe` to the GitHub release.
- Update the README (features, settings table, file table) when behavior changes.

## Don't read
`dist/`, `*.exe`, `docs/screenshot.png`, `app.ico`: generated or binary.
