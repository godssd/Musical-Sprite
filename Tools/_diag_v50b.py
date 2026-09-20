# -*- coding: utf-8 -*-
# 诊断第50轮-2：环厚基准 + generic Repeat 结构映射
from PIL import Image
from collections import Counter
import os

ROOT = r"D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"

def load(p):
    return Image.open(os.path.join(ROOT, p)).convert("RGBA")

def ring_thickness_4dir(im, body_test, name):
    """从四边中线向内数,直到碰到'体色'像素,打印环厚"""
    W, H = im.size
    px = im.load()
    res = {}
    cx, cy = W//2, H//2
    # top
    for tag, rng in [("top", ((cx,y) for y in range(H))),
                     ("bottom", ((cx,y) for y in range(H-1,-1,-1))),
                     ("left", ((x,cy) for x in range(W))),
                     ("right", ((x,cy) for x in range(W-1,-1,-1)))]:
        n = 0
        for (x,y) in rng:
            r,g,b,a = px[x,y]
            if a < 200:
                continue
            if body_test(r,g,b):
                break
            n += 1
        res[tag] = n
    print(f"{name}: 环厚(到体色为止) {res}")

is_olive = lambda r,g,b: r>150 and g>150   # 体色=奶油
is_gold  = lambda r,g,b: r>180 and g>150   # howl 体色=金

print("== 环厚对比 ==")
ring_thickness_4dir(load("005/Note_Tap_skill_5_bomb_rain.png"), is_olive, "005 Tap样本(84x137)")
ring_thickness_4dir(load("003/Note_Tap_Small_skill_3_howl.png"), is_gold, "howl Small(68x112)")
ring_thickness_4dir(load("003/Note_Tap_skill_3_howl.png"), is_gold, "howl Tap(84x137)")
ring_thickness_4dir(load("005/Note_Tap_Small_skill_5_bomb_rain.png"), is_olive, "生成Small被打回(68x112)")

print()
print("== 005 Tap样本 爪印描边色(环色之外的深色) ==")
tap = load("005/Note_Tap_skill_5_bomb_rain.png")
c = Counter((r,g,b) for r,g,b,a in tap.getdata() if a>=200)
for col,n in c.most_common(30):
    r,g,b = col
    if not (r>200) and not (r<130):  # 中间调=爪描边过渡
        print(f"   {col} x{n}")

print()
print("== base Repeat -> howl Repeat 众数映射(结构对应) ==")
rep_b = load("Note_Repeat.png"); rep_h = load("003/Note_Repeat_skill_3_howl.png")
pb, ph = rep_b.load(), rep_h.load()
W,H = rep_b.size
pair = Counter()
for y in range(H):
    for x in range(W):
        rb,gb,bb,ab = pb[x,y]; rh,gh,bh,ah = ph[x,y]
        if ab>=200 and ah>=200:
            # 源色量化到8步减少噪声
            pair[((rb//24*24,gb//24*24,bb//24*24),(rh,gh,bh))]+=1
agg = {}
for (src,dst),n in pair.items():
    agg.setdefault(src,Counter())[dst]+=n
for src in sorted(agg, key=lambda s:-sum(agg[s].values()))[:12]:
    tot = sum(agg[src].values())
    tops = ", ".join(f"{d}x{n}" for d,n in agg[src].most_common(3))
    print(f"   源~{src} (共{tot}): -> {tops}")

print()
print("== howl Repeat 垂直中线剖面 ==")
ph2 = load("003/Note_Repeat_skill_3_howl.png").load()
cx = 50
n=0
for y in range(150):
    r,g,b,a = ph2[cx,y]
    if a>=200:
        print(f"   y={y}: ({r},{g},{b})")
        n+=1
        if n>=30: break
