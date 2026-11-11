# 👥 Accounts

Save your Roblox accounts once, then open any of them, each in its own client, without going through your browser.

## Adding an account

1. Open the **Instances** window and click **Add account**.
2. Roblox's own login page opens inside PSYstrap. Sign in as usual (2-step verification and captchas work normally).
3. Done. The account appears in the list.

## Is it safe?

- **Your password is never seen or stored.** You type it into Roblox's own page; PSYstrap only keeps the sign-in (session) Roblox gives back afterwards.
- **The session is encrypted** with Windows DPAPI for *your Windows user on this PC*. Copied to another PC or user, `Accounts.json` is useless.
- **It's only ever sent to roblox.com**, and never written to PSYstrap's logs.
- **The login page runs in a temporary browser profile** that's deleted when the window closes, and only roblox.com pages can load in it.

## Opening accounts

Tick the accounts you want, pick a target, and click **Launch**:

| Target | Opens the accounts into… |
|---|---|
| **Join *name*'s server** | The exact server another running client is in. It appears once that client has joined a game. Great for bringing alts into your main's server without friending them first. |
| **Smallest server of a game** | The emptiest public server of the place ID you enter |
| **Any server of a game** | Any public server of that place |

Accounts open one at a time, about 12 seconds apart, each in its own client. Multi-instance is switched on automatically. The **place ID** is the number in a game's link: `roblox.com/games/`**`1234567890`**`/…`.

## How long do saved accounts last?

They stay saved until you remove them. Roblox can end a session, though, for example when you:

- change that account's password,
- use **Log out of all other sessions**, or
- leave it unused for a long time.

Logging out on your phone or browser doesn't affect it.

When a session has ended, launching shows *"Roblox refused … saved sign-in"*. Just click **Add account** and sign in to the same account again; it replaces the old one.

## Removing an account

Click the 🗑 next to it. This only deletes PSYstrap's saved sign-in; the Roblox account itself isn't touched.
