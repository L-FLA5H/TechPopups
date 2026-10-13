# -*- coding: utf-8 -*-
"""删掉设置菜单里所有解释性文案（SettingSubText + 对应语言包键）。

设置菜单里不应该出现解释性文案 —— 选项名本身就是标签。
界面文案一贯的原则是「不写解释性文字，
用画面代替」。设置项的名字本身就是标签，下面再挂一段说明就是解释性文字。

做法：
  1. Source/TechPopupsSettings.cs —— 删掉每一行 [SettingSubText("...")]
  2. Dialog/*.txt —— 删掉每一个 *_desc= 键，并把留下的连续空行压成一个

带 assert：改之前先确认锚点数量对得上，改完回读校验。
"""
import io
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SETTINGS = os.path.join(ROOT, "Source", "TechPopupsSettings.cs")
DIALOGS = [os.path.join(ROOT, "Dialog", "English.txt"),
           os.path.join(ROOT, "Dialog", "Simplified Chinese.txt")]


def read(path):
    with io.open(path, encoding="utf-8-sig") as f:
        return f.read()


def write(path, text):
    # 保持 UTF-8 BOM：含中文的源文件必须带 BOM，否则编译乱码
    with io.open(path, "w", encoding="utf-8-sig", newline="\n") as f:
        f.write(text)


# ---------- 1. C# ----------
cs = read(SETTINGS)
sub_before = len(re.findall(r'^\s*\[SettingSubText\("[^"]+"\)\]\s*$', cs, re.M))
name_before = len(re.findall(r'\[SettingName\("([^"]+)"\)\]', cs))
assert sub_before == 69, "预期 69 条 SubText，实际 %d —— 先核对再动手" % sub_before
assert name_before == 79, "预期 79 个 SettingName，实际 %d" % name_before

cs_new = re.sub(r'^\s*\[SettingSubText\("[^"]+"\)\]\s*\n', '', cs, flags=re.M)
assert cs_new.count("SettingSubText") == 0, "还有残留的 SettingSubText"
assert len(re.findall(r'\[SettingName\("([^"]+)"\)\]', cs_new)) == name_before, \
    "删 SubText 时误伤了 SettingName"
write(SETTINGS, cs_new)
print("C#      : 删掉 %d 条 SubText，SettingName 保持 %d 个" % (sub_before, name_before))

# ---------- 2. 语言包 ----------
for path in DIALOGS:
    text = read(path)
    keys_before = re.findall(r'^([A-Za-z0-9_]+)\s*=', text, re.M)
    desc_before = [k for k in keys_before if k.endswith("_desc")]
    assert desc_before, "%s 里找不到 _desc 键，锚点不对" % os.path.basename(path)

    lines = text.split("\n")
    kept = [ln for ln in lines if not re.match(r'^[A-Za-z0-9_]+_desc\s*=', ln)]

    # 连续空行压成一个
    out = []
    for ln in kept:
        if ln.strip() == "" and out and out[-1].strip() == "":
            continue
        out.append(ln)
    new_text = "\n".join(out)

    keys_after = re.findall(r'^([A-Za-z0-9_]+)\s*=', new_text, re.M)
    assert not [k for k in keys_after if k.endswith("_desc")], \
        "%s 还有 _desc 残留" % os.path.basename(path)
    assert len(keys_after) == len(keys_before) - len(desc_before), \
        "%s 删除数量对不上" % os.path.basename(path)

    write(path, new_text)
    print("%-24s: 删掉 %d 条 _desc，剩 %d 个键" %
          (os.path.basename(path), len(desc_before), len(keys_after)))
