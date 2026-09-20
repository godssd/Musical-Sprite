# -*- coding: utf-8 -*-
"""分析 Repeat 数字（0）的内/外色结构：普通 + 完成两张"""
from PIL import Image
import os, collections

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"

def load(name):
    return Image.open(os.path.join(BASE, name)).convert("RGBA")

def top_colors(im, topn=16, box=None):
    cnt = collections.Counter()
    w, h = im.size
    px = im.load()
    x0, y0, x1, y1 = box or (0, 0, w, h)
    for y in range(y0, y1):
        for x in range(x0, x1):
            c = px[x, y]
            if c[3] < 10:
                continue
            cnt[(c[0], c[1], c[2])] += 1
    return cnt.most_common(topn)

for name in ["Note_Repeat_0_skill_3_howl.png", "Note_Repeat_0_Select_skill_3_howl.png",
             "Note_Repeat_0.png", "Note_Repeat_0_Select.png"]:
    im = load(name)
    w, h = im.size
    print("=" * 70)
    print(f"### {name}  size={w}x{h}")
    # 数字一般在note中下部，取中间区域找数字色
    print("全局top:", top_colors(im))
    # 中心带（数字区域, 竖直 30%-75%）
    box = (int(w*0.15), int(h*0.30), int(w*0.85), int(h*0.78))
    print("中心区top:", top_colors(im, 12, box))
