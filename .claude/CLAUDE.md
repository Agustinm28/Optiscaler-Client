# OptiScaler Client

Desktop utility to install and manage the OptiScaler mod across game libraries. Built with C# and Avalonia UI targeting .NET 10.0.

## Commands

```bash
# Build
dotnet build

# Run (development)
dotnet run

# Publish single-file executable (Windows)
dotnet publish -r win-x64 -c Release

# Publish for Linux (produces Exe, not WinExe)
dotnet publish -r linux-x64 -c Release
```

## Architecture

```
Views/          # Avalonia XAML UI windows and code-behind (.axaml + .axaml.cs)
Services/       # Business logic — scanning, installation, GPU detection, updates
Models/         # Data models (Game, AppConfiguration, InstallationManifest, etc.)
Helpers/        # UI helpers (AnimationHelper)
Converters/     # Avalonia value converters
Languages/      # Localization resource files (EN, ES, PT-BR)
assets/         # Icons and static assets
config.json     # GitHub repo refs for OptiScaler, Fakenvapi, NukemFG, app update source
```

## Key Files

- `OptiscalerClient.csproj` — project config; version is set here (`<Version>`)
- `config.json` — GitHub API targets (repos for OptiScaler, extras, betas, components) and scan exclusions; copied to output on build
- `Program.cs` — entry point
- `App.axaml` / `App.axaml.cs` — Avalonia app bootstrap
- `Views/MainWindow.axaml.cs` — main application window

## Key Services

| Service | Responsibility |
|---------|---------------|
| `GameScannerService` | Orchestrates all platform scanners |
| `SteamScanner` | Steam scanner — cross-platform (Linux + Windows) |
| `EpicScanner`, `GogScanner`, etc. | Windows-only scanners (Registry-based) |
| `LinuxGpuDetectionService` | GPU detection via `/sys/class/drm/`, `lspci`, `nvidia-smi` |
| `WindowsGpuDetectionService` | GPU detection via WMI — Windows only |
| `GameInstallationService` | Downloads and installs OptiScaler into game dirs |
| `OptiscalerManagementService` | Manages installed OptiScaler versions |
| `ComponentManagementService` | Manages optional components (Fakenvapi, NukemFG, FSR4) |
| `AppUpdateService` | Self-update logic via GitHub releases |
| `GameAnalyzerService` | Detects game engine / DLL structure |
| `GameMetadataService` | Fetches game metadata (SteamGridDB) |

## Gotchas

- `PublishTrimmed` is disabled globally — Avalonia/WPF limitations cause NETSDK1168 when trimming is enabled.
- Output type switches automatically: `WinExe` on Windows, `Exe` on other platforms (see `.csproj` conditions).
- `config.json` must be present at runtime; it's set to `PreserveNewest` so it copies on build but won't overwrite a modified local copy.
- `SteamGridDBApiKey` in `config.json` is intentionally blank in the repo — populate locally if needed.
- `Debug: true` in `config.json` enables the debug window.
- On Linux, only Steam scanning is supported — EpicScanner, GogScanner, EaScanner, BattleNetScanner and UbisoftScanner are Windows-only (Registry-based) and are skipped automatically.
- `System.Management` (WMI) NuGet is referenced unconditionally so `WindowsGpuDetectionService.cs` compiles on Linux — but is never called there (`[SupportedOSPlatform("windows")]`).
- `update.bat` / `update.sh` are generated at runtime by `AppUpdateService` and are gitignored.
- Steam games via Proton share the same `steamapps/common/<Game>/` directory structure, so `GameInstallationService` works on Linux without changes.

## Localization

Languages live in `Languages/`. Add new strings there and bind via Avalonia's resource system. Current supported languages: English, Spanish, Brazilian Portuguese.

## Version Bumping

Version is set in three places in `OptiscalerClient.csproj`:
```xml
<Version>X.X.X.X</Version>
<FileVersion>X.X.X.X</FileVersion>
<AssemblyVersion>X.X.X.X</AssemblyVersion>
```
