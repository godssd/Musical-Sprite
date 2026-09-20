# -*- coding: utf-8 -*-
# 最终提交确认图：003 目录提交后的最终状态
import os
from PIL import Image, ImageDraw, ImageFont

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_cmp_v9"

CELL = 150; PAD = 12; BG = (90, 90, 100); HH = 34; RLW = 130

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

def p(name):
    return os.path.join(SP, "003", name + "_skill_3_howl.png")

rows = [
    ("Tap(样本·未动)",  [p("Note_Tap"),          p("Note_Tap_Select")]),
    ("Tap_Small(新版)", [p("Note_Tap_Small"),    p("Note_Tap_Small_Select")]),
    ("Wide(新版)",      [p("Note_Wide"),         p("Note_Wide_Select")]),
    ("Repeat(旧版保留)", [p("Note_Repeat"),      p("Note_Repeat_Select")]),
    ("数字0(旧版保留)",  [p("Note_Repeat_0"),    p("Note_Repeat_0_Select")]),
    ("数字5(旧版保留)",  [p("Note_Repeat_5"),    p("Note_Repeat_5_Select")]),
    ("Slide(新版)",     [p("Note_Slide_Link"),   p("Note_Slide_Link_Select")]),
]
W = RLW + PAD + CELL*2 + PAD*2
H = HH + PAD + len(rows)*(CELL+PAD)
img = Image.new("RGB", (W, H), (60,60,68)); d = ImageDraw.Draw(img)
f = font(14); fh = font(18)
for i, c in enumerate(["普通态", "Select态"]):
    d.text((RLW+PAD+i*CELL+CELL//2, HH//2), c, fill=(235,235,240), font=fh, anchor="mm")
d.line([(0,HH),(W,HH)], fill=(140,140,150), width=1)
for ri, (lab, srcs) in enumerate(rows):
    y = HH+PAD+ri*(CELL+PAD)
    d.text((RLW//2, y+CELL//2), lab, fill=(210,210,220), font=f, anchor="mm")
    for i, src in enumerate(srcs):
        x = RLW+PAD+i*CELL; cb(d, x, y); place(img, src, x, y)
out = os.path.join(OUT, "montage_final_003_committed.png")
img.save(out)
print("完成:", out, img.size)
