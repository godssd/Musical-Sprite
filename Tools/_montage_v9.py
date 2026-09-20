# -*- coding: utf-8 -*-
import os
from PIL import Image, ImageDraw, ImageFont

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_cmp_v9"
os.makedirs(OUT, exist_ok=True)

CELL = 150; PAD = 12; BG = (90, 90, 100); HH = 34; RLW = 120

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

def montage(rows, cols, title, outname):
    W = RLW + PAD + CELL*len(cols) + PAD*2
    H = HH + PAD + len(rows)*(CELL+PAD)
    img = Image.new("RGB", (W, H), (60,60,68)); d = ImageDraw.Draw(img)
    f = font(14); fh = font(18)
    for i, c in enumerate(cols):
        d.text((RLW+PAD+i*CELL+CELL//2, HH//2), c, fill=(235,235,240), font=fh, anchor="mm")
    d.line([(0,HH),(W,HH)], fill=(140,140,150), width=1)
    for ri, (lab, srcs) in enumerate(rows):
        y = HH+PAD+ri*(CELL+PAD)
        d.text((RLW//2, y+CELL//2), lab, fill=(210,210,220), font=f, anchor="mm")
        for i, src in enumerate(srcs):
            x = RLW+PAD+i*CELL; cb(d, x, y); place(img, src, x, y)
    img.save(os.path.join(OUT, outname))
    print("拼图", outname, img.size)

# 列解析
def p(name):  # v9 生成
    return os.path.join(GEN, name + "_skill_3_howl.png")
def base(name):
    return os.path.join(SP, name + ".png")
def old(name):  # 003 当前(旧/待替换)
    return os.path.join(SP, "003", name + "_skill_3_howl.png")
def s4(name):   # 004 参照
    sp = os.path.join(SP, "004", name + "_skill_4_croissant_heal.png")
    if os.path.exists(sp): return sp
    sp2 = sp.replace("_skill_4", "_skill_4")  # 兼容双下划线
    sp2 = os.path.join(SP, "004", name + "__skill_4_croissant_heal.png")
    return sp2 if os.path.exists(sp2) else sp

# ① 普通态：基础 | v9 | 004（补深边）；Tap 行 v9 列直接显示美术样本（保留不动）
rowsA = [
    ("Tap(样本)",      [base("Note_Tap"),        old("Note_Tap"),      s4("Note_Tap")]),
    ("Wide",       [base("Note_Wide"),       p("Note_Wide"),       s4("Note_Wide")]),
    ("Tap_Small",  [base("Note_Tap_Small"),  p("Note_Tap_Small"),  s4("Note_Tap_Small")]),
    ("Note_Repeat",[base("Note_Repeat"),     p("Note_Repeat"),     s4("Note_Repeat")]),
    ("Repeat_5",   [base("Note_Repeat_5"),   p("Note_Repeat_5"),   s4("Note_Repeat_5")]),
    ("Slide_Link", [base("Note_Slide_Link"), p("Note_Slide_Link"), s4("Note_Slide_Link")]),
]
montage(rowsA, ["基础", "v9(skill_3)", "004(参照)"], "普通态", "montage_normal_base_v9_004.png")

# ② Select 态：003旧 | v9 | 004（去斑点 + 无深边）；Tap_Select 行 v9 列显示美术样本
rowsB = [
    ("TapSel(样本)",    [old("Note_Tap_Select"),      old("Note_Tap_Select"),    s4("Note_Tap_Select")]),
    ("Wide_Select",     [old("Note_Wide_Select"),     p("Note_Wide_Select"),     s4("Note_Wide_Select")]),
    ("Tap_Small_Select",[old("Note_Tap_Small_Select"),p("Note_Tap_Small_Select"),s4("Note_Tap_Small_Select")]),
    ("Repeat_Select",   [old("Note_Repeat_Select"),   p("Note_Repeat_Select"),   s4("Note_Repeat_Select")]),
    ("Repeat_5_Select", [old("Note_Repeat_5_Select"), p("Note_Repeat_5_Select"), s4("Note_Repeat_5_Select")]),
    ("SlideLink_Select",[old("Note_Slide_Link_Select"),p("Note_Slide_Link_Select"),s4("Note_Slide_Link_Select")]),
]
montage(rowsB, ["003旧(待替换)", "v9(skill_3)", "004(参照)"], "Select态", "montage_select_old_v9_004.png")
