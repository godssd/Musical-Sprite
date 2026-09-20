# -*- coding: utf-8 -*-
from PIL import Image
from collections import Counter
im = Image.open("D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/003/Note_Tap_Select_skill_3_howl.png").convert("RGBA")
w,h = im.size; px = im.load()
c = Counter()
for y in range(h):
    for x in range(w):
        r,g,b,a = px[x,y]
        if a>=128: c[(r,g,b)] += 1
print("Tap_Select 全图主色 top10:")
for col,n in c.most_common(10):
    print(f"  {col} x{n}")
# 上/中/下三段采样
print("纵向扫描(中列):")
for y in range(0,h,max(1,h//10)):
    r,g,b,a = px[w//2,y]
    print(f"  y={y:3d} ({r},{g},{b})")
