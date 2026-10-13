# -*- coding: utf-8 -*-
"""把所有图标拼成一张预览图，方便在游戏外先看一眼效果。

原版 32 张现在也走同一套外框了，所以预览分成
  第一段「本轮新画 / 改画」—— 15 轮新增和改材质的
  第二段「原版重绘」        —— 只换外框、画面内容保持原作者的那 32 张
  第三段「实机大小」        —— 缩到弹窗里的真实尺寸再放大，看小尺寸下认不认得出
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

SRC = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "Graphics", "Atlases", "Gui", "techpopups", "icons"))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "..", "图标预览.png")

# 本轮新画（含 Corner Kick / Ceiling Pop / Grounded Ultra Cancel 三张新的），
# 以及 Crouch Dash —— 它是 Demodash 的复制件（设置里两个名字同一技巧）。
NEW = [
    "Demodash", "Crouch Dash",
    "Wallboost", "Crouch Climb", "Retention Tech",
    "Corner Kick", "Ceiling Pop",
    "Jelly Regrab", "Theo Regrab", "Jellyvator", "Theovator",
    "Fast Bubble", "Cassette Block Boost", "Water Boost",
    "Featherboost", "Neutral Drop", "Explosion Boost",
    "Grounded Ultra", "Double Block Boost",
]

# 原版 32 张，本轮只换外框
ORIGINAL = [
    "Archie", "Backboost", "Bunnyhop", "Core Hyper", "Core Super",
    "Cornerboost", "Delayed Wallbounce", "Demohyper", "Dream Double Jump",
    "Dream Grab", "Dream Hyper", "Dream Jump", "Dream Smuggle", "Dream Super",
    "Hyper", "Neutral Jump", "Overclock", "Reform Tech", "Reverse Core Hyper",
    "Reverse Core Super", "Reverse Demohyper", "Reverse Dream Hyper",
    "Reverse Dream Super", "Reverse Hyper", "Reverse Super", "Reverse Wavedash",
    "Spike Tech", "Super", "Transition Dash", "Ultra", "Wallbounce", "Wavedash",
]

# 实机大小那一段挑几张：最密的（物件+箭头）、最素的、以及三张新的
ACTUAL_PICK = [
    "Jelly Regrab", "Neutral Drop", "Cassette Block Boost",
    "Corner Kick", "Ceiling Pop", "Grounded Ultra",
    "Hyper", "Wallbounce", "Dream Smuggle", "Featherboost",
]

COLS = 5
CELL_W, CELL_H = 232, 214
ICON = 156
PAD = 22
HEADER = 78
SECTION = 48
GAP = 18

# 游戏里的实际大小：字号 64 的图集，弹窗按 0.55 * 0.73 缩放，
# 行高 76 -> textSize.Y ≈ 30.5，图标方块就是 30.5 x 30.5 逻辑像素。
ACTUAL = 30
ACTUAL_Z = 5


def font(size, bold=True):
    names = ("msyhbd.ttc", "msyh.ttc") if bold else ("msyh.ttc", "msyhbd.ttc")
    for name in names:
        p = os.path.join(r"C:\Windows\Fonts", name)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    for name in ("arialbd.ttf", "segoeui.ttf"):
        p = os.path.join(r"C:\Windows\Fonts", name)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def actual_strip(names):
    cells = []
    for name in names:
        p = os.path.join(SRC, name + ".png")
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGBA")
        im = im.resize((ACTUAL, ACTUAL), Image.LANCZOS)
        im = im.resize((ACTUAL * ACTUAL_Z, ACTUAL * ACTUAL_Z), Image.NEAREST)
        cells.append((name, im))
    return cells


def main():
    sections = [
        ("本轮新画 / 改画（%d 张）" % len(NEW), NEW),
        ("原版重绘（%d 张 · 只换外框，画面内容未动）" % len(ORIGINAL), ORIGINAL),
    ]

    strip = actual_strip(ACTUAL_PICK)
    rows_total = sum(math.ceil(len(g) / COLS) for _, g in sections)
    strip_rows = math.ceil(len(strip) / COLS)

    w = COLS * CELL_W + PAD * 2
    h = (HEADER
         + rows_total * CELL_H
         + SECTION * (len(sections) + 1)
         + strip_rows * (ACTUAL * ACTUAL_Z + 34)
         + PAD * 2)
    canvas = Image.new("RGB", (w, h), (22, 24, 30))
    d = ImageDraw.Draw(canvas)

    d.text((PAD, 18), "TechPopups  ·  技巧图标总览", font=font(30),
           fill=(232, 236, 245))
    d.text((PAD, 54),
           "51 张图标同一套外框：彩色盘面 → 暗缝 → 135° 金属斜面 → 描边，"
           "斜面左上角一道高光",
           font=font(15, False), fill=(140, 148, 163))

    label = font(15)
    sechead = font(19)
    y = HEADER
    for title, group in sections:
        d.text((PAD, y + 12), title, font=sechead, fill=(255, 212, 55))
        y += SECTION
        for i, name in enumerate(group):
            r, c = divmod(i, COLS)
            cx = PAD + c * CELL_W
            cy = y + r * CELL_H

            path = os.path.join(SRC, name + ".png")
            if not os.path.exists(path):
                d.text((cx, cy), "缺 " + name, font=label, fill=(255, 90, 90))
                continue
            icon = Image.open(path).convert("RGBA").resize((ICON, ICON),
                                                           Image.LANCZOS)
            canvas.paste(icon, (cx + (CELL_W - ICON) // 2, cy), icon)
            tw = d.textlength(name, font=label)
            d.text((cx + (CELL_W - tw) / 2, cy + ICON + 6), name, font=label,
                   fill=(206, 213, 225))
        y += math.ceil(len(group) / COLS) * CELL_H + GAP

    # 实机大小一段
    d.text((PAD, y + 12),
           "实机大小（约 %d 像素，放大 %d 倍看）" % (ACTUAL, ACTUAL_Z),
           font=sechead, fill=(255, 212, 55))
    y += SECTION
    for i, (name, im) in enumerate(strip):
        r, c = divmod(i, COLS)
        cx = PAD + c * CELL_W
        cy = y + r * (ACTUAL * ACTUAL_Z + 34)
        canvas.paste(im, (cx + (CELL_W - im.width) // 2, cy), im)
        tw = d.textlength(name, font=label)
        d.text((cx + (CELL_W - tw) / 2, cy + im.height + 4), name,
               font=label, fill=(206, 213, 225))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    canvas.save(OUT)
    print("saved", OUT, canvas.size)


if __name__ == "__main__":
    main()
