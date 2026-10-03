"""Compare one replay frame's small opponent card corners with our large hand.

The replay exposes historical opponent cards; these are used only as offline
templates. Live scanning must never reveal another player's hidden cards.
Annotations are visual, from one frame, and are not a production dataset.
"""

from collections import Counter
from pathlib import Path
import json
import sys

import cv2
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "samples" / "private" / "replay-step0.bmp"
CARDS = json.loads((ROOT / "samples" / "anonymous" / "hand-001.json").read_text(
    encoding="utf-8"))["cards_left_to_right"]

LEFT_TOP = [
    ("JOKER", None), ("2", "club"), ("2", "diamond"), ("K", "spade"),
    ("Q", "club"), ("10", "spade"), ("10", "club"), ("9", "spade"),
    ("9", "club"), ("8", "club"),
]
LEFT_BOTTOM = [
    ("7", "spade"), ("6", "spade"), ("6", "heart"), ("6", "club"),
    ("5", "spade"), ("5", "heart"), ("4", "spade"), ("3", "spade"),
    ("3", "club"),
]
RIGHT_TOP = [
    ("2", "spade"), ("2", "heart"), ("A", "club"), ("A", "diamond"),
    ("K", "diamond"), ("Q", "spade"), ("Q", "heart"), ("Q", "diamond"),
    ("J", "spade"), ("J", "diamond"),
]
RIGHT_BOTTOM = [
    ("10", "heart"), ("9", "diamond"), ("8", "spade"), ("8", "heart"),
    ("5", "diamond"), ("4", "heart"), ("3", "diamond"),
]


def normalized(image: np.ndarray, width: int, height: int) -> np.ndarray:
    if image.size == 0:
        return np.zeros((height, width), np.uint8)
    ink = (np.min(image, axis=2) < 150).astype(np.uint8) * 255
    count, components, stats, _ = cv2.connectedComponentsWithStats(ink, 8)
    keep = np.zeros_like(ink)
    for label in range(1, count):
        _, _, w, h, area = stats[label]
        if area >= 2 and w >= 2 and h >= 2:
            keep[components == label] = 255
    points = cv2.findNonZero(keep)
    if points is None:
        return np.zeros((height, width), np.uint8)
    x, y, w, h = cv2.boundingRect(points)
    cropped = keep[y:y+h, x:x+w]
    scale = min((width - 4) / w, (height - 4) / h)
    resized = cv2.resize(cropped, (max(1, round(w * scale)), max(1, round(h * scale))),
                         interpolation=cv2.INTER_LINEAR)
    out = np.zeros((height, width), np.uint8)
    ox = (width - resized.shape[1]) // 2
    oy = (height - resized.shape[0]) // 2
    out[oy:oy+resized.shape[0], ox:ox+resized.shape[1]] = resized
    return out


def extract(image: np.ndarray, x: int, y: int, size: str, kind: str) -> np.ndarray:
    if size == "small":
        # The upper replay hands show overlapping 26-pixel-wide cards.
        box = (x + 1, y + 1, x + 25, y + 25) if kind == "rank" else (
            x + 2, y + 25, x + 24, y + 47)
    else:
        # Bottom hand: same card art rendered at roughly twice the scale.
        box = (x + 2, y + 2, x + 39, y + 45) if kind == "rank" else (
            x + 7, y + 45, x + 35, y + 77)
    x1, y1, x2, y2 = box
    return normalized(image[y1:y2, x1:x2], 48, 52 if kind == "rank" else 48)


def similarity(a: np.ndarray, b: np.ndarray) -> float:
    # Permit one-pixel registration differences after normalization.
    scores = []
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            shifted = np.roll(b, (dy, dx), axis=(0, 1)) > 100
            left = a > 100
            union = np.count_nonzero(left | shifted)
            scores.append(np.count_nonzero(left & shifted) / union if union else 0)
    return max(scores)


