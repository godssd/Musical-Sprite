# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
Z = 1
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
BAK = "D:/unity/plan go/Musical Sprite/Tools/_bak_link_fix"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9"

rows = [
    ("普通", f"{SP}/Note_Slide_Link.png",
            f"{BAK}/Note_Slide_Link_skill_3_howl.png",
            f"{GEN}/Note_Slide_Link_skill_3_howl.png",
            f"{SP}/004/Note_Slide_Link_skill_4_croissant_heal.png"),
    ("完成", f"{SP}/Note_Slide_Link_Select.png",
            f"{BAK}/Note_Slide_Link_Select_skill_3_howl.png",
            f"{GEN}/Note_Slide_Link_Select_skill_3_howl.png",
            f"{SP}/004/Note_Slide_Link_Select_skill_4_croissant_heal.png"),
]
ZD = 2
d5n = Image.open(f"{SP}/003/Note_Repeat_5_skill_3_howl.png").convert("RGBA")
d5s = Image.open(f"{SP}/003/Note_Repeat_5_Select_skill_3_howl.png").convert("RGBA")
d5n = d5n.resize((d5n.width*ZD, d5n.height*ZD), Image.NEAREST)
d5s = d5s.resize((d5s.width*ZD, d5s.height*ZD), Image.NEAREST)

pad = 14
bars = []
for label, *paths in rows:
    row = []
    for tag, p in [("base",paths[0]),("old",paths[1]),("new",paths[2]),("004",paths[3])]:
        im = Image.open(p).convert("RGBA")
        row.append((tag, im.resize((im.width*Z, im.height*Z), Image.NEAREST)))
    bars.append(row)

W = pad*7 + sum(im.width for _,im in bars[0]) + d5n.width + d5s.width
H = pad + (69*Z + 40)*2 + pad
canvas = Image.new("RGBA", (W, H), (45, 45, 48, 255))
d = ImageDraw.Draw(canvas)
y = pad
for r5, row in zip(rows, bars):
    label = r5[0]
    x = pad
    d.text((x, y+2), label, fill=(235,235,235,255))
    x += 30
    for tag, im in row:
        canvas.paste(im, (x, y+22), im)
        d.text((x+2, y+2), tag, fill=(200,200,200,255))
        x += im.width + pad
    ref = d5n if label == "普通" else d5s
    d.text((x+2, y+2), "digit", fill=(200,200,200,255))
    canvas.paste(ref, (x, y+22), ref)
    y += 69*Z + 40
canvas.save("D:/unity/plan go/Musical Sprite/Tools/_cmp_v9/montage_link_bars_fix.png")
print("saved", canvas.size)
