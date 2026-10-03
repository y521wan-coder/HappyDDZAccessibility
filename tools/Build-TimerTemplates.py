"""Build anonymous visible-clock digit masks for the read-only timer pilot."""

from pathlib import Path
import base64
import json

from Probe_Timer import LABELS, SAMPLES, mask


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "models" / "timer" / "clock-v1.json"


def main():
    templates = []
    seen = set()
    for number, seconds in LABELS.items():
        if seconds in seen:
            continue
        seen.add(seconds)
        pixels = bytes(int(value) for value in mask(SAMPLES / f"timer-{number:04d}.png"))
        templates.append({"seconds": seconds,
                          "mask": base64.b64encode(pixels).decode("ascii")})
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps({"game_version": "8.057.106.17343",
                                  "mask_width": 39, "mask_height": 36,
                                  "templates": templates},
                                 ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"built {len(templates)} read-only timer templates: {OUTPUT}")


if __name__ == "__main__":
    main()
