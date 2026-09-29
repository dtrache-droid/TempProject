#!/usr/bin/env python3
"""Regenerates the taskbar-button icons in src/ (pure Python, no dependencies).

  icon-grid.png     colourful 2x2 grid           (drawn as-is)
  icon-dots.png     3x3 dots     \
  icon-list.png     list         |  white masks - AppLauncher tints them to
  icon-sparkle.png  sparkle      /  match the light/dark taskbar
  icon-savy.png     the "Savy S" logo (a rasterised copy of the supplied SVG)
  launcher.ico      the icon of AppLauncher.exe itself (the Savy S)
"""
import math, struct, zlib, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src')
N = 256
SS = 3  # supersampling per axis


def png_bytes(n, rows):
    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    raw = b''.join(b'\x00' + bytes(r) for r in rows)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', n, n, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def render(n, sample, ss=SS):
    """sample(x, y) with x,y in 0..1 -> (r,g,b) or None (transparent)."""
    rows = []
    for py in range(n):
        row = bytearray()
        for px in range(n):
            r = g = b = cnt = 0
            for sy in range(ss):
                for sx in range(ss):
                    c = sample((px + (sx + .5) / ss) / n, (py + (sy + .5) / ss) / n)
                    if c:
                        r += c[0]; g += c[1]; b += c[2]; cnt += 1
            if cnt:
                row += bytes([r // cnt, g // cnt, b // cnt, cnt * 255 // (ss * ss)])
            else:
                row += bytes(4)
        rows.append(row)
    return rows


def rrect(x, y, x0, y0, w, h, rad):
    cx = min(max(x, x0 + rad), x0 + w - rad)
    cy = min(max(y, y0 + rad), y0 + h - rad)
    return x0 <= x <= x0 + w and y0 <= y <= y0 + h and (x - cx) ** 2 + (y - cy) ** 2 <= rad * rad


def circle(x, y, cx, cy, r):
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


WHITE = (255, 255, 255)

# ---- grid -------------------------------------------------------------------
def grid(x, y):
    pad, gap = .10, .10
    cell = (1 - 2 * pad - gap) / 2
    cols = [(0x3B, 0x82, 0xF6), (0x22, 0xC5, 0x5E), (0xF5, 0x9E, 0x0B), (0xEF, 0x44, 0x44)]
    for i, (gx, gy) in enumerate([(0, 0), (1, 0), (0, 1), (1, 1)]):
        if rrect(x, y, pad + gx * (cell + gap), pad + gy * (cell + gap), cell, cell, cell * .28):
            return cols[i]

# ---- dots -------------------------------------------------------------------
def dots(x, y):
    for i in range(3):
        for j in range(3):
            if circle(x, y, .2 + i * .3, .2 + j * .3, .095):
                return WHITE

# ---- list -------------------------------------------------------------------
def lst(x, y):
    for j in range(3):
        cy = .22 + j * .28
        if circle(x, y, .17, cy, .06) or rrect(x, y, .32, cy - .055, .52, .11, .055):
            return WHITE

# ---- sparkle ----------------------------------------------------------------
def star(x, y, cx, cy, r):
    dx, dy = abs(x - cx) / r, abs(y - cy) / r
    return dx ** .55 + dy ** .55 <= 1

def sparkle(x, y):
    if star(x, y, .44, .56, .40) or star(x, y, .80, .22, .16):
        return WHITE

# ---- Savy S (from the supplied SVG, viewBox 0 0 100 100) ---------------------
FILL, STROKE, SW = (0xEC, 0xE8, 0xDF), (0x20, 0x1E, 0x1D), 2.5
SHAPES = [
    [(0, 0), (88, 0), (100, 12), (100, 22), (0, 22)],
    [(0, 26), (22, 26), (22, 39), (0, 39)],
    [(0, 39), (100, 39), (100, 61), (0, 61)],
    [(78, 65), (100, 65), (100, 78), (78, 78)],
    [(100, 78), (100, 100), (12, 100), (0, 88), (0, 78)],
]

def inside(px, py, poly):
    c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % len(poly)]
        if (y1 > py) != (y2 > py) and px < (x2 - x1) * (py - y1) / (y2 - y1) + x1:
            c = not c
    return c

def dist(px, py, poly):
    best = 1e9
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % len(poly)]
        dx, dy = x2 - x1, y2 - y1
        t = max(0, min(1, ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy)))
        best = min(best, math.hypot(px - (x1 + t * dx), py - (y1 + t * dy)))
    return best

BOXES = [(min(p[0] for p in s) - SW, min(p[1] for p in s) - SW, max(p[0] for p in s) + SW, max(p[1] for p in s) + SW) for s in SHAPES]

def savy(x, y):
    px, py = x * 100, y * 100
    result = None
    for poly, (bx0, by0, bx1, by1) in zip(SHAPES, BOXES):   # paint-order: stroke, then fill, shape by shape
        if not (bx0 <= px <= bx1 and by0 <= py <= by1):
            continue
        if inside(px, py, poly):
            result = FILL
        elif dist(px, py, poly) <= SW / 2:
            result = STROKE
    return result


def write_png(name, sample, ss=SS):
    with open(os.path.join(OUT, name), 'wb') as f:
        f.write(png_bytes(N, render(N, sample, ss)))
    print('wrote', name)


write_png('icon-grid.png', grid)
write_png('icon-dots.png', dots)
write_png('icon-list.png', lst)
write_png('icon-sparkle.png', sparkle)
write_png('icon-savy.png', savy, 4)

# ---- launcher.ico (Savy S, several sizes; BMP-encoded below 256 for old tools) --
def bmp_entry(n, rows):
    px = b''.join(bytes([r[i + 2], r[i + 1], r[i], r[i + 3]]) for r in reversed(rows) for i in range(0, len(r), 4))
    mask = bytes(((n + 31) // 32) * 4 * n)
    return struct.pack('<IiiHHIIiiII', 40, n, 2 * n, 1, 32, 0, len(px) + len(mask), 0, 0, 0, 0) + px + mask

sizes = [16, 24, 32, 48, 256]
imgs = [(png_bytes(s, render(s, savy, 4)) if s == 256 else bmp_entry(s, render(s, savy, 4))) for s in sizes]
ico = struct.pack('<HHH', 0, 1, len(sizes))
off = 6 + 16 * len(sizes)
for s, d in zip(sizes, imgs):
    ico += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(d), off)
    off += len(d)
with open(os.path.join(OUT, 'launcher.ico'), 'wb') as f:
    f.write(ico + b''.join(imgs))
print('wrote launcher.ico')
