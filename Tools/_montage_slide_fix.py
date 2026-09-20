# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
Z = 2
items = [
  ("base",  "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/Note_Slide_Link_Select.png"),
  ("v9(错)", "D:/unity/plan go/Musical Sprite/Tools/_bak_paw_fix/../_gen_v10/../../Assets/Art/UI/sprites/003_backup_2026-09-19_final/Note_Slide_Link_Select_skill_3_howl.png"),
  ("new",   "D:/unity/plan go/Musical Sprite/Tools/_gen_v9/Note_Slide_Link_Select_skill_3_howl.png"),
  ("004",   "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/004/Note_Slide_Link_Select_skill_4_croissant_heal.png"),
]
import os
cells = []
for tag, p in items:
    im = Image.open(p).convert("RGBA")
    w,h = im.size
    cells.append((tag, im.resize((w*Z, h*Z), Image.NEAREST)))
pad = 16
W = sum(c.width for _,c in cells) + pad*(len(cells)-1)
H = max(c.height for _,c in cells) + 30
canvas = Image.new("RGBA",(W,H),(45,45,48,255)); d = ImageDraw.Draw(canvas)
x = 0
for tag, im in cells:
    canvas.paste(im,(x,28),im)
    d.text((x+4,6), tag, fill=(235,235,235,255))
    x += im.width + pad
canvas.save("D:/unity/plan go/Musical Sprite/Tools/_cmp_v9/montage_slide_sel_fix.png")
print("saved", canvas.size)
