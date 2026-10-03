"""Capture one reversible, two-card selection on a known 17-card live table.

Development probe only. Never presses play/pass and never retries a click.
All screenshots stay in samples/private.
"""

import argparse
import ctypes
import statistics
import subprocess
import time
from datetime import datetime
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CAPTURE = ROOT / "native" / "bin" / "WindowCapture.exe"
PRIVATE = ROOT / "samples" / "private"
WIN = ctypes.windll.user32


class Point(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class Rect(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def grab(pid, hwnd, path):
    subprocess.run([str(CAPTURE), str(pid), str(hwnd), str(path)],
                   check=True, stdout=subprocess.DEVNULL, timeout=5)
    with Image.open(path) as source:
        return source.convert("RGB")


def pale(pixel):
    return min(pixel) > 175 and max(pixel) - min(pixel) < 45


def layout(image):
    if image.size != (1280, 720):
        return None
    found = []
    for x in range(200, 1160):
        if sum(pale(image.getpixel((x, y))) for y in range(532, 541)) >= 5:
            found.append(x)
    if not found:
        return None
    left, right = found[0], found[-1]
    count = round((right - left + 1 - 109) / 41) + 1
    if count != 17 or abs(left - 249) > 3 or abs(right - 1014) > 3:
        return None
    row = [min(image.getpixel((x, 625))) for x in range(left + 10, right - 10, 8)]
    if statistics.median(row) < 175:
        return None
    return left


def same_hand_surface(first, second):
    checked = changed = 0
    for y in range(535, 665, 11):
        for x in range(252, 1008, 7):
            a, b = first.getpixel((x, y)), second.getpixel((x, y))
            checked += 1
            if max(abs(a[i] - b[i]) for i in range(3)) > 25:
                changed += 1
    return changed / checked < 0.02


def raised(image, left, position):
    x0 = left + (position - 1) * 41
    return sum(
        sum(pale(image.getpixel((x, y))) for y in range(516, 522)) >= 4
        for x in range(x0, x0 + 109)
    ) >= 90


def click(hwnd, x, y):
    if WIN.IsIconic(hwnd):
        raise RuntimeError("Game minimized; no click")
    bounds = Rect()
    if not WIN.GetClientRect(hwnd, ctypes.byref(bounds)) or (bounds.right, bounds.bottom) != (1280, 720):
        raise RuntimeError("Game client size changed; no click")
    target = Point(x, y)
    if not WIN.ClientToScreen(hwnd, ctypes.byref(target)):
        raise RuntimeError("ClientToScreen failed; no click")
    WIN.keybd_event(18, 0, 0, 0)
    WIN.keybd_event(18, 0, 2, 0)
    WIN.SetForegroundWindow(hwnd)
    time.sleep(0.18)
    if WIN.GetForegroundWindow() != hwnd or WIN.GetAncestor(WIN.WindowFromPoint(target), 2) != hwnd:
        raise RuntimeError("Game not foreground at card; no click")
    WIN.SetCursorPos(target.x, target.y)
    WIN.mouse_event(0x0002, 0, 0, 0, 0)
    time.sleep(0.05)
    WIN.mouse_event(0x0004, 0, 0, 0, 0)
    time.sleep(0.22)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--hwnd", type=int, required=True)
    parser.add_argument("--wait-seconds", type=int, default=28)
    parser.add_argument("--second-position", type=int, default=8)
    args = parser.parse_args()
    if args.second_position < 3 or args.second_position > 17:
        raise ValueError("Second position must be 3..17 and different from the first position 2")
    pid = ctypes.c_ulong()
    WIN.GetWindowThreadProcessId(args.hwnd, ctypes.byref(pid))
    if pid.value != args.pid:
        raise RuntimeError("Game HWND/PID mismatch")
    PRIVATE.mkdir(parents=True, exist_ok=True)
    prefix = "two-raised-" + datetime.now().strftime("%Y%m%d-%H%M%S")
    previous_cursor = Point()
    WIN.GetCursorPos(ctypes.byref(previous_cursor))
    deadline = time.monotonic() + args.wait_seconds
    try:
        while time.monotonic() < deadline:
            first_path = PRIVATE / f"{prefix}-baseline-a.bmp"
            second_path = PRIVATE / f"{prefix}-baseline-b.bmp"
            first = grab(args.pid, args.hwnd, first_path)
            first_left = layout(first)
            if first_left is None or raised(first, first_left, 2) or raised(first, first_left, args.second_position):
                time.sleep(0.25)
                continue
            time.sleep(0.15)
            second = grab(args.pid, args.hwnd, second_path)
            if layout(second) != first_left or not same_hand_surface(first, second):
                time.sleep(0.25)
                continue

            click(args.hwnd, first_left + 41 + 20, 560)
            after_one = grab(args.pid, args.hwnd, PRIVATE / f"{prefix}-after-one.bmp")
            if layout(after_one) != first_left or not raised(after_one, first_left, 2):
                print("First lift unconfirmed; stopped without second click")
                return 2
            click(args.hwnd, first_left + (args.second_position - 1) * 41 + 20, 560)
            after_two = grab(args.pid, args.hwnd, PRIVATE / f"{prefix}-after-two-a.bmp")
            time.sleep(0.15)
            after_two_stable = grab(args.pid, args.hwnd, PRIVATE / f"{prefix}-after-two-b.bmp")
            both = (layout(after_two) == first_left and
                    layout(after_two_stable) == first_left and
                    all(raised(frame, first_left, index)
                        for frame in (after_two, after_two_stable)
                        for index in (2, args.second_position)))
            print(f"Both raised and stable: {both}; private sample prefix: {prefix}")
            if both:
                click(args.hwnd, first_left + (args.second_position - 1) * 41 + 20, 560)
                click(args.hwnd, first_left + 41 + 20, 560)
                dropped = grab(args.pid, args.hwnd, PRIVATE / f"{prefix}-after-drop.bmp")
                print("Returned to unraised hand:", layout(dropped) == first_left and
                      not raised(dropped, first_left, 2) and
                      not raised(dropped, first_left, args.second_position))
            return 0 if both else 3
        print("No stable 17-card live layout within wait; no clicks")
        return 1
    finally:
        WIN.SetCursorPos(previous_cursor.x, previous_cursor.y)


if __name__ == "__main__":
    raise SystemExit(main())
