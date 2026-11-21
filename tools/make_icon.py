#!/usr/bin/env python3
"""Draws modicon.png (256x256): a dark tile with a cross and a heartbeat line. Needs Pillow."""
from PIL import Image, ImageDraw

S = 1024  # drawn large, then scaled down for smooth edges
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=150, fill=(38, 32, 28, 255), outline=(204, 158, 77, 255), width=22)
# The cross.
arm, half = 330, 112
cx, cy = S // 2, S // 2 - 30
cream = (238, 226, 200, 255)
d.rounded_rectangle((cx - half, cy - arm, cx + half, cy + arm), radius=34, fill=cream)
d.rounded_rectangle((cx - arm, cy - half, cx + arm, cy + half), radius=34, fill=cream)
# The heartbeat across it.
red = (214, 77, 71, 255)
y = cy
pts = [(cx - arm + 30, y), (cx - 150, y), (cx - 95, y - 120), (cx - 25, y + 150), (cx + 45, y - 190), (cx + 105, y + 60), (cx + 150, y), (cx + arm - 30, y)]
d.line(pts, fill=red, width=34, joint="curve")
for p in (pts[0], pts[-1]):
    d.ellipse((p[0] - 17, p[1] - 17, p[0] + 17, p[1] + 17), fill=red)
# Three dots underneath: passed, warning, problem.
for i, colour in enumerate([(107, 191, 77, 255), (235, 158, 46, 255), (217, 77, 71, 255)]):
    x = cx - 120 + i * 120
    d.ellipse((x - 34, S - 190, x + 34, S - 122), fill=colour, outline=(20, 16, 14, 255), width=8)
img.resize((256, 256), Image.LANCZOS).save("modicon.png")
print("wrote modicon.png")
