# -*- coding: utf-8 -*-
from PIL import Image
paths = {
  "基础Link":      "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/Note_Slide_Link.png",
  "v9_Link":       "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/003/Note_Slide_Link_skill_3_howl.png",
  "004_Link":      "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/004/Note_Slide_Link_skill_4_croissant_heal.png",
  "基础LinkSel":   "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/Note_Slide_Link_Select.png",
  "004_LinkSel":   "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/004/Note_Slide_Link_Select_skill_4_croissant_heal.png",
}
for tag,p in paths.items():
    im = Image.open(p).convert("RGBA"); w,h = im.size; px = im.load()
    from collections import Counter
    c = Counter()
    for y in range(h):
        for x in range(w):
            r,g,b,a = px[x,y]
            if a>=128: c[(r,g,b)] += 1
    top = c.most_common(4)
    print(f"{tag:12s} ({w}x{h}) 主色:", " | ".join(f"{col}x{n}" for col,n in top))
