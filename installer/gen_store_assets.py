# Regenerates the MSIX logos, the Store listing images and GGCalendar.ico from img/GGCalendar_box_art.png.
# Requires Python with Pillow (pip install pillow). Run: python installer/gen_store_assets.py
#
# Only the images declared in src/GGCalendar.App/Package.appxmanifest are generated (StoreLogo, Square44x44Logo,
# Square150x150Logo). The Store rejects package images over 200 KB, so bigger PNGs are saved with a 256-colour palette.
import os
from PIL import Image, ImageDraw

MAX_PACKAGE_IMAGE_BYTES = 190 * 1024  # the limit is 204800 bytes; keep a margin
SCALES = (100, 125, 150, 200, 400)

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = os.path.join(root, "src", "GGCalendar.App", "Assets")
store = os.path.join(root, "Microsoft_deploy", "store")
os.makedirs(store, exist_ok=True)

src = Image.open(os.path.join(root, "img", "GGCalendar_box_art.png")).convert("RGBA")
# the box art has an opaque light-grey (~239) frame outside the rounded card: make it transparent
W = src.width
for pt in [(0, 0), (W - 1, 0), (0, W - 1), (W - 1, W - 1)]:
    ImageDraw.floodfill(src, pt, (0, 0, 0, 0), thresh=9)
src = src.crop(src.getchannel("A").getbbox())
side = max(src.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(src, ((side - src.width) // 2, (side - src.height) // 2))
src = square


def sq(n):
    return src.resize((n, n), Image.LANCZOS)


def save_package_image(img, name):
    path = os.path.join(out, name)
    img.save(path, optimize=True)
    if os.path.getsize(path) > MAX_PACKAGE_IMAGE_BYTES:
        img.quantize(colors=256, method=Image.Quantize.FASTOCTREE).save(path, optimize=True)
    size = os.path.getsize(path)
    if size > MAX_PACKAGE_IMAGE_BYTES:
        raise SystemExit(f"{name}: {size} bytes, over the Store limit even with a palette")


# Files this script used to generate for assets the manifest does not declare: remove leftovers.
for name in os.listdir(out):
    if name.startswith(("Square71x71Logo", "Square310x310Logo", "Wide310x150Logo", "SplashScreen")):
        os.remove(os.path.join(out, name))

made = []
for base, size in {"StoreLogo": 50, "Square44x44Logo": 44, "Square150x150Logo": 150}.items():
    for sc in SCALES:
        name = f"{base}.scale-{sc}.png"
        save_package_image(sq(round(size * sc / 100)), name)
        made.append(name)
for t in (16, 24, 32, 48, 256):
    for suffix in ("", "_altform-unplated"):
        name = f"Square44x44Logo.targetsize-{t}{suffix}.png"
        save_package_image(sq(t), name)
        made.append(name)

# Store listing images (uploaded in Partner Center, not part of the package)
sq(1080).save(os.path.join(store, "GGCalendar_box_art_1080x1080.png"), optimize=True)
poster = Image.new("RGBA", (720, 1080), (255, 255, 255, 255))
poster.paste(sq(720), (0, 180), sq(720))
poster.save(os.path.join(store, "GGCalendar_poster_720x1080.png"), optimize=True)

# Application icon (exe, Setup.exe, window icons)
sq(256).save(os.path.join(out, "GGCalendar.ico"),
             sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

biggest = max(made, key=lambda n: os.path.getsize(os.path.join(out, n)))
print(f"{len(made)} package images, biggest {biggest} ({os.path.getsize(os.path.join(out, biggest)) // 1024} KB)")
