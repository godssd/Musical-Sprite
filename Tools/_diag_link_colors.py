# -*- coding: utf-8 -*-
from PIL import Image
from collections import Counter
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
files = {
  "004_Link普通":     f"{SP}/004/Note_Slide_Link_skill_4_croissant_heal.png",
  "004_Link完成":     f"{SP}/004/Note_Slide_Link_Select_skill_4_croissant_heal.png",
  "004数字5普通":     f"{SP}/004/Note_Repeat_5_skill_4_croissant_heal.png",
  "004数字5完成":     f"{SP}/004/Note_Repeat_5_Select_skill_4_croissant_heal.png",
  "003数字5普通(旧版)": f"{SP}/003/Note_Repeat_5_skill_3_howl.png",
  "003数字5完成(旧版)": f"{SP}/003/Note_Repeat_5_Select_skill_3_howl.png",
  "003_Link普通(当前)": f"{SP}/003/Note_Slide_Link_skill_3_howl.png",
  "003_Link完成(当前)": f"{SP}/003/Note_Slide_Link_Select_skill_3_howl.png",
  "基础Link":         f"{SP}/Note_Slide_Link.png",
  "基础Link完成":      f"{SP}/Note_Slide_Link_Select.png",
}
for tag, p in files.items():
    im = Image.open(p).convert("RGBA"); w,h = im.size; px = im.load()
    c = Counter()
    for y in range(h):
        for x in range(w):
            r,g,b,a = px[x,y]
            if a >= 128: c[(r,g,b)] += 1
    tot = sum(c.values())
    tops = " | ".join(f"{col} {n*100//tot}%" for col,n in c.most_common(4))
    print(f"{tag:20s} ({w}x{h}): {tops}")
