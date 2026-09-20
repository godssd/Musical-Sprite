# -*- coding: utf-8 -*-
"""
_fix_link_bars.py — Note_Slide_Link / Note_Slide_Link_Select 重做（v13）
========================================================================
用户反馈（2026-09-19）：普通和完成正好跟数字的颜色是对应的。
004 实测对应规则（逐色验证成立）：
  Link普通: 芯 = 数字普通深描边色, 边 = 数字普通亮内芯色
  Link完成: 芯 = 数字完成亮内芯色, 边 = 数字完成描边色
003 已验收数字实测原色：
  数字普通: 描边 (176,141,77) 深金棕 / 内芯 (240,255,158) 亮黄
  数字完成: 描边 (221,191,101) 金棕   / 内芯 (238,254,209) 淡黄
=> 目标：
  Link普通: 芯(176,141,77) / 边(240,255,158)
  Link完成: 芯(238,254,209) / 边(221,191,101)
做法：轴投影平涂重映射（同 v12），AA 过渡保留，零渐变、零自造色。
"""
from PIL import Image
from collections import Counter

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
OUTDIR = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9"

JOBS = [
    {   # 普通条
        "src":  f"{SP}/Note_Slide_Link.png",
        "out":  f"{OUTDIR}/Note_Slide_Link_skill_3_howl.png",
        "RIM_B": (253, 147, 120),   # 基础边条
        "FILL_B": (187, 87, 59),    # 基础内芯
        "RIM_T": (240, 255, 158),   # 数字普通内芯亮黄
        "FILL_T": (176, 141, 77),   # 数字普通描边深金棕
    },
    {   # 完成条
        "src":  f"{SP}/Note_Slide_Link_Select.png",
        "out":  f"{OUTDIR}/Note_Slide_Link_Select_skill_3_howl.png",
        "RIM_B": (253, 196, 123),   # 基础边条
        "FILL_B": (255, 253, 169),  # 基础内芯
        "RIM_T": (221, 191, 101),   # 数字完成描边金棕
        "FILL_T": (238, 254, 209),  # 数字完成内芯淡黄
    },
]

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

for job in JOBS:
    im = Image.open(job["src"]).convert("RGBA")
    w, h = im.size
    px = im.load()
    out = Image.new("RGBA", (w, h))
    op = out.load()
    RB, FB, RT, FT = job["RIM_B"], job["FILL_B"], job["RIM_T"], job["FILL_T"]
    dx, dy, dz = (FB[i]-RB[i] for i in range(3))
    len2 = dx*dx + dy*dy + dz*dz
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a)
                continue
            t = ((r-RB[0])*dx + (g-RB[1])*dy + (b-RB[2])*dz) / len2
            t = max(0.0, min(1.0, t))
            c = lerp(RT, FT, t)
            op[x, y] = (c[0], c[1], c[2], a)
    out.save(job["out"])
    c2 = Counter()
    px2 = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px2[x, y]
            if a >= 128:
                c2[(r, g, b)] += 1
    print(job["out"].split("/")[-1], "主色:", c2.most_common(3))
