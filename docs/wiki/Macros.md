# ⌨️ Macros

Record what you do in Roblox and play it back.

## Recording

1. In Roblox, press **F6**. The overlay shows **● REC**.
2. Do the thing: keys, clicks, mouse movement and scrolling are all recorded.
3. Press **F6** again. It's saved as *Macro 1*, *Macro 2*, … and selected automatically.

Only input made while Roblox is focused is recorded. Mouse positions are stored relative to the Roblox window, so a macro still works if you move or resize the window. A key you were still holding when you stopped gets released at the end, so playback never leaves one stuck.

## Playing

Press **F7** in Roblox to play the selected macro, and **F7** again to stop. The overlay shows **▶ name 2/5**.

Under **Settings → Macros** (or the in-game menu) you can choose:

| Option | Choices |
|---|---|
| **Macro** | Which one F7 plays. The 🗑 deletes it; 📁 opens the macros folder |
| **Repeat** | Once, 5, 10, 50 times, or until stopped |
| **Speed** | 0.5×, 1×, 1.5×, 2× |

## Safety

- **Playback pauses whenever Roblox isn't the focused window** and lets go of held keys, so it can never type into another app.
- Every key and mouse button is released when playback stops or finishes.
- PSYstrap sends input the same way a real keyboard and mouse do, using hardware scan codes, which is what games read.

## Files

Macros are plain JSON in `%LocalAppData%\PSYstrap\Macros`, so you can back them up or share them. Want macros that react to what's on screen? See [Smart macros](Smart-macros.md).
