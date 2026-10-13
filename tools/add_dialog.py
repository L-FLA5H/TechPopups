# -*- coding: utf-8 -*-
"""给 Dialog/English.txt 追加本轮新增选项的文案。每条都 assert 锚点，改不动就炸。"""
import io, os

P = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "Dialog", "English.txt"))
with io.open(P, "r", encoding="utf-8") as f:
    s = f.read()

# 锚点：挂在「逐渐low-Driven」已有的 chain_window 说明后面
ANCHOR = "modoptions_techpopups_chain_window="
assert ANCHOR in s, "anchor not found: chain_window"
i = s.index(ANCHOR)
eol = s.index("\n", i) + 1

ADD = """modoptions_techpopups_delayedultra_enabled=Delayed Ultra
modoptions_techpopups_delayedultra_enabled_desc=Split Delayed Ultra out from Ultra when you waited longer than this before jumping. There is no boolean for it in the game, so this is a knob - left off by default so it never steals the plain Ultra.
modoptions_techpopups_delayedultra_window=Delayed Ultra delay
modoptions_techpopups_delayedultra_window_desc=How long after the diagonal-down dash lands still counts as a normal Ultra. Anything later becomes a Delayed Ultra. In hundredths of a second.
"""

assert "modoptions_techpopups_delayedultra_enabled=" not in s, "already added"
s = s[:eol] + ADD + s[eol:]

ANCHOR2 = "modoptions_techpopups_popup_scale=Popup Scale (%)"
assert ANCHOR2 in s, "anchor not found: popup_scale"
j = s.index(ANCHOR2)
eol2 = s.index("\n", j) + 1

ADD2 = """modoptions_techpopups_popup_scale_desc=How big the popup is drawn. 100 is the size shipped with the mod.
modoptions_techpopups_popup_displaymode=Display Mode
modoptions_techpopups_popup_displaymode_desc=Show only the icon, only the tech name, or both.
modoptions_techpopups_popup_labeloffset=Label height
modoptions_techpopups_popup_labeloffset_desc=How far above Madeline's head the popup sits. Raise it if it covers her hair.
modoptions_techpopups_popup_perchar=Per-letter animation
modoptions_techpopups_popup_perchar_desc=Letters fly in one at a time. Turn off to show the whole label at once - easier to read when chaining techs fast.
"""

assert "modoptions_techpopups_popup_labeloffset=" not in s, "already added"
s = s[:eol2] + ADD2 + s[eol2:]

with io.open(P, "w", encoding="utf-8", newline="\n") as f:
    f.write(s)
print("ok")
