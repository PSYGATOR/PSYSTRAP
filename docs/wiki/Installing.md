# Installing

You need Windows 10 (1809 or newer) or Windows 11, and the [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0). If you don't have the runtime, Windows asks to install it the first time you open PSYstrap.

1. Download `PSYstrap.exe` from the [latest release](https://github.com/AGamingotter/PSYSTRAP/releases/latest).
2. Run it and click Install.

It installs to `%LocalAppData%\PSYstrap` and adds Start menu and desktop shortcuts. After that, the Play button on the Roblox website opens Roblox through PSYstrap. The first launch downloads Roblox, so it takes a bit longer.

Use the exe from a release (or from `fork\dist` if you build it yourself). The exe in a `bin` folder won't work on its own.

## Settings

Open PSYstrap Settings from the Start menu. Most things apply when you hit Save. The overlay and macro options are also in the in-game menu (Insert), and those save right away.

## Updates

PSYstrap checks for a new release every time it launches Roblox and updates itself. You can turn that off in Settings > PSYstrap.

## Uninstalling

Windows Settings > Apps > Installed apps > PSYstrap > Uninstall. This puts the normal Roblox launcher back.

## Where stuff is saved

All in `%LocalAppData%\PSYstrap`:

- `Settings.json` - your settings
- `Accounts.json` - saved accounts (encrypted, see [Accounts](Accounts.md))
- `Versions` - the Roblox client
- `Modifications` - mods (fonts, skybox, fast flags...)
- `Macros` - your macros, smart macros are in `Macros\Smart`
- `Instances` - used for [multi instance](Multi-instance.md)
- `Logs` - log files, useful if something breaks
