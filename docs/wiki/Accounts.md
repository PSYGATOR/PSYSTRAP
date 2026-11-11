# Accounts

Save your accounts once and open any of them later without logging in through your browser.

## Adding one

In the Instances window, click "Add account". The normal Roblox login page opens. Log in like you usually would (2-step verification works). That's it, the account shows up in the list.

## Is this safe?

- PSYstrap never sees your password. You type it into Roblox's page, and PSYstrap only keeps the login Roblox gives back after.
- That login is encrypted with Windows (DPAPI) so it only works for your Windows user on this PC. If someone copies `Accounts.json` to another PC it's useless.
- It's only ever sent to roblox.com and never goes in the log files.
- The login page uses a temporary browser profile that gets deleted after, and it can only load roblox.com.

## Opening accounts

Tick the accounts you want, choose where to send them and click Launch:

- **Join (name)'s server** - puts them in the same server another one of your windows is in. Shows up once that window is in a game. Good for getting alts into your main's server without friending them.
- **Smallest server of a game** - the emptiest public server of a game
- **Any server of a game** - any public server

For the last two, type the place ID. It's the number in the game link: roblox.com/games/**1234567890**/...

They open one at a time, about 12 seconds apart, each in its own window. Multi instance gets turned on automatically.

## How long do they last?

They stay until you remove them. But Roblox can log the session out if you change that account's password, use "log out of all other sessions", or don't use it for a long time. Logging out on your phone or browser doesn't affect it.

If one stops working you'll see "Roblox refused ... saved sign-in". Just click Add account and log into the same account again, it replaces the old one.

## Removing one

Click the bin icon next to it. That only removes it from PSYstrap, your actual account isn't touched.
