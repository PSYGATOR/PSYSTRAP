# Macros

## Recording

Press F6 in Roblox and you'll see REC on the overlay. Do whatever you want recorded (keys, clicks, mouse movement, scrolling), then press F6 again. It saves as "Macro 1", "Macro 2" etc. and gets selected.

It only records while Roblox is the focused window. Mouse positions are saved relative to the Roblox window, so it still works if you move or resize the window. If you were still holding a key when you stopped, it gets let go at the end so nothing gets stuck.

## Playing

F7 plays the selected macro, F7 again stops it.

In Settings > Macros (or the in-game menu) you can pick:

- which macro F7 plays (bin icon deletes it, folder icon opens the macros folder)
- how many times: once, 5, 10, 50, or until you stop it
- speed: 0.5x, 1x, 1.5x or 2x

## Safety stuff

If you click off Roblox, playback pauses and lets go of any keys, so it can't start typing into another window. Everything gets released when it stops too.

Macros are just JSON files in `%LocalAppData%\PSYstrap\Macros` so you can back them up or share them.

If you want macros that react to what's on screen, see [Smart macros](Smart-macros.md).
