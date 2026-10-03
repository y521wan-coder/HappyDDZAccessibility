"""Find stable local hand layouts in private replay captures, export anonymous rows.

This is an offline layout probe. A rejected frame is never treated as a hand.
"""

from pathlib import Path
import json

import cv2
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
PRIVATE = ROOT / "samples" / "private"
OUTPUT = ROOT / "samples" / "anonymous" / "replay-hand-rows"
OUTPUT.mkdir(parents=True, exist_ok=True)


def locate(image: np.ndarray) -> dict:
    strip = image[532:541, 200:1080]
    pale = (np.min(strip, axis=2) > 175) & (
        np.max(strip, axis=2) - np.min(strip, axis=2) < 45)
    strength = np.mean(pale, axis=0)
    positions = np.flatnonzero(strength > 0.45)
    if positions.size == 0:
        return {"stable": False, "reason": "no hand edge"}
    left = int(positions[0] + 200)
    right = int(positions[-1] + 200)
    width = right - left + 1
    count = round((width - 109) / 41) + 1
    expected = 109 + (count - 1) * 41
    center = (left + right) / 2
    stable = 1 <= count <= 20 and abs(width - expected) <= 3 and abs(center - 631.5) <= 4
    return {
        "stable": stable, "left": left, "right": right, "width": width,
        "count": count, "expected_width": expected, "center": center,
        "reason": "aligned" if stable else "animation, overlap, or unexpected layout",
    }


def main() -> None:
    files = [(0, PRIVATE / "replay-step0.bmp")]
    files.extend((step, PRIVATE / "replay-frames" / f"step-{step:02}.bmp")
                 for step in range(1, 33))
    report = []
    for step, path in files:
        image = cv2.imread(str(path), cv2.IMREAD_COLOR)
        if image is None:
            raise FileNotFoundError(path)
        info = {"step": step, **locate(image)}
        report.append(info)
        if info["stable"]:
            # Own hand only; exclude avatar, account, opponents, and match ID.
            crop = image[526:615, info["left"]-2:info["right"]+3]
            cv2.imwrite(str(OUTPUT / f"step-{step:02}.png"), crop)
        print(f"step={step:02} count={info.get('count', '-'):>2} "
              f"width={info.get('width', '-'):>3} stable={info['stable']}")
    (OUTPUT / "layout-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2),
                                                 encoding="utf-8")
    print(f"stable frames: {sum(item['stable'] for item in report)}/{len(report)}")


if __name__ == "__main__":
    main()
