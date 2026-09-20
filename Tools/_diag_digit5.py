# -*- coding: utf-8 -*-
from PIL import Image
from collections import Counter
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
files = {
  "基础数字5":      f"{SP}/Note_Repeat_5.png",
  "基础数字5完成":   f"{SP}/Note_Repeat_5_Select.png",
  "003数字5普通":   f"{SP}/003/Note_Repeat_5_skill_3_howl.png",
  "003数字5完成":   f"{SP}/003/Note_Repeat_5_Select_skill_3_howl.png",
}
for tag,p in files.items():
    im = Image.open(p).convert("RGBA"); w,h = im.size; px = im.load()
    c = Counter()
    for y in range(h):
        for x in range(w):
            r,g,b,a = px[x,y]
            if a>=128: c[(r,g,b)] += 1
    tot = sum(c.values())
    print(f"== {tag} ({w}x{h})")
    for col,n in c.most_common(5):
        print(f"   {col} {n*100//tot}%")
    # 中心点(填充) 与 边缘点(描边) 采样
    cx, cy = w//2, h//2
    print(f"   中心({cx},{cy}): {px[cx,cy]}")
    # 找最上不透明行中点
    for y in range(h):
        if px[cx,y][3]>=128:
            print(f"   顶缘({cx},{y}): {px[cx,y]}"); break
