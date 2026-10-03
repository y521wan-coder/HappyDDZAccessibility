"""Compare visible bid-clock crops with the read-only play-clock model."""

import base64
import json
from pathlib import Path

from PIL import Image


root = Path(__file__).resolve().parents[1]
model = json.loads((root / "models/timer/clock-v1.json").read_text(encoding="utf-8"))
templates = [(item["seconds"], base64.b64decode(item["mask"]))
             for item in model["templates"]]
for frame in range(8, 21):
    image = Image.open(root / f"samples/private/bid-probe-found-{frame}.bmp").convert("RGB")
    scores = []
    for offset_y in range(-7, 0):
        for offset_x in range(72, 79):
            pixels = []
            for y in range(438, 474):
                for x in range(539, 578):
                    rgb = image.getpixel((x + offset_x, y + offset_y))
                    pixels.append(int(min(rgb) >= 210 and max(rgb) - min(rgb) <= 55))
            best = min((sum(a != b for a, b in zip(pixels, mask)) / len(pixels), seconds)
                       for seconds, mask in templates)
            scores.append((round(best[0], 4), offset_x, offset_y, best[1]))
    print(frame, sorted(scores)[:3])
