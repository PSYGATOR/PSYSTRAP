# Building

You need Windows, Git and the .NET SDK (6 or newer works).

```
git clone --recursive https://github.com/AGamingotter/PSYSTRAP.git
cd PSYSTRAP
powershell -ExecutionPolicy Bypass -File fork\build.ps1
```

You get `fork\dist\PSYstrap.exe` plus the license files. Don't use the exe that a normal `dotnet build` puts in `bin`, it's only a small launcher and won't work once it's installed.

## Releases

Every push gets built by the Build workflow. To make a release:

```
git tag v2.12.1
git push origin v2.12.1
```

GitHub builds it and puts out a release with the exe, and installed copies update themselves next time they launch. Two things:

- the tag has to be higher than the version in `Bloxstrap/Bloxstrap.csproj` (2.11.4 right now), otherwise the updater thinks it's older
- the repo has to be public, because the updater checks GitHub without logging in

## rebrand.ps1

Changes the app name and which repo it updates from:

```
powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name PSYstrap -Repo AGamingotter/PSYSTRAP -Icon path\to\icon.ico
```

It updates the exe name, install folder, window titles, translations and links. You can run it again whenever. It leaves the code namespace as `Bloxstrap` on purpose so Bloxstrap updates are easy to merge.

## Getting Bloxstrap updates

```
git fetch upstream
git merge upstream/main
```

## Where the PSYstrap stuff is

- `Bloxstrap/Integrations/OverlayController.cs` - overlay, hotkeys, menu
- `Bloxstrap/Integrations/FrameRateMonitor.cs` - FPS counter
- `Bloxstrap/Integrations/Macros/` - macros, smart macros, detectors, OCR, turn taking
- `Bloxstrap/MultiInstance.cs` - multi instance
- `Bloxstrap/Accounts/` - saved accounts
- `Bloxstrap/Skybox.cs` and `Utility/DdsBc1.cs` - skybox
- `Bloxstrap/PotatoMode.cs`, `RobloxUserSettings.cs`, `RobloxServers.cs` - potato mode, FPS cap, smallest server
- `Bloxstrap/UI/Style/PSY.xaml` - the theme
- `fork/` - build and rebrand scripts, logo
