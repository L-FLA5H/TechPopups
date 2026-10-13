# -*- coding: utf-8 -*-
"""生成一份自包含的技巧表（图标 + 名字 + 触发方式），用于评审图标。

数据来源：
  - Source/TechRegistry.cs   面板上的条目（顺序、分类、有没有判定）
  - Graphics/.../icons/*.png 图标
  - 下面的 TRIG / SAMPLE 两张表是人工维护的说明

用法：
  python make_tech_table.py
"""
import base64
import io
import os
import re

SRC = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), ".."))
ICONS = os.path.join(SRC, "Graphics", "Atlases", "Gui", "techpopups", "icons")
OUT = os.path.join(SRC, "..", "技巧表.html")   # 生成到哪里，按需改

THUMB = 128          # 卡片里图标的显示边长

# ── 触发方式 ────────────────────────────────────────────────────────────
# 都对着 Source/TechTracker.cs 里的实现写，一条一句话。
TRIG = {
    # ---------------- Dash ----------------
    "Super": "冲刺之后（没按住下键）起跳。代码里就是 Player.SuperJump 且没有 Ducking",
    "Hyper": "按住下键、在地面上起手冲刺，然后起跳（斜下冲刺接跳）",
    "Wavedash": "和 Hyper 是同一个动作，区别只在冲刺是在空中起手的",
    "Demohyper": "先蹲冲（Demodash），再接一次 Hyper",
    "Ultra": "斜下冲刺落地吃到 1.2 倍速度后起跳，速度还大于 240 px/s",
    "Chained Ultra": "两次 Ultra 之间隔得比 0.85 秒还短（连招）",
    "Wallbounce": "贴墙冲刺后踢墙跳（Player.SuperWallJump）",
    "Delayed Wallbounce": "踢墙的时候冲刺已经结束、但冲刺的余波还在（dashAttackTimer > 0）",
    "Crouch Dash": "按住下键冲刺。冲刺状态一结束就出框，不再等你跳",
    "Transition Dash": "切板的那一瞬间人在空中、冲刺数没满，之后把那个冲刺用掉",
    "Archie": "按住下键进入泡泡 / 助推状态（StBoost + 蹲）",
    "Mid-air Super": "冲刺起手在空中的 Super。默认关 —— 开着会顶掉普通 Super 的名字",
    "Extended Super / Hyper": "冲刺还没结束、冲刺数就已经回充了，此时起跳就是「延长」版",

    # ---------------- Dashless ----------------
    "Bunnyhop": "小跳（按住跳跃键不到 0.10 秒）落地后立刻再跳；在空中预输入也算",
    "Chained Bunnyhop": "两次 Bunnyhop 之间隔得比 0.85 秒还短",
    "Neutral Jump": "贴墙跳的时候方向键完全不按（MoveX == 0）",
    "Overclock": "地面爬墙跳时，按的方向和游戏给的 wallBoost 方向一致",
    "Cornerboost": "爬墙跳之后越过墙角、速度被游戏保留下来",
    "Reverse Cornerboost": "同上，但保留下来的速度方向和你当时的朝向相反",
    "Wallboost": "游戏自己的 wallBoosting 标志上升沿 —— 爬墙跳被折算成踢墙跳、体力返还",
    "Crouch Climb": "按住下键抓住墙",
    "Retention Tech": "墙跳的时候，游戏还在保留你碰墙之前的速度",
    "Corner Kick": "还没写判定：游戏里它就是一次普通的 WallJump，没有独立状态位",
    "Slow Falling": "还没写判定：两份 wiki 都没有这个条目，待确认具体指什么",
    "Spike Tech": "碰到刺但这一帧没死（穿刺）",

    # ---------------- Entity ----------------
    "Dream Jump": "从梦块冲出来之后起跳",
    "Dream Double Jump": "Dream Jump 之后 0.07 秒内又跳了一次 —— 这时改显示这个名字",
    "Dream Super / Hyper": "梦块快退给的 coyote 窗口内做 Super / Hyper",
    "Dream Grab": "从梦块冲刺状态直接转成抓墙",
    "Dream Smuggle": "梦冲刺的时候手里还拿着东西",
    "Core Super / Hyper": "被核心块摇开、coyote 窗口还在时做 Super / Hyper",
    "Reform Tech": "碰到正在复原（刚从虚变实）的平台",
    "Backboost": "朝背后扔出东西之后，立刻往回移动",
    "Neutral Drop": "按住「下」的同时松开抓键，东西原地放下而不是扔出去",
    "Explosion Boost": "被爆炸推出后、摇杆朝着被推的方向（这样才吃到那 1.2 倍）。普通撞一下不算",
    "Double Block Boost": "离开移动方块后 10 帧内再跳一次或冲刺一次（游戏里门槛是 100 px/s）",
    "Jelly Regrab": "冲刺的过程中抓住水母",
    "Theo Regrab": "冲刺的过程中抓住 Theo 水晶",
    "Jellyvator": "竖直向上冲刺时抓住水母",
    "Theovator": "竖直向上冲刺时抓住 Theo 水晶",
    "Fast Bubble": "待在泡泡里的时候按下冲刺（或蹲冲），提前从泡泡里射出去",
    "Cassette Block Boost": "脚下节奏块刷新的那一帧起跳（Jump 不能预输入）",
    "Featherboost": "羽毛真正开始移动的那一帧，按着的方向是斜的",

    # ---------------- Obscure ----------------
    # 面板里「冲刺类」和「冷门 / 待补」两项都叫 Grounded Ultra（默认分别是关 / 开）。
    # TRIG 按显示名查，所以两项共用这一句 —— 写全，别再写成「还没写判定」。
    "Grounded Ultra": "两项共用一个显示名。<b>冲刺类</b>那条（默认关）：冲刺起手那一瞬间人在（或刚离开）地面，只看冲刺开始那一刻。<b>冷门类</b>那条（默认开）：斜下冲刺落地吃到 uber 后 0.35 秒内抓住水母 / Theo 或踩到河豚顶；或在地面上按出斜下冲刺后的 0.6 秒内做同样的事",
    "Delayed Ultra": "从冲刺结束到落地之间隔得比阈值久（默认 35 厘秒）。默认关",
    "Dash Bounce": "还没写判定：要靠像素级位置或体力反推，误报率会很高",
    "Ceiling Pop": "还没写判定：游戏里的相关字段是编译期常量，一处引用都没有",
    "Bino Clip": "还没写判定：要靠像素级位置反推",
    "Water Boost": "从水面起跳 —— 用的就是游戏自己 SwimJumpCheck 的那一行",
    "Half Stamina Climb": "还没写判定：靠体力数值反推",
}

