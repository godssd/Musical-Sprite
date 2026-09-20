# -*- coding: utf-8 -*-
from PIL import Image
from collections import Counter
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
# 描边 = 不透明像素中与透明像素4邻接的；填充 = 其余
def stroke_fill(p):
    im = Image.open(p).convert("RGBA"); w,h = im.size; px = im.load()
    op = [[px[x,y][3]>=128 for x in range(w)] for y in range(h)]
    cs, cf = Counter(), Counter()
    for y in range(h):
        for x in range(w):
            if not op[y][x]: continue
            edge = False
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                nx,ny = x+dx,y+dy
                if nx<0 or ny<0 or nx>=w or ny>=h or not op[ny][nx]:
                    edge = True; break
            (cs if edge else cf)[px[x,y][:3]] += 1
    return cs, cf

for tag,p in {
  "003数字5普通": f"{SP}/003/Note_Repeat_5_skill_3_howl.png",
  "003数字5完成": f"{SP}/003/Note_Repeat_5_Select_skill_3_howl.png",
  "004数字5普通(验证规则)": f"{SP}/004/Note_Repeat_5_skill_4_croissant_heal.png",
  "004数字5完成(验证规则)": f"{SP}/004/Note_Repeat_5_Select_skill_4_croissant_heal.png",
}.items():
    cs, cf = stroke_fill(p)
    print(f"== {tag}")
    print("   描边 top3:", " | ".join(f"{c}x{n}" for c,n in cs.most_common(3)))
    print("   填充 top3:", " | ".join(f"{c}x{n}" for c,n in cf.most_common(3)))
