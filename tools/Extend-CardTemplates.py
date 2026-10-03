"""Build an experimental model from the replay and one labeled live hand.

Only visible card corners are exported. The remaining labeled hands stay out
of this model for validation. The live screenshot itself remains private.
"""

import base64
import json
import runpy
from pathlib import Path

import cv2


root = Path(__file__).resolve().parents[1]
base = json.loads((root / "models/cards/replay-one-match.json").read_text(encoding="utf-8"))
labels = json.loads((root / "samples/anonymous/live-hand-labels-2026-10-02.json").read_text(
    encoding="utf-8"))
training = next(hand for hand in labels["hands"] if hand["file"] == "match-01-start.bmp")
image = cv2.imread(str(root / "samples/private" / training["file"]), cv2.IMREAD_COLOR)
if image is None:
    raise FileNotFoundError(training["file"])
extract = runpy.run_path(str(root / "tools/Probe-ReplayCards.py"))["extract"]
suit_names = {"黑桃": "spade", "红桃": "heart", "梅花": "club", "方块": "diamond"}

for index, (rank, suit) in enumerate(training["cards"]):
    if rank in ("大王", "小王"):
        continue
    x = 249 + index * 41
    rank_mask = (extract(image, x, 529, "large", "rank") > 100).astype("uint8")
    suit_mask = (extract(image, x, 529, "large", "suit") > 100).astype("uint8")
    base["templates"].append({
        "rank": rank,
        "suit": suit_names[suit],
        "rank_mask": base64.b64encode(rank_mask.tobytes()).decode("ascii"),
        "suit_mask": base64.b64encode(suit_mask.tobytes()).decode("ascii"),
    })

base["source"] = "one historical replay plus 15 visible card corners from match-01-start; other labeled hands held out"
target = root / "models/cards/replay-plus-live-one-hand.json"
target.write_text(json.dumps(base, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"Wrote {len(base['templates'])} templates to {target.name}")