# ── 图标解析 ────────────────────────────────────────────────────────────
# 复刻 Source/TechIconLoader.cs：先按原名找，找不到就剥一层修饰前缀再找。
# SAMPLE 用来覆盖「面板上的标签」和「实际弹出的名字」不一致的那几条。
SAMPLE = {
    "Extended Super / Hyper": "Extended Super",
    "Dream Super / Hyper": "Dream Super",
    "Core Super / Hyper": "Core Super",
}

PREFIXES = ["extended ", "reverse ", "mid-air ", "chained ", "delayed ", "grounded "]


def collapse(s):
    while "  " in s:
        s = s.replace("  ", " ")
    return s


def resolve(name, files):
    """返回 (文件名, 剥了几层前缀)，找不到返回 (None, depth)。"""
    cur = name
    for depth in range(3):
        flat = collapse(cur.replace("/", " ")).strip()
        low = flat.lower()
        for cand in (flat, low, flat.replace(" ", "_"), low.replace(" ", "_")):
            for f in files:
                base = f[:-4]                       # 去掉 .png
                if base.lower() == cand.lower():
                    return f, depth
        nxt = None
        for p in PREFIXES:
            if cur.lower().startswith(p):
                nxt = cur[len(p):].strip()
                break
        if not nxt or nxt == cur:
            return None, depth
        cur = nxt
    return None, 3


# ── 生成 ────────────────────────────────────────────────────────────────
def main():
    files = sorted(f for f in os.listdir(ICONS) if f.lower().endswith(".png"))

    reg = open(os.path.join(SRC, "Source", "TechRegistry.cs"), encoding="utf-8").read()
    rows = re.findall(
        r'E\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*TechCategory\.(\w+)\s*,\s*(true|false)',
        reg)
    assert rows, "TechRegistry 一条都没解析到，锚点不匹配"

    CAT_CN = {"Dash": "冲刺类", "Dashless": "无冲刺类",
              "Entity": "实体类", "Obscure": "冷门 / 待补"}
    CAT_ORDER = ["Dash", "Dashless", "Entity", "Obscure"]

    cards = {}
    for tid, name, cat, impl in rows:
        impl = (impl == "true")
        sample = SAMPLE.get(name, name)
        fname, depth = resolve(sample, files)
        trig = TRIG.get(name, "（说明待补）")

        b64 = None
        if fname:
            try:
                from PIL import Image
            except ImportError:
                raise SystemExit("需要 Pillow：装到 python venv 里再跑")
            im = Image.open(os.path.join(ICONS, fname)).convert("RGBA")
            im = im.resize((THUMB, THUMB), Image.LANCZOS)
            buf = io.BytesIO()
            im.save(buf, "PNG", optimize=True)
            b64 = base64.b64encode(buf.getvalue()).decode("ascii")

        cards.setdefault(cat, []).append({
            "id": tid, "name": name, "impl": impl,
            "icon": fname, "b64": b64, "trig": trig, "depth": depth,
        })

    total = sum(len(v) for v in cards.values())
    done = sum(1 for v in cards.values() for c in v if c["impl"])
    n_icon = sum(1 for v in cards.values() for c in v if c["b64"])

    def card_html(c):
        if c["b64"]:
            img = ('<img src="data:image/png;base64,%s" alt="%s">'
                   % (c["b64"], c["name"]))
            filetxt = c["icon"] + ("" if c["depth"] == 0 else
                                   "（复用 %s 的图）" % c["icon"][:-4])
        else:
            img = '<div class="nopic">%s</div>' % c["name"][0].upper()
            filetxt = "没有图标，弹窗里画的是字母牌"

        cls = "" if c["impl"] else " todo"
        state = "" if c["impl"] else '<span class="tag off">无判定</span>'
        return (
            '<div class="card%s">'
            '<div class="ico">%s</div>'
            '<div class="body">'
            '<div class="name">%s%s</div>'
            '<div class="trig">%s</div>'
            '<div class="file">%s</div>'
            '</div></div>'
        ) % (cls, img, c["name"], state, c["trig"], filetxt)

    sections = []
    for cat in CAT_ORDER:
        items = cards.get(cat, [])
        if not items:
            continue
        sections.append(
            '<section><h2>%s <span class="count">%d 项</span></h2>'
            '<div class="grid">%s</div></section>'
            % (CAT_CN.get(cat, cat), len(items),
               "".join(card_html(c) for c in items)))

    html = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>TechPopups 技巧表</title>
