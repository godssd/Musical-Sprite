"""
附魔改色生成器（修正版，v2）。

核心修正：旧版按"基础图明度"分暗/中/亮三档去套技能调色板，
但实测 004 范例的真实改法是"按改色后 skill4 目标的明度"分档。
两者不一致率 ~18.6%，且错的全是亮部/高光，导致 skill_3 发金发飘。

正确方法（从 004 范例学习，作为 ground truth）：
  1) 用 004 里 6 种基础音符的 (base -> skill4) 成对图，建立逐基色 -> skill4 颜色的 LUT。
  2) 对每个基础音符像素：查最近基色 -> 得到 skill4 目标色 -> 按 skill4 目标色明度定档(暗<90/中<170/亮>=170)
     -> 输出 技能调色板[该档]。
  3) 技能调色板从"该技能 Tap 普通态+完成态"两张参照图按明度三档吸取（不造色）。

这样：基础图里"橙红描边"在 004 映射到 暗绿 -> skill_3 就映射到 暗棕；
     基础图里"米白主体"在 004 映射到 中绿 -> skill_3 就映射到 中棕；
     基础图里"金色高光"在 004 映射到 亮青 -> skill_3 就映射到 亮金。
与 004 的改色结构完全一致，只是换成该技能的配色。

用法：
  python Tools/enchant_tier.py
生成到 OUT_DIR（默认 003 同级，可用参数改预览目录）。
"""
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SP = os.path.join(ROOT, "Assets", "Art", "UI", "sprites")
F4 = os.path.join(SP, "004")

# ---- 配置（按技能调整）----
SKILL = "skill_3_howl"
REF_DIR = os.path.join(SP, "003")          # 该技能 Tap 参照图所在目录
OUT_DIR = os.path.join(SP, "003")          # 输出目录（预览时改这里）
# 如果 REF_DIR 与 OUT_DIR 不同（预览），生成到 OUT_DIR

# 004 里 6 种基础音符的 (base文件名, 004技能文件名) —— 注意 Tap_Small 双下划线历史坑
PAIRS_NORM = [
    ("Note_Tap.png",        "Note_Tap_skill_4_croissant_heal.png"),
    ("Note_Wide.png",       "Note_Wide_skill_4_croissant_heal.png"),
    ("Note_Tap_Small.png",  "Note_Tap_Small__skill_4_croissant_heal.png"),
    ("Note_Repeat.png",     "Note_Repeat_skill_4_croissant_heal.png"),
    ("Note_Repeat_0.png",   "Note_Repeat_0_skill_4_croissant_heal.png"),
    ("Note_Slide_Link.png", "Note_Slide_Link_skill_4_croissant_heal.png"),
]
PAIRS_SEL = [
    ("Note_Tap_Select.png",        "Note_Tap_Select_skill_4_croissant_heal.png"),
    ("Note_Wide_Select.png",       "Note_Wide_Select_skill_4_croissant_heal.png"),
    ("Note_Tap_Small_Select.png",  "Note_Tap_Small_Select__skill_4_croissant_heal.png"),
    ("Note_Repeat_Select.png",     "Note_Repeat_Select_skill_4_croissant_heal.png"),
    ("Note_Repeat_0_Select.png",   "Note_Repeat_0_Select_skill_4_croissant_heal.png"),
    ("Note_Slide_Link_Select.png", "Note_Slide_Link_Select_skill_4_croissant_heal.png"),
]

# 需要生成的音符类型（跳过 REF_DIR 已存在的 Tap 参照，不二次海报化）
TYPES = ["Wide", "Tap_Small", "Repeat", "Repeat_0",
         "Repeat_1", "Repeat_2", "Repeat_3", "Repeat_4", "Repeat_5",
         "Repeat_6", "Repeat_7", "Repeat_8", "Repeat_9",
         "Slide_Link", "Slide_Judgment"]


def load(p):
    im = Image.open(p).convert("RGBA")
    return im.load(), im.size


def lum(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def tier(L):
    return 0 if L < 90 else (1 if L < 170 else 2)   # 0暗 1中 2亮


def build_lut(pairs):
    M = {}
    for base, sk in pairs:
        bp, bsz = load(os.path.join(SP, base))
        spx, ssz = load(os.path.join(F4, sk))
        w, h = bsz
        for y in range(h):
            for x in range(w):
                rb, gb, bb, ab = bp[x, y]
                rt, gt, bt, at = spx[x, y]
                if ab < 128 or at < 128:
                    continue
                key = (rb // 12 * 12, gb // 12 * 12, bb // 12 * 12)
                M.setdefault(key, []).append((rt, gt, bt))
    return {k: tuple(round(sum(c[i] for c in v) / len(v)) for i in range(3)) for k, v in M.items()}


def nearest(src, M):
    best = None
    bd = 1e18
    for k in M:
        d = (src[0] - k[0]) ** 2 + (src[1] - k[1]) ** 2 + (src[2] - k[2]) ** 2
        if d < bd:
            bd = d
            best = k
    return M[best]


def skill_pal(p):
    px, sz = load(os.path.join(REF_DIR, p))
    w, h = sz
    bk = [[], [], []]
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                continue
            bk[tier(lum(r, g, b))].append((r, g, b))
    out = []
    for c in bk:
        if not c:
            out.append(None)
        else:
            out.append(tuple(round(sum(q[i] for q in c) / len(c)) for i in range(3)))
    return out


def recolor(base_path, M, pal, fallback_pal):
    """对单张基础图改色：base像素 -> 查最近的 M 基色 -> skill4目标 -> 目标明度定档 -> pal[档]。"""
    bp, bsz = load(base_path)
    w, h = bsz
    out = Image.new("RGBA", (w, h))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = bp[x, y]
            if a < 128:
                op[x, y] = (0, 0, 0, 0)
                continue
            key = (r // 12 * 12, g // 12 * 12, b // 12 * 12)
            sk = M.get(key) or nearest((r, g, b), M)
            tt = tier(lum(*sk))
            col = pal[tt] or fallback_pal[tt] or pal[1] or fallback_pal[1]
            op[x, y] = (col[0], col[1], col[2], a)
    return out


def main():
    M_norm = build_lut(PAIRS_NORM)
    M_sel = build_lut(PAIRS_SEL)
    s3n = skill_pal(f"Note_Tap_{SKILL}.png")
    s3s = skill_pal(f"Note_Tap_Select_{SKILL}.png")
    print(f"skill 调色板 普通态: {s3n}")
    print(f"skill 调色板 完成态: {s3s}")
    os.makedirs(OUT_DIR, exist_ok=True)
    n = 0
    for t in TYPES:
        # 普通态
        bp = os.path.join(SP, f"Note_{t}.png")
        if os.path.exists(bp):
            out = recolor(bp, M_norm, s3n, s3s)
            out.save(os.path.join(OUT_DIR, f"Note_{t}_{SKILL}.png"))
            n += 1
        # 完成态（Slide_Judgment 无完成态）
        bps = os.path.join(SP, f"Note_{t}_Select.png")
        if os.path.exists(bps):
            out = recolor(bps, M_sel, s3s, s3n)
            out.save(os.path.join(OUT_DIR, f"Note_{t}_Select_{SKILL}.png"))
            n += 1
    print(f"已生成 {n} 张到 {OUT_DIR}")


if __name__ == "__main__":
    main()
