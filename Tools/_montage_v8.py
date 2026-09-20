"""拼 base | v8(skill_3_howl) | 004(skill_4) 三栏对比图"""
import os
from PIL import Image, ImageDraw, ImageFont

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v8"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_cmp_v8"
os.makedirs(OUT, exist_ok=True)

CELL = 170
PAD = 12
BG = (90, 90, 100)
HEADER_H = 34
ROWLABEL_W = 130

def font(sz):
    try:
        return ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", sz)
    except Exception:
        return ImageFont.load_default()

def place(img, src, x, y):
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    s = min(CELL / w, CELL / h) * 0.92
    im = im.resize((max(1, int(w*s)), max(1, int(h*s))), Image.LANCZOS)
    cw, ch = im.size
    img.paste(im, (x + (CELL-cw)//2, y + (CELL-ch)//2), im)

def cell_bg(d, x, y):
    d.rectangle([x, y, x+CELL-1, y+CELL-1], fill=BG)
    d.rectangle([x, y, x+CELL-1, y+CELL-1], outline=(140,140,150), width=1)

types = [
    ("Tap",        "Note_Tap.png",            "Note_Tap_skill_3_howl.png",            "Note_Tap_skill_4_croissant_heal.png"),
    ("Wide",       "Note_Wide.png",           "Note_Wide_skill_3_howl.png",           "Note_Wide_skill_4_croissant_heal.png"),
    ("Tap_Small",  "Note_Tap_Small.png",       "Note_Tap_Small_skill_3_howl.png",      "Note_Tap_Small_skill_4_croissant_heal.png"),
    ("Repeat_5",   "Note_Repeat_5.png",        "Note_Repeat_5_skill_3_howl.png",       "Note_Repeat_5_skill_4_croissant_heal.png"),
    ("Slide_Link", "Note_Slide_Link.png",      "Note_Slide_Link_skill_3_howl.png",     "Note_Slide_Link_skill_4_croissant_heal.png"),
    ("Tap_Select", "Note_Tap_Select.png",      "Note_Tap_Select_skill_3_howl.png",     "Note_Tap_Select_skill_4_croissant_heal.png"),
    ("Wide_Select","Note_Wide_Select.png",     "Note_Wide_Select_skill_3_howl.png",    "Note_Wide_Select_skill_4_croissant_heal.png"),
    ("Repeat_5_Select","Note_Repeat_5_Select.png","Note_Repeat_5_Select_skill_3_howl.png","Note_Repeat_5_Select_skill_4_croissant_heal.png"),
]

cols = ["基础 (base)", "v8 (skill_3)", "004 (skill_4)"]
W = ROWLABEL_W + PAD + CELL*3 + PAD*2
H = HEADER_H + PAD + len(types)*(CELL+PAD)
img = Image.new("RGB", (W, H), (60, 60, 68))
d = ImageDraw.Draw(img)
f = font(16); fh = font(20)
for i, c in enumerate(cols):
    d.text((ROWLABEL_W + PAD + i*CELL + CELL//2, HEADER_H//2), c, fill=(235,235,240), font=fh, anchor="mm")
d.line([(0, HEADER_H), (W, HEADER_H)], fill=(140,140,150), width=1)

for ri, (label, base, v8, ref4) in enumerate(types):
    y = HEADER_H + PAD + ri*(CELL+PAD)
    d.text((ROWLABEL_W//2, y+CELL//2), label, fill=(210,210,220), font=f, anchor="mm")
    for i, fn in enumerate((base, v8, ref4)):
        x = ROWLABEL_W + PAD + i*CELL
        cell_bg(d, x, y)
        src = os.path.join(SP, fn) if i == 0 else (os.path.join(GEN, fn) if i == 1 else os.path.join(SP, "004", fn))
        if os.path.exists(src):
            place(img, src, x, y)
        else:
            d.text((x+CELL//2, y+CELL//2), "— 无 —", fill=(160,160,170), font=f, anchor="mm")

out = os.path.join(OUT, "montage_base_v8_004.png")
img.save(out)
print("已生成对比图:", out, img.size)
