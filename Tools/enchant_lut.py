#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
附魔数字换色生成器（LUT 法）
================================
规则：用户手工调好某技能一种音符的「普通态/完成态」配色作为范例，
     本脚本用该范例的(基准图 -> 附魔图)逐像素配对建立颜色查表(LUT)，
     再把同一套基准数字(1-9)整体套用这套配色，输出带技能后缀的附魔贴图。

为什么用 LUT 而不是公式：用户换色不是单一全局色相旋转（实测各亮度分层偏移不同），
逐像素查表能 100% 复刻用户手工调的配色，不靠公式近似。

用法（默认针对 skill_4 连点数字）：
    python enchant_lut.py
可调参数见下方 CONFIG。复用：换其它技能时只需改 SKILL_SUFFIX / REF_TYPE 与基准目录。
"""
import os
from collections import defaultdict
from PIL import Image

# ----------------------------- 配置 -----------------------------
SPRITES = os.path.join(os.path.dirname(__file__), "..", "Assets", "Art", "UI", "sprites")
SPRITES = os.path.abspath(SPRITES)

SKILL_SUFFIX = "skill_4_croissant_heal"     # 技能专属后缀（其它技能换这个）
REF_DIR      = os.path.join(SPRITES, "004")  # 用户手工配色范例所在目录
BASE_DIR      = SPRITES                       # 基准原版数字贴图所在目录（也是输出目录）

# 范例参照类型：用 Repeat_0 的(基准,附魔)两对图建立 LUT
REF_BASE_NORMAL = os.path.join(BASE_DIR, "Note_Repeat_0.png")
REF_ENCH_NORMAL = os.path.join(REF_DIR,   "Note_Repeat_0_%s.png" % SKILL_SUFFIX)
REF_BASE_SELECT = os.path.join(BASE_DIR, "Note_Repeat_0_Select.png")
REF_ENCH_SELECT = os.path.join(REF_DIR,   "Note_Repeat_0_Select_%s.png" % SKILL_SUFFIX)

# 要生成的序号
DIGITS = list(range(1, 10))

# LUT 量化步长（RGB 各通道整除后作为键）；越小越精细
Q = 16
# ----------------------------------------------------------------

def build_lut(base_path, ench_path):
    """从(基准图, 附魔图)同坐标像素配对，建立 量化基准色 -> 平均附魔色 的查表。"""
    b = Image.open(base_path).convert("RGBA")
    e = Image.open(ench_path).convert("RGBA")
    if b.size != e.size:
        # 同类型尺寸应一致；不一致则缩放附魔图对齐
        e = e.resize(b.size)
    pb, pe = b.load(), e.load()
    w, h = b.size
    acc = defaultdict(lambda: [0, 0, 0, 0])  # key -> [sumR, sumG, sumB, count]
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = pb[x, y]
            re_, ge, be, ae = pe[x, y]
            if ab < 8 or ae < 8:
                continue  # 透明区不参与（保持形状靠 alpha，不靠颜色）
            key = (rb // Q, gb // Q, bb // Q)
            a = acc[key]
            a[0] += re_; a[1] += ge; a[2] += be; a[3] += 1
    lut = {}
    for k, (sr, sg, sb, n) in acc.items():
        lut[k] = (sr // n, sg // n, sb // n)
    return lut, list(lut.keys())

def nearest_key(r, g, b, keys):
    """LUT 未命中时找最近量化键（欧几里得）。"""
    best = None; best_d = None
    for k in keys:
        kr, kg, kb = k
        d = (kr * Q - r) ** 2 + (kg * Q - g) ** 2 + (kb * Q - b) ** 2
        if best_d is None or d < best_d:
            best_d = d; best = k
    return best

def apply_lut(src_path, out_path, lut, keys):
    """对基准数字图逐像素套用 LUT，保留 alpha，输出附魔图。"""
    im = Image.open(src_path).convert("RGBA")
    px = im.load()
    w, h = im.size
    out = Image.new("RGBA", (w, h))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 8:
                op[x, y] = (r, g, b, a)  # 透明：原样
                continue
            key = (r // Q, g // Q, b // Q)
            if key not in lut:
                key = nearest_key(r, g, b, keys)
            tr, tg, tb = lut[key]
            op[x, y] = (tr, tg, tb, a)
    out.save(out_path)
    print("  生成 %s" % os.path.basename(out_path))

def main():
    print("基准目录: %s" % BASE_DIR)
    print("范例目录: %s" % REF_DIR)
    print("技能后缀: %s" % SKILL_SUFFIX)

    print("\n[普通态] 用 Repeat_0 建立 LUT ...")
    lut_n, keys_n = build_lut(REF_BASE_NORMAL, REF_ENCH_NORMAL)
    print("[完成态] 用 Repeat_0_Select 建立 LUT ...")
    lut_s, keys_s = build_lut(REF_BASE_SELECT, REF_ENCH_SELECT)

    for d in DIGITS:
        base_n = os.path.join(BASE_DIR, "Note_Repeat_%d.png" % d)
        base_s = os.path.join(BASE_DIR, "Note_Repeat_%d_Select.png" % d)
        out_n  = os.path.join(BASE_DIR, "Note_Repeat_%d_%s.png" % (d, SKILL_SUFFIX))
        out_s  = os.path.join(BASE_DIR, "Note_Repeat_%d_Select_%s.png" % (d, SKILL_SUFFIX))
        if not os.path.exists(base_n):
            print("  ! 缺基准 %s 跳过" % os.path.basename(base_n)); continue
        apply_lut(base_n, out_n, lut_n, keys_n)
        if os.path.exists(base_s):
            apply_lut(base_s, out_s, lut_s, keys_s)
        else:
            print("  ! 缺基准 %s" % os.path.basename(base_s))
    print("\n完成：共生成 %d 张普通态 + %d 张完成态" % (len(DIGITS), len(DIGITS)))

if __name__ == "__main__":
    main()
