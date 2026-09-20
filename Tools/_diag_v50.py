# -*- coding: utf-8 -*-
# 诊断第50轮：用户打回 Note_Tap_Small / Note_Repeat(generic) 两张 005 normal
from PIL import Image
from collections import Counter
import os

ROOT = r"D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"

def load(p):
    return Image.open(os.path.join(ROOT, p)).convert("RGBA")

def top_colors(im, n=10, min_a=200):
    px = list(im.getdata())
    c = Counter((r,g,b) for r,g,b,a in px if a >= min_a)
    return c.most_common(n)

def ring_profile(im, name):
    """沿垂直中线和水平中线,从边缘向内打印颜色变化,测环厚"""
    W, H = im.size
    px = im.load()
    print(f"-- {name} size=({W},{H}) 垂直中线自顶向下 (前24个不透明像素):")
    cx = W // 2
    cnt = 0
    for y in range(H):
        r,g,b,a = px[cx, y]
        if a >= 200:
            print(f"   y={y}: ({r},{g},{b})")
            cnt += 1
            if cnt >= 24: break

print("========== A. 005 Tap 样本(真美术) 环厚/配色基准 ==========")
tap_s = load("005/Note_Tap_skill_5_bomb_rain.png")
ring_profile(tap_s, "005 Tap 样本")
print("主色:", top_colors(tap_s, 8))

print()
print("========== B. 生成 Small(被打回) ==========")
small_g = load("005/Note_Tap_Small_skill_5_bomb_rain.png")
ring_profile(small_g, "生成 Small")
print("主色:", top_colors(small_g, 8))

print()
print("========== C. base Small(红棕平涂基底) ==========")
small_b = load("Note_Tap_Small.png")
ring_profile(small_b, "base Small")
print("主色:", top_colors(small_b, 8))

print()
print("========== D. howl Small(验收过的金琥珀) ==========")
small_h = load("003/Note_Tap_Small_skill_3_howl.png")
ring_profile(small_h, "howl Small")
print("主色:", top_colors(small_h, 8))

print()
print("========== E. 生成 generic Repeat(被打回) ==========")
rep_g = load("005/Note_Repeat_skill_5_bomb_rain.png")
print("size:", rep_g.size)
print("主色:", top_colors(rep_g, 12))
ring_profile(rep_g, "生成 generic Repeat")

print()
print("========== F. base generic Repeat ==========")
rep_b = load("Note_Repeat.png")
print("size:", rep_b.size)
print("主色:", top_colors(rep_b, 12))

print()
print("========== G. howl generic Repeat(验收过的) ==========")
rep_h = load("003/Note_Repeat_skill_3_howl.png")
print("size:", rep_h.size)
print("主色:", top_colors(rep_h, 12))
