# SensorDeck

Windows WPF app (.NET 10): hardware sensor tree (LibreHardwareMonitorLib) + always-on-top overlay toggled by a global hotkey. Everything in the repo (UI text, README, release notes, commits) must be in English: the repo is public and the owner wants no Russian on GitHub, even though they talk to Claude in Russian.

## Layout

- `SensorDeck/` — the project (`SensorDeck.csproj`). Services/, ViewModels/, `MainWindow.xaml`, `OverlayWindow.xaml`, `App.xaml.cs` (startup, tray, CLI test modes).
- `docs/` — README screenshots.
- `publish/`, `SensorDeck-app/` — local build output, git-ignored.

## Build / run

```bash
cd SensorDeck
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../publish
```

The app manifest is `requireAdministrator`, so every launch shows a UAC prompt. If a copy is already running, publishing over its exe fails ("access denied"); publish to another folder or quit it from the tray first.

## Testing without clicking around

- `SensorDeck.exe --dump file.txt` — no UI; writes every sensor, value, admin/PawnIO state.
- `SensorDeck.exe --snap dir` — uses a throwaway settings file in `dir`, adds demo sensors, renders `main.png` and `overlay.png` and exits. Read the PNGs to check the UI.
- To run without elevation (fewer sensors, but no UAC prompt): set env `__COMPAT_LAYER=RunAsInvoker` before starting the exe.

## Gotchas

- The bundled `SensorDeck/Resources/PawnIO_setup.exe` must match what LibreHardwareMonitorLib needs. When bumping the NuGet package, check LibreHardwareMonitor's own `PawnIO_setup.exe` / minimum version and update both the file and `PawnIoInstaller.BundledVersion` / `MinimumVersion`. Keep `THIRD-PARTY-NOTICES.md` (version + SHA-256) in sync.
- LibreHardwareMonitorLib sometimes gives two sensors the same `Identifier`; `MainViewModel.Rebuild` disambiguates with `|name`. Panel entries are saved by that id.
- `PawnIo.Version` in the library is cached in a static constructor; use `PawnIoInstaller.InstalledVersion()` (reads the registry fresh).

## Releases

GitHub repo: `pugachoveo/SensorDeck` (public). Release assets: `SensorDeck.exe` (framework-dependent) and `SensorDeck-standalone.exe` (self-contained). Bump `<Version>` in the csproj before tagging.
