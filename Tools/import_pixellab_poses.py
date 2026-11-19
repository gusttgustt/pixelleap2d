"""Bring PixelLab character animations into Resources/Sprites as hero_<id>_<dir>_<frame>.png.

Usage: python -I Tools/import_pixellab_poses.py <extracted character zip dir> <hero id>

The frames are laid out like the existing hero art: the 64 px rotation is cut to a
48 px wide, 69 px tall canvas with the feet on row 63 and a soft ground shadow under
them (wider poses get a wider canvas, still centred). PixelLab stores animation frames
on a bigger canvas than the rotation, centred, so they are shifted back by that margin.
Frame 0 of an animation (PixelLab's copy of the rotation) is skipped.
"""
import os
import sys

from PIL import Image

SPRITES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Sprites")
H = 69
SHADOW = (0, 0, 0, 70)

# animation display name -> (game direction, first frame number)
POSES = {
    "walk-side": ("side", 1),
    "attack-side": ("side", 20),
    "sit": ("front", 50),
    "chop": ("side", 60),
    "gather": ("front", 70),
}

# PixelLab frames left out because they came out broken: (hero, animation) -> frame files
SKIP = {
    ("redberet", "attack-side"): {"frame_006.png"},  # a white smear drawn across her body
}
# Animations already in the game laid out by hand (another width): never overwritten.
KEEP = {("gothbutterfly", "sit")}


def shadow_rows():
    """The ground shadow of the existing 48 px wide art: for each row, the x span it covers."""
    rows = {}
    for name in os.listdir(SPRITES):
        if not name.endswith(".png") or "_front_" not in name:
            continue
        im = Image.open(os.path.join(SPRITES, name)).convert("RGBA")
        if im.size != (48, H):
            continue
        for y in range(55, H):
            for x in range(48):
                if im.getpixel((x, y)) == SHADOW:
                    lo, hi = rows.get(y, (x, x))
                    rows[y] = (min(lo, x), max(hi, x))
    return rows


def place(src, width):
    """src: a PixelLab frame (square, the 64 px character centred on it)."""
    margin = (src.size[0] - 64) // 2
    out = Image.new("RGBA", (width, H))
    out.paste(src, (-(margin + 8 - (width - 48) // 2), -(margin - 2)))
    return out


def with_shadow(img, rows):
    w = img.size[0]
    dx = (w - 48) // 2
    out = Image.new("RGBA", img.size)
    for y, (lo, hi) in rows.items():
        for x in range(lo + dx, hi + dx + 1):
            out.putpixel((x, y), SHADOW)
    out.alpha_composite(img)
    return out


def visible_pixels(img):
    return sum(1 for p in img.getdata() if p[3] > 0)


def pick_width(frames):
    """Narrowest of 48 / 72 / 96 / 120 that keeps every frame whole."""
    for w in (48, 72, 96, 120):
        if all(visible_pixels(place(f, w)) == visible_pixels(place(f, 200)) for f in frames):
            return w
    return 120


def save(img, hero, direction, frame):
    path = os.path.join(SPRITES, f"hero_{hero}_{direction}_{frame}.png")
    img.save(path)
    return os.path.basename(path)


def main():
    root, hero = sys.argv[1], sys.argv[2]
    rows = shadow_rows()
    state = next(d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d)))
    base = os.path.join(root, state)
    written = []

    east = Image.open(os.path.join(base, "rotations", "east.png")).convert("RGBA")
    written.append(save(with_shadow(place(east, 48), rows), hero, "side", 0))

    anims = os.path.join(base, "animations")
    for name in sorted(os.listdir(anims)):
        if name not in POSES or (hero, name) in KEEP:
            continue
        direction, first = POSES[name]
        src_dir = os.path.join(anims, name, "east" if direction == "side" else "south")
        if not os.path.isdir(src_dir):
            continue
        files = sorted(f for f in os.listdir(src_dir) if f.endswith(".png"))[1:]  # skip the reference frame
        files = [f for f in files if f not in SKIP.get((hero, name), ())]
        frames = [Image.open(os.path.join(src_dir, f)).convert("RGBA") for f in files]
        w = pick_width(frames)
        for i, f in enumerate(frames):
            written.append(save(with_shadow(place(f, w), rows), hero, direction, first + i))
    print(hero, "->", len(written), "files")
    print(" ".join(written))


if __name__ == "__main__":
    main()
