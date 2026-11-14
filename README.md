# Mouse Battery Tray

*Formerly "Pulsar Battery Tray". Installing this version automatically removes the old one.*

Shows the battery level of a wireless **Pulsar** or **Logitech** mouse in the Windows notification area (next to the clock), with a details window on click.

> **Unofficial** project, not affiliated with Pulsar Gaming Gears or Logitech. "Pulsar" and "Logitech" are trademarks of their owners.

![Tray icons and details window](docs/screenshot.png)

## Features

- **Battery-shaped icon** that fills according to the level: green, then orange, then red. It turns blue with a lightning bolt while charging.
- **Left click**: details window (mouse name, percentage, status, voltage, connection type, time of the last reading). The gear in its top-right corner opens the settings.
- **Right click**: show details, refresh, mouse to display, settings, start with Windows, low battery alert, exit.
- **Several mice**: the icon shows the first mouse that answers; when more than one is detected, pick the one to display from the right-click menu or the settings. The low battery alert works for every detected mouse.
- **English and French**, **light, dark or system theme** (see [Settings](#settings)).
- A single `.exe`, nothing else to install on Windows 10 and 11.

## Settings

Open them with the gear of the details window, or with **Settings…** in the right-click menu. It is a normal window with its own taskbar button; closing it does not stop the app. Every change applies at once:

| Setting | Choices |
| --- | --- |
| Language | Français / English. On first launch the app uses the language chosen in the installer, otherwise the Windows language (English by default). |
| Theme | System (follows Windows), Light, Dark. Applies to the details window, the settings window (title bar included) and the right-click menu. |
| Start with Windows | On / off |
| Low battery alert | On / off, and the threshold (5% to 50%, 15% by default) |
| Mouse to display | Automatic, or one of the detected mice (when several are detected) |
| Read the mouse every | 30 s, 1, 2, 5 or 10 min (1 min by default) |
| Read less often while the level does not change | On by default (see [When the mouse is read](#when-the-mouse-is-read)) |
| Write a diagnostic log | Off by default (see [Reporting a problem](#reporting-a-problem)) |

The settings window also has a **Refresh now** button and an **About** section (version, GitHub page, license, and why the level is off while charging).

### When the mouse is read

- At the interval chosen in the settings.
- **Every 30 s while charging**, to follow the charge.
- **Less often while nothing changes** (option on by default): after 3 identical readings the interval doubles, after 6 it is multiplied by 4, up to 15 min. It goes back to normal as soon as the level moves by 2% or more, the mouse is plugged in or out, falls asleep, or gets close to the low battery threshold.
- **Right away after an event**, without waiting: the PC wakes up from sleep, the session is unlocked, or a Pulsar / Logitech receiver, cable or Bluetooth mouse is connected or disconnected.

The settings window shows the time of the last reading and of the next one.

Only one copy of the app runs at a time: launching it again opens the settings window of the running copy.

### Updates

The **Updates** section shows the current version. **Check for updates** reads the latest release published on this GitHub page (pre-releases are ignored). When a newer version exists, **Install** downloads its installer, checks its SHA-256 fingerprint published by GitHub, closes the app and opens the installer in the app's language. A portable copy is updated in its own folder, a standard installation in its installation folder. Nothing is downloaded or installed without a click and a confirmation.

For this to work, each release must have the installer (`MouseBatteryTray-Setup-x.y.z.exe`) attached and a tag like `v1.3.0`.

The right-click menu and the settings window stay in sync. Settings are stored in `HKCU\Software\MouseBatteryTray` (standard installation) or in `portable.ini` next to the exe (portable version). In portable mode nothing is written to the registry: "Start with Windows" then uses a shortcut in your Startup folder.

## Installation

1. Download `MouseBatteryTray-Setup-x.y.z.exe` from the [Releases](../../releases/latest) page.
2. Run it and choose the installation type:
   - **Standard**: Start menu shortcut, uninstaller, and a "Start with Windows" option.
   - **Portable**: plain copy to the folder of your choice. Settings are kept in a `portable.ini` file next to the exe and nothing is written to the registry.
3. The icon appears next to the clock. If it is hidden behind the `^` arrow, drag it onto the taskbar.

The installer does not require administrator rights. It is available in English and French, and the app starts in the language you picked in the installer.

### "Windows protected your PC" warning

The installer is not code-signed (certificates cost money), so Windows SmartScreen may show this warning. Click **More info**, then **Run anyway**. The full source code is in this repository if you prefer to review it or build it yourself.

## Compatibility

| Hardware | Status |
| --- | --- |
| Pulsar X2 V2 Mini, plugged in over USB | Tested |
| Pulsar X2 V2 Mini, 2.4 GHz wireless receiver | Tested |
| Other Pulsar mice | Not tested |
| Logitech G502 Wireless | Tested (reported working by a user) |
| Other Logitech wireless mice (HID++ 2.0: Lightspeed, Unifying, Bolt, USB cable) | Experimental, not tested |
| Logitech mice over Bluetooth | Experimental, not tested |
| Older Logitech mice (HID++ 1.0) | Not supported |

- **Pulsar**: the app looks for Pulsar devices (USB vendor ID `3554`). Other models using the same protocol may work.
- **Logitech**: the app uses Logitech's HID++ 2.0 protocol and reads the mouse name and battery from the device itself (battery features `0x1004`, `0x1000` or `0x1001`). Keyboards, headsets and other non-mouse devices are ignored. It has been confirmed working on a Logitech G502 Wireless; other HID++ 2.0 mice should work the same way but have not been tested.

Feedback is welcome in the *Issues*, ideally with the output of the diagnostic mode (see below).

The app is independent from Pulsar Fusion, Logitech G HUB and Logi Options+: it talks to the mouse directly and only sends read requests. It never changes the mouse settings, and it can run alongside those apps.

## Why does the level jump to ~95% as soon as the mouse is plugged in?

This comes from the mouse, not from this app. Some mice (all Pulsar mice supported here, and Logitech mice that only report a voltage) estimate their battery from the voltage: when plugged in, the voltage rises and the reported level is overestimated. Once unplugged, it settles back to the real value within a few minutes. Logitech mice that report a real state of charge are not affected, and their percentage stays visible while charging.

To avoid showing a misleading number, the app:

- hides the percentage while charging (the reported level stays visible, prefixed with "≈");
- waits 5 minutes after unplugging before showing it again;
- shows "Charged" when the plugged-in mouse reports 100%.

## Reporting a problem

Attach the output of the diagnostic mode to your report. In PowerShell, from the app folder:

```powershell
.\MouseBatteryTray.exe --once diag.txt
```

The `diag.txt` file lists the detected interfaces and the exchanges with the mouse. It contains no personal information.

For a problem that comes and goes, turn on **Write a diagnostic log** in the settings instead: every reading is then recorded with the same details, along with the app events (wake from sleep, device plugged in or out, time of the next reading). **Open the folder** shows the log files:

- standard installation: `%LOCALAPPDATA%\MouseBatteryTray\logs`
- portable version: `logs` folder next to the exe

The log is limited to 3 files of 1 MB (`MouseBatteryTray.log`, `.1.log`, `.2.log`); the oldest is deleted automatically.

## Building from source

No tools are needed for the exe: the script uses the C# compiler that ships with Windows.

```powershell
.\build.ps1
```

To also build the installer (into `dist\`), install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and run:

```powershell
.\build.ps1 -Setup
```

| File | Purpose |
| --- | --- |
| `src/MouseBatteryTray.cs` | Mouse detection, Pulsar and Logitech HID++ protocols |
| `src/TrayContext.cs` | Notification area icon, right-click menu, readings |
| `src/DetailsForm.cs` | Details window |
| `src/SettingsForm.cs` | Settings window |
| `src/Settings.cs` | Settings storage (registry or `portable.ini`) |
| `src/Updater.cs` | Update check and download from GitHub Releases |
| `src/DeviceWatcher.cs` | Device plugged in / out notifications, reading pace |
| `src/Logger.cs` | Optional diagnostic log with size rotation |
| `src/Lang.cs` | All the texts, in English and French |
| `src/Theme.cs` | Light / dark theme |
| `src/Ui.cs` | Tray icon and gear drawing |
| `src/Program.cs` | Entry point, diagnostic and preview modes |
| `build.ps1` | Builds the exe and the installer |
| `installer/setup.iss` | Installer script |
| `tools/make-icon.ps1` | Generates `app.ico` |

## Credits

The Pulsar communication protocol comes from Andrew Rabert's reverse-engineering work in [python-pulsar-mouse-tool](https://github.com/andrewrabert/python-pulsar-mouse-tool) (MIT license). Logitech support follows the public documentation of the HID++ 2.0 protocol.

## License

[MIT](LICENSE)
