# -*- coding: utf-8 -*-
from PIL import Image
paths = {
  "默认基础":   "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/Note_Slide_Link_Select.png",
  "v9输出":     "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/003/Note_Slide_Link_Select_skill_3_howl.png",
  "004参照":    "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/004/Note_Slide_Link_Select_skill_4_croissant_heal.png",
}
for tag,p in paths.items():
    im = Image.open(p).convert("RGBA"); w,h = im.size; px = im.load()
    print(f"== {tag} ({w}x{h}) 逐行中点颜色 ==")
    for y in range(0, h, max(1,h//12)):
        r,g,b,a = px[w//2,y]
        print(f"   y={y:3d}  ({r},{g},{b}) a={a}")
