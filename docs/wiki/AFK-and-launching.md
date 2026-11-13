# AFK and launching

Settings > AFK & launching. Games you open a lot, opening them on a timer, and getting a message on your phone when something goes wrong. Hit Save at the bottom when you're done.

## Favourites

Paste a game link, a private server link or a place ID and press Add. "Add the game I'm in" works too. Share links (roblox.com/share?code=...) need a [saved account](Accounts.md) to read, so for those it's easier to paste the normal game link.

Each favourite has:

- **a name** you can change
- **as** - which saved account it opens as. "Whoever is signed in" uses the account Roblox is logged in to.
- **Part of my AFK setup** - see below
- **Play** - opens it now
- **Desktop shortcut** - puts a shortcut on your desktop with the game's icon. Double click it to go straight into the game, no menus.
- **Server browser** - opens the [server browser](#server-browser) for that game

If you delete a favourite, its desktop shortcut stops working (it tells you when you click it).

## AFK setup

Tick "Part of my AFK setup" on the favourites you leave running. "Open my AFK setup" opens all of them, each as its own saved account, about 12 seconds apart, then tiles the windows over your screen. "Desktop shortcut for it" makes a shortcut that does the same thing.

**Start with Windows** opens the AFK setup about 20 seconds after you log in, once the internet is up. Good for a setup that should come back by itself after your PC restarts.

## Keeping windows alive

These sit on top of rejoining and the crash watchdog on the [Gameplay](Gameplay.md) page.

- **Wait out internet drops** (on) - if your Wi-Fi drops, PSYstrap waits for it to come back and then rejoins, instead of using up its 3 rejoins on attempts that can't work.
- **Try another server if the old one's gone** (on) - if a rejoin can't get back into the same server (full, shut down), it goes to the emptiest server of the same game. Roblox and game updates that kick everyone also count as "server gone", so those rejoin into any server.
- **Try again later instead of giving up** (on) - after 3 rejoins in 10 minutes it tries once more 5 minutes later.
- **Unstick windows stuck joining** (3 minutes) - a window sitting on the loading screen that long gets closed and rejoins the game.
- **Restart windows using too much RAM** (off) - Roblox slowly eats memory over long sessions. A window over the limit you pick restarts into the same server. Only one window restarts every 2 minutes, so they don't all go at once.

## While you're away

- **Keep the PC awake** (off) - while Roblox is open the PC doesn't go to sleep. The screen can still turn off.
- **Minimize Roblox when you're away** (off) - after the time you pick without touching the PC, every Roblox window gets minimized (they barely use the graphics card that way), and they come back when you move the mouse or press a key. Anti-AFK still works on minimized windows: it pops the window up for its nudge and puts it back.
- **Turn the screen off now** - turns the monitor off straight away. Roblox keeps running; moving the mouse turns it back on.

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

**Screenshots with alerts** (on) - disconnect and rejoin messages come with a picture of the window, so you can see what was on screen.

**Status report** (off) - a short message every 30 minutes to 8 hours: how many windows, which account is in which game, and how many disconnects, rejoins and crashes there were since the last one.

Anyone with the webhook link can post in that channel, so don't share it.

## Phone remote

Off by default. Turn it on and PSYstrap shows a PIN and an address like `http://192.168.1.20:8787`. Open that on your phone while it's on the same Wi-Fi and type the PIN. You get every Roblox window with its account, game, how long it's been in, and RAM, plus buttons to rejoin, close or screenshot each one, open your AFK setup, and turn the screen off. It refreshes every 15 seconds.

- It only runs while Roblox is open.
- The first time, Windows may ask whether PSYstrap can get through the firewall. Allow it on private networks only.
- It's plain HTTP for your own network. Don't forward the port on your router to reach it from outside.
- Five wrong PINs lock that phone out for 5 minutes. "New PIN" makes a new one (phones that are signed in stay signed in until they sign out or 12 hours pass).

## AFK history

At the bottom of the page: everything your windows did (joined, left, disconnected with the reason, rejoined, crashed, froze, got stuck, restarted for RAM, internet down and back, scheduled opens and closes), newest first, plus today's totals and how long each account was in a game today.