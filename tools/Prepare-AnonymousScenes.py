"""Crop public UI regions while excluding player names and account portraits."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
private = root / "samples" / "private"
anonymous = root / "samples" / "anonymous"
anonymous.mkdir(parents=True, exist_ok=True)

regions = {
    "mode-grid.png": ("ddz-entry.png", (267, 114, 1147, 581)),
    "room-grid.png": ("classic-mode.png", (237, 199, 1048, 530)),
    "action-buttons.png": ("ranked-table-3.png", (374, 415, 905, 506)),
    "timeout-dialog.png": ("wgc-client.bmp", (370, 215, 914, 531)),
    "settlement-panel.png": ("ranked-later.png", (612, 155, 1270, 598)),
}

for output, (source, box) in regions.items():
    with Image.open(private / source) as image:
        image.crop(box).save(anonymous / output)
    print(output)
