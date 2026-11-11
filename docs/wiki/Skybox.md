# 🌌 Skybox

<img src="../images/skybox.jpg" width="640" alt="Skybox picker with an unfolded-cube preview" />

Replace Roblox's default sky with a preset or your own images. You'll find it under **Settings → Mods → Skybox**.

## Presets

<img src="../images/skybox-presets.jpg" width="480" alt="PSY Green, Midnight, Sunset, Cotton Candy and Void" />

**PSY Green**, **Midnight**, **Sunset**, **Cotton Candy** and **Void** (pure black). Pick one, click **Save**, then restart Roblox.

## Your own sky

Click **Choose images** and pick either:

- **6 images**, one per side, with the side in the file name: `bk`, `dn`, `ft`, `lf`, `rt`, `up`, or `back`, `down`, `front`, `left`, `right`, `top`. Examples: `sky_bk.png`, `SkyboxUp.png`, `my sky left.jpg`.
- **1 image**, used on every side.

PNG, JPG, BMP, GIF and TIFF work, as do existing Roblox `.tex`/`.dds` sky files. Any size works; images are resized to 1024×1024. The preview shows the sky as an unfolded cube before you save. Side images are the same way up as a Roblox `Sky` object's textures, so skybox packs made for Roblox line up as-is.

Choose **Roblox default** to go back.

## "My sky shows for a second, then disappears"

That's normal for that game. PSYstrap replaces Roblox's **default** sky. Many games (most shooters and showcases, and new Studio baseplates) set their own sky, which loads a moment after you join and covers it. The custom sky shows in games that don't set their own.

No Roblox setting can force the default sky over a game's own sky.
