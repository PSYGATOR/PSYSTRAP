# Building

You need Windows, Git and the .NET SDK (6 or newer works).

```
git clone --recursive https://github.com/PSYGATOR/PSYSTRAP.git
cd PSYSTRAP
powershell -ExecutionPolicy Bypass -File fork\build.ps1
```

You get `fork\dist\PSYstrap.exe` plus the license files. Don't use the exe that a normal `dotnet build` puts in `bin`, it's only a small launcher and won't work once it's installed.

## Releases

Every push gets built by the Build workflow. To make a release:

```
git tag v2.13.1
git push origin v2.13.1
```

GitHub builds it and puts out a release with the exe, and installed copies update themselves next time they launch. Two things:

- bump `<Version>` in `Bloxstrap/Bloxstrap.csproj` to the same number as the tag first, otherwise the updater keeps offering the release to people who already have it
- the repo has to be public, because the updater checks GitHub without logging in

## Website and wiki

The website is in `site/` and goes up on GitHub Pages by itself when you push (the Website workflow). The wiki pages on it are built from the markdown in `docs/wiki`, so to change the wiki just edit those files. To look at it on your PC first:

```
pip install markdown
python fork/wiki/build.py
python -m http.server 8000 --directory site
```

then open http://localhost:8000. The sidebar order comes from the list on `Home.md`.

## rebrand.ps1

Changes the app name and which repo it updates from:

```
powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name PSYstrap -Repo PSYGATOR/PSYSTRAP -Icon path\to\icon.ico
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
