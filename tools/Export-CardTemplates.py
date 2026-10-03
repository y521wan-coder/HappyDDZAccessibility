"""Export anonymous binary card glyphs from one historical replay frame.

The output is an experimental read-only recognition model. It does not contain
avatars, names, card coordinates, or any hidden state from a live match.
"""

import base64
import json
import runpy
from pathlib import Path

import cv2


root = Path(__file__).resolve().parents[1]
module = runpy.run_path(str(root / "tools" / "Probe-ReplayCards.py"))
source = cv2.imread(str(root / "samples" / "private" / "replay-step0.bmp"), cv2.IMREAD_COLOR)
if source is None:
    raise FileNotFoundError("samples/private/replay-step0.bmp")

templates = []
for labels, start_x, y in (
    (module["LEFT_TOP"], 338, 193),
    (module["LEFT_BOTTOM"], 338, 242),
    (module["RIGHT_TOP"], 670, 193),
    (module["RIGHT_BOTTOM"], 746, 242),
):
    for index, (rank, suit) in enumerate(labels):
        if rank == "JOKER":
            continue
        x = round(start_x + index * 25.2)
        rank_mask = (module["extract"](source, x, y, "small", "rank") > 100).astype("uint8")
        suit_mask = (module["extract"](source, x, y, "small", "suit") > 100).astype("uint8")
        templates.append({
            "rank": rank,
            "suit": suit,
            "rank_mask": base64.b64encode(rank_mask.tobytes()).decode("ascii"),
            "suit_mask": base64.b64encode(suit_mask.tobytes()).decode("ascii"),
        })

model = {
    "game_version": "8.057.106.17343",
    "source": "one anonymous historical replay frame; not independently validated",
    "mask_width": 48,
    "rank_height": 52,
    "suit_height": 48,
    "templates": templates,
}
target = root / "models" / "cards" / "replay-one-match.json"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(model, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
print(f"Exported {len(templates)} card corner masks to {target}")
