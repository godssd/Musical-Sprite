# -*- coding: utf-8 -*-
"""Tap_Select 爪印内圈/外圈色分析"""
from PIL import Image
import os, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"

im = Image.open(os.path.join(BASE, "Note_Tap_Select_skill_3_howl.png")).convert("RGBA")
px = im.load(); w, h = im.size

def vscan(x, label):
    runs = []
    for y in range(h):
        c = px[x, y]
        if runs and runs[-1][0] == c: runs[-1][1] += 1
        else: runs.append([c, 1])
    print(f"--- vscan x={x} ({label}) ---")
    print(" | ".join(f"{c[0]},{c[1]},{c[2]}x{n}" for c, n in runs))

# 大肉垫中心大约在 w*0.5, 先横扫 y=h*0.62 找肉垫位置
y = int(h*0.62)
runs = []
for x in range(w):
    c = px[x, y]
    if runs and runs[-1][0] == c: runs[-1][1] += 1
    else: runs.append([c, 1])
print(f"### Tap_Select 水平 y={y}:")
print(" | ".join(f"{c[0]},{c[1]},{c[2]}x{n}" for c, n in runs))

vscan(w//2, "中线穿大肉垫")
