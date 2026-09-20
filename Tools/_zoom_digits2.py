# -*- coding: utf-8 -*-
"""放大数字区域, 保存局部裁剪图供查看"""
from PIL import Image
import os

BASE = r"D:\unity\plan go\Musical Sprite\Assets\Art\UI\sprites\003"
OUT  = r"D:\unity\plan go\Musical Sprite\Tools\_cmp_v9"
os.makedirs(OUT, exist_ok=True)

jobs = [
    ("Note_Repeat_0_skill_3_howl.png", "digit0_normal"),
    ("Note_Repeat_0_Select_skill_3_howl.png", "digit0_select"),
    ("Note_Tap_skill_3_howl.png", "tap_normal"),
    ("Note_Tap_Select_skill_3_howl.png", "tap_select"),
    ("Note_Repeat_5_skill_3_howl.png", "digit5_normal"),
]
for name, tag in jobs:
    im = Image.open(os.path.join(BASE, name)).convert("RGBA")
    w, h = im.size
    # 数字区域: 中部
    box = (int(w*0.10), int(h*0.28), int(w*0.90), int(h*0.80))
    crop = im.crop(box)
    crop = crop.resize((crop.width*5, crop.height*5), Image.NEAREST)
    bg = Image.new("RGBA", crop.size, (40, 40, 48, 255))
    bg.alpha_composite(crop)
    bg.convert("RGB").save(os.path.join(OUT, f"_zoom_{tag}.png"))
    print(tag, crop.size)
