# -*- coding: utf-8 -*-
"""检查 Dialog/Simplified Chinese.txt 与 English.txt 的 key 是否一一对应。

中文文案直接维护在 Simplified Chinese.txt 里，不由脚本生成：
技巧名和 Dash Attack 这类术语保留英文，通用说明文字用中文。
本脚本只做校验，避免出现「漏翻某一行」或「多出一行没人管」的情况。

⚠️ 语言包文件名必须和游戏 Content/Dialog 里的语言名对得上 —— 游戏里那份叫
"Simplified Chinese.txt"。叫 "Chinese.txt" 是永远不会被加载的。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIALOG = os.path.normpath(os.path.join(HERE, "..", "Dialog"))
EN = os.path.join(DIALOG, "English.txt")
ZH = os.path.join(DIALOG, "Simplified Chinese.txt")


def keys(path):
    out = []
    for line in io.open(path, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        out.append(line.split("=", 1)[0])
    return out


def main():
    en, zh = keys(EN), keys(ZH)
    se, sz = set(en), set(zh)
    bad = False
    for k in sorted(se - sz):
        print("中文包缺这一行:", k)
        bad = True
    for k in sorted(sz - se):
        print("中文包多出这一行:", k)
        bad = True
    if en != zh:
        print("顺序不一致（不影响加载，但建议对齐）")
    if bad:
        sys.exit(1)
    print("OK：%d 个 key，两边一一对应。" % len(en))


if __name__ == "__main__":
    main()
