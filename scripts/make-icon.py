#!/usr/bin/env python3
"""Generate MPLC.ico — a multi-resolution Windows icon.

Pure stdlib (zlib only): draws an anti-aliased orange "play" mark on a
dark rounded card, rasterizes at several sizes, encodes each as PNG, and
packs them into a single .ico (PNG-in-ICO, supported by Windows Vista+).

Run:  python3 scripts/make-icon.py   (writes src/MyPlayer/MPLC.ico)
"""
import struct
import zlib
import os

SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling factor for anti-aliasing

# Palette (matches the app's dark UI + orange accent #FF8800)
BG_TOP = (255, 150, 40)     # light orange
BG_BOT = (214, 96, 0)       # deep orange
TRI = (255, 255, 255)       # white play triangle
TRI_SHADOW = (170, 60, 0)   # subtle shadow under the triangle
CORNER_RADIUS = 0.22       # rounded-corner radius as a fraction of size


def rounded_rect_alpha(x, y, size, radius):
    """Coverage (0..1) of a rounded-rect at pixel center (x,y)."""
    half = size / 2.0
    cx, cy = size / 2.0, size / 2.0
    # Work in a normalized [-1,1] space
    px = (x - cx) / half
    py = (y - cy) / half
    # Half-extents of inner rounded rect in normalized space
    rx = 1.0 - radius
    ry = 1.0 - radius
    # Distance to inner rect corner
    dx = abs(px) - rx
    dy = abs(py) - ry
    if dx <= 0 or dy <= 0:
        return 1.0
    dist = (dx * dx + dy * dy) ** 0.5
    if dist == 0:
        return 1.0
    # Edge at dist == radius; 1px feather
    feather = radius / max(1, 2.0)
    cov = (radius - dist) / feather + 0.5
    return max(0.0, min(1.0, cov))


def triangle_alpha(x, y, size):
    """Coverage of a right-pointing play triangle centered in the card."""
    half = size / 2.0
    # Triangle in normalized coords, shifted slightly right (optical center)
    px = (x - half) / half
    py = (y - half) / half
    # Vertices of the play triangle (apex right)
    ax, ay = 0.62, 0.0        # apex
    bx, by = -0.46, -0.60     # top-left
    cx, cy = -0.46, 0.60      # bottom-left

    def sign(p1x, p1y, p2x, p2y, p3x, p3y):
        return (p1x - p3x) * (p2y - p3y) - (p2x - p3x) * (p1y - p3y)

    d1 = sign(px, py, ax, ay, bx, by)
    d2 = sign(px, py, bx, by, cx, cy)
    d3 = sign(px, py, cx, cy, ax, ay)
    has_neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    has_pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    return 0.0 if (has_neg and has_pos) else 1.0


def render(size):
    """Return RGBA bytearray (row-major, top-down) at the given size."""
    big = size * SS
    buf = bytearray(big * big * 4)
    # Draw a drop shadow ring just outside the card for depth
    for yy in range(big):
        for xx in range(big):
            # map supersampled pixel center to logical coords
            lx = (xx + 0.5) / SS
            ly = (yy + 0.5) / SS
            cov = rounded_rect_alpha(lx, ly, size, CORNER_RADIUS)
            # vertical gradient
            t = ly / size
            r = int(BG_TOP[0] + (BG_BOT[0] - BG_TOP[0]) * t)
            g = int(BG_TOP[1] + (BG_BOT[1] - BG_TOP[1]) * t)
            b = int(BG_TOP[2] + (BG_BOT[2] - BG_TOP[2]) * t)

            # triangle with a thin shadow offset down-right
            tsh = triangle_alpha(lx - 0.02, ly + 0.02, size)
            tri = triangle_alpha(lx, ly, size)
            # shadow behind triangle
            if tsh > 0:
                sc = tsh * 0.35
                r = int(r * (1 - sc) + TRI_SHADOW[0] * sc)
                g = int(g * (1 - sc) + TRI_SHADOW[1] * sc)
                b = int(b * (1 - sc) + TRI_SHADOW[2] * sc)
            # main triangle
            if tri > 0:
                r = int(r * (1 - tri) + TRI[0] * tri)
                g = int(g * (1 - tri) + TRI[1] * tri)
                b = int(b * (1 - tri) + TRI[2] * tri)

            a = int(255 * cov)
            off = (yy * big + xx) * 4
            buf[off] = r & 0xFF
            buf[off + 1] = g & 0xFF
            buf[off + 2] = b & 0xFF
            buf[off + 3] = a

    # Box-downsample big -> size
    out = bytearray(size * size * 4)
    for y in range(size):
        for x in range(size):
            rs = gs = bs = asum = 0
            for dy in range(SS):
                for dx in range(SS):
                    o = ((y * SS + dy) * big + (x * SS + dx)) * 4
                    rs += buf[o]; gs += buf[o + 1]; bs += buf[o + 2]; asum += buf[o + 3]
            n = SS * SS
            oo = (y * size + x) * 4
            out[oo] = rs // n
            out[oo + 1] = gs // n
            out[oo + 2] = bs // n
            out[oo + 3] = asum // n
    return out


def png_encode(size, rgba):
    """Encode RGBA bytes into a PNG (color type 6, 8-bit)."""
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    # add filter byte 0 to each scanline
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]
    idat = zlib.compress(bytes(raw), 9)
    return sig + chunk(b"IHDR", ihdr) + chunk(b"IDAT", idat) + chunk(b"IEND", b"")


def build_ico(path):
    images = []
    for s in SIZES:
        images.append((s, png_encode(s, render(s))))

    # ICONDIR
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    dir_entries = b""
    body = b""
    for size, data in images:
        w = 0 if size >= 256 else size  # 256 is encoded as 0
        dir_entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        body += data
        offset += len(data)

    with open(path, "wb") as f:
        f.write(header + dir_entries + body)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "src", "MyPlayer", "MPLC.ico")
    out = os.path.normpath(out)
    build_ico(out)
    print("wrote", out, os.path.getsize(out), "bytes")
