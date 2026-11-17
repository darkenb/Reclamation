"""Draw the 80x80 item icons for points (a token with a gem) and heat (a flame) into models/.
Usage: make-item-icons.py   (needs Pillow)
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

S, OUT = 320, 80  # drawn 4x and downscaled for anti-aliasing
HERE = Path(__file__).parent / 'models'


def layer():
    return Image.new('RGBA', (S, S), (0, 0, 0, 0))


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def points():
    img = layer()
    glow = layer()
    ImageDraw.Draw(glow).ellipse((34, 34, S - 34, S - 34), fill=(70, 190, 255, 150))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(14)))
    d = ImageDraw.Draw(img)
    # rim: light top left to dark bottom right
    for i in range(40):
        t = i / 39
        r = 128 - i * 0.35
        d.ellipse((160 - r - 6 + t * 12, 160 - r - 6 + t * 12, 160 + r - 6 + t * 12, 160 + r - 6 + t * 12), fill=mix((170, 230, 255), (30, 90, 150), t) + (255,))
    # face
    for i in range(30):
        t = i / 29
        r = 104 - i * 2.2
        d.ellipse((160 - r, 160 - r, 160 + r, 160 + r), fill=mix((18, 40, 72), (40, 92, 150), t) + (255,))
    d.ellipse((56, 56, S - 56, S - 56), outline=(120, 210, 255, 255), width=5)
    # gem: a diamond, light left facet and dark right facet
    top, mid, bottom, w = (160, 86), 150, (160, 238), 56
    left, right = (160 - w, mid), (160 + w, mid)
    d.polygon([top, left, bottom], fill=(255, 226, 140, 255))
    d.polygon([top, right, bottom], fill=(230, 150, 50, 255))
    d.polygon([top, (160 - w * 0.45, mid), (160, mid + 14), (160 + w * 0.45, mid)], fill=(255, 245, 200, 255))
    d.line([left, top, right, bottom, left], fill=(120, 60, 10, 255), width=4)
    # spark
    s = layer()
    sd = ImageDraw.Draw(s)
    cx, cy, a, b = 214, 100, 30, 7
    sd.polygon([(cx, cy - a), (cx + b, cy - b), (cx + a, cy), (cx + b, cy + b), (cx, cy + a), (cx - b, cy + b), (cx - a, cy), (cx - b, cy - b)], fill=(255, 255, 255, 255))
    img.alpha_composite(s.filter(ImageFilter.GaussianBlur(3)))
    img.alpha_composite(s)
    return img


def flame_shape(cx, base, w, h):
    """Round at the bottom, a tip leaning right at the top."""
    bottom = [(cx + w * math.cos(math.radians(a)), base - w * 0.55 + w * 0.55 * math.sin(math.radians(a))) for a in range(0, 181, 6)]
    left = [(cx - w * 0.92, base - h * 0.42), (cx - w * 0.45, base - h * 0.62), (cx - w * 0.38, base - h * 0.80), (cx + w * 0.10, base - h)]
    right = [(cx + w * 0.42, base - h * 0.74), (cx + w * 0.50, base - h * 0.56), (cx + w * 0.80, base - h * 0.60)]
    return bottom + left + right


def heat():
    img = layer()
    f = layer()
    fd = ImageDraw.Draw(f)
    fd.polygon(flame_shape(160, 286, 92, 262), fill=(232, 70, 24, 255))
    fd.polygon(flame_shape(162, 284, 70, 206), fill=(255, 140, 36, 255))
    fd.polygon(flame_shape(164, 280, 42, 130), fill=(255, 220, 110, 255))
    fd.polygon(flame_shape(165, 276, 18, 62), fill=(255, 250, 225, 255))
    img.alpha_composite(f.filter(ImageFilter.GaussianBlur(16)))
    img.alpha_composite(f)
    return img


for name, draw in (('points_icon.png', points), ('heat_icon.png', heat)):
    draw().resize((OUT, OUT), Image.LANCZOS).save(HERE / name)
    print('wrote', HERE / name)
