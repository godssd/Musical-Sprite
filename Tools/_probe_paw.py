"""聚焦诊断: 爪印颜色对齐样本 + 爪印形状是否被破坏 + Repeat_5 数字"""
import os
from collections import Counter
from PIL import Image
import enchant_skin_v7 as E

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v7"
REF = os.path.join(SP, "003")

def lum(c): return 0.299*c[0]+0.587*c[1]+0.114*c[2]
def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

# ---- 1. 样本中爪印的标准色(从 skill_3 样本 Tap 抓爪印簇) ----
print("=== 样本中(Tap_skill_3_howl)的爪印配色 ===")
spx, (w, h) = load(os.path.join(REF, "Note_Tap_skill_3_howl.png"))
# 爪印 = 基础图里"非身体(距主色>35)且非环带"的像素, 在样本里的颜色
bpx, _ = load(os.path.join(SP, "Note_Tap.png"))
D = E.edge_dist(bpx, w, h)
dom = E.dominant_color(bpx, w, h)
paw_samp = Counter()
for y in range(h):
    for x in range(w):
        rb, gb, bb, ab = bpx[x, y]
        rs, gs, bs, as_ = spx[x, y]
        if ab < 128 or as_ < 128: continue
        if E.dist((rb, gb, bb), dom) > 35 and D[y][x] > E.RING_D:
            paw_samp[(rs, gs, bs)] += 1
print("  样本爪印色簇(前6):", [c for c, _ in paw_samp.most_common(6)])

# ---- 2. v7 生成图中同一爪印区域的颜色 ----
print("\n=== v7 生成图(Wide/Tap_Small)同位置爪印配色 ===")
for name in ["Wide", "Tap_Small"]:
    bp = os.path.join(SP, f"Note_{name}.png")
    gp = os.path.join(GEN, f"Note_{name}_skill_3_howl.png")
    if not os.path.exists(bp) or not os.path.exists(gp): 
        print(f"  [skip] {name}"); continue
    bpx, (w, h) = load(bp); gpx, _ = load(gp)
    D = E.edge_dist(bpx, w, h)
    dom = E.dominant_color(bpx, w, h)
    paw_gen = Counter()
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rg, gg, bg, ag = gpx[x, y]
            if ab < 128: continue
            if E.dist((rb, gb, bb), dom) > 35 and D[y][x] > E.RING_D:
                paw_gen[(rg, gg, bg)] += 1
    print(f"  {name:12s} 生成爪印色簇(前6):", [c for c, _ in paw_gen.most_common(6)])

# ---- 3. 爪印形状是否被破坏: base 爪印掩码 vs v7 生成图爪印掩码(应完全相同) ----
print("\n=== 爪印形状保持检查 (base 爪印掩码 vs v7 生成图, 应100%一致) ===")
for name in ["Wide", "Tap_Small"]:
    bp = os.path.join(SP, f"Note_{name}.png")
    gp = os.path.join(GEN, f"Note_{name}_skill_3_howl.png")
    if not os.path.exists(bp) or not os.path.exists(gp): 
        print(f"  [skip] {name}"); continue
    bpx, (w, h) = load(bp); gpx, _ = load(gp)
    dom = E.dominant_color(bpx, w, h)
    D = E.edge_dist(bpx, w, h)
    same = 0; diff = 0
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rg, gg, bg, ag = gpx[x, y]
            if ab < 128: continue
            base_is_paw = E.dist((rb, gb, bb), dom) > 35 and D[y][x] > E.RING_D
            gen_is_paw = (rg, gg, bg) != (rb, gb, bb)  # 生成图非基础原色 = 被重着色(含爪印)
            if base_is_paw and gen_is_paw: same += 1
            elif base_is_paw and not gen_is_paw: diff += 1
    print(f"  {name:12s}: 基础爪印像素总数={same+diff}, 生成图仍同位置={same}, 丢失={diff}")

# ---- 4. Repeat_5 数字: 描边=深棕? 填充=奶油? ----
print("\n=== Repeat_5 数字配色 (digit 模式) ===")
bp = os.path.join(SP, "Note_Repeat_5.png")
gp = os.path.join(GEN, "Note_Repeat_5_skill_3_howl.png")
bpx, (w, h) = load(bp); gpx, _ = load(gp)
dom = E.dominant_color(bpx, w, h)
outline = Counter(); fill = Counter()
for y in range(h):
    for x in range(w):
        rb, gb, bb, ab = bpx[x, y]
        rg, gg, bg, ag = gpx[x, y]
        if ab < 128: continue
        if rb >= 200 and gb <= 132 and 60 <= bb <= 105:  # 数字描边族
            outline[(rg, gg, bg)] += 1
        elif rb >= 245 and gb >= 225 and 150 <= bb <= 230:  # 数字象牙填充族
            fill[(rg, gg, bg)] += 1
print("  描边生成色(前3):", [c for c, _ in outline.most_common(3)])
print("  填充生成色(前3):", [c for c, _ in fill.most_common(3)])
