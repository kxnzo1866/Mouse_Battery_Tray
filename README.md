# Mouse Battery Tray

*Formerly "Pulsar Battery Tray". Installing this version automatically removes the old one.*

Shows the battery level of a wireless **Pulsar** or **Logitech** mouse in the Windows notification area (next to the clock), with a details window on click.

> **Unofficial** project, not affiliated with Pulsar Gaming Gears or Logitech. "Pulsar" and "Logitech" are trademarks of their owners.

![Tray icons and details window](docs/screenshot.png)

## Features

- **Battery-shaped icon** that fills according to the level: green, then orange, then red. It turns blue with a lightning bolt while charging.
- **Left click**: details window (mouse name, percentage, status, voltage, connection type, time of the last reading).
- **Right click**: refresh, start with Windows, low battery alert (15%), exit.
- **Several mice**: the icon shows the first mouse that answers; when more than one is detected, pick the one to display from the right-click menu ("Mouse to display"). The low battery alert works for every detected mouse.
- Reads the mouse every 60 seconds and follows the Windows light or dark theme.
- A single `.exe`, nothing else to install on Windows 10 and 11.

## Installation

1. Download `MouseBatteryTray-Setup-x.y.z.exe` from the [Releases](../../releases/latest) page.
2. Run it and choose the installation type:
   - **Standard**: Start menu shortcut, uninstaller, and a "Start with Windows" option.
   - **Portable**: plain copy to the folder of your choice. Settings are kept in a `portable.ini` file next to the exe and nothing is written to the registry.
3. The icon appears next to the clock. If it is hidden behind the `^` arrow, drag it onto the taskbar.

The installer does not require administrator rights. It is available in English and French.

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
| `MouseBatteryTray.cs` | All of the application code |
| `build.ps1` | Builds the exe and the installer |
| `installer/setup.iss` | Installer script |
| `tools/make-icon.ps1` | Generates `app.ico` |

## Credits

The Pulsar communication protocol comes from Andrew Rabert's reverse-engineering work in [python-pulsar-mouse-tool](https://github.com/andrewrabert/python-pulsar-mouse-tool) (MIT license). Logitech support follows the public documentation of the HID++ 2.0 protocol.

## License

[MIT](LICENSE)
