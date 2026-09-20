# -*- coding: utf-8 -*-
"""
enchant_wide_v11.py — Note_Wide 换皮 v11：爪印改角色对角色锚点映射
======================================================================
v10 问题：爪族用「亮度百分位整体搬运」，004 爪区的抗锯齿杂色全被
摊进真样本色阶 → 掌心噪点、趾头脏边（用户 2026-09-19 反馈"旧爪印
不太对了"）。

v11 爪族改显式三锚点映射（两边爪同为三角色结构）：
  004: 填充(115,240,203) / 亮青光(97,248,250) / 暗青边(69,185,152)
  真样本: 填充(240,185,116) / 亮黄光(253,239,101) / 暗金边(189,145,80)
每个爪族像素找最近两锚点、线段投影取 u，输出对应目标色 lerp——
保住抗锯齿的柔和过渡。圈↔身族映射与 v10 完全一致。
"""
from PIL import Image
import os

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
SRC_004 = f"{SP}/004/Note_Wide_skill_4_croissant_heal.png"
OUT     = "D:/unity/plan go/Musical Sprite/Tools/_gen_v10/Note_Wide_skill_3_howl.png"

RING4, BODY4 = (34, 113, 85), (98, 188, 159)    # 004 圈/身
RING3, BODY3 = (115, 77, 34), (222, 189, 126)   # skill_3 圈/身
T_LINE = 10.0    # 距轴线小于此值 → 圈↔身族

# 爪族三锚点（004 实测 → 真样本实测原色，角色一一对应）
# PAW3 直接取 Note_Tap 真样本爪区实测原色，不做均值混合：
#   填充(240,185,116) / 亮黄光(255,246,98) / 暗金边(191,153,88)
PAW4 = [(115, 240, 203), (97, 248, 250), (69, 185, 152)]   # 填充/亮光/暗边
PAW3 = [(240, 185, 116), (255, 246, 98), (191, 153, 88)]   # 填充/亮光/暗边

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

def dist(c1, c2):
    return sum((c1[i]-c2[i])**2 for i in range(3)) ** 0.5

def paw_map(col):
    """爪族像素 → 目标色：最近两锚点线段投影插值。"""
    ds = [dist(col, a) for a in PAW4]
    order = sorted(range(3), key=lambda i: ds[i])
    i0, i1 = order[0], order[1]
    a, b = PAW4[i0], PAW4[i1]
    ab = [b[k]-a[k] for k in range(3)]
    len2 = sum(v*v for v in ab) or 1.0
    u = ((col[0]-a[0])*ab[0] + (col[1]-a[1])*ab[1] + (col[2]-a[2])*ab[2]) / len2
    u = max(0.0, min(1.0, u))
    return lerp(PAW3[i0], PAW3[i1], u)

def main():
    im = Image.open(SRC_004).convert("RGBA")
    w, h = im.size; px = im.load()
    dx, dy, dz = (BODY4[i]-RING4[i] for i in range(3))
    len2 = dx*dx + dy*dy + dz*dz
    out = Image.new("RGBA", (w, h)); op = out.load()
    n_line = n_paw = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            col = (r, g, b)
            t = ((col[0]-RING4[0])*dx + (col[1]-RING4[1])*dy + (col[2]-RING4[2])*dz) / len2
            t = max(0.0, min(1.0, t))
            proj = tuple(RING4[i] + t*(BODY4[i]-RING4[i]) for i in range(3))
            if dist(col, proj) <= T_LINE:
                # 圈↔身族：轴线投影 → skill_3 轴线（与 v10 一致）
                c3 = lerp(RING3, BODY3, t); n_line += 1
            else:
                # 爪族：三锚点角色映射（v11 新）
                c3 = paw_map(col); n_paw += 1
            op[x, y] = (c3[0], c3[1], c3[2], a)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save(OUT)
    print(f"完成: line族 {n_line} px, paw族 {n_paw} px -> {OUT}")

if __name__ == "__main__":
    main()