<style>
  :root { color-scheme: dark; }
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 32px 28px 80px;
    background: #12141a; color: #e6e9f0;
    font: 15px/1.6 "Microsoft YaHei", "PingFang SC", system-ui, sans-serif;
  }
  header { max-width: 1180px; margin: 0 auto 8px; }
  h1 { font-size: 26px; margin: 0 0 6px; letter-spacing: .5px; }
  .sub { color: #8d95a6; font-size: 14px; }
  .sub b { color: #ffd437; font-weight: 600; }
  section { max-width: 1180px; margin: 34px auto 0; }
  h2 {
    font-size: 18px; margin: 0 0 14px; padding-bottom: 8px;
    border-bottom: 1px solid #262a35; display: flex; align-items: baseline; gap: 10px;
  }
  .count { font-size: 13px; color: #737b8c; font-weight: 400; }
  .grid {
    display: grid; gap: 14px;
    grid-template-columns: repeat(auto-fill, minmax(430px, 1fr));
  }
  .card {
    display: flex; gap: 16px; align-items: flex-start;
    background: #1b1e27; border: 1px solid #262b37; border-radius: 12px;
    padding: 14px 16px;
  }
  .card.todo { opacity: .62; border-style: dashed; }
  .ico {
    flex: 0 0 128px; width: 128px; height: 128px;
    display: flex; align-items: center; justify-content: center;
    background: #0e1015; border-radius: 10px;
  }
  .ico img { width: 128px; height: 128px; image-rendering: auto; display: block; }
  .nopic {
    width: 88px; height: 88px; border-radius: 14px;
    background: linear-gradient(160deg, #fdd23f, #d8a418); color: #1a1a1a;
    display: flex; align-items: center; justify-content: center;
    font-size: 46px; font-weight: 700;
  }
  .body { flex: 1 1 auto; min-width: 0; }
  .name {
    font-size: 17px; font-weight: 600; color: #fff;
    display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
  }
  .tag {
    font-size: 11px; font-weight: 500; padding: 2px 7px; border-radius: 99px;
    background: #3a3040; color: #c9a7ff; border: 1px solid #4b3d55;
  }
  .tag.off { background: #332b22; color: #d5a866; border-color: #4a3c2c; }
  .trig { color: #b9c0cf; font-size: 14px; margin-top: 7px; }
  .file { color: #5f6878; font-size: 12px; margin-top: 9px; }
  .hint {
    max-width: 1180px; margin: 22px auto 0; padding: 14px 18px;
    background: #1a1d26; border-left: 3px solid #ffd437; border-radius: 0 10px 10px 0;
    color: #a8b0bf; font-size: 14px;
  }
</style>
</head>
<body>
<header>
  <h1>TechPopups · 技巧表</h1>
  <div class="sub">
    共 <b>%d</b> 项，其中 <b>%d</b> 项已经有判定、<b>%d</b> 项有专属图标。
    图标按游戏里的实际大小（30×30 像素）挑过，这里放大了 4 倍看细节。
  </div>
</header>
<div class="hint">
  要改哪张图标，直接说名字就行（比如「Featherboost 的尾巴再长一点」）。
  下面每张卡片最后一行的灰字是图标文件名，「复用 XX 的图」表示它没有独立图、弹窗里借用的是另一张。
</div>
%s
</body>
</html>
""" % (total, done, n_icon, "".join(sections))

    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write(html)
    print("saved", OUT, "%.0f KB" % (os.path.getsize(OUT) / 1024.0))
    print("共 %d 项 / 有判定 %d / 有图标 %d" % (total, done, n_icon))
    miss = [c["name"] for v in cards.values() for c in v if not c["b64"]]
    print("没有图标的：", miss if miss else "无")


if __name__ == "__main__":
    main()
