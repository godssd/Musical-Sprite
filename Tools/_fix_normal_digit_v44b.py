# -*- coding: utf-8 -*-
# 第44轮（修正版）：普通数字 + 两张 Slide_Link 条 取色修正
# 先还原 BAK（上一步 bug 版），再用正确公式重映射。
from PIL import Image
import os, shutil, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
BAK  = r"D:\unity\plan go\Musical Sprite\Tools\_bak_digit_fix_v44"
CMP  = r"D:\unity\plan go\Musical Sprite\Tools\_cmp_v9"

def remap(img, p0, p1, n0, n1):
    """轴投影平涂（修正）：new = n0 + t*(n1-n0)，t=像素在旧轴(p0->p1)上的投影位置。"""
    px = img.load(); w, h = img.size
    ax, ay, az = p1[0]-p0[0], p1[1]-p0[1], p1[2]-p0[2]
    denom = ax*ax + ay*ay + az*az
    if denom == 0: denom = 1
    nx, ny, nz = n1[0]-n0[0], n1[1]-n0[1], n1[2]-n0[2]
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 10:
                continue
            dx, dy, dz = r-p0[0], g-p0[1], b-p0[2]
            t = (dx*ax + dy*ay + dz*az) / denom
            if t < 0: t = 0
            elif t > 1: t = 1
            nr = n0[0] + t*nx
            ng = n0[1] + t*ny
            nb = n0[2] + t*nz
            nr = int(round(max(0, min(255, nr))))
            ng = int(round(max(0, min(255, ng))))
            nb = int(round(max(0, min(255, nb))))
            px[x, y] = (nr, ng, nb, a)
    return img

def topc(path, n=4):
    im = Image.open(path).convert("RGBA"); px = im.load()
    cnt = collections.Counter()
    for y in range(im.height):
        for x in range(im.width):
            c = px[x, y]
            if c[3] >= 10: cnt[(c[0], c[1], c[2])] += 1
    return " ; ".join(f"{c}x{k}" for c, k in cnt.most_common(n))

# 1) 还原 BAK -> BASE
files = [f"Note_Repeat_{n}_skill_3_howl.png" for n in range(10)] + \
        ["Note_Slide_Link_skill_3_howl.png", "Note_Slide_Link_Select_skill_3_howl.png"]
for f in files:
    shutil.copy(os.path.join(BAK, f), os.path.join(BASE, f))
print("restored from BAK")

# 2) 重新映射
dig_old = ((191,153,88), (255,246,98)); dig_new = ((114,77,34), (222,189,126))
print(">>> 普通数字 0-9")
for n in range(10):
    name = f"Note_Repeat_{n}_skill_3_howl.png"
    im = Image.open(os.path.join(BASE, name)).convert("RGBA")
    remap(im, dig_old[0], dig_old[1], dig_new[0], dig_new[1])
    im.save(os.path.join(BASE, name))
    print(f"  {name}: {topc(os.path.join(BASE, name))}")

barN = os.path.join(BASE, "Note_Slide_Link_skill_3_howl.png")
im = Image.open(barN).convert("RGBA")
remap(im, (176,141,77), (240,255,158), (114,77,34), (222,189,126))
im.save(barN); print(">>> 普通条:", topc(barN))

barS = os.path.join(BASE, "Note_Slide_Link_Select_skill_3_howl.png")
im = Image.open(barS).convert("RGBA")
remap(im, (221,191,101), (238,254,209), (251,179,91), (255,246,98))
im.save(barS); print(">>> 完成条:", topc(barS))

# 3) 对比图（旧=BAK，新=BASE）
def zoom(path, sc=4, bg=(40,40,48)):
    im = Image.open(path).convert("RGBA")
    im2 = im.resize((im.width*sc, im.height*sc), Image.NEAREST)
    b = Image.new("RGBA", im2.size, bg+(255,)); b.alpha_composite(im2)
    return b.convert("RGB")
def side(oldp, newp, tag):
    o = zoom(oldp); n = zoom(newp)
    W = o.width + n.width + 12
    canvas = Image.new("RGB", (W, max(o.height, n.height)), (40,40,48))
    canvas.paste(o, (0,0)); canvas.paste(n, (o.width+12, 0))
    canvas.save(os.path.join(CMP, tag)); print("saved", tag)
side(os.path.join(BAK, "Note_Repeat_0_skill_3_howl.png"), os.path.join(BASE, "Note_Repeat_0_skill_3_howl.png"), "_digit_v44_cmp.png")
side(os.path.join(BAK, "Note_Slide_Link_skill_3_howl.png"), barN, "_barN_v44_cmp.png")
side(os.path.join(BAK, "Note_Slide_Link_Select_skill_3_howl.png"), barS, "_barS_v44_cmp.png")
print("DONE")
