# 🔁 Turn-taking

Run a [smart macro](Smart-macros.md) on several clients at once.

## Why "turns"?

Roblox only accepts keyboard and mouse input in the **focused** window, and Windows has one focused window and one cursor. Input sent to a background Roblox window is ignored; we tested this. The only way around it would be injecting into each client, which Roblox's anti-cheat bans for. So PSYstrap goes round the clients quickly instead:

1. Focus client 1, read its screen, fire its next ready rule.
2. Move to client 2, then 3, … and loop.

For AFK-style tasks (reconnect, collect, sell, anti-idle), each client gets attention every second or two.

## Using it

1. Open your clients ([Multi-instance](Multi-instance.md)) and press **Arrange windows**. Clients must be **visible**; minimised ones are skipped.
2. In the **Instances** window, pick a **Smart macro** for each client. They can be the same or different.
3. Press **Start taking turns**. Each client's row shows what it last did, e.g. *Reconnect seen → Click it*.

| Setting | Default | Meaning |
|---|---|---|
| **Time on each client** | 700 ms | Minimum time spent on a client per turn |
| **Resume after** | 5 s | How long after you stop touching the PC before it carries on |

## Taking over

**Move the mouse or press a key, and it pauses instantly.** A running action lets go of its keys. Once you leave the PC alone for the resume time, it carries on.

While it's running and you're idle it keeps switching focus between clients. If you want to use another app, stop it, or keep using the mouse and keyboard so it stays paused.

## Good to know

- **Rejoin server** works per client. If that client is one of your [saved accounts](Accounts.md), it comes back as that account, into the same server.
- Overlays are hidden from screenshots while turns are running, so they don't confuse detectors.
- Closing the Instances window stops turn-taking.