def main() -> None:
    image = cv2.imread(str(SOURCE), cv2.IMREAD_COLOR)
    if image is None:
        raise FileNotFoundError(SOURCE)
    test_source = Path(sys.argv[1]) if len(sys.argv) > 1 else SOURCE
    test_image = cv2.imread(str(test_source), cv2.IMREAD_COLOR)
    if test_image is None:
        raise FileNotFoundError(test_source)
    print(f"template frame: {SOURCE.name}; test frame: {test_source.name}")
    templates = []
    for labels, start_x, y in ((LEFT_TOP, 338, 193), (LEFT_BOTTOM, 338, 242),
                               (RIGHT_TOP, 670, 193), (RIGHT_BOTTOM, 746, 242)):
        for i, (rank, suit) in enumerate(labels):
            if rank == "JOKER":
                continue
            x = round(start_x + i * 25.2)
            templates.append((rank, suit, extract(image, x, y, "small", "rank"),
                              extract(image, x, y, "small", "suit")))
    for kind, index in (("rank", 2), ("suit", 3)):
        right = total = 0
        for card in CARDS:
            if card["rank"] == "JOKER":
                continue
            x = 255 + (card["index"] - 1) * 41
            query = extract(test_image, x, 529, "large", kind)
            scored = sorted(((similarity(query, row[index]), row[0 if kind == "rank" else 1])
                             for row in templates), reverse=True)
            prediction = scored[0][1]
            expected = card[kind]
            right += prediction == expected
            total += 1
            print(f"{kind:4} card={card['index']:02} actual={expected:7} predicted={prediction:7} "
                  f"score={scored[0][0]:.3f}")
        print(f"{kind}: {right}/{total} against opponent cards in same replay frame; "
              "not an independent-session accuracy estimate")
    print("training labels:", dict(Counter(row[0] for row in templates)))

    # Manually annotated changes to our own hand in the same replay. Positions
    # refer to the initial hand's left-to-right numbering in hand-001.json.
    states = {
        0: list(range(1, 18)),
        5: [1, 2, 3, 4, 5, 6, 7, 12, 13, 16, 17],
        15: [1, 3, 4, 5, 6, 7, 12, 13, 16, 17],
        17: [3, 4, 5, 6, 7, 12, 13],
        20: [3, 4, 5, 6, 7],
        23: [3, 6, 7],
        29: [6, 7],
    }
    layouts = {row["step"]: row for row in json.loads((ROOT / "samples" / "anonymous" /
        "replay-hand-rows" / "layout-report.json").read_text(encoding="utf-8"))}
    for step, indices in states.items():
        path = SOURCE if step == 0 else ROOT / "samples" / "private" / "replay-frames" / f"step-{step:02}.bmp"
        frame = cv2.imread(str(path), cv2.IMREAD_COLOR)
        layout = layouts[step]
        if not layout["stable"] or layout["count"] != len(indices):
            raise ValueError(f"Step {step}: expected {len(indices)} stable cards, got {layout}")
        matched = [0, 0]
        accepted = [0, 0]
        accepted_correct = [0, 0]
        total = 0
        for position, card_index in enumerate(indices):
            card = CARDS[card_index - 1]
            if card["rank"] == "JOKER":
                continue
            total += 1
            x = layout["left"] + position * 41
            for kind_index, kind in enumerate(("rank", "suit")):
                query = extract(frame, x, 529, "large", kind)
                by_label = {}
                for row in templates:
                    label = row[kind_index]
                    by_label[label] = max(by_label.get(label, 0),
                                          similarity(query, row[2 + kind_index]))
                choices = sorted(((value, label) for label, value in by_label.items()),
                                 reverse=True)
                best_score, prediction = choices[0]
                margin = best_score - choices[1][0]
                matched[kind_index] += prediction == card[kind]
                confident = (best_score >= (0.60 if kind == "rank" else 0.85)
                             and margin >= 0.03)
                accepted[kind_index] += confident
                accepted_correct[kind_index] += confident and prediction == card[kind]
                if prediction != card[kind]:
                    print(f"  mismatch step={step:02} position={position + 1} "
                          f"{kind} expected={card[kind]} predicted={prediction} "
                          f"score={best_score:.3f} margin={margin:.3f} "
                          f"{'accepted' if confident else 'rejected as unknown'}")
        print(f"replay step {step:02}: hand {len(indices)} cards, "
              f"rank {matched[0]}/{total} raw, {accepted_correct[0]}/{accepted[0]} accepted; "
              f"suit {matched[1]}/{total} raw, {accepted_correct[1]}/{accepted[1]} accepted")


if __name__ == "__main__":
    main()
