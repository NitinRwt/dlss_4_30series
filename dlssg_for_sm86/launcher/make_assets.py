"""Build helper: makes app.ico (if missing or --icon) and payload.zip (the mod files embedded in the exe)."""
import os, struct, sys, zipfile, io

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.dirname(here)


def make_icon(path):
    from PIL import Image, ImageDraw, ImageFont  # pip install pillow
    S = 256
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # dark rounded tile with a vertical gradient
    tile = Image.new("RGBA", (S, S))
    td = ImageDraw.Draw(tile)
    for y in range(S):
        c = int(18 + 22 * y / S)
        td.line([(0, y), (S, y)], fill=(c, c + 4, c + 10, 255))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([8, 8, S - 8, S - 8], radius=52, fill=255)
    img.paste(tile, (0, 0), mask)
    d = ImageDraw.Draw(img)
    green = (118, 230, 60, 255)
    # three "frame" bars: real frame, generated frame, real frame
    for i, (x, h) in enumerate([(40, 96), (104, 96), (168, 96)]):
        col = green if i != 1 else (255, 255, 255, 235)
        d.rounded_rectangle([x, 56, x + 48, 56 + h], radius=10, fill=col)
    # play chevrons under the bars
    for k in range(3):
        x = 46 + k * 62
        d.polygon([(x, 176), (x + 40, 200), (x, 224)], fill=green)
    img.save(path, sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])


def make_payload(path):
    items = ["version.dll", "dlssg_sm86.ini", "dlssg_sm86ini_Configurator.html", "dlssg_sm86"]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for it in items:
            p = os.path.join(root, it)
            if os.path.isdir(p):
                for dp, dn, fn in os.walk(p):
                    dn[:] = [x for x in dn if x.lower() != "logs"]
                    for f in fn:
                        full = os.path.join(dp, f)
                        z.write(full, os.path.relpath(full, root).replace("\\", "/"))
            elif os.path.exists(p):
                z.write(p, it)
            else:
                print("warning: missing", it)
    print("payload.zip", os.path.getsize(path) // 1024 // 1024, "MB")


if __name__ == "__main__":
    ico = os.path.join(here, "app.ico")
    if "--icon" in sys.argv or not os.path.exists(ico):
        make_icon(ico)
    make_payload(os.path.join(here, "payload.zip"))
