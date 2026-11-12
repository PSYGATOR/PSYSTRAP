# Gameplay

Settings > Gameplay. Stuff for normal everyday playing. Remember to hit Save at the bottom.

## Rejoin when you get disconnected

On by default. If Roblox drops you (lost connection, server shut down) PSYstrap closes that Roblox and opens it again in the same server. If the server shut down it puts you in another server of the same game instead.

It doesn't rejoin if:

- you left the game yourself or closed Roblox
- you got kicked or banned
- you joined from somewhere else on the same account ("you have joined a game from another device")
- it already rejoined 3 times in the last 10 minutes, so it can't get stuck in a loop

If you're on a [saved account](Accounts.md) it comes back as that account.

## Anti-AFK

Off by default. Roblox kicks you after 20 minutes of not doing anything. With this on, PSYstrap moves the mouse by 1 pixel and taps right click after 15 minutes of no input (you can change it to 5, 10 or 18).

If the Roblox window is in the background it waits until you haven't touched your PC for a minute, quickly does the wiggle in Roblox, then puts you back in the window you were using. Macros playing count as activity, so it won't get in their way.

## Mute windows in the background

On by default. With more than one Roblox open, only the window you used last makes sound. The others get muted in the Windows volume mixer and come back when you click into them.

Windows remembers a mute per app, so if you close a window while it's muted it would open muted next time. PSYstrap unmutes it when it opens, so you don't have to go into the volume mixer.

## Remap keys

Make a key or mouse button act like a different one, only while Roblox is the focused window. Outside Roblox your keys do what they normally do.

Pick or type the two names and press Add. Some examples:

| From | Acts as | Why |
|---|---|---|
| Mouse4 | E | interact with the side button |
| Mouse5 | LeftShift | sprint on the other side button |
| CapsLock | LeftCtrl | shift lock on a key you don't use |
| MouseMiddle | F | |

Key names are the normal ones: letters, numbers `0`-`9`, `F1`-`F12`, `Space`, `LeftShift`, `LeftCtrl`, `LeftAlt`, `Tab`, `CapsLock`, `Enter`, `Up`/`Down`/`Left`/`Right` and so on. Mouse buttons are `MouseLeft`, `MouseRight`, `MouseMiddle`, `Mouse4` (back) and `Mouse5` (forward).

Each key or button can only have one remap. Adding a new one for the same key replaces the old one.

## Game profiles

Give one game its own FPS cap or potato mode. For example potato mode on for an AFK game, and 240 FPS for a game you actually play.

Paste a place ID or a roblox.com/games/... link and press Add, or press "Add the game I'm in" while you're in it. Then pick the FPS cap and potato mode for it. "Normal setting" means it uses whatever you have set on the [Overlay](Overlay.md) page.

A profile kicks in when you open that game from the website, a link or the account launcher. It does nothing if you open Roblox on its own and then pick the game inside Roblox, because PSYstrap can't know the game before Roblox starts.

## Playtime

PSYstrap counts how long you play each game and shows your top games here: today, the last 7 days and all time. It's only stored on your PC (`%LocalAppData%\PSYstrap\Playtime.json`).

You can also put "Playtime today" for the game you're in on the [overlay](Overlay.md).

## Friend alerts

Off by default. A popup in the corner of your screen when someone on your friends list starts playing a game. Click it to join them.

It needs a [saved account](Accounts.md), because Roblox only tells you where your friends are if you're logged in. Pick which account's friends to watch. It checks once a minute while Roblox is open. If a friend has joins turned off in their privacy settings you get the alert but can't follow them in.

With a few Roblox windows open, only one of them does the checking, so you don't get the same popup 3 times.

## Discord

These go with the Discord options in Settings > Integrations. "Show game activity" has to be on there.

**Show server info** - adds the player count of your server (shows as "3 of 12" next to the status) and the region, if "Query server location" is on in Integrations.

**Follow the window you're using** - Discord only has one status. With more than one Roblox open it shows the game in the window you clicked into last. Turn it off and it sticks with the first window you opened.

**Your own Discord app ID** - leave it empty and it uses the normal app. If you want Discord to say your own app name:

1. Make an application on the [Discord developer portal](https://discord.com/developers/applications). The name you give it is what shows up as "Playing ...".
2. Under Rich Presence > Art Assets upload an image called `psystrap` (the PSYstrap logo is in `fork/branding` in this repo).
3. Copy the Application ID into this box and save. It's used next time Roblox opens.

**Hidden games** - games on this list show up on Discord as just "Playing Roblox". No name, no picture, no join button. Add one with a place ID or link, or press "Hide the game I'm in". You can also hide or unhide the game you're in from the in-game menu (Insert), and it changes straight away.
