# -*- coding: utf-8 -*-
"""生成 TechPopups 的技巧图标。

规格是从原版图标里量出来的：
  576x576 像素 = 32x32 格，每格 18px
  中心是一个符号，深色外圈 + 底色圆盘

配色是从原版 PNG 直接采样出来的（见 Hyper.png / Wallbounce.png）：
  金  (255,212,55) / (198,161,24) / (140,114,17)
  墙  (70,70,70) 面 + (42,42,42) 影 + (0,0,0) 描边

设计原则（2026-10-12 第四次改版）：
  1. **三种材质**，一个图标里谁的活儿谁用谁的颜色：
       gold  —— 手里的物件、主角相关的东西（水母、水晶、泡泡…）
       stone —— 场景里的固体（墙、方块、节奏块、地面、刺）
       flow  —— 运动和方向（箭头、波纹）
  2. **箭头什么时候变青色**：单独一支箭头（Super / Hyper / Ultra…）保持金色，
     因为画面里没别的东西跟它抢；但箭头旁边只要有 gold 的物件，
     就换成 flow 青色 —— 否则两块金色会糊成一坨。这条来自实际使用反馈：
     「箭头和部分元素也撞色了，分不太开（比如 jelly regrab）」。
  3. **每个部件都有自己的光暗面**。沿 135°（左上打光）把部件自身的包围盒
     切成亮 / 中 / 暗三段，再把最上一行提亮、最下一行压暗。
  4. **外框重做**。旧外框是一圈扁平单色灰环，30 像素下就是个瓶盖。现在分成
     四层：彩色盘面 → 一圈很暗的缝隙（盘面看起来是嵌进去的）→ 带 135° 渐变的
     金属斜面 → 更暗的外轮廓；斜面的左上角再点一道高光弧。

  血泪教训（保留）：`m_arrow` 的头部半宽公式原来写成
  `headw * (1.0 + u / head)`，u=0 是尖端 —— 尖端最宽、底边收成一点，
  每支箭头看起来都指向反方向。这个 bug 连着返工了两轮，
  根因全在这一行。

  原版那 32 张图标：`save_original_restyled()` 只换外框，画面内容一个像素不动。
  （本来想连符号也重画，试的两版提取器都不可靠 —— 详见那个函数的文档注释。
  「只换外框」是唯一能逐张肉眼复核的方案。）原始 PNG 备份在 tools/orig_icons/。
  加上新画的 15 张，整个 mod 的 47 张图标现在是同一套外框。

跑法：
  python make_icons.py
输出到 ../Graphics/Atlases/Gui/techpopups/icons/
"""
import math
import os
import shutil

from PIL import Image

G = 32           # 网格边长（格）
C = 18           # 每格像素
S = G * C        # 576

INK = (92, 107, 108)

PLATE_DASH = (211, 70, 71)
PLATE_ULTRA = (142, 99, 196)
PLATE_DASHLESS = (222, 196, 80)
PLATE_ENTITY = (86, 140, 180)
PLATE_WALL = (90, 95, 105)

# 部件配色。hi/mid/lo 是沿光轴的三段，line 是描边，gloss 是钉死的高光色。
PAL = {
    "gold":  {"hi": (255, 212, 55), "mid": (198, 161, 24),
              "lo": (140, 114, 17), "line": (108, 86, 10),
              "gloss": (255, 240, 150)},
    "stone": {"hi": (224, 224, 218), "mid": (170, 168, 162),
              "lo": (110, 108, 106), "line": (20, 20, 24),
              "gloss": (255, 255, 255)},
    # 运动 / 方向。选青色是因为它在五种盘面色（红黄蓝紫板岩）上都能站得住，
    # 而且和金色、石灰色都不撞。
    "flow":  {"hi": (170, 246, 236), "mid": (96, 206, 200),
              "lo": (44, 142, 152), "line": (16, 62, 80),
              "gloss": (230, 255, 252)},
}

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(
    HERE, "..", "Graphics", "Atlases", "Gui", "techpopups", "icons"))
ORIG = os.path.join(HERE, "orig_icons")


# ---------------------------------------------------------------- 光栅工具

def seg_dist(px, py, x0, y0, x1, y1):
    dx, dy = x1 - x0, y1 - y0
    l2 = dx * dx + dy * dy
    if l2 == 0:
        return math.hypot(px - x0, py - y0)
    t = max(0.0, min(1.0, ((px - x0) * dx + (py - y0) * dy) / l2))
    return math.hypot(px - (x0 + t * dx), py - (y0 + t * dy))


def new_mask():
    return [[False] * G for _ in range(G)]


def m_line(m, x0, y0, x1, y1, thick=2.0):
    """普通粗线（没有箭头）。"""
    for gy in range(G):
        for gx in range(G):
            if seg_dist(gx + 0.5, gy + 0.5, x0, y0, x1, y1) <= thick * 0.5:
                m[gy][gx] = True


