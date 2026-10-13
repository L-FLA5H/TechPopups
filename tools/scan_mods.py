# -*- coding: utf-8 -*-
"""在蔚蓝 Mods 目录里横扫所有 DLL，找「别人已经写好的技巧判定」。

这是找现成思路的第一站：蔚蓝的自制图生态里，helper 和地图常常自带各种
tech indicator / detector。用法直接改下面的 KEYS 然后跑。
"""
import os, sys, zipfile, io

MD = r"D:\Steam\steamapps\common\Celeste\Mods"

KEYS = [
    b"CeilingPop", b"Cpop", b"ceilingPop",
    b"CornerKick", b"cornerKick",
    b"Moonboost", b"MoonSpring",
    b"DelayedUltra", b"UltraIndicator", b"StoredUltra",
    b"Retention", b"WallBoost", b"Wallboost",
    b"WaveDash", b"wavedash", b"Wavedash",
    b"ExtendedHyper", b"ExtendedSuper", b"ReverseCornerBoost", b"ReverseCornerboost",
    b"Demodash", b"DemoDash", b"Hyperdash", b"Superdash",
    b"Bunnyhop", b"BunnyHop",
    b"TechDetector", b"TechIndicator", b"TechAnnouncer", b"TechDisplay",
    b"DreamHyper", b"DreamSuper", b"Cornerboost", b"CornerBoost",
    b"GroundedUltra", b"Gultra", b"ChainedUltra",
    b"Bubsdrop", b"BubsDrop", b"DashBounce",
    b"Overclock", b"Archie", b"SpikeTech", b"ReformTech",
]


def dlls():
    """产出 (来源名, dll 字节)。文件夹形式的 mod 也照顾到。"""
    for f in sorted(os.listdir(MD)):
        p = os.path.join(MD, f)
        if f.lower().endswith(".zip"):
            try:
                z = zipfile.ZipFile(p)
            except Exception:
                continue
            for n in z.namelist():
                if n.lower().endswith(".dll"):
                    try:
                        yield f + "!" + n, z.read(n)
                    except Exception:
                        pass
        elif os.path.isdir(p):
            for root, _, files in os.walk(p):
                for n in files:
                    if n.lower().endswith(".dll"):
                        fp = os.path.join(root, n)
                        try:
                            yield os.path.relpath(fp, MD), open(fp, "rb").read()
                        except Exception:
                            pass


found = {}
for src, d in dlls():
    for k in KEYS:
        if k in d:
            found.setdefault(src, set()).add(k.decode())

interesting = {"DelayedUltra", "UltraIndicator", "CeilingPop", "Cpop",
               "CornerKick", "Moonboost", "TechDetector", "Bubsdrop",
               "Superdash", "Retention", "ReverseCornerBoost", "GroundedUltra"}

print("=" * 72)
print("命中的 Mod（按「有没有我们缺的判定」排序）")
print("=" * 72)
def score(s):
    return -len(s & interesting)
for src in sorted(found, key=lambda s: (score(found[s]), s)):
    ks = found[src]
    mark = "★" if (ks & interesting) else " "
    print("%s %-46s %s" % (mark, src[:46], ", ".join(sorted(ks))))
print()
print("共 %d 个 DLL 命中" % len(found))
