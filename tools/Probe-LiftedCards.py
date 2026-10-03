"""Inspect the upper edge of locally saved hand screenshots for raised cards."""

from pathlib import Path
import sys


def pale_runs(path: Path) -> list[tuple[int, int]]:
    data = path.read_bytes()
    if int.from_bytes(data[18:22], "little", signed=True) != 1280 or \
            int.from_bytes(data[22:26], "little", signed=True) != -720:
        raise ValueError(f"Expected top-down 1280x720 BMP: {path}")

    def pale(x: int) -> bool:
        count = 0
        for y in range(516, 522):
            offset = 54 + (y * 1280 + x) * 4
            pixel = data[offset:offset + 3]
            count += min(pixel) > 175 and max(pixel) - min(pixel) < 45
        return count >= 4

    runs = []
    left = None
    for x in range(200, 1081):
        if x < 1080 and pale(x):
            if left is None:
                left = x
        elif left is not None:
            if x - left >= 30:
                runs.append((left, x - 1))
            left = None
    return runs


for name in sys.argv[1:]:
    path = Path(name)
    print(f"{path.name}: {pale_runs(path)}")
