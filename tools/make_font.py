# -*- coding: utf-8 -*-
r"""Generate a Celeste (BMFont XML) font atlas from HarmonyOS Sans SC Bold.

Output layout expected by Everest:
    <Mod>/Dialog/Fonts/techpopups.fnt
    <Mod>/Dialog/Fonts/techpopups_0.png

Usage (any Python 3 with Pillow):
    python tools/make_font.py

and on Windows, either shell works too:
    python tools\make_font.py

Tunables below are the only things you normally want to touch.
"""
import os
import struct
from PIL import Image, ImageDraw, ImageFont

# --- tunables -------------------------------------------------------------
# Where to look for HarmonyOS Sans SC Bold. User-level installs live under
# %LOCALAPPDATA%, system-level under C:\Windows\Fonts.
SRC_FONT_CANDIDATES = [
    r"C:\Windows\Fonts\HarmonyOS_Sans_SC_Bold.ttf",
    os.path.join(os.environ.get("LOCALAPPDATA", ""),
                 r"Microsoft\Windows\Fonts\HarmonyOS_Sans_SC_Bold.ttf"),
    r"C:\Windows\Fonts\HarmonyOS_Sans_SC_Black.ttf",
    os.path.join(os.environ.get("LOCALAPPDATA", ""),
                 r"Microsoft\Windows\Fonts\HarmonyOS_Sans_SC_Black.ttf"),
    r"C:\Windows\Fonts\msyhbd.ttc",          # fallback heavies already on the box
    r"C:\Windows\Fonts\Noto Sans SC Bold (TrueType).otf",
]
FONT_SIZE = 64          # must match <info size="">; vanilla renogare64 uses 64
FACE = "techpopups"
START = 32              # printable ASCII 32..126 covers every tech name
END = 126
ATLAS_W = 1024
GLYPH_GAP = 4           # padding between glyph tiles (avoids texture bleeding)
EXPAND = 0              # extra bold simulated by drawing offsets (0 = off)
# --------------------------------------------------------------------------

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT_DIR = os.path.join(ROOT, "Dialog", "Fonts")
OUT_FNT = os.path.join(OUT_DIR, FACE + ".fnt")
OUT_PNG = os.path.join(OUT_DIR, FACE + "_0.png")

CHARS = [chr(c) for c in range(START, END + 1)]


def build_glyphs():
    font = ImageFont.truetype(SRC_FONT, FONT_SIZE)
    ascent, descent = font.getmetrics()
    base = int(ascent)
    line_height = int(ascent + descent)

    pen = 80  # slack so negative bearings still land inside the scratch image
    scratch = max(pen * 2, FONT_SIZE * 3)

    glyphs = []
    for ch in CHARS:
        img = Image.new("RGBA", (scratch, scratch), (0, 0, 0, 0))
        draw = ImageDraw.Draw(img)
        if EXPAND > 0:
            for dx in range(-EXPAND, EXPAND + 1):
                for dy in range(-EXPAND, EXPAND + 1):
                    if dx == 0 and dy == 0:
                        continue
                    draw.text((pen + dx, pen + base + dy), ch, font=font,
                              fill=(255, 255, 255, 255), anchor="ls")
        draw.text((pen, pen + base), ch, font=font,
                  fill=(255, 255, 255, 255), anchor="ls")

        bbox = img.getbbox()
        advance = int(round(font.getlength(ch)))

        if bbox is None:
            # Whitespace: no ink. Vanilla gives it a 1x1 dummy tile.
            tile = Image.new("RGBA", (1, 1), (255, 255, 255, 0))
            glyphs.append(dict(ch=ch, tile=tile, xoffset=0,
                               yoffset=line_height - 1, advance=advance))
            continue

        tile = img.crop(bbox)
        glyphs.append(dict(ch=ch, tile=tile,
                           xoffset=bbox[0] - pen,
                           yoffset=bbox[1] - pen,
                           advance=advance))

    return font, glyphs, base, line_height


def pack(glyphs):
    x, y, row_h = GLYPH_GAP, 0, 0
    placed = []
    for g in glyphs:
        w, h = g["tile"].size
        w = max(w, 1)
        h = max(h, 1)
        if x + w + GLYPH_GAP > ATLAS_W:
            x = GLYPH_GAP
            y += row_h + GLYPH_GAP
            row_h = 0
        placed.append((g, x, y))
        x += w + GLYPH_GAP
        row_h = max(row_h, h)
    atlas_h = 1
    while atlas_h < y + row_h + GLYPH_GAP:
        atlas_h *= 2
    return placed, atlas_h


def write_atlas(placed, atlas_h):
    atlas = Image.new("RGBA", (ATLAS_W, atlas_h), (255, 255, 255, 0))
    for g, gx, gy in placed:
        atlas.paste(g["tile"], (gx, gy), g["tile"])
    os.makedirs(OUT_DIR, exist_ok=True)
    atlas.save(OUT_PNG, "PNG", optimize=True)
    return atlas.size


def write_bmfont(placed, atlas_size, line_height, base):
    aw, ah = atlas_size
    lines = []
    lines.append('<?xml version="1.0"?>')
    lines.append('<font>')
    lines.append('  <info face="%s" size="%d" bold="1" italic="0" charset="" '
                 'unicode="1" stretchH="100" smooth="1" aa="4" '
                 'padding="0,0,0,0" spacing="1,1" outline="0"/>'
                 % (FACE, FONT_SIZE))
    lines.append('  <common lineHeight="%d" base="%d" scaleW="%d" scaleH="%d" '
                 'pages="1" packed="0" alphaChnl="0" redChnl="4" greenChnl="4" '
                 'blueChnl="4"/>' % (line_height, base, aw, ah))
    lines.append('  <pages>')
    lines.append('    <page id="0" file="%s_0.png" />' % FACE)
    lines.append('  </pages>')
    lines.append('  <chars count="%d">' % len(placed))
    for g, gx, gy in placed:
        w, h = g["tile"].size
        lines.append(
            '    <char id="%d" x="%d" y="%d" width="%d" height="%d" '
            'xoffset="%d" yoffset="%d" xadvance="%d" page="0" chnl="15" />'
            % (ord(g["ch"]), gx, gy, max(w, 1), max(h, 1),
               g["xoffset"], g["yoffset"], g["advance"]))
    lines.append('  </chars>')
    lines.append('</font>')
    text = "\r\n".join(lines) + "\r\n"
    with open(OUT_FNT, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def pick_font():
    for p in SRC_FONT_CANDIDATES:
        if p and os.path.exists(p):
            return p
    raise SystemExit("no source font found; tried:\n  " + "\n  ".join(
        p for p in SRC_FONT_CANDIDATES if p))


def main():
    global SRC_FONT
    SRC_FONT = pick_font()
    print("using font: %s" % SRC_FONT)
    font, glyphs, base, line_height = build_glyphs()
    placed, atlas_h = pack(glyphs)
    size = write_atlas(placed, atlas_h)
    write_bmfont(placed, size, line_height, base)
    print("done")
    print("  glyphs        : %d (%d..%d)" % (len(glyphs), START, END))
    print("  lineHeight/%s: %d" % ("base", line_height))
    print("  base          : %d" % base)
    print("  atlas         : %dx%d" % size)
    print("  out fnt       : %s (%.1f KB)" % (OUT_FNT, os.path.getsize(OUT_FNT) / 1024.0))
    print("  out png       : %s (%.1f KB)" % (OUT_PNG, os.path.getsize(OUT_PNG) / 1024.0))


if __name__ == "__main__":
    main()
