# -*- coding: utf-8 -*-
"""诊断 Tap 内圈/外圈颜色 + Repeat 数字当前内/外色"""
from PIL import Image
import os, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
ALT  = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites"

def load(name, base=BASE):
    p = os.path.join(base, name)
    im = Image.open(p).convert("RGBA")
    return im, p

def scanline(im, axis, frac, label):
    """沿某轴打印一条扫描线的颜色段（合并相邻相同色）"""
    w, h = im.size
    px = im.load()
    if axis == "h":  # 水平线, y = frac*h
        y = int(h * frac)
        seq = [px[x, y] for x in range(w)]
    else:
        x = int(w * frac)
        seq = [px[x, y] for y in range(h)]
    # 合并连续
    runs = []
    for c in seq:
        if runs and runs[-1][0] == c:
            runs[-1][1] += 1
        else:
            runs.append([c, 1])
    txt = " | ".join(f"{c[0]},{c[1]},{c[2]}x{n}" for c, n in runs[:40])
    print(f"  [{label}] {txt}")

def top_colors(im, ignore_alpha_below=10, topn=14, region=None):
    cnt = collections.Counter()
    w, h = im.size
    px = im.load()
    x0, y0, x1, y1 = region or (0, 0, w, h)
    for y in range(y0, y1):
        for x in range(x0, x1):
            c = px[x, y]
            if c[3] < ignore_alpha_below:
                continue
            cnt[(c[0], c[1], c[2])] += 1
    return cnt.most_common(topn)

print("=" * 70)
print("### Note_Tap_skill_3_howl.png  (普通点击)")
im, _ = load("Note_Tap_skill_3_howl.png")
print("size:", im.size)
scanline(im, "h", 0.5, "水平中线")
scanline(im, "h", 0.3, "水平0.3")
scanline(im, "v", 0.5, "垂直中线")
print("top colors:", top_colors(im))

print()
print("=" * 70)
print("### Note_Tap_Select_skill_3_howl.png  (完成/选中点击)")
im, _ = load("Note_Tap_Select_skill_3_howl.png")
print("size:", im.size)
scanline(im, "h", 0.5, "水平中线")
scanline(im, "v", 0.5, "垂直中线")
print("top colors:", top_colors(im))
