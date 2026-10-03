"""Create anonymous hand-region probes from a private table screenshot."""
from pathlib import Path
from PIL import Image, ImageOps, ImageEnhance

root = Path(__file__).resolve().parents[1]
source = Image.open(root / "samples" / "private" / "ranked-table-3.png").convert("RGB")
target = root / "samples" / "anonymous"
target.mkdir(parents=True, exist_ok=True)

hand = source.crop((255, 529, 1021, 677))
hand.save(target / "hand-raw.png")
hand.resize((hand.width * 3, hand.height * 3), Image.Resampling.LANCZOS).save(target / "hand-upscaled.png")

for index in range(17):
    x = 255 + index * 41
    corner = source.crop((x + 3, 529, x + 40, 621))
    corner.resize((corner.width * 4, corner.height * 4), Image.Resampling.LANCZOS).save(
        target / f"corner-{index + 1:02d}.png")