def m_arrow(m, x0, y0, x1, y1, thick=2.6, head=6.0, headw=4.4):
    """一支箭头，头在 (x1,y1)。

    头部半宽是 `headw * (-u / head)`：u=0（尖端）时为 0，u=-head（底边）时
    为 headw。写反过一次，全套箭头都会指向反方向。
    """
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1.0
    ux, uy = dx / L, dy / L

    # 箭杆收在头部底边以内，免得杆端的半圆头盖在尖端上（30 像素下就是钝头）。
    back = head * 0.82
    if L > back:
        m_line(m, x0, y0, x1 - ux * back, y1 - uy * back, thick)
    else:
        m_line(m, x0, y0, x1, y1, thick)

    for gy in range(G):
        for gx in range(G):
            cx, cy = gx + 0.5 - x1, gy + 0.5 - y1
            u = cx * ux + cy * uy           # 0 = 尖端, -head = 底边
            v = -cx * uy + cy * ux
            if -head <= u <= 0.0 and abs(v) <= headw * (-u / head):
                m[gy][gx] = True


def m_disc(m, cx, cy, r):
    for gy in range(G):
        for gx in range(G):
            if math.hypot(gx + 0.5 - cx, gy + 0.5 - cy) <= r:
                m[gy][gx] = True


def m_quadrant(m, cx, cy, r, a0, a1):
    """以 (cx,cy) 为圆心的扇形块。角度是屏幕坐标（y 向下）：0=右 90=下 180=左 270=上。

    用来画 Cornerboost 那种「缺了一角的圆盘」—— 原版右下角那块深色圆角就是它。
    """
    for gy in range(G):
        for gx in range(G):
            px, py = gx + 0.5 - cx, gy + 0.5 - cy
            if math.hypot(px, py) > r:
                continue
            a = math.degrees(math.atan2(py, px))
            while a < a0:
                a += 360.0
            if a <= a1:
                m[gy][gx] = True


def m_roundrect(m, x0, y0, x1, y1, r):
    for gy in range(G):
        for gx in range(G):
            px, py = gx + 0.5, gy + 0.5
            qx = max(x0 + r - px, px - (x1 - r), 0.0)
            qy = max(y0 + r - py, py - (y1 - r), 0.0)
            if x0 <= px <= x1 and y0 <= py <= y1 and math.hypot(qx, qy) <= r:
                m[gy][gx] = True


def m_pentagon(m, cx, cy, r, rot=-90.0):
    """正五边形（Theo 水晶）。rot=-90 让一个顶点朝上，跟游戏里的样子一致。"""
    pts = [(cx + r * math.cos(math.radians(rot + i * 72.0)),
            cy + r * math.sin(math.radians(rot + i * 72.0))) for i in range(5)]
    m_tri(m, pts[0], pts[1], pts[2])
    m_tri(m, pts[0], pts[2], pts[3])
    m_tri(m, pts[0], pts[3], pts[4])


def m_tail(m, x0, y0, x1, y1, w0, w1=0.0, bulge=1.0):
    """从 (x0,y0)（半宽 w0）锥形收到 (x1,y1)（半宽 w1）的尾巴。"""
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1.0
    ux, uy = dx / L, dy / L
    for gy in range(G):
        for gx in range(G):
            cx, cy = gx + 0.5 - x0, gy + 0.5 - y0
            u = cx * ux + cy * uy
            v = -cx * uy + cy * ux
            if 0.0 <= u <= L:
                t = u / L
                if abs(v) <= w0 + (w1 - w0) * (t ** bulge):
                    m[gy][gx] = True


def m_wave(m, x0, x1, y, amp, thick=1.8):
    for gx in range(int(round(x0)), int(round(x1)) + 1):
        yy = y + amp * math.sin((gx - x0) / max(1e-6, (x1 - x0)) * math.pi * 2.0)
        for gy in range(G):
            if abs(gy + 0.5 - yy) <= thick * 0.5:
                m[gy][gx] = True


def m_tri(m, p0, p1, p2):
    def side(a, b, p):
        return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
    for gy in range(G):
        for gx in range(G):
            p = (gx + 0.5, gy + 0.5)
            d1, d2, d3 = side(p0, p1, p), side(p1, p2, p), side(p2, p0, p)
            if (d1 >= 0 and d2 >= 0 and d3 >= 0) or (d1 <= 0 and d2 <= 0 and d3 <= 0):
                m[gy][gx] = True


def m_bell(m, cx, cy, r):
    """水母伞盖：上半圆。"""
    for gy in range(G):
        for gx in range(G):
            px, py = gx + 0.5, gy + 0.5
            if py <= cy and math.hypot(px - cx, py - cy) <= r:
                m[gy][gx] = True


