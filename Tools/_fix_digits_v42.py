# -*- coding: utf-8 -*-
"""
修复普通数字（Note_Repeat_0..9 skill_3_howl）颜色:
  内色(填充) -> 点击内圈亮黄 (255,246,98)
  外色(描边) -> 外圈暗金     (191,153,88)
方法: 轴投影平涂 (逐像素沿 old 轴投影 t, 输出 lerp(new描边, new填充, t))
先备份到 Tools/_bak_digit_fix/
"""
from PIL import Image
import os, shutil, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
BAK  = r"D:\unity\plan go\Musical Sprite\Tools\_bak_digit_fix"
OUT  = r"D:\unity\plan go\Musical Sprite\Tools\_cmp_v9"
os.makedirs(BAK, exist_ok=True)

S_OLD = (176, 141, 77)    # 旧描边(金棕)
F_OLD = (240, 255, 158)   # 旧填充(黄绿)
S_NEW = (191, 153, 88)    # 新描边 = Tap 外圈暗金
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

names = [f"Note_Repeat_{n}_skill_3_howl.png" for n in range(10)]
for name in names:
    src = os.path.join(BASE, name)
    shutil.copy2(src, os.path.join(BAK, name))          # 备份
    im = Image.open(src).convert("RGBA")
    before = collections.Counter(p[:3] for p in im.getdata() if p[3] >= 10)
    im = remap(im)
    im.save(src)
    after = collections.Counter(p[:3] for p in im.getdata() if p[3] >= 10)
    print(f"{name}: 前{before.most_common(2)} -> 后{after.most_common(2)}")

# ---- 对比图: 每个数字 前/后 并排 ----
tiles = []
for name in names:
    a = Image.open(os.path.join(BAK, name)).convert("RGBA")
    b = Image.open(os.path.join(BASE, name)).convert("RGBA")
    pair = Image.new("RGBA", (a.width + b.width + 6, max(a.height, b.height)), (40, 40, 48, 255))
    pair.alpha_composite(a, (0, 0))
    pair.alpha_composite(b, (a.width + 6, 0))
    tiles.append(pair)
W = sum(t.width for t in tiles) + 4 * (len(tiles) - 1)
H = max(t.height for t in tiles)
sheet = Image.new("RGBA", (W, H), (40, 40, 48, 255))
x = 0
for t in tiles:
    sheet.alpha_composite(t, (x, (H - t.height) // 2)); x += t.width + 4
sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
sheet.convert("RGB").save(os.path.join(OUT, "_digit_fix_cmp.png"))
print("对比图:", os.path.join(OUT, "_digit_fix_cmp.png"))
