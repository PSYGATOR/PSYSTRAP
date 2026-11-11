<p align="center">
  <img src="fork/branding/logo-256.png" width="128" alt="PSYstrap logo" />
</p>

# PSYstrap

A Roblox bootstrapper for Windows, based on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap). It launches and updates Roblox like the official launcher, and adds a green PSY theme, an in-game overlay, multi-instance, saved accounts and macros.

PSYstrap never injects anything into Roblox. Everything works through Roblox's own files and settings, Windows APIs and normal input.

## Features

- **PSY theme**: dark-green theme built around the logo.
- **In-game overlay** (press Insert in game): FPS, clicks-per-second, RAM, session time and clock counters, plus a quick menu for:
  - frame-rate cap (applied on launch, with a "restart and rejoin" button)
  - potato mode
  - hopping to the smallest server
- **Custom skybox**: presets or your own images. Only shows in games that don't set their own sky.
- **Multi-instance**: run several Roblox clients at once. The Instances window shows RAM and CPU per client, can lower the priority of background clients, trim their RAM and tile their windows.
- **Saved accounts**:
  - Sign in once on Roblox's own login page. Sessions are stored encrypted for your Windows user (DPAPI); passwords are never seen or stored.
  - Open several accounts straight into a running client's server, the smallest server, or any server of a game.
- **Macros**: F6 records keys and mouse, F7 plays them back. Playback pauses whenever Roblox isn't focused.
- **Smart macros**: watch the screen with pixel, picture or text (Windows OCR) checks, and react with rules such as "when *Disconnected* is seen → rejoin". F8 runs one on the focused client.
- **Turn-taking**: runs a smart macro on each of several clients by switching between them. It pauses the moment you touch the mouse or keyboard.
- **Potato mode**: lowest graphics, using only flags on Roblox's client allowlist.

## Building

Requirements: Windows 10/11 and a .NET SDK (6 or newer; `global.json` rolls forward).

```powershell
git clone --recursive <this repo>
cd <repo>
powershell -ExecutionPolicy Bypass -File fork\build.ps1
```

This produces a single `fork\dist\PSYstrap.exe`. Use that file, not the `bin` output. A plain `dotnet build` makes a launcher plus DLLs, and the installer only copies the exe.

Running it needs the .NET 6 Desktop Runtime.

### Releases and auto-update

1. Point the app at this repo once: `powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name PSYstrap -Repo <owner>/<repo>`
2. Push a tag newer than the version in `Bloxstrap/Bloxstrap.csproj` (e.g. `v2.12.0`).
3. The `Build` workflow creates a draft release with `PSYstrap.exe`. Publish it, and installed copies update themselves.

## Credits

PSYstrap is based on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) by pizzaboxer and contributors, under the MIT License (see [LICENSE](LICENSE)). It isn't affiliated with Roblox Corporation or the Bloxstrap project.