def m_arc(m, cx, cy, r, thick=1.8, a0=180.0, a1=360.0):
    """一段普通的弧线 / 一小段圆环（震荡波、泡泡高光用）。"""
    for gy in range(G):
        for gx in range(G):
            px, py = gx + 0.5 - cx, gy + 0.5 - cy
            d = math.hypot(px, py)
            if abs(d - r) > thick * 0.5:
                continue
            a = math.degrees(math.atan2(py, px))
            while a < a0:
                a += 360.0
            if a0 <= a <= a1:
                m[gy][gx] = True


def m_and(a, b):
    """把 a 裁进 b 里（高光不能超出容器）。"""
    for gy in range(G):
        for gx in range(G):
            if a[gy][gx] and not b[gy][gx]:
                a[gy][gx] = False


def despeckle(m, keep_if_neighbors=2):
    """去掉孤立像素：原版图标提取出来的轮廓偶尔会带一两个杂点。"""
    for gy in range(G):
        for gx in range(G):
            if not m[gy][gx]:
                continue
            n = 0
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0:
                        continue
                    ny, nx = gy + dy, gx + dx
                    if 0 <= nx < G and 0 <= ny < G and m[ny][nx]:
                        n += 1
            if n < keep_if_neighbors:
                m[gy][gx] = False


def scale(c, f):
    return (max(0, min(255, int(c[0] * f))),
            max(0, min(255, int(c[1] * f))),
            max(0, min(255, int(c[2] * f))))


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return (int(a[0] + (b[0] - a[0]) * t),
            int(a[1] + (b[1] - a[1]) * t),
            int(a[2] + (b[2] - a[2]) * t))


def clamp01(v):
    return max(0.0, min(1.0, v))


# ---------------------------------------------------------------- 外框

# 盘面 / 缝隙 / 金属斜面 / 外轮廓 的半径。旧版是一圈扁平灰环（12.9 -> 15.0），
# 读起来像瓶盖；现在把盘面放大、环收薄，并在环内部做出光暗层次。
CLIP_R = 12.6
PLATE_R = 13.6
GAP_R = 14.1
BEZEL_R = 15.0
EDGE_R = 15.5


class Icon:
    def __init__(self, plate):
        self.plate = plate

    def build(self, parts):
        """parts = [(mask, kind[, tone]), ...]。kind 取 PAL 的键；
        可选第三项把整个部件的色调钉死（做高光用）。"""
        clipped = []
        union = new_mask()
        kindof = [[None] * G for _ in range(G)]

        for part in parts:
            mask, kind = part[0], part[1]
            fixed = part[2] if len(part) > 2 else None
            c = new_mask()
            for gy in range(G):
                for gx in range(G):
                    if mask[gy][gx] and math.hypot(gx + 0.5 - 16, gy + 0.5 - 16) <= CLIP_R:
                        c[gy][gx] = True
                        union[gy][gx] = True
                        kindof[gy][gx] = kind
            clipped.append((c, kind, fixed))

        grid = [[None] * G for _ in range(G)]

        # ---- 外框：盘面 -> 缝隙 -> 金属斜面 -> 外轮廓 --------------------
        for gy in range(G):
            for gx in range(G):
                cx, cy = gx + 0.5, gy + 0.5
                d = math.hypot(cx - 16, cy - 16)
                t = ((cx - 16) + (cy - 16)) / 26.0            # -1 .. 1
                if d <= PLATE_R:
                    f = 1.0 - 0.14 * (t + 1.0)                 # 1.00 -> 0.72
                    f *= 1.0 - 0.12 * clamp01((d - 9.4) / 4.2)
                    # 靠外的一圈再压暗，盘面看起来是嵌进框里的
                    f *= 1.0 - 0.32 * clamp01((d - (PLATE_R - 1.3)) / 1.3)
                    grid[gy][gx] = scale(self.plate, f)
                elif d <= GAP_R:
                    grid[gy][gx] = scale(INK, 0.24)            # 很暗的缝隙
                elif d <= BEZEL_R:
                    f = 1.0 - 0.52 * (t + 1.0)                 # 1.00 -> 0.48
                    col = scale(INK, f)
                    # 左上角一道高光弧，金属感全靠它
                    ang = math.degrees(math.atan2(cy - 16, cx - 16))
                    k = 1.0 - min(1.0, abs(ang + 135.0) / 46.0)
                    if k > 0.0:
                        col = mix(col, (232, 240, 244), 0.85 * k * k)
                    grid[gy][gx] = col
                elif d <= EDGE_R:
                    grid[gy][gx] = scale(INK, 0.28)            # 外轮廓

        # 描边：并集外扩一圈，颜色跟着最近的那个部件走
        for gy in range(G):
            for gx in range(G):
                if union[gy][gx]:
                    continue
                if math.hypot(gx + 0.5 - 16, gy + 0.5 - 16) > 13.1:
                    continue
                hit = None
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = gy + dy, gx + dx
                        if 0 <= nx < G and 0 <= ny < G and union[ny][nx]:
                            hit = kindof[ny][nx]
                            break
                    if hit:
                        break
                if hit:
                    grid[gy][gx] = PAL[hit]["line"]

        # 部件本体：沿左上 -> 右下切成亮 / 中 / 暗三段，
        # 再单独把最上一行提亮、最下一行压暗。
        for c, kind, fixed in clipped:
            pal = PAL[kind]
            xs = [gx for gy in range(G) for gx in range(G) if c[gy][gx]]
            ys = [gy for gy in range(G) for gx in range(G) if c[gy][gx]]
            if not xs:
                continue
            x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
            span = max(1.0, (x1 - x0) + (y1 - y0))

            for gy in range(y0, y1 + 1):
                for gx in range(x0, x1 + 1):
                    if not c[gy][gx]:
                        continue
                    if fixed:
                        grid[gy][gx] = pal[fixed]
                        continue

                    s = ((gx - x0) + (gy - y0)) / span
                    if s < 0.34:
                        tone = pal["hi"]
                    elif s < 0.72:
                        tone = pal["mid"]
                    else:
                        tone = pal["lo"]

                    above = gy > 0 and c[gy - 1][gx]
                    below = gy < G - 1 and c[gy + 1][gx]
                    if not above:
                        tone = pal["hi"]
                    elif not below:
                        tone = pal["lo"]
                    grid[gy][gx] = tone

        return grid

    def save(self, path, parts):
        grid = self.build(parts)
        im = Image.new("RGBA", (G, G), (0, 0, 0, 0))
        px = im.load()
        for gy in range(G):
            for gx in range(G):
                c = grid[gy][gx]
                if c is not None:
                    px[gx, gy] = (c[0], c[1], c[2], 255)
        im = im.resize((S, S), Image.NEAREST)
        im.save(path)
        return path


