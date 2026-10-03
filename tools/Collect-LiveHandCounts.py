"""Read-only window captures for layouts after a landlord hand changes size.

Run only while a manually entered ordinary match is active. Full screenshots
are private; no image is published or used to make game decisions.
"""

from pathlib import Path
import argparse
import statistics
import subprocess
import time

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CAPTURE = ROOT / "native" / "bin" / "WindowCapture.exe"
PRIVATE = ROOT / "samples" / "private"
CACHE = ROOT / "cache"


def capture(pid: int, hwnd: int, path: Path) -> bool:
    result = subprocess.run([str(CAPTURE), str(pid), str(hwnd), str(path)],
                            capture_output=True, text=True, timeout=8)
    return result.returncode == 0


def layout(path: Path):
    with Image.open(path) as image:
        image = image.convert("RGB")
        positions = []
        for x in range(200, 1160):
            pale = 0
            for y in range(532, 541):
                color = image.getpixel((x, y))
                if min(color) > 175 and max(color) - min(color) < 45:
                    pale += 1
            if pale >= 5:
                positions.append(x)
        if not positions:
            return None
        left, right = positions[0], positions[-1]
        width = right - left + 1
        count = round((width - 109) / 41) + 1
        expected = 109 + (count - 1) * 41
        if count < 1 or count > 20 or abs(width - expected) > 3:
            return None
        lights = [min(image.getpixel((x, 625)))
                  for x in range(left + 10, right - 9, 8)]
        if not lights or statistics.median(lights) < 175:
            return None
        return count, left, right, (left + right) / 2


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pid", type=int)
    parser.add_argument("hwnd", type=int)
    parser.add_argument("--duration", type=int, default=90)
    options = parser.parse_args()
    CACHE.mkdir(exist_ok=True)
    PRIVATE.mkdir(exist_ok=True)
    temp = CACHE / "live-count-probe.bmp"
    saved = set()
    deadline = time.monotonic() + options.duration
    attempts = 0
    try:
        while time.monotonic() < deadline and saved != {18, 19, 20}:
            attempts += 1
            if not capture(options.pid, options.hwnd, temp):
                break
            first = layout(temp)
            if first and first[0] in {18, 19, 20} - saved:
                other = CACHE / "live-count-probe-second.bmp"
                time.sleep(0.18)
                if capture(options.pid, options.hwnd, other):
                    second = layout(other)
                    if second and first[:2] == second[:2]:
                        count = first[0]
                        stamp = time.strftime("%Y%m%d-%H%M%S")
                        destination = PRIVATE / f"live-{count}-cards-{stamp}.bmp"
                        destination2 = PRIVATE / f"live-{count}-cards-{stamp}-second.bmp"
                        destination.write_bytes(temp.read_bytes())
                        destination2.write_bytes(other.read_bytes())
                        saved.add(count)
                        print(f"saved {count} cards: left={first[1]}, right={first[2]}, "
                              f"center={first[3]:.1f}; pair={destination.name}", flush=True)
                other.unlink(missing_ok=True)
            time.sleep(0.35)
    finally:
        temp.unlink(missing_ok=True)
    print(f"captures={attempts}; saved counts={sorted(saved)}", flush=True)


if __name__ == "__main__":
    main()
