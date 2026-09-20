"""
附魔音符生成器 v5 —— 区域感知 + 亮度保真换色（修复 v4 三大问题）

v4 三大问题(已量化定位):
  ① 脏色: build_refs 对同基色多像素技能色取均值 -> 混出样本外中间色
  ② 结构搅乱: 只用 Tap 建逐色LUT, 基色板不同的音符(Wide/Tap_Small/Slide_Link)错配, 明暗 r 暴跌甚至反转
  ③ 爪印失效: 默认基础图纯棕无金 -> claw_refs 空 -> 爪印从不映射成金

v5 方法:
  - 调色板直接来自样本(Tap普通 / Tap完成), 拆成 爪印(金)调色板 + 主体(棕)调色板, 各按亮度排序
  - 目标基础图像素: 区域=爪印(基础图里是金即 detect) 或 主体; 在该区域调色板内按【自身亮度】取最近样本色
    -> 输出必在样本调色板内(①解决), 且亮度单调映射保留结构(②解决)
  - 爪印: 基础图带爪印(金)的音符自动上金; 用户提供的带爪底图也能被 is_claw_color 检出(③解决)
  - 可选爪印mask: 若基础图爪印非金色不可检, 提供 Note_<t>_pawmask.png(白=爪印)放在 --maskdir

用法:
  python enchant_skin_v5.py --skill skill_3_howl --refdir 003 --outdir <目录> [--maskdir <目录>]
"""
import os, sys, math, bisect, argparse
from collections import deque
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP_ROOT = os.path.join(PROJ, "Assets/Art/UI/sprites")
sys.path.insert(0, os.path.join(PROJ, "Tools"))

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def lum(c):
    return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def is_claw_color(r, g, b):
    return r >= 222 and 118 <= g <= 200 and 55 <= b <= 155 and (r - b) >= 95

def build_sample_palette(sample_path):
    """返回 (claw_sorted[(L,color)...], body_sorted[...])"""
    px, (w, h) = load(sample_path)
    claw = []; body = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128: continue
            L = lum((r, g, b))
            (claw if is_claw_color(r, g, b) else body).append((L, (r, g, b)))
    claw.sort(key=lambda t: t[0]); body.sort(key=lambda t: t[0])
    return claw, body

def map_lum(L, pal):
    if not pal: return None
    i = bisect.bisect_left(pal, (L,))
    cands = []
    if i < len(pal): cands.append(pal[i])
    if i > 0: cands.append(pal[i-1])
    return min(cands, key=lambda p: abs(p[0]-L))[1]

NORMAL_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

def apply_v5(base_path, claw_pal, body_pal, mask_px, out_path):
    px, (w, h) = load(base_path)
    out = Image.new("RGBA", (w, h)); op = out.load()
    memo = {}
    n_claw = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            isc = (mask_px is not None and mask_px[x, y][0] >= 128) or is_claw_color(r, g, b)
            key = (r, g, b, isc)
            t = memo.get(key)
            if t is None:
                pal = claw_pal if isc else body_pal
                c = map_lum(lum((r, g, b)), pal)
                t = c if c is not None else (r, g, b)
                memo[key] = t
            if isc: n_claw += 1
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_path)
    return n_claw

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skill", required=True)
    ap.add_argument("--refdir", required=True)
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--maskdir", default=None, help="爪印mask目录(白=爪印, 非金爪印用)")
    a = ap.parse_args()
    ref = os.path.join(SP_ROOT, a.refdir)
    os.makedirs(a.outdir, exist_ok=True)
    clawN, bodyN = build_sample_palette(os.path.join(ref, f"Note_Tap_{a.skill}.png"))
    clawS, bodyS = build_sample_palette(os.path.join(ref, f"Note_Tap_Select_{a.skill}.png"))
    print(f"普通样本: 爪印色{len(clawN)}种 主体色{len(bodyN)}种 | 完成样本: 爪印色{len(clawS)}种 主体色{len(bodyS)}种")
    # 自检: 用普通调色板重建 Tap 自身
    apply_v5(os.path.join(SP_ROOT, "Note_Tap.png"), clawN, bodyN, None,
             os.path.join(a.outdir, "_selftest_Tap.png"))
    n = 0
    for t in NORMAL_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}.png")
        if not os.path.exists(bp): continue
        mask = None
        if a.maskdir:
            mp = os.path.join(a.maskdir, f"Note_{t}_pawmask.png")
            if os.path.exists(mp): mask, _ = load(mp)
        apply_v5(bp, clawN, bodyN, mask, os.path.join(a.outdir, f"Note_{t}_{a.skill}.png"))
        n += 1
    for t in SELECT_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}_Select.png")
        if not os.path.exists(bp): continue
        mask = None
        if a.maskdir:
            mp = os.path.join(a.maskdir, f"Note_{t}_pawmask.png")
            if os.path.exists(mp): mask, _ = load(mp)
        apply_v5(bp, clawS, bodyS, mask, os.path.join(a.outdir, f"Note_{t}_Select_{a.skill}.png"))
        n += 1
    print(f"已生成 {n} 张到 {a.outdir}")

if __name__ == "__main__":
    main()
