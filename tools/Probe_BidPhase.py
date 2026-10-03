"""Compare visible button lettering across bid and rob snapshots (private inputs)."""
import base64
import json
import sys
from itertools import combinations, product
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
private = root / "samples" / "private"
call = [private / f"bid-probe-found-{n}.bmp" for n in range(8, 17)]
rob = [private / f"bid-next-20261003-{n}.bmp" for n in range(12, 16)]


def mask(path: Path, box: tuple[int, int, int, int]) -> tuple[bool, ...]:
    with Image.open(path) as image:
        return tuple(r >= 225 and g >= 220 and b >= 175
                     for r, g, b in image.crop(box).convert("RGB").get_flattened_data())


def compare(box: tuple[int, int, int, int]) -> None:
    a = [mask(path, box) for path in call]
    b = [mask(path, box) for path in rob]
    distance = lambda x, y: sum(v != w for v, w in zip(x, y))
    aa = [distance(x, y) for x, y in combinations(a, 2)]
    bb = [distance(x, y) for x, y in combinations(b, 2)]
    ab = [distance(x, y) for x, y in product(a, b)]
    print(box, "call", min(aa), max(aa), "rob", min(bb), max(bb),
          "cross", min(ab), max(ab))


for rectangle in [(460, 440, 550, 475), (470, 442, 540, 470),
                  (710, 440, 810, 475), (720, 442, 800, 470)]:
    compare(rectangle)

box = (460, 440, 550, 475)
left_call = mask(call[0], box)
left_rob = mask(rob[0], box)
for path in sorted(private.glob("*bid*.bmp")):
    candidate = mask(path, box)
    c = sum(a != b for a, b in zip(candidate, left_call))
    r = sum(a != b for a, b in zip(candidate, left_rob))
    print(path.name, "call", c, "rob", r)

if "--write" in sys.argv:
    boxes = {"left": (460, 440, 550, 475),
             "right": (710, 440, 810, 475)}
    model = {
        "game_version": "8.057.106.17343",
        "threshold": "RGB >= (225,220,175)",
        "boxes": {},
    }
    for name, rectangle in boxes.items():
        model["boxes"][name] = {
            "x": rectangle[0], "y": rectangle[1],
            "width": rectangle[2] - rectangle[0],
            "height": rectangle[3] - rectangle[1],
            "call": base64.b64encode(bytes(mask(call[0], rectangle))).decode(),
            "rob": base64.b64encode(bytes(mask(rob[0], rectangle))).decode(),
        }
    output = root / "models" / "controls" / "bid-phase-v1.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(model, ensure_ascii=False, separators=(",", ":")),
                      encoding="utf-8")
    print("Wrote", output)
