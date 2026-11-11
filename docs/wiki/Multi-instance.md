# Multi instance

<img src="../images/instances.jpg" width="520" alt="Instances window" />

Lets you have more than one Roblox open.

## Turning it on

Open the Instances window (Settings > Instances, or the button in the in-game menu) and switch on Multi-instance. After that, every time you launch Roblox through PSYstrap you get a new window instead of it jumping to the one that's already open.

If Roblox was already open when you turned it on, close all your Roblox windows once and then it'll work.

## Instances window

- **Launch new instance** - opens another Roblox
- **Arrange windows** - puts all the Roblox windows in a grid
- each window has buttons to focus it, minimize it, trim its RAM or close it
- **Efficiency mode for windows you aren't using** (on by default) - same as the leaf icon in Task Manager. Windows you're not playing on get the lowest CPU priority and run in power saving mode, so they get out of the way of the one you're using. Whichever window you click on gets full speed back within a second.
- **Lower priority of clients you aren't using** - a lighter version of the above, only used if efficiency mode is off
- **Trim RAM of clients you aren't using** (off by default) - every minute it pushes the unused memory of background windows to the page file. Saves a lot of RAM but there can be a small freeze when you switch back to one.

It also shows how much RAM and CPU each one is using.

## Making lots of windows run faster

1. Turn on potato mode with the 60 FPS cap (see [Overlay](Overlay.md)). This is the biggest one.
2. Leave efficiency mode on.
3. Turn on Trim RAM if you're low on memory.
4. Click Arrange windows. Smaller windows have fewer pixels to draw.
5. Minimize windows you don't need to watch. A minimized Roblox barely draws anything (but turn taking skips minimized windows).

## Different accounts

Each window can be on a different account. You can use Roblox's own account switcher, or save your accounts in PSYstrap and open them from here (see [Accounts](Accounts.md)).

## How it works

Roblox normally stops you opening a second one in two ways. PSYstrap gets around the first by taking the lock names Roblox checks before the first window opens. For the second, each extra window starts from its own folder link (`Instances\2`, `Instances\3`...) that points at the same Roblox files. Nothing gets copied or injected and Roblox's files aren't changed. Max is 16 windows.
