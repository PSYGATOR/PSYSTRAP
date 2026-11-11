# Turn taking

Runs a [smart macro](Smart-macros.md) on several Roblox windows.

## Why does it switch windows?

Roblox only takes keyboard and mouse input in the window you have focused, and there's only one focused window. Sending input to a window in the background just gets ignored (I tested it). The only way around that is injecting into each Roblox, which gets you banned, so PSYstrap just switches between the windows really fast instead. It focuses one, checks the screen, does whatever the rule says, then goes to the next one.

For AFK stuff like reconnecting, collecting or selling, each window gets checked every second or two, which is usually plenty.

## Setting it up

1. Open your windows (see [Multi instance](Multi-instance.md)) and click Arrange windows. They need to be visible, minimized ones get skipped.
2. In the Instances window, pick a smart macro for each Roblox window. Can be the same one or different ones.
3. Click Start taking turns. Each window shows what it did last.

You can change how long it stays on each window (700ms by default) and how long it waits before carrying on after you touch the PC (5 seconds by default).

## Taking over

As soon as you move your mouse or press a key it pauses, and lets go of any keys a macro was holding. When you stop touching the PC it carries on after a few seconds.

Since it keeps switching windows while you're not touching anything, stop it if you want to go do something else on your PC.

## Other stuff

- Rejoin works per window. If that window is one of your [saved accounts](Accounts.md) it comes back on the same account and server.
- Overlays get hidden from screenshots while it's running.
- Closing the Instances window stops it.
