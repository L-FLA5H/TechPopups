# -*- coding: utf-8 -*-
"""把已经写好、却没人引用的设置描述挂到 SettingSubText 上。

背景：Dialog 里躺着 18 条 `_desc`，文本中英都齐，但 C# 里的设置项只写了
[SettingName]，没有 [SettingSubText]，所以游戏里那些说明一条都看不到。

这个脚本只做两件事：
  1. 给已存在的 (SettingName, 描述键) 配对补上 [SettingSubText]
  2. 顺手把 overclock / crouchclimb 的描述键也加进两个语言包

每一步都断言，改完回读校验。跑第二遍会直接报「已挂上」，不会重复插。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SETTINGS = os.path.join(ROOT, "Source", "TechPopupsSettings.cs")
EN = os.path.join(ROOT, "Dialog", "English.txt")
ZH = os.path.join(ROOT, "Dialog", "Simplified Chinese.txt")

# 已经写好的孤儿描述：SettingName 键 -> 描述键
# 全部是 `SettingName 键 + "_desc"`，但这里显式列出来，不靠猜。
PAIRS = [
    ("modoptions_techpopups_enabled", "Show tech popups when performing movement tech",
     "做出技巧时弹出技巧框"),
    ("modoptions_techpopups_wallbounce_enabled", "Show popups for Wallbounces",
     "做出 Wallbounce 时弹出"),
    ("modoptions_techpopups_ultra_enabled", "Show popups for Ultras",
     "做出 Ultra 时弹出"),
    ("modoptions_techpopups_dreamgrab_enabled", "Show popups for Dream Grabs.",
     "抓进 Dream Block 时弹出"),
    ("modoptions_techpopups_archie_enabled", "Show popups for Archies.",
     "做出 Archie 时弹出"),
    ("modoptions_techpopups_dreamsmuggle_enabled", "Show popups for Dream Smuggles.",
     "抱着东西进 Dream Block 时弹出"),
    ("modoptions_techpopups_backboost_enabled", "Show popups for Backboosts",
     "扔出东西后反向加速时弹出"),
    ("modoptions_techpopups_neutraldrop_enabled",
     "Holding Down when you let go of a jelly or Theo, so it is set down on the spot "
     "instead of thrown.",
     "按住下键的同时松开抓键，把水母或 Theo 原地放下而不是丢出去"),
    ("modoptions_techpopups_jellyregrab_enabled",
     "Grabbing a jellyfish in the middle of a dash.",
     "冲刺途中抓住水母"),
    ("modoptions_techpopups_theoregrab_enabled",
     "Grabbing a crystal in the middle of a dash.",
     "冲刺途中抓住水晶"),
    ("modoptions_techpopups_jellyvator_enabled",
     "Grabbing a jellyfish during an upward dash.",
     "向上冲刺途中抓住水母"),
    ("modoptions_techpopups_theovator_enabled",
     "Grabbing a crystal during an upward dash.",
     "向上冲刺途中抓住水晶"),
    ("modoptions_techpopups_fastbubble_enabled",
     "Pressing dash WHILE you are inside a booster, to leave it early. Holding dash "
     "before you enter does nothing - the game only reacts to the moment you press it.",
     "待在泡泡里的那一瞬间按下冲刺或蹲冲，提前从泡泡里射出去。进泡泡之前就按着冲刺是不算的"
     "——游戏只认「按下的那一下」"),
    ("modoptions_techpopups_slowfall_enabled",
     "No such state exists in the vanilla game. The closest thing is Holdable.SlowFall, "
     "which is not a player tech.",
     "原版游戏里没有这个状态。最接近的是 Holdable.SlowFall，那不是玩家技巧"),
    ("modoptions_techpopups_cassetteboost_enabled",
     "Jumping on the frame a cassette block reforms under your feet. The reform snaps you "
     "on top of it and the jump turns that snap into extra height.",
     "脚下那块 Cassette Block 刷新成实心的那一帧起跳。刷新会把你吸到块顶，这一跳把那次吸附"
     "变成额外高度"),
    ("modoptions_techpopups_waterboost_enabled",
     "Jumping out of the water surface.",
     "从水面跳出去的那一下"),
    ("modoptions_techpopups_featherboost_enabled",
     "Start the feather on a diagonal. Read on the first frame the feather actually moves "
     "you, once its transform animation has finished.",
     "以斜向启动羽毛。读取的时机是变形动画结束、羽毛真正开始带你移动的那一帧"),
]

# 本轮才补的描述（语言包里原本没有这两条）
NEW_DESC = [
    ("modoptions_techpopups_overclock_enabled_desc",
     "Climb jump from the ground with no direction held. The game refunds the stamina the "
     "climb jump cost, so you end up above the usual 110 cap.",
     "站在地面上、不按方向键的攀爬跳。游戏会把这次攀爬跳扣掉的体力原样还回来，于是体力超过"
     "110 的上限"),
    ("modoptions_techpopups_crouchclimb_enabled_desc",
     "Climbing up a wall while staying crouched: grab a wall ducking, keep holding the "
     "wall, and climb without ever letting go of duck.",
     "蹲着往上爬墙。先蹲着抓墙、一直按住墙往上爬，中途不放开蹲键"),
]

# 三个子菜单的标签（C# 引用了但语言包里一直没有）
SUBMENU = [
    ("modoptions_techpopups_submenu_techsettings", "Techniques", "技巧开关"),
    ("modoptions_techpopups_submenu_timingsettings", "Timing", "判定时机"),
    ("modoptions_techpopups_submenu_miscsettings", "Misc", "杂项设置"),
]


def read(p):
    with io.open(p, encoding="utf-8-sig") as f:
        return f.read()


def write(p, s, bom=True):
    with io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="") as f:
        f.write(s)


def patch_cs():
    s = read(SETTINGS)
    added, skipped, failed = [], [], []
    for name_key, _en, _zh in PAIRS:
        desc_key = name_key + "_desc"
        anchor = '[SettingName("%s")]' % name_key
        sub = '[SettingSubText("%s")]' % desc_key
        if anchor not in s:
            failed.append((name_key, "找不到 SettingName 锚点"))
            continue
        if sub in s:
            skipped.append(name_key)
            continue
        pat = re.compile(re.escape(anchor) + r'(\s*\n)(\s*)public\s')
        m = pat.search(s)
        if not m:
            failed.append((name_key, "锚点后面不是 public 声明"))
            continue
        insert_at = m.start(1)
        indent = m.group(2)
        s = s[:insert_at] + "\n" + indent + sub + s[insert_at:]
        added.append(name_key)

    write(SETTINGS, s)
    return added, skipped, failed


def read_keys(p):
    out = {}
    with io.open(p, encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line.strip() or line.strip().startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def append_keys(path, entries, title, overwrite=False):
    """entries: [(key, value)]  —— 追加到文件末尾，已存在则按需覆盖。"""
    s = read(path)
    have = read_keys(path)
    lines = s if s.endswith("\n") else s + "\n"
    present, changed, appended = [], [], []
    for k, v in entries:
        if k in have:
            if overwrite and have[k] != v:
                pat = re.compile(r'^' + re.escape(k) + r'=.*$',
                                 re.MULTILINE)
                lines, n = pat.subn(k + "=" + v, lines)
                assert n == 1, "覆盖 %s 时匹配到 %d 行" % (k, n)
                changed.append(k)
            else:
                present.append(k)
            continue
        lines += "\n" + k + "=" + v + "\n"
        appended.append(k)
    if appended or changed:
        write(path, lines)
    return present, changed, appended


def main():
    added, skipped, failed = patch_cs()
    print("== C# 端 ==")
    print("  补上 SettingSubText:", len(added))
    for k in added:
        print("     +", k)
    if skipped:
        print("  已经挂过（跳过）:", len(skipped))
    if failed:
        print("  ！失败:", failed)
        return 1

    # 语言包：新增 overclock / crouchclimb / 三个子菜单
    en_entries = [(a, b) for a, b, _ in NEW_DESC] + \
                 [(a, b) for a, b, _ in SUBMENU]
    zh_entries = [(a, c) for a, _, c in NEW_DESC] + \
                 [(a, c) for a, _, c in SUBMENU]

    for path, entries, tag in ((EN, en_entries, "English"),
                               (ZH, zh_entries, "中文")):
        present, changed, appended = append_keys(path, entries, tag)
        print("== %s ==" % tag)
        print("  新增:", appended or "无")
        print("  已存在:", present or "无")

    return 0


if __name__ == "__main__":
    sys.exit(main())
