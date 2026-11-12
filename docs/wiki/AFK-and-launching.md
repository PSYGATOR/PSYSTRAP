# AFK and launching

Settings > AFK & launching. Games you open a lot, opening them on a timer, and getting a message on your phone when something goes wrong. Hit Save at the bottom when you're done.

## Favourites

Paste a game link, a private server link or a place ID and press Add. "Add the game I'm in" works too. Share links (roblox.com/share?code=...) need a [saved account](Accounts.md) to read, so for those it's easier to paste the normal game link.

Each favourite has:

- **a name** you can change
- **as** - which saved account it opens as. "Whoever is signed in" uses the account Roblox is logged in to.
- **Open when Windows starts** - see below
- **Play** - opens it now
- **Desktop shortcut** - puts a shortcut on your desktop with the game's icon. Double click it to go straight into the game, no menus.
- **Server browser** - opens the [server browser](#server-browser) for that game

If you delete a favourite, its desktop shortcut stops working (it tells you when you click it).

## Start with Windows

Turn it on and tick "Open when Windows starts" on the favourites you want. About 20 seconds after you log in, PSYstrap opens them one after the other. Good for an AFK setup that should come back by itself after your PC restarts.

## Schedule

Open a favourite every day at a time you pick, and close it again after a while if you want (30 minutes up to 12 hours).

It uses Windows' Task Scheduler, so PSYstrap doesn't need to be running. You can see the tasks in Task Scheduler under a "PSYstrap" folder. Your PC has to be on at that time, a sleeping PC won't wake up for it. Times are typed as 24 hour time, like 07:30 or 22:00.

Closing after a while only works if the game is still open in the same window. If you close it yourself first, nothing happens.

## Recent servers

The last servers you were in, from any window and any account. Rejoin puts you back in that exact server if it's still running (as the same saved account if it was one).

## Server browser

Also in the [in-game menu](Overlay.md#in-game-menu). Shows a game's public servers with how many players are in them and the players' ping. You can hide full servers, sort them, pick which saved account to join as, and join any server you want.

"Players' ping" is the average ping of the people already in that server, as Roblox reports it. It isn't your ping, but a low number usually means most of them (and the server) are close to each other.

There's no "pick a region" filter. Roblox only tells you where a server is when its own client asks, and PSYstrap doesn't pretend to be the Roblox client to find out.

## Phone alerts

PSYstrap can post to a Discord webhook, which shows up as a notification on your phone. Handy for AFKing: you find out straight away when a window got disconnected or crashed.

To get a webhook link: in your own Discord server go to Server Settings > Integrations > Webhooks > New Webhook, pick a channel, then Copy Webhook URL. Paste it in the box and press Test.

Pick what you want a message for:

- **Disconnected** - got disconnected and it isn't rejoining (kicked, logged in somewhere else, rejoined too often, or auto rejoin is off)
- **Rejoining** - got disconnected and it's rejoining
- **Roblox crashed or froze** - and it's opening it again
- **Scheduled open/close** - a scheduled launch opened or closed a game
- **Smart macro rejoined or stopped** - a smart macro rule rejoined or stopped
- **Friend started playing** - same as [friend alerts](Gameplay.md#friend-alerts), on your phone

Messages say which saved account it was, so you can tell your alts apart.

Smart macros also have a **Send phone alert** action. Put your own message in it, like "boss is up", and it gets sent when that rule fires. See [Smart macros](Smart-macros.md).

Anyone with the webhook link can post in that channel, so don't share it.
