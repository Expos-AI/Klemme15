"""Packs the rendered PNGs into a multi-size Windows .ico (needs Pillow). Run after render.js."""
from pathlib import Path
from PIL import Image

logo = Path(__file__).resolve().parents[2] / "src/Klemme15.App/Assets/Logo"
sizes = [16, 24, 32, 48, 64, 128, 256]
images = [Image.open(logo / f"klemme15-{s}.png").convert("RGBA") for s in sizes]
images[-1].save(logo / "klemme15.ico", format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
print("klemme15.ico written.")
