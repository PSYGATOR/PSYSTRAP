# FAQ

**Will I get banned?**
PSYstrap doesn't inject into Roblox, touch its memory or bypass the anti-cheat. It changes the same files Bloxstrap does and sends normal keyboard/mouse input. But macros are still automation, and some games have their own rules against AFK macros, so that part's up to you and the game.

**Is this made by Roblox or the Bloxstrap people?**
No. It's based on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) (MIT license) but it's a separate project.

**Can it limit how much internet Roblox uses?**
Not really. If you cap Roblox's connection you just rubber band and disconnect. Potato mode makes each window download a lot less, and minimizing windows you're not using helps too.

## Common problems

**Settings or the shortcuts won't open after installing**
You probably installed from an exe in a `bin` folder. Get `PSYstrap.exe` from the [releases](https://github.com/PSYGATOR/PSYSTRAP/releases/latest) and run that instead.

**My skybox goes away after I join**
That game has its own sky. See [Skybox](Skybox.md).

**FPS says n/a**
See [Overlay](Overlay.md#fps-says-na).

**The overlay isn't showing**
It doesn't work in exclusive fullscreen, use windowed or F11. It also hides when Roblox isn't the focused window.

**The FPS cap didn't change**
Roblox only reads it when it starts. Use "Restart Roblox to apply" in the in-game menu.

**Opening a second Roblox just switches to the first one**
Turn on Multi-instance in the Instances window and close all your Roblox windows once.

**"Roblox refused ... saved sign-in"**
The login expired (password change, logged out everywhere etc). Click Add account and log in again.

**Roblox has no sound**
If you have more than one Roblox open, only the one you clicked into last has sound. See [Gameplay](Gameplay.md#mute-windows-in-the-background). If it's the only window, check the Windows volume mixer.

**It rejoined when I didn't want it to (or didn't when I did)**
Turn "Rejoin when you get disconnected" off in Settings > Gameplay. If it got it wrong, send the log (see below), the disconnect reason is in there.

**My smart macro never does anything**
Take a new screenshot in the editor and click Test. Also check the Roblox window is visible, focused, and the same size as when you made it.

## Logs and reporting bugs

Logs are in `%LocalAppData%\PSYstrap\Logs`. If something's broken, [open an issue](https://github.com/PSYGATOR/PSYSTRAP/issues/new) and say what you did and what happened, and attach the newest log. Logs don't have passwords or saved logins in them, but they do have your Windows username in file paths, so have a quick look before you post one.
