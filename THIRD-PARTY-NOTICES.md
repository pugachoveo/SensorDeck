# Third-party notices

SensorDeck's own code is MIT licensed (see `LICENSE`). It uses and redistributes the following components, each under its own license.

## PawnIO driver installer

- File: `SensorDeck/Resources/PawnIO_setup.exe` (embedded unmodified into `SensorDeck.exe`)
- Version: 2.2.0, SHA-256 `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`
- Author: namazso. The binary is Authenticode-signed by `namazso.eu`
- License: GNU General Public License v2.0
- Binary obtained from: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- Complete corresponding source code: https://github.com/namazso/PawnIO

SensorDeck only runs this installer (with the user's consent, via the "Install PawnIO" button). It does not link to or modify it; the two are separate programs distributed together.

## LibreHardwareMonitorLib

- NuGet package `LibreHardwareMonitorLib` 0.9.6, including the signed PawnIO modules embedded in it
- License: Mozilla Public License 2.0
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

LibreHardwareMonitorLib brings in further NuGet dependencies (for example HidSharp, DiskInfoToolkit, RAMSPDToolkit-NDD and System.Management). Their licenses are listed on their NuGet pages and are included in the packages.

## .NET

- Microsoft .NET runtime and Windows Desktop (WPF, Windows Forms), MIT license: https://github.com/dotnet/runtime, https://github.com/dotnet/wpf
