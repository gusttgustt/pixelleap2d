"""Bring the 45 degree views of a PixelLab character into Resources/Sprites.

Usage: python -I Tools/import_pixellab_diagonals.py <extracted character zip dir> <hero id> <cut row>

Writes hero_<id>_se_<frame>.png (turned three-quarters toward the camera, facing right) and
hero_<id>_ne_<frame>.png (three-quarters away, facing right); the game flips them for left.
Frame 0 is PixelLab's diagonal rotation. Frames 1..N are a walk made without new generations:
the rotation from the top down to <cut row> (just under the skirt / shorts, in the 64 px
rotation's rows) over the legs of the side-on walk ("walk-side"), so the stride matches the
side walk the character already has. Laid out like the other hero art (see
import_pixellab_poses.py), with a .meta copied from an existing hero sprite.
"""
import os
import sys
import uuid

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from import_pixellab_poses import SPRITES, place, shadow_rows, with_shadow  # noqa: E402

VIEWS = {"south-east": "se", "north-east": "ne"}


def write(img, hero, view, frame, meta_template):
    name = f"hero_{hero}_{view}_{frame}.png"
    path = os.path.join(SPRITES, name)
    img.save(path)
    if not os.path.exists(path + ".meta"):
        text = meta_template.replace(f"hero_{hero}_side_1_", name[:-4] + "_")
        lines = text.splitlines(True)
        lines = [f"guid: {uuid.uuid4().hex}\n" if l.startswith("guid:")
                 else l.split(":")[0] + f": {uuid.uuid4().hex[:16]}0000000000000000\n" if l.strip().startswith("spriteID: ") and l.strip() != "spriteID:"
                 else l for l in lines]
        with open(path + ".meta", "w", newline="\n") as f:
            f.write("".join(lines))
    return name


def main():
    root, hero, cut = sys.argv[1], sys.argv[2], int(sys.argv[3])
    rows = shadow_rows()
    state = next(d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d)))
    base = os.path.join(root, state)
    with open(os.path.join(SPRITES, f"hero_{hero}_side_1.png.meta")) as f:
        meta = f.read()

    walk_dir = os.path.join(base, "animations", "walk-side", "east")
    walk = [Image.open(os.path.join(walk_dir, f)).convert("RGBA")
            for f in sorted(os.listdir(walk_dir)) if f.endswith(".png")][1:]  # skip the reference frame
    legs = []
    for src in walk:
        m = (src.size[0] - 64) // 2
        legs.append(src.crop((m, m + cut, m + 64, m + 64)))

    written = []
    for pl_dir, view in VIEWS.items():
        rot = Image.open(os.path.join(base, "rotations", pl_dir + ".png")).convert("RGBA")
        written.append(write(with_shadow(place(rot, 48), rows), hero, view, 0, meta))
        top = rot.crop((0, 0, 64, cut))
        for i, leg in enumerate(legs):
            frame = Image.new("RGBA", (64, 64))
            frame.paste(top, (0, 0))
            frame.alpha_composite(leg, (0, cut))
            written.append(write(with_shadow(place(frame, 48), rows), hero, view, i + 1, meta))
    print(hero, "->", len(written), "files")


if __name__ == "__main__":
    main()
