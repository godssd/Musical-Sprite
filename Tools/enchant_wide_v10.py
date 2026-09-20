# -*- coding: utf-8 -*-
"""
enchant_wide_v10.py — Note_Wide 换皮 v10：直接拿 004 样本整图变色
======================================================================
用户 2026-09-19 指示："你就应该直接拿样本来变色，这样会准点"。
不再用公式建模圈厚/端帽/肩部——几何 100% 用 004 参照
(Note_Wide_skill_4_croissant_heal.png)，只做色彩重映射到 skill_3 调色板。

方法（两族分开映射）：
  1. 圈↔身族：004 的 RING4(34,113,85)→BODY4(98,188,159) 渐变轴线上的像素
     （含爪填充，其色恰好在轴线上），按投影参数 t 映射到
     RING3(115,77,34)→BODY3(222,189,126)。
  2. 爪印族（轴线外：亮青描边光/白青高光/暗青爪缘）：按亮度百分位
     迁移真样本(Note_Tap_skill_3_howl) 爪区色阶
     （暗铜→铜金→亮金→高黄光）。
"""
from PIL import Image
from collections import Counter

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
SRC_004 = f"{SP}/004/Note_Wide_skill_4_croissant_heal.png"
SAMPLE  = f"{SP}/003/Note_Tap_skill_3_howl.png"   # 真样本（爪色阶来源）
OUT     = "D:/unity/plan go/Musical Sprite/Tools/_gen_v10/Note_Wide_skill_3_howl.png"

RING4, BODY4 = (34, 113, 85), (98, 188, 159)    # 004 圈/身
RING3, BODY3 = (115, 77, 34), (222, 189, 126)   # skill_3 圈/身（真样本实测）
T_LINE = 10.0    # 距轴线小于此值 → 圈↔身族
PAW_EXCLUDE = 30 # 真样本爪区提取时排除贴近体色/圈色的像素

def lum(c): return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

def dist(c1, c2):
    return sum((c1[i]-c2[i])**2 for i in range(3)) ** 0.5

def build_paw_ramp():
    """真样本爪区色阶（排除体色/圈色），按亮度升序 + 权重累计百分位。
    返回 [(cum_pct, color)]，query 时按百分位取色。"""
    im = Image.open(SAMPLE).convert("RGBA"); w, h = im.size; px = im.load()
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128: continue
            col = (r, g, b)
            if dist(col, BODY3) <= PAW_EXCLUDE: continue   # 体色
            if dist(col, RING3) <= PAW_EXCLUDE: continue   # 圈色
            c[col] += 1
    total = sum(c.values())
    items = sorted(c.items(), key=lambda kv: lum(kv[0]))
    ramp, cum = [], 0
    for col, n in items:
        cum += n
        ramp.append((cum / total, col))
    return ramp

def ramp_lookup(ramp, q):
    for cum, col in ramp:
        if q <= cum: return col
    return ramp[-1][1]

def main():
    ramp = build_paw_ramp()
    print(f"爪色阶 {len(ramp)} 档: 低={ramp[0][1]} 高={ramp[-1][1]}")
    im = Image.open(SRC_004).convert("RGBA")
    w, h = im.size; px = im.load()
    dx, dy, dz = (BODY4[i]-RING4[i] for i in range(3))
    len2 = dx*dx + dy*dy + dz*dz
    # 第一遍：分类并收集爪印族的亮度
    recs = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                recs.append((x, y, r, g, b, a, None, None)); continue
            col = (r, g, b)
            t = ((col[0]-RING4[0])*dx + (col[1]-RING4[1])*dy + (col[2]-RING4[2])*dz) / len2
            t = max(0.0, min(1.0, t))
            proj = tuple(RING4[i] + t*(BODY4[i]-RING4[i]) for i in range(3))
            perp = dist(col, proj)
            if perp <= T_LINE:
                recs.append((x, y, r, g, b, a, "line", t))
            else:
                recs.append((x, y, r, g, b, a, "paw", lum(col)))
    # 第二遍：爪印族按亮度百分位取真样本色阶
    paws = sorted([rc for rc in recs if rc[6] == "paw"], key=lambda rc: rc[7])
    n_paw = len(paws)
    rank = {}
    for i, rc in enumerate(paws):
        rank[(rc[0], rc[1])] = ramp_lookup(ramp, (i+1) / n_paw)
    out = Image.new("RGBA", (w, h)); op = out.load()
    n_line = n_paw2 = 0
    for x, y, r, g, b, a, fam, v in recs:
        if fam is None:
            op[x, y] = (r, g, b, a); continue
        if fam == "line":
            t = lerp(RING3, BODY3, v); n_line += 1
        else:
            t = rank[(x, y)]; n_paw2 += 1
        op[x, y] = (t[0], t[1], t[2], a)
    import os
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    out.save(OUT)
    print(f"完成: line族 {n_line} px, paw族 {n_paw2} px -> {OUT}")

if __name__ == "__main__":
    main()
