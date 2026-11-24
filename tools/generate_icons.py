"""Regenerate packaged icons from resource/logo.png. Requires Pillow."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
image = Image.open(root / "resource/logo.png").convert("RGBA")
image.save(root / "resource/key_flow.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
image.thumbnail((256,256), Image.Resampling.LANCZOS)
image.save(root / "resource/key_flow.png")
