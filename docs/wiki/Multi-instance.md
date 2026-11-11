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
- **Lower priority of clients you aren't using** (on by default) - the windows you're not playing on get less CPU
- **Trim RAM of clients you aren't using** (off by default) - every minute it pushes the unused memory of background windows to the page file. Saves a lot of RAM but there can be a small freeze when you switch back to one.

It also shows how much RAM and CPU each one is using. Potato mode (see [Overlay](Overlay.md)) helps a lot if you're running a bunch.

## Different accounts

Each window can be on a different account. You can use Roblox's own account switcher, or save your accounts in PSYstrap and open them from here (see [Accounts](Accounts.md)).

## How it works

Roblox normally stops you opening a second one in two ways. PSYstrap gets around the first by taking the lock names Roblox checks before the first window opens. For the second, each extra window starts from its own folder link (`Instances\2`, `Instances\3`...) that points at the same Roblox files. Nothing gets copied or injected and Roblox's files aren't changed. Max is 16 windows.
