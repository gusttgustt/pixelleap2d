"""Bring a whole new PixelLab hero into Resources/Sprites in one go.

Usage: python -I Tools/import_pixellab_hero.py <extracted character zip dir> <hero id> <width> <cut row>

Writes every hero_<id>_<front|back|side|se|ne>_<frame>.png the game reads, each with a
.meta (copied from an existing hero sprite, fresh ids), laid out like the other heroes
(see import_pixellab_poses.py) but on a <width> px canvas (48 for a slim figure, 72 for
one with a wide silhouette such as big tails), centred, so every frame lines up.

Animation names (PixelLab display names) -> game frames:
  walk-v3  south/north -> front/back 1..      walk-side-v3 east -> side 1..
  idle     south/north -> 10..                attack      south/north -> 20..
  attack-side east     -> side 20..           hurt        south/north -> 30..
  death    south/north -> 40..                sit         south -> front 50..
  chop     east        -> side 60..           gather      south -> front 70..
A frame 0 that is a copy of the rotation (v3 clips) is skipped. The 45 degree views are the
diagonal rotations, walking with the legs of the side-on walk below <cut row>
(see import_pixellab_diagonals.py).
"""
import os
import sys
import uuid

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from import_pixellab_poses import SPRITES, place, shadow_rows, with_shadow  # noqa: E402

TEMPLATE_META = "hero_redberet_side_1"
ANIMS = {
    "walk-v3": {"south": ("front", 1), "north": ("back", 1)},
    "walk-side-v3": {"east": ("side", 1)},
    "idle": {"south": ("front", 10), "north": ("back", 10)},
    "attack": {"south": ("front", 20), "north": ("back", 20)},
    "attack-side": {"east": ("side", 20)},
    "hurt": {"south": ("front", 30), "north": ("back", 30)},
    "death": {"south": ("front", 40), "north": ("back", 40)},
    "sit": {"south": ("front", 50)},
    "chop": {"east": ("side", 60)},
    "gather": {"south": ("front", 70)},
}
# frames left out because they came out broken: (hero, animation, direction) -> frame files
SKIP = {
    ("kitsune", "attack-side", "east"): {"frame_003.png"},  # a white smear over his whole body
}
ROTATIONS = {"south": "front", "north": "back", "east": "side", "south-east": "se", "north-east": "ne"}


def shadow_for(width, rows):
    """The 48 px shadow rows, widened a little for a wider canvas."""
    grow = (width - 48) // 4
    return {y: (lo - grow, hi + grow) for y, (lo, hi) in rows.items()}


def meta_text(name):
    with open(os.path.join(SPRITES, TEMPLATE_META + ".png.meta")) as f:
        text = f.read().replace(TEMPLATE_META + "_", name + "_")
    out = []
    for line in text.splitlines(True):
        s = line.strip()
        if line.startswith("guid:"):
            line = f"guid: {uuid.uuid4().hex}\n"
        elif s.startswith("spriteID: ") and s != "spriteID:":
            line = line.split(":")[0] + f": {uuid.uuid4().hex[:16]}0000000000000000\n"
        out.append(line)
    return "".join(out)


def save(img, hero, view, frame):
    name = f"hero_{hero}_{view}_{frame}"
    path = os.path.join(SPRITES, name + ".png")
    img.save(path)
    if not os.path.exists(path + ".meta"):
        with open(path + ".meta", "w", newline="\n") as f:
            f.write(meta_text(name))
    return name


def visible(img):
    return sum(1 for p in img.getdata() if p[3] > 0)


def frames_of(folder, rotation, skip=()):
    files = sorted(f for f in os.listdir(folder) if f.endswith(".png") and f not in skip)
    imgs = [Image.open(os.path.join(folder, f)).convert("RGBA") for f in files]
    if imgs:
        m = (imgs[0].size[0] - 64) // 2
        if imgs[0].crop((m, m, m + 64, m + 64)).tobytes() == rotation.tobytes():
            imgs = imgs[1:]
    return imgs


def main():
    root, hero, width, cut = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
    rows = shadow_for(width, shadow_rows())
    # with_shadow centres these rows on any canvas, so the shadow is the same size on all
    state = next(d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d)))
    base = os.path.join(root, state)
    rot = {d: Image.open(os.path.join(base, "rotations", d + ".png")).convert("RGBA") for d in ROTATIONS}

    def lay(img, w=width):
        return with_shadow(place(img, w), rows)

    def fit(frames):
        """Narrowest canvas from <width> up (24 px steps) that keeps every frame whole."""
        whole = [visible(place(f, 240)) for f in frames]
        for w in range(width, 240, 24):
            if all(visible(place(f, w)) == n for f, n in zip(frames, whole)):
                return w
        return 240

    written = [save(lay(rot[d]), hero, v, 0) for d, v in ROTATIONS.items()]

    anims = os.path.join(base, "animations")
    found = {name: os.path.join(anims, name) for name in os.listdir(anims)}
    side_walk = []
    for name, dirs in ANIMS.items():
        if name not in found:
            print("missing animation:", name)
            continue
        for pl_dir, (view, first) in dirs.items():
            folder = os.path.join(found[name], pl_dir)
            if not os.path.isdir(folder):
                print("missing direction:", name, pl_dir)
                continue
            frames = frames_of(folder, rot[pl_dir], SKIP.get((hero, name, pl_dir), ()))
            if name == "walk-side-v3":
                side_walk = frames
            w = fit(frames)
            for i, f in enumerate(frames):
                written.append(save(lay(f, w), hero, view, first + i))

    # 45 degree walks: diagonal top over the side-on walk's legs
    for pl_dir in ("south-east", "north-east"):
        top = rot[pl_dir].crop((0, 0, 64, cut))
        for i, src in enumerate(side_walk):
            m = (src.size[0] - 64) // 2
            frame = Image.new("RGBA", (64, 64))
            frame.paste(top, (0, 0))
            frame.alpha_composite(src.crop((m, m + cut, m + 64, m + 64)), (0, cut))
            written.append(save(lay(frame), hero, ROTATIONS[pl_dir], i + 1))
    print(hero, "->", len(written), "sprites")


if __name__ == "__main__":
    main()
