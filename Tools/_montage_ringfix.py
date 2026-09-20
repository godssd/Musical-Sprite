# -*- coding: utf-8 -*-
# 修复前后对比：基础 | 修复前(缺侧边圈) | 修复后 | 004参照
import os
from PIL import Image, ImageDraw, ImageFont

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9"
BAK = "D:/unity/plan go/Musical Sprite/Tools/_bak_ring_fix"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_cmp_v9"

CELL = 190; PAD = 12; BG = (90, 90, 100); HH = 34; RLW = 130

def font(sz):
    try: return ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", sz)
    except: return ImageFont.load_default()

def place(img, src, x, y):
    if not os.path.exists(src):
        d = ImageDraw.Draw(img)
        d.text((x+CELL//2, y+CELL//2), "—", fill=(160,160,170), font=font(20), anchor="mm")
        return
    im = Image.open(src).convert("RGBA"); w, h = im.size
    s = min(CELL/w, CELL/h) * 0.92
    im = im.resize((max(1, int(w*s)), max(1, int(h*s))), Image.LANCZOS)
    img.paste(im, (x + (CELL-im.size[0])//2, y + (CELL-im.size[1])//2), im)

def cb(d, x, y):
    d.rectangle([x, y, x+CELL-1, y+CELL-1], fill=BG)
    d.rectangle([x, y, x+CELL-1, y+CELL-1], outline=(140,140,150), width=1)

rows = [
    ("Wide", [SP+"/Note_Wide.png", BAK+"/Note_Wide_skill_3_howl.png",
              GEN+"/Note_Wide_skill_3_howl.png", SP+"/004/Note_Wide_skill_4_croissant_heal.png"]),
    ("Tap_Small", [SP+"/Note_Tap_Small.png", BAK+"/Note_Tap_Small_skill_3_howl.png",
                   GEN+"/Note_Tap_Small_skill_3_howl.png", SP+"/004/Note_Tap_Small_skill_4_croissant_heal.png"]),
]
cols = ["基础", "修复前(缺侧边圈)", "修复后", "004(参照)"]
W = RLW + PAD + CELL*len(cols) + PAD*2
H = HH + PAD + len(rows)*(CELL+PAD)
img = Image.new("RGB", (W, H), (60,60,68)); d = ImageDraw.Draw(img)
fh = font(16)
for i, c in enumerate(cols):
    d.text((RLW+PAD+i*CELL+CELL//2, HH//2), c, fill=(235,235,240), font=fh, anchor="mm")
d.line([(0,HH),(W,HH)], fill=(140,140,150), width=1)
for ri, (lab, srcs) in enumerate(rows):
    y = HH+PAD+ri*(CELL+PAD)
    d.text((RLW//2, y+CELL//2), lab, fill=(210,210,220), font=font(14), anchor="mm")
    for i, src in enumerate(srcs):
        x = RLW+PAD+i*CELL; cb(d, x, y); place(img, src, x, y)
p = os.path.join(OUT, "montage_ringfix_wide_tapsmall.png")
img.save(p); print("完成:", p, img.size)