# ---------------------------------------------------------------- 材质简写

def gold(m):
    return (m, "gold")


def stone(m):
    return (m, "stone")


def flow(m):
    return (m, "flow")


# ---------------------------------------------------------------- 每个技巧

def demodash():
    """下蹲冲刺：一支普通的向右箭头，别的什么也不加（单支箭头 = 保持金色）。"""
    m = new_mask()
    m_arrow(m, 4.5, 16.0, 27.0, 16.0, 3.2, 6.8, 5.0)
    return [gold(m)]


def wall_bar(m, x=6.0, y0=3.0, y1=29.0, wdt=4.4):
    """一堵竖直的墙（超出圆面的部分会被裁掉，原版 Wallbounce 也是这么裁的）。"""
    m_roundrect(m, x, y0, x + wdt, y1, 1.0)


def wallboost():
    """踹墙跳：石墙 + 青色箭头（金色箭头会跟石墙糊在一起，所以用 flow）。"""
    w = new_mask()
    wall_bar(w)
    a = new_mask()
    m_line(a, 11.2, 22.8, 17.6, 16.4, 2.9)             # 贴着墙往上蹬
    m_arrow(a, 16.8, 15.8, 26.6, 15.8, 2.9, 6.4, 4.8)  # 折成水平蹬出去
    return [stone(w), flow(a)]


def crouch_climb():
    """蹲着抓墙：石墙 + 一支贴着墙笔直向上的青色细箭头。"""
    w = new_mask()
    wall_bar(w)
    a = new_mask()
    m_arrow(a, 16.0, 27.2, 16.0, 5.6, 2.1, 5.4, 3.8)
    return [stone(w), flow(a)]


def retention_tech():
    """速度保留：撞上障碍 -> 沿面滑上去，整段抬高之后继续冲。"""
    b = new_mask()
    m_roundrect(b, 14.0, 15.0, 24.0, 25.0, 1.0)          # 挡路的方块

    a = new_mask()
    m_line(a, 7.2, 24.8, 14.4, 17.6, 2.7)                # 撞上来的那截，停在方块左面
    m_arrow(a, 14.0, 12.0, 20.6, 4.8, 2.7, 5.6, 4.4)     # 抬高之后继续冲的那截
    return [stone(b), flow(a)]


def jelly_body(m, cx=15.5, cy=16.5, r=6.0):
    m_bell(m, cx, cy, r)
    for dx in (-3.6, 0.0, 3.6):
        m_line(m, cx + dx, cy + 0.5, cx + dx * 0.6, cy + 7.0, 1.5)


