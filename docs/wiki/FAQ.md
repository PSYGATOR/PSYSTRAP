# ❓ FAQ & troubleshooting

## General

<details>
<summary><b>Will PSYstrap get me banned?</b></summary>

PSYstrap doesn't inject into Roblox, touch its memory or bypass its anti-cheat. It works like Bloxstrap (launcher settings, Roblox's own settings file, file mods) plus normal Windows input. Macros are still automation, though, and some games' own rules don't allow AFK macros, so check the rules of the game you play.
</details>

<details>
<summary><b>Is PSYstrap made by Roblox or Bloxstrap?</b></summary>

No. It's an independent project based on [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap) (MIT License) and isn't affiliated with Roblox Corporation or the Bloxstrap team.
</details>

<details>
<summary><b>Can PSYstrap limit Roblox's internet use?</b></summary>

Not as a throttle. Capping Roblox's connection makes you rubber-band and disconnect, and doesn't steady weak Wi-Fi. [Potato mode](Overlay.md#potato-mode-disable-graphics) cuts what each client downloads, and keeping extra clients minimised helps too.
</details>

## Problems

| Problem | Fix |
|---|---|
| **Settings or shortcuts don't open after installing** | You installed from a `bin` build folder. Use `PSYstrap.exe` from a [release](https://github.com/AGamingotter/PSYSTRAP/releases/latest) and run it again. |
| **Custom skybox disappears after joining** | That game sets its own sky; see [Skybox](Skybox.md). |
| **FPS counter says n/a** | You need admin or the *Performance Log Users* group, and the Direct3D renderer; see [Overlay](Overlay.md#fps-says-na). |
| **Overlay doesn't show** | Exclusive fullscreen hides it; use windowed or F11 borderless. The counters also hide when Roblox isn't focused. |
| **Frame-rate cap didn't change** | Roblox only reads it at startup. Use **Restart Roblox to apply** in the in-game menu. |
| **Second client just switches to the first** | Turn on **Multi-instance** in the Instances window, then close every Roblox window once. |
| **"Roblox refused … saved sign-in"** | That session ended (password change, log out everywhere…). Click **Add account** and sign in again. |
| **Smart macro never fires** | Press **Test** in the editor on a fresh screenshot. Check the window is visible, focused and the same size as when you made it. |
| **Macro typed into the wrong window** | It can't: playback pauses whenever Roblox isn't focused. If something looks off, check the macro wasn't recorded with another app focused. |

## Logs and reporting issues

Logs live in `%LocalAppData%\PSYstrap\Logs`. When reporting a problem, [open an issue](https://github.com/AGamingotter/PSYSTRAP/issues/new) with:

- what you did and what happened,
- the newest log file(s).

Logs don't contain passwords or saved sign-ins. They do contain your Windows user folder path and Roblox server IDs, so skim them before posting publicly.
