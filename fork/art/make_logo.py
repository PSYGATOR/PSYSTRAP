"""Makes the loading window's logo pieces from the new hand-drawn logo:
SketchBody.png (the new drawing, see-through) and SketchCrown.png (the old crown).

So the crown sits exactly where it did on the original logo, the new drawing is lined up with the old body
(best fit of position, size and angle by overlap), and both pieces are drawn in the old logo's frame,
scaled up to 1000x1000. The crown is then just the old crown, in its old spot.

usage: python make_logo.py logo-sketch.jpg old-body.png crown.png out_dir preview.png
"""
import sys
import numpy as np
from PIL import Image

src, old_body_src, crown_src, out_dir, preview = sys.argv[1:6]
CANVAS = 1000


def ink_to_rgba(path):
    """White background -> transparent ("colour to alpha" against white); marker ink kept solid."""
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    a0 = (255.0 - rgb.min(axis=2)) / 255.0
    a0[a0 < 0.10] = 0  # jpeg noise on the white
    safe = np.where(a0 > 0, a0, 1)
    true = np.clip(255.0 - (255.0 - rgb) / safe[..., None], 0, 255)
    alpha = np.clip(a0 * 1.5, 0, 1)
    return Image.fromarray(np.dstack([true, alpha[..., None] * 255]).astype(np.uint8))


def affine_coeffs(angle_deg, scale, tx, ty):
    """PIL wants the output->input mapping. Forward map (input -> output): p' = s*R*p + t."""
    a = np.radians(angle_deg)
    m = np.array([[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]]) * scale
    inv = np.linalg.inv(m)
    off = -inv @ np.array([tx, ty])
    return (inv[0, 0], inv[0, 1], off[0], inv[1, 0], inv[1, 1], off[1])


new_body = ink_to_rgba(src)                                   # 2000x2000
old_body = Image.open(old_body_src).convert("RGBA")           # 320x320, the old logo's frame
OLD = old_body.size[0]

# --- find the fit: new drawing (shrunk to search size) -> old frame ---
SEARCH_DOWN = new_body.size[0] / OLD
small_new = new_body.resize((OLD, OLD), Image.LANCZOS)
old_mask = (np.asarray(old_body)[..., 3] > 60).astype(np.float32)
new_alpha = small_new.getchannel("A")


def centroid(mask):
    ys, xs = np.nonzero(mask)
    return xs.mean(), ys.mean()


ocx, ocy = centroid(old_mask)
ncx, ncy = centroid(np.asarray(new_alpha) > 60)

best = (-1, None)
for angle in np.arange(-40, 41, 2.0):
    for scale in np.arange(0.70, 1.31, 0.03):
        # rotate+scale about the new drawing's centroid, put that centroid on the old one's, then let FFT find the shift
        a = np.radians(angle)
        r = np.array([[np.cos(a), -np.sin(a)], [np.sin(a), np.cos(a)]]) * scale
        t = np.array([ocx, ocy]) - r @ np.array([ncx, ncy])
        warped = new_alpha.transform((OLD, OLD), Image.AFFINE, affine_coeffs(angle, scale, *t), resample=Image.BILINEAR)
        wm = (np.asarray(warped) > 60).astype(np.float32)
        corr = np.real(np.fft.ifft2(np.fft.fft2(old_mask) * np.conj(np.fft.fft2(wm))))
        dy, dx = np.unravel_index(np.argmax(corr), corr.shape)
        dy = dy - OLD if dy > OLD // 2 else dy
        dx = dx - OLD if dx > OLD // 2 else dx
        if abs(dx) > 40 or abs(dy) > 40:
            continue
        shifted = np.roll(np.roll(wm, dy, axis=0), dx, axis=1)
        iou = (shifted * old_mask).sum() / np.maximum(shifted, old_mask).sum()
        if iou > best[0]:
            best = (iou, (angle, scale, t[0] + dx, t[1] + dy))

iou, (angle, scale, tx, ty) = best
print(f"fit: angle={angle:.1f} scale={scale:.2f} shift=({tx:.1f},{ty:.1f}) overlap={iou:.2f}")

# --- draw both pieces in the old frame, scaled up to the canvas ---
K = CANVAS / OLD
# full-size drawing -> canvas: shrink to search size, apply the fit, scale up
full_scale = scale * K / SEARCH_DOWN
body = new_body.transform((CANVAS, CANVAS), Image.AFFINE, affine_coeffs(angle, full_scale, tx * K, ty * K), resample=Image.BICUBIC)
body.save(f"{out_dir}/SketchBody.png", optimize=True)

crown = Image.open(crown_src).convert("RGBA").resize((CANVAS, CANVAS), Image.LANCZOS)
crown.save(f"{out_dir}/SketchCrown.png", optimize=True)

print(f"crown box (500 units)={tuple(round(v / 2) for v in crown.getbbox())} body box (500 units)={tuple(round(v / 2) for v in body.getbbox())}")

# preview on the loading window's background colour, old logo on the left, new on the right
def on_bg(*layers):
    bg = Image.new("RGBA", (CANVAS, CANVAS), (16, 38, 21, 255))
    for layer in layers:
        bg.alpha_composite(layer)
    return bg.convert("RGB").resize((500, 500), Image.LANCZOS)


sheet = Image.new("RGB", (1000, 500))
sheet.paste(on_bg(old_body.resize((CANVAS, CANVAS), Image.LANCZOS), crown), (0, 0))
sheet.paste(on_bg(body, crown), (500, 0))
sheet.save(preview)