def theo_body(m, cx=15.5, cy=16.5, r=6.4):
    """Theo 水晶：正五边形（游戏里的 Theo 就是个五边形）。"""
    m_pentagon(m, cx, cy, r)


def jelly_regrab():
    """水母再抓：水母在右上角，左下角一支青色箭头指过去。

    金色箭头配金色物件就是会连成一块，这张是最典型的一张。
    """
    j = new_mask()
    jelly_body(j, 20.5, 10.5, 5.0)
    a = new_mask()
    m_arrow(a, 7.8, 24.6, 14.8, 17.6, 2.8, 5.6, 4.0)
    return [gold(j), flow(a)]


def jellyvator():
    """水母电梯：水母在上边，下边一支青色箭头往上指。"""
    j = new_mask()
    jelly_body(j, 16.0, 10.2, 5.4)
    a = new_mask()
    m_arrow(a, 16.0, 27.2, 16.0, 19.4, 2.9, 6.2, 4.6)
    return [gold(j), flow(a)]


def theo_regrab():
    """水晶再抓：五边形在右上角，左下角一支青色箭头指过去。"""
    t = new_mask()
    theo_body(t, 20.5, 10.5, 5.4)
    a = new_mask()
    m_arrow(a, 7.8, 24.6, 14.8, 17.6, 2.8, 5.6, 4.0)
    return [gold(t), flow(a)]


def theovator():
    """水晶电梯：五边形在上边，下边一支青色箭头往上指。"""
    t = new_mask()
    theo_body(t, 16.0, 10.2, 5.8)
    a = new_mask()
    m_arrow(a, 16.0, 27.2, 16.0, 19.4, 2.9, 6.2, 4.6)
    return [gold(t), flow(a)]


def fast_bubble():
    """快速泡泡：一个实心泡泡 + 一道亮金弧形高光。"""
    disc = new_mask()
    m_disc(disc, 16.0, 16.0, 10.2)

    gloss = new_mask()
    m_arc(gloss, 16.0, 16.0, 7.4, 3.0, 200.0, 274.0)
    m_and(gloss, disc)
    return [gold(disc), (gloss, "gold", "gloss")]


def cassette_boost():
    """节奏块加速：石块 + 上方一支带杆的青色向上箭头。"""
    b = new_mask()
    m_roundrect(b, 7.0, 19.5, 25.0, 24.5, 1.5)
    a = new_mask()
    m_arrow(a, 16.0, 17.2, 16.0, 4.4, 2.6, 6.2, 4.6)
    return [stone(b), flow(a)]


def water_boost():
    """水上跳：水面的波纹 + 一颗跳起来的水珠（单块图形，保持金色）。"""
    m = new_mask()
    m_wave(m, 3.0, 29.0, 25.5, 1.6, 1.9)
    m_wave(m, 5.0, 27.0, 22.0, 1.3, 1.5)
    m_disc(m, 16.0, 12.0, 3.1)                          # 水珠的下半
    m_tri(m, (16.0, 4.2), (12.9, 11.4), (19.1, 11.4))   # 水珠的上半
    return [gold(m)]


def featherboost():
    """羽毛加速：一颗彗星 —— 右上角一个圆头，向左下拖一条尾巴。"""
    m = new_mask()
    m_tail(m, 18.0, 14.0, 5.8, 26.2, 5.2, 0.8, 1.6)
    m_disc(m, 19.6, 12.4, 5.7)
    return [gold(m)]


def neutral_drop():
    """原地放下：上面一个金色方块，下面一支青色向下的箭头。"""
    b = new_mask()
    m_roundrect(b, 10.2, 4.6, 21.8, 15.2, 1.2)
    a = new_mask()
    m_arrow(a, 16.0, 17.6, 16.0, 27.2, 3.0, 6.4, 4.8)
    return [gold(b), flow(a)]


def puffer_boost():
    """河豚 / 弹球加速：三道向右扩散的震荡波弧线。"""
    m = new_mask()
    for r in (4.5, 11.0, 17.5):
        m_arc(m, 5.4, 16.0, r, 2.3, -34.0, 34.0)
    return [gold(m)]


# ---- 本轮新增的三张 -----------------------------------------------------

def corner_kick():
    """Corner Kick：在墙的**最底下一格**踢墙。

    墙画短一点，让它的下端露在画面里；箭头从那个墙角斜着出去。
    """
    w = new_mask()
    wall_bar(w, x=4.8, y0=3.0, y1=19.0, wdt=4.4)
    # 墙角下方留一小截，强调「踢的是底端」
    m_roundrect(w, 4.8, 19.0, 6.6, 22.4, 0.8)

    a = new_mask()
    m_arrow(a, 10.2, 23.4, 26.4, 9.6, 2.9, 6.2, 4.6)
    return [stone(w), flow(a)]


