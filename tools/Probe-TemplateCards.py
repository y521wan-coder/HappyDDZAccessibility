"""Offline experiment on the one anonymous hand. Never enables live card actions.

The card corners below came from a single frame and therefore cannot measure
generalization. This script reports leave-one-out comparisons only for classes
that appear at least twice in that frame, plus the unseen/ambiguous cases.
"""

from collections import Counter
from pathlib import Path
import json

import cv2
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
SAMPLES = ROOT / "samples" / "anonymous"
CARDS = json.loads((SAMPLES / "hand-001.json").read_text(encoding="utf-8"))["cards_left_to_right"]


def glyph(index: int, kind: str) -> np.ndarray:
    image = cv2.imread(str(SAMPLES / f"corner-{index:02d}.png"), cv2.IMREAD_COLOR)
    if image is None:
        raise FileNotFoundError(f"corner-{index:02d}.png")
    # Probes are enlarged 4x. Use the rank/suit bands separately and remove
    # the near-white card background, preserving either red or black ink.
    scale = 4
    if kind == "rank":
        region = image[3 * scale:45 * scale, 1 * scale:37 * scale]
        out_size = (36, 42)
    else:
        region = image[44 * scale:75 * scale, 5 * scale:34 * scale]
        out_size = (32, 32)
    dark = np.min(region, axis=2) < 135
    mask = dark.astype(np.uint8) * 255
    count, components, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
    keep = np.zeros_like(mask)
    for label in range(1, count):
        x, y, width, height, area = stats[label]
        if area >= 10 and width >= 3 and height >= 3:
            keep[components == label] = 255
    points = cv2.findNonZero(keep)
    if points is None:
        return np.zeros(out_size[::-1], dtype=np.uint8)
    x, y, width, height = cv2.boundingRect(points)
    cropped = keep[y:y + height, x:x + width]
    target_width, target_height = out_size
    factor = min((target_width - 2) / width, (target_height - 2) / height)
    resized = cv2.resize(cropped, (max(1, round(width * factor)), max(1, round(height * factor))),
                         interpolation=cv2.INTER_AREA)
    output = np.zeros((target_height, target_width), dtype=np.uint8)
    start_x = (target_width - resized.shape[1]) // 2
    start_y = (target_height - resized.shape[0]) // 2
    output[start_y:start_y + resized.shape[0], start_x:start_x + resized.shape[1]] = resized
    return output


def score(left: np.ndarray, right: np.ndarray) -> float:
    a = left > 100
    b = right > 100
    union = np.count_nonzero(a | b)
    return np.count_nonzero(a & b) / union if union else 0.0


def probe(kind: str) -> None:
    labels = [(card["rank"] if kind == "rank" else card["suit"]) for card in CARDS]
    images = [glyph(card["index"], kind) for card in CARDS]
    counts = Counter(label for label in labels if label is not None)
    correct = tested = 0
    for position, label in enumerate(labels):
        if label is None or (kind == "rank" and label == "JOKER"):
            continue
        candidates = sorted(((score(images[position], images[other]), labels[other], other + 1)
                             for other in range(len(labels)) if other != position and labels[other] is not None),
                            reverse=True)
        best, prediction, example = candidates[0]
        runner_up = next((item for item in candidates if item[1] != prediction), None)
        margin = best - (runner_up[0] if runner_up else 0)
        eligible = counts[label] > 1
        if eligible:
            tested += 1
            correct += prediction == label
        print(f"{kind:4} card={position + 1:02} actual={label:7} predicted={prediction:7} "
              f"score={best:.3f} margin={margin:.3f} example={example:02} "
              f"{'same-frame check' if eligible else 'class has one example'}")
    print(f"{kind}: same-frame repeated-class check {correct}/{tested}; "
          "not an independent accuracy estimate")


if __name__ == "__main__":
    probe("rank")
    probe("suit")
