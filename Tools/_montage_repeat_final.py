# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
Z = 2
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
OLD = f"{SP}/003_backup_2026-09-19_final"
NEW = f"{SP}/003"
files = ["Note_Repeat_skill_3_howl.png","Note_Repeat_0_skill_3_howl.png",
         "Note_Repeat_5_skill_3_howl.png","Note_Repeat_Select_skill_3_howl.png",
         "Note_Repeat_9_Select_skill_3_howl.png"]
imgs=[]
for f in files:
    o = Image.open(f"{OLD}/{f}").convert("RGBA").resize((84*Z,302*Z),Image.NEAREST)
    nw = Image.open(f"{NEW}/{f}").convert("RGBA").resize((84*Z,302*Z),Image.NEAREST)
    imgs.append((f, o, nw))
W = 84*Z*2 + 40
H = sum(302*Z+34 for _ in imgs)
canvas = Image.new("RGBA",(W,H),(45,45,48,255)); d=ImageDraw.Draw(canvas)
y=0
for f,o,nw in imgs:
    d.text((4,y+4),"旧橄榄版",fill=(235,235,235,255))
    d.text((84*Z+20,y+4),"新v9",fill=(235,235,235,255))
    canvas.paste(o,(0,y+28),o); canvas.paste(nw,(84*Z+20,y+28),nw)
    y += 302*Z+34
canvas.save("D:/unity/plan go/Musical Sprite/Tools/_cmp_v9/montage_repeat_old_new.png")
print("saved", canvas.size)