def ceiling_pop():
    """Ceiling Pop（cp）：斜下冲向一个墙角。

    第三版。第一版「短墙 + 近乎水平的箭头」跟 Corner Kick 分不开；第二版肘形不够直白；
    第三版把朝向摆对了、主体也居中了，但墙角在左边 —— 现在整张**左右镜像**：
    墙角在右侧，箭头从左上斜下指向它。主体按圆面中心重新摆过（重心 y≈15）。
    """
    w = new_mask()
    wall_bar(w, x=22.6, y0=7.6, y1=20.8, wdt=5.0)

    a = new_mask()
    m_arrow(a, 7.2, 10.0, 20.4, 19.6, 2.9, 6.4, 4.6)       # 从左上斜下指向墙角
    return [stone(w), flow(a)]


def dash_bounce():
    """Dash Bounce：一道斜下冲，接一个弹起来的拱。

    形状就是一根**斜下的冲**接一个**开口朝下的半圆**（像字母 n），
    两段连成一条轨迹、一个材质（flow 青）—— 它是「一段运动」，
    不是一个物件加一支箭头。
    """
    a = new_mask()
    m_line(a, 5.6, 9.2, 11.4, 15.0, 2.8)                  # 斜下冲：左上 -> 右下（45°）
    m_arc(a, 16.4, 15.0, 5.0, 2.8, 180.0, 360.0)          # 弹起来的拱（开口朝下）
    m_arrow(a, 21.4, 15.0, 25.2, 23.0, 2.8, 6.0, 4.4)     # 拱的这一头收在右下
    return [flow(a)]

def reverse_cornerboost():
    """Reverse Cornerboost（rcb）：斜上通过一个墙角。

    和 Corner Kick 同一套语言 —— 墙**从上往下**、下端（墙角）露在画面里，
    箭头从那个墙角斜上出去。差别只在**箭头更缓**：Corner Kick 那支约 40°，
    rcb 这支约 26°。
    """
    w = new_mask()
    wall_bar(w, x=3.4, y0=1.4, y1=17.2, wdt=4.8)

    a = new_mask()
    m_arrow(a, 9.0, 19.4, 26.0, 11.0, 2.9, 6.4, 4.6)      # 约 26°，从墙角斜上
    return [stone(w), flow(a)]

def _super_hyper_arrows(left_x):
    """「Super / Hyper」这几张成对条目共用的两支箭头。

    斜上那支是 Super（金），平前那支是 Hyper（青）。一金一青是为了能分开 ——
    同色、或者贴太近，30 像素下就是两块糊在一起的亮斑。
    `left_x` 是箭头起点，留了起跳面（果冻 / 岩浆块）的版本要往右让一截。
    """
    up = new_mask()
    m_arrow(up, left_x, 13.6, left_x + 8.4, 5.0, 2.5, 5.4, 4.0)      # Super：斜上

    hy = new_mask()
    m_arrow(hy, left_x, 20.6, left_x + 13.4, 20.6, 2.5, 5.4, 4.0)    # Hyper：平前
    return up, hy


def extended_super_hyper():
    """Extended Super / Hyper（充能Super / Hyper）：冲刺冷却在冲刺还没结束前就
    走完了，于是「跳出去」和「补回来的那一下冲刺」同时成立。

    这条不依赖任何实体，所以左边没有起跳面，箭头可以从最左边起。
    """
    up, hy = _super_hyper_arrows(6.6)
    return [gold(up), flow(hy)]


def dream_super_hyper():
    """Dream Super / Hyper（果冻Super / Hyper）：果冻给的两种起跳。

    左边一颗果冻（金），右边两支箭头。果冻往左下让开一点，免得和金色箭头
    糊成同一块亮斑（第一版就是这么糊掉的）。跟 Core Super / Hyper 完全对称，
    只有那个「起跳面」不一样 —— 并排看就是一句话：换个东西起跳。
    """
    j = new_mask()
    jelly_body(j, 7.4, 18.0, 3.4)

    up, hy = _super_hyper_arrows(14.0)
    return [gold(j), gold(up), flow(hy)]


def core_super_hyper():
    """Core Super / Hyper（岩浆块Super / Hyper）：岩浆块被顶起来时给的两种起跳。

    和 Dream Super / Hyper 同一个版式，只把果冻换成石色的岩浆块。
    """
    b = new_mask()
    m_roundrect(b, 4.8, 12.6, 12.0, 19.4, 1.4)             # 岩浆块

    up, hy = _super_hyper_arrows(14.0)
    return [stone(b), gold(up), flow(hy)]


