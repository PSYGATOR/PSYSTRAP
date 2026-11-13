# Overlay

Small counters in the corner of the Roblox window, plus a menu you open with Insert. It's its own window sitting on top of Roblox, not something inside the game.

## Counters

- **FPS** - green at 55+, yellow at 30+, red below 30
- **CPS** - left | right clicks in the last second (only counts while Roblox is focused)
- **RAM** - how much memory that Roblox window is using
- **Session** - how long Roblox has been open
- **Clock** - your PC time
- **Server** - how many players are in your server, plus the region if "Query server location" is on in Settings > Integrations (off by default)
- **Today** - how long you've played this game today, see [Playtime](Gameplay.md#playtime) (off by default)

You can turn each one on/off and pick which corner they go in, either in Settings > Overlay or the in-game menu. They hide when Roblox isn't the focused window.

The overlay works in windowed and F11 fullscreen. It won't show up if you use exclusive fullscreen (the Alt+Enter fast flag), because that draws over everything.

### FPS says n/a

The FPS counter uses Windows performance events, so it needs admin or for your account to be in the "Performance Log Users" group. It also only works with Roblox's normal Direct3D renderer, not Vulkan or OpenGL.

## In-game menu

Press Insert while you're in Roblox. Press it again (or click back into Roblox) to close it. Anything you change here saves straight away.

**FPS cap** - PSYstrap writes this into Roblox's settings file right before Roblox starts. Pick one of the same options Roblox has (default 60, 60, 120, 144, 240), or pick **Custom** and type any number from 5 to 1000, like 75 or 165 to match your monitor. Roblox only reads it on startup, so if you change it while playing, hit "Restart Roblox to apply". That closes Roblox and puts you back in the same server.

**Potato mode** - turns everything down: lowest textures, no grass, no anti-aliasing, lowest detail, graphics level 1. Good for slow PCs, bad wifi (less to download when you join a game) and running lots of windows. It only uses flags that Roblox still allows, and it kicks in next time Roblox opens.

By default potato mode also caps Roblox at 60 FPS, so your alt windows aren't drawing hundreds of frames nobody's watching. You can turn that part off with "Cap at 60 FPS in potato mode". While it's on it overrides your normal FPS cap.

If you're running a bunch of windows, also turn on efficiency mode in the [Instances window](Multi-instance.md).

**Hop to the smallest server** - moves you to the emptiest public server of the game you're in.

**Server browser** - every public server of the game you're in, so you can pick one. See [Server browser](AFK-and-launching.md#server-browser).

**Hide this game from Discord** - your Discord status just says "Playing Roblox" for this game from now on. Press it again to undo. See [Discord](Gameplay.md#discord).

**Instances and macros** - opens the [Instances window](Multi-instance.md).
