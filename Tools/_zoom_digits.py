# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
Z = 5
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
items = [
  ("003数5普通", f"{SP}/003/Note_Repeat_5_skill_3_howl.png"),
  ("003数5完成", f"{SP}/003/Note_Repeat_5_Select_skill_3_howl.png"),
  ("004数5普通", f"{SP}/004/Note_Repeat_5_skill_4_croissant_heal.png"),
  ("004数5完成", f"{SP}/004/Note_Repeat_5_Select_skill_4_croissant_heal.png"),
]
cells = []
for tag,p in items:
    im = Image.open(p).convert("RGBA")
    w,h = im.size
    cells.append((tag, im.resize((w*Z,h*Z), Image.NEAREST)))
pad = 14
W = sum(c.width for _,c in cells) + pad*(len(cells)-1)
H = max(c.height for _,c in cells) + 26
canvas = Image.new("RGBA",(W,H),(45,45,48,255)); d = ImageDraw.Draw(canvas)
x = 0
for tag,im in cells:
    canvas.paste(im,(x,24),im)
    d.text((x+2,4), tag, fill=(235,235,235,255))
    x += im.width + pad
canvas.save("D:/unity/plan go/Musical Sprite/Tools/_cmp_v9/zoom_digits_4way.png")
print("saved", canvas.size)
