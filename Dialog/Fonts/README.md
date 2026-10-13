# 标签字体

| 文件 | 是什么 |
| --- | --- |
| `techpopups.fnt` | BMFont XML 描述文件（AngelCode 格式），95 个字形，ASCII 32–126 |
| `techpopups_0.png` | RGBA 字形图集，1024×512，白色字形 + alpha 通道 |

由 **HarmonyOS Sans SC Bold** 在 **64 号**下渲染生成，脚本是 `tools/make_font.py`。
度量参数：`lineHeight` 76，`base` 60。

## 协议说明

HarmonyOS Sans 由华为发布，使用其自带的字体协议。
这里**只分发渲染出来的位图图集，不含字体原文件** ——
`make_font.py` 读取的是运行机器上已安装的那份字体，再输出字形位图。
安装鸿蒙体是使用者自己的行为，仍然受华为的协议约束。

如果你要二次分发这份构建，请保持「只发图集、不发字体」的做法，
或者换成你有权使用的字体重新生成。脚本的查找顺序是：

1. `HarmonyOS_Sans_SC_Bold.ttf`
2. `HarmonyOS_Sans_SC_Black.ttf`
3. `msyhbd.ttc`（微软雅黑粗体）
4. `Noto Sans SC Bold`（SIL OFL 1.1）

## 重新生成

```powershell
python tools\make_font.py
```

字符范围改脚本顶部的 `START` / `END`。
技巧名里出现图集没有的字符时，运行时会**自动退回原来的字体**，
最坏只是字没变，不会报错也不会不显示。
