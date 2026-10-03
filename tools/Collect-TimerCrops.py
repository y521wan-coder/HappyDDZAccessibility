"""Capture only the visible own-turn timer crop for offline recognition work."""

from pathlib import Path
import argparse
import subprocess
import time

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CAPTURE = ROOT / "native" / "bin" / "WindowCapture.exe"
CACHE = ROOT / "cache" / "timer-frame.bmp"
OUTPUT = ROOT / "samples" / "anonymous" / "timer-crops-v2"
PRIVATE = ROOT / "samples" / "private"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pid", type=int, nargs="?")
    parser.add_argument("hwnd", type=int, nargs="?")
    parser.add_argument("--duration", type=int, default=45)
    parser.add_argument("--offline", action="store_true")
    options = parser.parse_args()
    CACHE.parent.mkdir(exist_ok=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    seen = set()
    saved = 0
    def consider(image: Image.Image, name: str):
        nonlocal saved
        if image.size != (1280, 720):
            return
        crop = image.convert("RGB").crop((515, 415, 595, 490))
        pixels = list(crop.get_flattened_data())
        yellow = sum(1 for red, green, blue in pixels
                     if red > 220 and 140 < green < 245 and blue < 100)
        white = sum(1 for red, green, blue in pixels
                    if min(red, green, blue) > 225)
        left = crop.getpixel((15, 40))
        right = crop.getpixel((65, 40))
        bottom = crop.getpixel((40, 67))
        clock_shape = (left[0] > 200 and left[1] > 175 and left[2] > 120 and
                       right[0] > 180 and right[1] > 100 and right[2] < 130 and
                       bottom[0] > 190 and bottom[1] > 115 and bottom[2] < 75)
        if yellow <= 300 or white <= 80 or not clock_shape:
            return
        key = crop.resize((40, 38)).tobytes()
        if key in seen:
            return
        seen.add(key)
        crop.save(OUTPUT / name)
        saved += 1

    if options.offline:
        for index, path in enumerate(sorted(PRIVATE.rglob("*.bmp"))):
            try:
                with Image.open(path) as image:
                    consider(image, f"timer-offline-{index:04d}.png")
            except OSError:
                pass
        print(f"offline timer crops saved={saved}; output={OUTPUT}", flush=True)
        return
    if options.pid is None or options.hwnd is None:
        parser.error("pid and hwnd are required unless --offline is used")
    try:
        while time.monotonic() - start < options.duration:
            result = subprocess.run([str(CAPTURE), str(options.pid), str(options.hwnd), str(CACHE)],
                                    capture_output=True, text=True, timeout=8)
            if result.returncode:
                break
            with Image.open(CACHE) as image:
                consider(image, f"timer-{int((time.monotonic() - start) * 10):04d}.png")
            time.sleep(0.4)
    finally:
        CACHE.unlink(missing_ok=True)
    print(f"timer crops saved={saved}; output={OUTPUT}", flush=True)


if __name__ == "__main__":
    main()
