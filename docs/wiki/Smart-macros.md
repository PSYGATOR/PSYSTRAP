# Smart macros

<img src="../images/smart-macro-editor.jpg" width="600" alt="Smart macro editor" />

A smart macro looks at the Roblox window and does stuff when something shows up. Like "if the disconnected message is there for 2 seconds, rejoin" or "if the bag full icon shows up, play my Sell macro".

Make one in Settings > Macros > Smart macros > New. Select it on that page, then press F8 in Roblox to start/stop it.

## 1. Screenshot

Get Roblox on the screen you want to react to (the error, the full bag icon, whatever) and click "Screenshot Roblox". The editor moves out of the way while it takes it. You can also open a screenshot you saved before.

Make the smart macro at the same window size you're going to run it at. Positions are saved relative to the window, but the Roblox top bar and some game UI stay the same size in pixels no matter how big the window is. If you're going to run a few windows side by side, click Arrange windows first and take the screenshot then.

## 2. Detectors

Pick a mode and click or drag on the screenshot:

- **Pixel check** (click) - checks if that spot is a certain colour. You can change how close the colour has to be.
- **Picture check** (drag a box) - checks if that picture shows up near the same spot. It can move a little bit. You can change how close the match has to be (90% by default).
- **Text check** (drag a box) - reads the text there using the OCR built into Windows. It fills in the text for you, and you can shorten it to just the word that matters. Caps, spaces and punctuation don't matter.

Give them names so your rules make sense ("Reconnect button", "Bag full"). Click Test to see if each one finds its thing on the current screenshot.

## 3. Rules

Each rule is: when (detector) is seen / is gone for (X seconds), do (action).

Actions:
- **Play macro** - plays one of your normal [macros](Macros.md)
- **Press key** - E, Space, F, 1, LeftShift, Enter etc.
- **Click it** - clicks where the detector found its thing
- **Wait** - waits some seconds
- **Rejoin server** - closes Roblox and opens it back in the same server
- **Stop smart macro**

It goes through the rules top to bottom and runs the first one that's ready. Use the up arrow to move them around. The "wait at least X s before firing again" box stops a rule from spamming. "Check every" at the top is how often it looks at the screen (400ms by default).

## Running it

Press F8 in Roblox. The overlay shows what it did last. F8 again to stop.

It pauses if Roblox isn't the focused window, and the window has to actually be visible (not minimized or covered) because it's reading the screen. While it's running the PSYstrap overlay is hidden from screenshots so it doesn't mess up the detectors.

To run smart macros on more than one window, see [Turn taking](Turn-taking.md).

## Example: auto reconnect

1. Get the disconnected popup on screen and take a screenshot.
2. Text check, drag over the popup title. It should read "Disconnected".
3. Picture check, drag over the Reconnect button. Call it "Reconnect".
4. Rules:
   - when Reconnect is seen, Click it (wait 5s before firing again)
   - when Disconnected is seen for 15s, Rejoin server
5. Save, select it on the Macros page and press F8 in game.
