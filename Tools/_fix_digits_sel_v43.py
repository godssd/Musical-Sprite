# -*- coding: utf-8 -*-
"""
修复完成数字（Note_Repeat_0..9_Select skill_3_howl）颜色:
  内色(填充) -> 亮黄 (255,246,98)   [=完成条芯=Tap内圈亮黄]
  外色(描边) -> 果冻金橙 (251,179,91) [=完成条边=Tap_Select果冻主色]
依据: 004样本规律 完成条芯=完成数字内色/边=完成数字外色; howl完成条第40轮已验收 (255,246,98)/(251,179,91)
先备份到 Tools/_bak_digit_fix_sel/
"""
from PIL import Image
import os, shutil, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
BAK  = r"D:\unity\plan go\Musical Sprite\Tools\_bak_digit_fix_sel"
OUT  = r"D:\unity\plan go\Musical Sprite\Tools\_cmp_v9"
os.makedirs(BAK, exist_ok=True)

S_OLD = (221, 191, 101)   # 旧描边(泛白金棕)
F_OLD = (238, 254, 209)   # 旧填充(泛白奶油)
S_NEW = (251, 179, 91)    # 新描边 = Tap_Select 果冻金橙
F_NEW = (255, 246, 98)    # 新填充 = Tap 内圈亮黄

dx = F_OLD[0]-S_OLD[0]; dy = F_OLD[1]-S_OLD[1]; dz = F_OLD[2]-S_OLD[2]
dd = dx*dx + dy*dy + dz*dz

def remap(im):
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            t = ((r-S_OLD[0])*dx + (g-S_OLD[1])*dy + (b-S_OLD[2])*dz) / dd
            t = 0.0 if t < 0 else (1.0 if t > 1 else t)
            px[x, y] = (round(S_NEW[0]+(F_NEW[0]-S_NEW[0])*t),
                        round(S_NEW[1]+(F_NEW[1]-S_NEW[1])*t),
                        round(S_NEW[2]+(F_NEW[2]-S_NEW[2])*t), a)
    return im

names = [f"Note_Repeat_{n}_Select_skill_3_howl.png" for n in range(10)]
for name in names:
    src = os.path.join(BASE, name)
    shutil.copy2(src, os.path.join(BAK, name))
    im = Image.open(src).convert("RGBA")
    before = collections.Counter(p[:3] for p in im.getdata() if p[3] >= 10)
    im = remap(im)
    im.save(src)
    after = collections.Counter(p[:3] for p in im.getdata() if p[3] >= 10)
    print(f"{name}: 前{before.most_common(2)} -> 后{after.most_common(2)}")

# ---- 对比图: 上排=普通(已修), 下排=完成(本次修), 附 Tap/Tap_Select 色板 ----
def load(base, n): return Image.open(os.path.join(base, n)).convert("RGBA")

tiles_sel = []
for n in range(10):
    a = Image.open(os.path.join(BAK, f"Note_Repeat_{n}_Select_skill_3_howl.png")).convert("RGBA")
    b = load(BASE, f"Note_Repeat_{n}_Select_skill_3_howl.png")
    pair = Image.new("RGBA", (a.width + b.width + 6, max(a.height, b.height)), (40, 40, 48, 255))
    pair.alpha_composite(a, (0, 0)); pair.alpha_composite(b, (a.width + 6, 0))
    tiles_sel.append(pair)
W = sum(t.width for t in tiles_sel) + 4*9
H = max(t.height for t in tiles_sel)
sheet = Image.new("RGBA", (W, H + 108), (40, 40, 48, 255))
x = 0
for t in tiles_sel:
    sheet.alpha_composite(t, (x, (H - t.height)//2)); x += t.width + 4
# 色板行: Tap外圈暗金 | Tap内圈亮黄 | 果冻金橙
sw = 64
pal = [((191,153,88),"Tap外圈"), ((255,246,98),"内圈亮黄"), ((251,179,91),"果冻金橙")]
px0 = 0
for c, _ in pal:
    r = Image.new("RGBA", (sw, sw), c + (255,))
    sheet.alpha_composite(r, (px0, H + 20)); px0 += sw + 8
sheet = sheet.resize((sheet.width*2, sheet.height*2), Image.NEAREST)
sheet.convert("RGB").save(os.path.join(OUT, "_digit_sel_fix_cmp.png"))
print("对比图:", os.path.join(OUT, "_digit_sel_fix_cmp.png"))
