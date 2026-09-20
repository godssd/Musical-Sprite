# -*- coding: utf-8 -*-
"""
_fix_slide_sel.py — Note_Slide_Link_Select 重做（v12）
======================================================
用户反馈（2026-09-19）：进度充能条不对，004 的处理是【逐簇 1:1 平涂重映射】：
  基础图两个色簇（边条/内芯）各自整体换色，像素数一一对应，零渐变。
  004 实测: fill 20139px->20139px, rim 5698px->6576px(含AA)。
v9 错误：把 Tap_Select 果冻百分位曲线用在条上 -> 渐变条。

做法：轴投影重映射（同 v10/v11 方法）
  基础锚点: RIM_B=(253,196,123) 边条橙 / FILL_B=(255,253,169) 内芯亮黄
  目标色（真样本实测原色）:
    FILL_T=(255,246,98)  Tap 样本亮黄光（最鲜亮的充能色）
    RIM_T =(251,179,91)  Tap_Select 果冻主金
  每像素在 RIM_B->FILL_B 轴上投影 t，输出 lerp(RIM_T, FILL_T, t) —— AA 过渡保留。
"""
from PIL import Image

SRC = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/Note_Slide_Link_Select.png"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9/Note_Slide_Link_Select_skill_3_howl.png"

RIM_B = (253, 196, 123)
FILL_B = (255, 253, 169)
RIM_T = (251, 179, 91)
FILL_T = (255, 246, 98)

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

im = Image.open(SRC).convert("RGBA")
w, h = im.size
px = im.load()
out = Image.new("RGBA", (w, h))
op = out.load()

dx, dy, dz = (FILL_B[i]-RIM_B[i] for i in range(3))
len2 = dx*dx + dy*dy + dz*dz

for y in range(h):
    for x in range(w):
        r, g, b, a = px[x, y]
        if a < 128:
            op[x, y] = (r, g, b, a)
            continue
        t = ((r-RIM_B[0])*dx + (g-RIM_B[1])*dy + (b-RIM_B[2])*dz) / len2
        t = max(0.0, min(1.0, t))
        c = lerp(RIM_T, FILL_T, t)
        op[x, y] = (c[0], c[1], c[2], a)

out.save(OUT)
print("saved", OUT)

# 校验：输出应只有两个主色 + AA 过渡
from collections import Counter
c2 = Counter()
px2 = out.load()
for y in range(h):
    for x in range(w):
        r, g, b, a = px2[x, y]
        if a >= 128:
            c2[(r, g, b)] += 1
print("输出主色 top4:", c2.most_common(4))
