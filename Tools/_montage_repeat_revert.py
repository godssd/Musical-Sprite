# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
Z = 2
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
files = ["Note_Repeat_skill_3_howl.png","Note_Repeat_Select_skill_3_howl.png",
         "Note_Repeat_5_skill_3_howl.png","Note_Repeat_9_Select_skill_3_howl.png"]
imgs = [(f, Image.open(f"{SP}/003/{f}").convert("RGBA").resize((84*Z,302*Z),Image.NEAREST)) for f in files]
# 数字是方图，按各自尺寸排
raws = [(f, Image.open(f"{SP}/003/{f}").convert("RGBA")) for f in files]
Z2 = 3
cells = []
for f, im in raws:
    w,h = im.size
    cells.append((f, im.resize((w*Z2,h*Z2), Image.NEAREST)))
pad = 18
W = sum(c.width for _,c in cells) + pad*(len(cells)-1)
H = max(c.height for _,c in cells) + 30
canvas = Image.new("RGBA",(W,H),(45,45,48,255)); d = ImageDraw.Draw(canvas)
x = 0
for f, im in cells:
    canvas.paste(im,(x,28),im)
    d.text((x+2,6), f.replace("_skill_3_howl.png",""), fill=(235,235,235,255))
    x += im.width + pad
canvas.save("D:/unity/plan go/Musical Sprite/Tools/_cmp_v9/montage_repeat_reverted.png")
print("saved", canvas.size)
