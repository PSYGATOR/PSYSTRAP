# 🚀 Installing

## Requirements

- Windows 10 (version 1809 or newer) or Windows 11
- The [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0). If it's missing, Windows asks to install it the first time you run PSYstrap.

## Install

1. Download **`PSYstrap.exe`** from the [latest release](https://github.com/AGamingotter/PSYSTRAP/releases/latest).
2. Run it. The installer opens.
3. Click **Install**. PSYstrap copies itself to `%LocalAppData%\PSYstrap`, adds Start-menu and desktop shortcuts, and becomes your Roblox launcher. The website's **Play** button now opens Roblox through PSYstrap.
4. The first launch downloads Roblox into PSYstrap's own folder.

> 💡 Use the `PSYstrap.exe` from a release (or `fork\dist` if you [build it yourself](Building.md)). Copying the exe out of a `bin` folder doesn't work, because the program lives in a separate DLL there.

## Settings

Open **PSYstrap Settings** from the Start menu (or run `PSYstrap.exe -settings`). Most changes apply when you click **Save**. Overlay, macro and instance options are also available in game from the **Insert** menu, and those save straight away.

## Updating

PSYstrap checks for a new release each time it launches Roblox and updates itself. You can turn this off under **Settings → PSYstrap → Automatically update**.

## Uninstalling

Use **Settings → Apps → Installed apps → PSYstrap → Uninstall** in Windows. This removes PSYstrap and restores the normal Roblox launcher.

## Where PSYstrap keeps things

Everything lives in `%LocalAppData%\PSYstrap`:

| Path | What it is |
|---|---|
| `Settings.json` | Your PSYstrap settings |
| `Accounts.json` | Saved accounts, encrypted for your Windows user (see [Accounts](Accounts.md)) |
| `Versions\` | The Roblox client PSYstrap launches |
| `Modifications\` | Mods applied on top of Roblox (fonts, skybox, Fast Flags…) |
| `Macros\` | Recorded macros (`*.json`) and smart macros (`Smart\`) |
| `Instances\` | Links used to run extra clients (see [Multi-instance](Multi-instance.md)) |
| `Logs\` | Log files, useful when something goes wrong (see [FAQ](FAQ.md)) |
