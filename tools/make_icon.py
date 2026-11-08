"""Generates src/SonicScrewdriver/Assets/app.ico (16-256 px) and a 256 px PNG preview.

A stylized sonic screwdriver lying diagonally, glowing blue emitter, sound waves.
Small sizes (<= 24 px) use a bolder, simplified drawing so they stay readable in the tray.
Run: python tools/make_icon.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "SonicScrewdriver", "Assets")
S = 1024  # supersampled canvas

BLUE = (64, 170, 255, 255)
BLUE_GLOW = (40, 140, 255, 255)
WHITE = (235, 245, 255, 255)
STEEL = (178, 186, 198, 255)
STEEL_DARK = (110, 118, 132, 255)
GRIP = (44, 50, 62, 255)
OUTLINE = (12, 16, 24, 255)


def lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def segment(draw, p0, p1, width, fill, outline_w=0):
    """Thick capsule from p0 to p1 (optionally with a dark outline)."""
    if outline_w:
        segment(draw, p0, p1, width + 2 * outline_w, OUTLINE)
    draw.line([p0, p1], fill=fill, width=int(width))
    r = width / 2
    for p in (p0, p1):
        draw.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def render(simple: bool) -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Axis: handle at bottom-left, emitter towards top-right.
    a = (S * 0.10, S * 0.90) if simple else (S * 0.14, S * 0.86)
    b = (S * 0.60, S * 0.40) if simple else (S * 0.62, S * 0.38)
    ow = S * (0.035 if simple else 0.02)  # outline width

    handle_end = lerp(a, b, 0.48)
    collar_end = lerp(a, b, 0.58)
    body_end = lerp(a, b, 0.92)

    # Handle (dark grip), collar (steel), slim body (steel), emitter (blue).
    segment(d, a, handle_end, S * (0.30 if simple else 0.17), GRIP, ow)
    segment(d, handle_end, collar_end, S * (0.32 if simple else 0.19), STEEL_DARK, ow)
    segment(d, collar_end, body_end, S * (0.22 if simple else 0.10), STEEL, ow)

    if not simple:
        # Grip rings and a highlight stripe for depth.
        for t in (0.12, 0.22, 0.32, 0.42):
            c = lerp(a, handle_end, t / 0.48)
            n = (math.sqrt(0.5), math.sqrt(0.5))  # perpendicular of the 45 degree axis
            h = S * 0.085
            d.line([(c[0] - n[0] * h, c[1] - n[1] * h), (c[0] + n[0] * h, c[1] + n[1] * h)],
                   fill=STEEL_DARK, width=int(S * 0.018))
        hl0 = lerp(collar_end, body_end, 0.05)
        hl1 = lerp(collar_end, body_end, 0.95)
        off = (-S * 0.022, -S * 0.022)
        segment(d, (hl0[0] + off[0], hl0[1] + off[1]), (hl1[0] + off[0], hl1[1] + off[1]),
                S * 0.022, (225, 230, 240, 255))

    # Glow behind the emitter.
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    tip = b
    gr = S * (0.17 if simple else 0.15)
    gd.ellipse([tip[0] - gr, tip[1] - gr, tip[0] + gr, tip[1] + gr], fill=BLUE_GLOW)
    glow = glow.filter(ImageFilter.GaussianBlur(S * 0.05))
    img = Image.alpha_composite(glow, img)
    d = ImageDraw.Draw(img)

    # Emitter tip: blue bulb with a white hot core.
    er = S * (0.15 if simple else 0.085)
    d.ellipse([tip[0] - er - ow, tip[1] - er - ow, tip[0] + er + ow, tip[1] + er + ow], fill=OUTLINE)
    d.ellipse([tip[0] - er, tip[1] - er, tip[0] + er, tip[1] + er], fill=BLUE)
    cr = er * 0.45
    d.ellipse([tip[0] - cr, tip[1] - cr, tip[0] + cr, tip[1] + cr], fill=WHITE)

    # Sound waves radiating to the top-right.
    waves = [(0.30, 0.08)] if simple else [(0.19, 0.03), (0.29, 0.03)]
    for radius, w in waves:
        r = S * radius
        box = [tip[0] - r, tip[1] - r, tip[0] + r, tip[1] + r]
        span = 34 if simple else 38
        d.arc(box, start=-45 - span, end=-45 + span, fill=OUTLINE, width=int(S * w + 2 * ow))
        d.arc(box, start=-45 - span + 2, end=-45 + span - 2, fill=BLUE, width=int(S * w))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    detailed = render(simple=False)
    simple = render(simple=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [(simple if s <= 24 else detailed).resize((s, s), Image.LANCZOS) for s in sizes]
    frames[-1].save(os.path.join(OUT, "app.ico"), format="ICO", sizes=[(s, s) for s in sizes],
                    append_images=frames[:-1])
    # Preview sheet: 256, 48, 32, 24, 16 on dark and light backgrounds.
    sheet = Image.new("RGBA", (256 + 4 * 64 + 40, 2 * 270), (0, 0, 0, 0))
    for row, bg in enumerate([(32, 32, 32, 255), (240, 240, 240, 255)]):
        tile = Image.new("RGBA", (sheet.width, 270), bg)
        tile.alpha_composite(frames[-1], (5, 7))
        x = 280
        for s in (48, 32, 24, 16):
            f = frames[sizes.index(s)]
            tile.alpha_composite(f, (x, 135 - s // 2))
            x += 64
        sheet.alpha_composite(tile, (0, row * 270))
    sheet.save(os.path.join(ROOT, "tools", "icon-preview.png"))
    print("wrote", os.path.join(OUT, "app.ico"))


if __name__ == "__main__":
    main()
