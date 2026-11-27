"""Build the cutout poses and loose pick for the two-character desktop scenes."""
from dataclasses import replace
from pathlib import Path
import sys

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "art/steve"))
from classic_scenes import base
from render_steve import part_matrices, point, render, Renderer, v

OUT = ROOT / "src/assets"


def bocchi(key, index):
    path = ROOT / "art/hitori/actions" / (key + ".png")
    with Image.open(path) as image:
        return image.convert("RGBA").crop((index * 192, 0, (index + 1) * 192, 208))


def strip(name, cells):
    result = Image.new("RGBA", (192 * len(cells), 208))
    for i, cell in enumerate(cells):
        result.alpha_composite(cell, (i * 192, 0))
    result.save(OUT / ("duet-" + name + ".png"), optimize=True)


def build():
    loose = bocchi("pick", 1)
    # Detached islands in the source drawings are rendered once on the scene
    # layer so that the same pick can travel between independently posed actors.
    loose.crop((40, 146, 54, 161)).save(OUT / "duet-pick.png", optimize=True)
    slip = loose.copy()
    slip.paste((0, 0, 0, 0), (30, 114, 55, 161))
    watching = bocchi("pick", 2)
    watching.paste((0, 0, 0, 0), (31, 172, 63, 199))
    reaching = bocchi("pickcatch", 2)
    reaching.paste((0, 0, 0, 0), (3, 101, 18, 119))
    reaching.paste((0, 0, 0, 0), (16, 113, 32, 129))
    strip("hitori", [bocchi("pick", 0), slip, watching, reaching,
                     bocchi("pick", 3), bocchi("pick", 4)])

    camera = Renderer()
    for direction, turn in (("left", -46), ("right", 48)):
        frames, hands = [], []
        # All six arm/leg rotations are from cached Java 1.8 ModelBiped
        # samples. Only the whole player's facing changes for the other side.
        for row, col in ((0, 0), (8, 0), (5, 3), (0, 2), (3, 1), (3, 2)):
            key = "base.%s.%s" % (row, col)
            frame = replace(base(row, col), turn=turn)
            frames.append(render(key, frame))
            hand = point(part_matrices(key, frame)[2], v(-1, -9, .5))
            hands.append((round(96 + hand @ camera.right * camera.scale),
                          round(camera.baseline - hand @ camera.up * camera.scale)))
        strip("steve-" + direction, frames)
        print(direction, "right-hand anchors:", hands)


if __name__ == "__main__":
    build()
