<div align="center">

<img src="fork/branding/logo-256.png" width="140" alt="PSYstrap logo" />

# PSYstrap

A Roblox launcher based on Bloxstrap, with an overlay, multi instance, saved accounts and macros added on top.

[![Download](https://img.shields.io/github/v/release/AGamingotter/PSYSTRAP?label=download&color=36C42E)](https://github.com/AGamingotter/PSYSTRAP/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-E8213F)](LICENSE)

**[Download](https://github.com/AGamingotter/PSYSTRAP/releases/latest)** | **[Wiki](docs/wiki/Home.md)**

</div>

## What it does

Everything Bloxstrap does (mods, fast flags, Discord status, etc.), plus:

- **Overlay** - FPS, CPS, RAM, session time and a clock in the corner of Roblox. Press Insert in game for a menu with the FPS cap, potato mode and a "go to smallest server" button.
- **Multi instance** - run as many Roblox windows as you want. There's a window to manage them (lower their priority, trim RAM, tile them on screen).
- **Saved accounts** - log in once on the normal Roblox login page and you can open that account later in one click, even straight into the server your main is in.
- **Macros** - F6 to record, F7 to play.
- **Smart macros** - watch the screen for a colour, a picture or some text and do something when it shows up. For example: if the disconnected message shows up, rejoin. Press F8 to run one.
- **Turn taking** - run smart macros on all your Roblox windows by switching between them really fast.
- **Custom skybox** - a few presets, or use your own images.
- **Potato mode** - lowest graphics in one click.
- A green theme to match the logo.

It doesn't inject anything into Roblox or touch its memory. It only changes Roblox's own settings files and sends normal keyboard and mouse input.

## Screenshots

<table>
  <tr>
    <td width="50%"><img src="docs/images/settings.jpg" alt="Settings" /></td>
    <td width="50%"><img src="docs/images/instances.jpg" alt="Instances window" /></td>
  </tr>
  <tr>
    <td><img src="docs/images/smart-macro-editor.jpg" alt="Smart macro editor" /></td>
    <td><img src="docs/images/skybox.jpg" alt="Skybox settings" /></td>
  </tr>
</table>

## Install

1. Get `PSYstrap.exe` from [releases](https://github.com/AGamingotter/PSYSTRAP/releases/latest).
2. Run it and click Install.
3. Play Roblox like normal. It opens through PSYstrap now.

You need Windows 10 or 11 and the [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0) (Windows will ask to install it if you don't have it). It updates itself when there's a new release.

Hotkeys in game: **Insert** menu, **F6** record macro, **F7** play macro, **F8** smart macro.

How everything works is in the [wiki](docs/wiki/Home.md).

## Building

```
git clone --recursive https://github.com/AGamingotter/PSYSTRAP.git
cd PSYSTRAP
powershell -ExecutionPolicy Bypass -File fork\build.ps1
```

The exe ends up in `fork\dist`. More info on the [building page](docs/wiki/Building.md).

## Credits

Based on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) by pizzaboxer and contributors (MIT license, see [LICENSE](LICENSE)). Not affiliated with Roblox or the Bloxstrap team.