def grounded_ultra_cancel():
    """Grounded Ultra Cancel：地面上斜下冲刺吃到 1.2 倍速度，然后抓住抓取物。

    地面（石）+ 斜下箭头（青）+ 手里那个物件（金）。
    """
    g = new_mask()
    m_roundrect(g, 3.0, 24.6, 29.0, 27.4, 1.0)           # 地面

    a = new_mask()
    m_arrow(a, 7.0, 8.0, 20.4, 22.0, 2.8, 6.0, 4.4)      # 斜下冲刺

    j = new_mask()
    jelly_body(j, 23.4, 9.0, 4.6)                        # 抓到的水母
    return [stone(g), flow(a), gold(j)]


def double_block_boost():
    """Double Block Boost：从移动方块（Kevin 机关）上跳起的加速能叠两次。

    石色的圆角方块（机关本体）+ 上下叠起来的两道青色向上箭头 —— 两道箭头就是
    「离开方块后 9 帧内再跳 / 再冲刺一次，还能再叠一次速度」。

    和 Cassette Block Boost 是同族画法（方块 + 向上箭头），差别在这张给两道
    箭头、方块也更方；配色照旧走 stone + flow 两个材质。
    """
    b = new_mask()
    m_roundrect(b, 10.8, 21.2, 21.2, 27.6, 1.8)          # 圆角机关方块

    a = new_mask()
    m_arrow(a, 16.0, 19.4, 16.0, 12.2, 2.8, 4.4, 4.2)    # 下面那道加速
    m_arrow(a, 16.0, 10.4, 16.0, 3.8, 2.8, 4.4, 4.2)     # 上面那道（再叠一次）
    return [stone(b), flow(a)]


TODO = [
    ("Demodash", PLATE_ULTRA, demodash),
    ("Wallboost", PLATE_WALL, wallboost),
    ("Crouch Climb", PLATE_WALL, crouch_climb),
    ("Retention Tech", PLATE_WALL, retention_tech),
    ("Jelly Regrab", PLATE_ENTITY, jelly_regrab),
    ("Theo Regrab", PLATE_ENTITY, theo_regrab),
    ("Jellyvator", PLATE_ENTITY, jellyvator),
    ("Theovator", PLATE_ENTITY, theovator),
    ("Fast Bubble", PLATE_ENTITY, fast_bubble),
    ("Cassette Block Boost", PLATE_ENTITY, cassette_boost),
    ("Water Boost", PLATE_ENTITY, water_boost),
    ("Featherboost", PLATE_ENTITY, featherboost),
    ("Neutral Drop", PLATE_ENTITY, neutral_drop),
    ("Explosion Boost", PLATE_ENTITY, puffer_boost),
    ("Corner Kick", PLATE_WALL, corner_kick),
    ("Ceiling Pop", PLATE_WALL, ceiling_pop),
    ("Grounded Ultra", PLATE_ENTITY, grounded_ultra_cancel),
    ("Double Block Boost", PLATE_ENTITY, double_block_boost),
    ("Dash Bounce", PLATE_DASH, dash_bounce),
    # 原来这张靠「去掉 reverse 前缀」复用 Cornerboost 的图，看不出是反向的，
    # 现在给它一张自己的（左边那个缺口圆角镜像过来）。
    ("Reverse Cornerboost", PLATE_DASHLESS, reverse_cornerboost),
    # 下面三张的文件名**故意不含斜杠**：面板/弹窗里这三个条目的名字带 " / "
    # （"Dream Super / Hyper"），而图标查找会把斜杠拍成空格再合并连续空格
    # （见 TechIconLoader.Variants 的 Collapse），所以文件名必须写成
    # "Dream Super Hyper.png" 才命中。写回去带斜杠反而找不到。
    ("Extended Super Hyper", PLATE_DASH, extended_super_hyper),
    ("Dream Super Hyper", PLATE_ENTITY, dream_super_hyper),
    ("Core Super Hyper", PLATE_ENTITY, core_super_hyper),
]

# 「Crouch Dash」和「Demodash」是同一个技巧的两个名字（设置里可切换），
# 所以两张图必须一模一样，直接复制。
COPIES = [
    ("Demodash", "Crouch Dash"),
]

# 上一版生成过、现在改成复用基础图的：删掉
STALE = [
    "Chained Ultra", "Delayed Ultra", "Mid-air Super",
    # 改名的残留：这张图还在，只是名字换成了 Explosion Boost（同一个技巧）
    "Pufferfish Bumper Boost",
]


# ---------------------------------------------------------------- 原版图标重绘

