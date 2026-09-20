# -*- coding: utf-8 -*-
"""全图放大 + 精确测量数字5的填充色/描边色, 以及Tap爪印的圈色"""
from PIL import Image
import os, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
OUT  = r"D:\unity\plan go\Musical Sprite\Tools\_cmp_v9"

# 1) 全图放大
for name, tag in [("Note_Repeat_0_skill_3_howl.png","full_r0"),
                  ("Note_Repeat_skill_3_howl.png","full_r_nodigit"),
                  ("Note_Repeat_Select_skill_3_howl.png","full_r_sel_nodigit")]:
    im = Image.open(os.path.join(BASE, name)).convert("RGBA")
    im2 = im.resize((im.width*4, im.height*4), Image.NEAREST)
    bg = Image.new("RGBA", im2.size, (40,40,48,255)); bg.alpha_composite(im2)
    bg.convert("RGB").save(os.path.join(OUT, f"_zoom_{tag}.png"))

# 2) 数字5结构: 沿竖直中线扫描
im = Image.open(os.path.join(BASE, "Note_Repeat_5_skill_3_howl.png")).convert("RGBA")
px = im.load(); w,h = im.size
print("### Note_Repeat_5 竖直中线扫描 (x=w//2):")
runs=[]
for y in range(h):
    c = px[w//2, y]
    if runs and runs[-1][0]==c: runs[-1][1]+=1
    else: runs.append([c,1])
print(" | ".join(f"{c[0]},{c[1]},{c[2]}x{n}" for c,n in runs if c[3]>10 or n>0))

# 3) Tap 爪印大肉垫的圈: 水平扫过大肉垫中心
im2 = Image.open(os.path.join(BASE, "Note_Tap_skill_3_howl.png")).convert("RGBA")
px2 = im2.load(); w2,h2 = im2.size
print("\n### Note_Tap 竖直 x=w//2 扫描:")
runs=[]
for y in range(h2):
    c = px2[w2//2, y]
    if runs and runs[-1][0]==c: runs[-1][1]+=1
    else: runs.append([c,1])
print(" | ".join(f"{c[0]},{c[1]},{c[2]}x{n}" for c,n in runs))
