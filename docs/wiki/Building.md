# 🛠️ Building

## Requirements

- Windows 10/11
- A .NET SDK, 6 or newer (`global.json` rolls forward to newer SDKs)
- Git, to clone with submodules

## Build

```powershell
git clone --recursive https://github.com/AGamingotter/PSYSTRAP.git
cd PSYSTRAP
powershell -ExecutionPolicy Bypass -File fork\build.ps1
```

The result is a single **`fork\dist\PSYstrap.exe`**, with the license files next to it.

> ⚠️ Don't distribute the `bin\…\PSYstrap.exe` from a plain `dotnet build`. That's only a small launcher, and the program lives in a separate DLL. The installer copies only the exe, so the installed copy wouldn't start.

## Releasing

The **Build** workflow (`.github/workflows/build.yml`) builds every push. To publish a release:

```powershell
git tag v2.12.1
git push origin v2.12.1
```

GitHub builds the exe and publishes **PSYstrap v2.12.1** with `PSYstrap.exe` attached. Installed copies update themselves on their next launch.

- Tags must be **newer than the version in `Bloxstrap/Bloxstrap.csproj`** (currently 2.11.4), or the updater treats them as old.
- The repo must be **public**, because the updater checks GitHub without signing in.

## Renaming / pointing at another repo

`fork\rebrand.ps1` renames the app and points updates and links at a repo:

```powershell
powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name PSYstrap -Repo AGamingotter/PSYSTRAP -Icon path\to\icon.ico
```

It updates the exe name, install folder, window titles, all 36 translations and the repo links. It's safe to run again. It deliberately keeps the internal `Bloxstrap` namespace (so upstream fixes merge easily) and the Bloxstrap license credit.

## Pulling Bloxstrap updates

Bloxstrap is set up as the `upstream` remote:

```powershell
git fetch upstream
git merge upstream/main
```

## Project layout (PSYstrap additions)

| Path | What |
|---|---|
| `Bloxstrap/Integrations/OverlayController.cs` | In-game overlay, hotkeys, menu |
| `Bloxstrap/Integrations/FrameRateMonitor.cs` | FPS counter (Windows ETW) |
| `Bloxstrap/Integrations/Macros/` | Recorder, player, smart macros, detectors, OCR, turn-taking |
| `Bloxstrap/MultiInstance.cs` | Multi-instance host, instance folders, priority/RAM |
| `Bloxstrap/Accounts/` | Saved accounts, Roblox sign-in, rejoin |
| `Bloxstrap/Skybox.cs`, `Utility/DdsBc1.cs` | Skybox presets and the DXT1 encoder |
| `Bloxstrap/PotatoMode.cs`, `RobloxUserSettings.cs`, `RobloxServers.cs` | Potato mode, frame-rate cap, smallest server |
| `Bloxstrap/UI/Style/PSY.xaml` | The PSY theme |
| `fork/` | Build/rebrand scripts and branding |