# 原版那 32 张。盘面色从原图里直接取，符号只要轮廓 —— 这样它们的造型一个没变，
# 变的是外框和光暗面。
ORIGINAL = [
    "Archie", "Backboost", "Bunnyhop", "Core Hyper", "Core Super",
    "Cornerboost", "Delayed Wallbounce", "Demohyper", "Dream Double Jump",
    "Dream Grab", "Dream Hyper", "Dream Jump", "Dream Smuggle", "Dream Super",
    "Hyper", "Neutral Jump", "Overclock", "Reform Tech", "Reverse Core Hyper",
    "Reverse Core Super", "Reverse Demohyper", "Reverse Dream Hyper",
    "Reverse Dream Super", "Reverse Hyper", "Reverse Super", "Reverse Wavedash",
    "Spike Tech", "Super", "Transition Dash", "Ultra", "Wallbounce", "Wavedash",
]


def save_original_restyled(name, path):
    """原版图标重绘：**画面内容一个像素都不动，只换外框。**

    本来想把符号轮廓自动提取出来、用新材质重画一遍。试了两版都不可靠：

      1. 「跟盘面色比远近」—— 原版盘面本身就是渐变，几百个格子几百种颜色，
         任何单一阈值都会把整块盘面判成符号，整组图标变成金饼。
      2. 「连通域漫水」—— Wallbounce / Bunnyhop 的墙贴着盘面外缘，
         把盘面劈成两半；只取最大连通块就漏一半，按面积或按外圈占比挑
         也会在别的图标上翻车（Symbol 一大就比盘面还占地方）。

    与其塞一个我没办法逐张肉眼复核的提取器进构建流程，不如只换外框：
    被指出的是外框，那就只动外框，画面内容保持原作者的水平不变。

    旧外圈（12.9 ~ 13.6）沿同一方向把盘面往外续上，接缝才看不出来。
    """
    src_path = os.path.join(ORIG, name + ".png")
    if not os.path.exists(src_path):
        return False
    im = Image.open(src_path).convert("RGBA")
    w = im.width // G
    src = [[im.getpixel((x * w + w // 2, y * w + w // 2))
            for x in range(G)] for y in range(G)]

    grid = [[None] * G for _ in range(G)]

    def sample(rr, ang):
        x = int(16.0 + rr * math.cos(ang))
        y = int(16.0 + rr * math.sin(ang))
        if 0 <= x < G and 0 <= y < G:
            c = src[y][x]
            if c[3] > 0:
                return c[:3]
        return None

    for gy in range(G):
        for gx in range(G):
            cx, cy = gx + 0.5, gy + 0.5
            d = math.hypot(cx - 16, cy - 16)
            t = ((cx - 16) + (cy - 16)) / 26.0

            if d <= 12.9:
                c = src[gy][gx]
                if c[3] > 0:
                    grid[gy][gx] = c[:3]
                continue

            if d <= PLATE_R:
                # 旧外圈那一圈：沿同一方向取盘面内侧的颜色续上来，
                # 再补一点边缘压暗，和 build() 里的盘面边沿接得上。
                base = sample(12.2, math.atan2(cy - 16, cx - 16))
                if base is None:
                    base = sample(11.0, math.atan2(cy - 16, cx - 16))
                if base is None:
                    continue
                f = 0.92 - 0.24 * clamp01((d - 12.9) / 0.7)
                grid[gy][gx] = scale(base, f)
                continue

            if d <= GAP_R:
                grid[gy][gx] = scale(INK, 0.24)
            elif d <= BEZEL_R:
                col = scale(INK, 1.0 - 0.52 * (t + 1.0))
                ang = math.degrees(math.atan2(cy - 16, cx - 16))
                k = 1.0 - min(1.0, abs(ang + 135.0) / 46.0)
                if k > 0.0:
                    col = mix(col, (232, 240, 244), 0.85 * k * k)
                grid[gy][gx] = col
            elif d <= EDGE_R:
                grid[gy][gx] = scale(INK, 0.28)

    out = Image.new("RGBA", (G, G), (0, 0, 0, 0))
    px = out.load()
    for gy in range(G):
        for gx in range(G):
            c = grid[gy][gx]
            if c is not None:
                px[gx, gy] = (c[0], c[1], c[2], 255)
    out.resize((S, S), Image.NEAREST).save(path)
    return True


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in STALE:
        p = os.path.join(OUT, name + ".png")
        if os.path.exists(p):
            os.remove(p)
            print("removed stale", name)

    made = []

    for name, plate, fn in TODO:
        Icon(plate).save(os.path.join(OUT, name + ".png"), fn())
        made.append(name)

    for name in ORIGINAL:
        if not save_original_restyled(name, os.path.join(OUT, name + ".png")):
            print("  ！缺少原版备份，跳过", name)
            continue
        made.append(name)

    for src, dst in COPIES:
        s = os.path.join(OUT, src + ".png")
        d = os.path.join(OUT, dst + ".png")
        if os.path.exists(s):
            shutil.copyfile(s, d)
            made.append(dst)

    print("generated %d icons into %s" % (len(made), OUT))
    for n in made:
        print("  ", n)


if __name__ == "__main__":
    main()
