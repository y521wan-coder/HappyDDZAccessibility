"""Offline timer-glyph experiment on anonymous visible-clock crops."""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SAMPLES = ROOT / "samples" / "anonymous" / "timer-crops-v2"
OFFLINE_LABELS = {13: 19, 30: 6, 31: 6, 46: 11}
LABELS = {
    281: 20, 286: 20, 292: 19, 297: 19, 302: 18,
    307: 17, 312: 17, 318: 16, 323: 16, 328: 15, 333: 15,
    338: 14, 343: 14, 349: 13, 354: 13, 359: 12, 364: 12,
    369: 11, 374: 11, 380: 10, 385: 10, 390: 9, 395: 9,
    400: 8, 406: 8, 411: 7, 416: 7, 421: 6, 426: 6,
    437: 5, 442: 4, 447: 4,
}


def mask(path: Path):
    image = Image.open(path).convert("RGB")
    return tuple(min(image.getpixel((x, y))) >= 210 and
                 max(image.getpixel((x, y))) - min(image.getpixel((x, y))) <= 55
                 for y in range(23, 59) for x in range(24, 63))


def distance(left, right):
    return sum(a != b for a, b in zip(left, right)) / len(left)


def main():
    samples = {number: mask(SAMPLES / f"timer-{number:04d}.png")
               for number in LABELS}
    templates = {}
    for number, label in LABELS.items():
        templates.setdefault(label, samples[number])
    errors = 0
    for number, label in LABELS.items():
        scores = sorted((distance(samples[number], candidate), digit)
                        for digit, candidate in templates.items())
        if scores[0][1] != label:
            errors += 1
        print(f"{number:04d} actual={label:2d} predicted={scores[0][1]:2d} "
              f"distance={scores[0][0]:.3f} margin={scores[1][0]-scores[0][0]:.3f}")
    print(f"train/near-frame: {len(samples)} crops, {errors} errors; "
          f"distinct values={len(templates)}")
    for number, label in OFFLINE_LABELS.items():
        candidate = mask(SAMPLES / f"timer-offline-{number:04d}.png")
        scores = sorted((distance(candidate, template), digit)
                        for digit, template in templates.items())
        print(f"offline {number:04d} actual={label:2d} predicted={scores[0][1]:2d} "
              f"distance={scores[0][0]:.3f} margin={scores[1][0]-scores[0][0]:.3f}")


if __name__ == "__main__":
    main()
