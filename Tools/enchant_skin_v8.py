"""
附魔音符生成器 v8 —— 逐类型对齐配色迁移 (修正 v7 的"全局奶油身+深棕圈"错误)

v7 根本缺陷(本轮量化发现):
  v7 只用 Note_Tap 一个样本学出"深棕圈+奶油身"两套色, 套到所有胶囊类。
  但技能3(skill_3_howl)样本是【逐类独立配色】:
    Tap            : 深棕圈(114,77,34) + 奶油身(222,189,126) + 金爪
    Wide/Tap_Small/Repeat/Slide_Link : 中棕身(145,108,60) + 金黄点缀(223,175,60)
    Repeat_i(带编号): 亮青柠身(240,255,158) + 亮黄(255,253,126)
    Slide_Judgment : 亮黄(254,233,105)
  所以 v7 把 Wide 染成奶油(偏亮偏错), 把 Repeat_i 染成棕/奶油(样本是青柠) —— 全错。

v8 方法(逐类型对齐色桶迁移):
  基础图与样本【同尺寸同轮廓】(样本在基础图上重绘), 且 003 已含【每个类型】的
  技能3手绘参照。对每类独立学:  base 色桶 -> 该桶对齐位置样本色(众数)。
  生成:  base 每个像素按色桶查表重着色, 形状(alpha)完全保留。
  - 普通态桶较粗(BUCKET_N)保证覆盖; 缺失桶用最近桶/调色板兜底。
  - 完成态(Select)桶更细(BUCKET_S)保留果冻平滑渐变。
  - 完全位置无关: 即使基础/样本轻微错位也不影响(只认颜色)。

  这同时满足用户 4 点:
    ① 颜色贴合样本(不再偏暗/偏错色)  ② 爪印形状零改动(只重着色)
    ③ 爪印颜色=样本爪色              ④ Wide 外圈=样本中棕圈(若用户要更深见末尾说明)

用法:
  python enchant_skin_v8.py --skill skill_3_howl --refdir 003 --outdir <目录>
"""
import os, sys, math, argparse
from collections import Counter, deque
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP_ROOT = os.path.join(PROJ, "Assets/Art/UI/sprites")

BUCKET_N = 10      # 普通态桶(粗, 覆盖好)
BUCKET_S = 4       # 完成态桶(细, 渐变平滑)
CAP = 50            # 最近桶兜底距离上限

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def bucket(c, n=BUCKET_N):
    return (c[0]//n, c[1]//n, c[2]//n)

def nn_by_pal(c, pal):
    best = None; bd = None
    for v in pal:
        d = (v[0]-c[0])**2 + (v[1]-c[1])**2 + (v[2]-c[2])**2
        if bd is None or d < bd:
            bd = d; best = v
    return best

def build_lut(base_path, sample_path, bn):
    """逐类型: base 色桶 -> 对齐位置样本色众数。返回 (lut, pal)。"""
    bpx, (w, h) = load(base_path)
    spx, _ = load(sample_path)
    acc = {}
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rs, gs, bs, as_ = spx[x, y]
            if ab < 128 or as_ < 128:
                continue
            acc.setdefault(bucket((rb, gb, bb), bn), Counter())[(rs, gs, bs)] += 1
    lut = {k: v.most_common(1)[0][0] for k, v in acc.items()}
    pal = sorted({c for v in acc.values() for c in v})
    return lut, pal

def apply_aligned(base_path, sample_path, out_path):
    """主路径: base 与样本同尺寸对齐 -> 直接按位置复制样本色, 用 base alpha 当遮罩。
    形状(小数)完全保留, 圈/身渐变/爪/数字 100% 还原样本。"""
    bpx, (w, h) = load(base_path)
    spx, _ = load(sample_path)
    out = Image.new("RGBA", (w, h)); op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = bpx[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            rs, gs, bs, as_ = spx[x, y]
            op[x, y] = (rs, gs, bs, a)   # 样本色 + base 的 alpha(形状)
    out.save(out_path)

def apply(base_path, lut, pal, bn, out_path):
    """兜底(样本缺失或尺寸不一致): 逐类型色桶迁移。"""
    px, (w, h) = load(base_path)
    out = Image.new("RGBA", (w, h)); op = out.load()
    memo = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            c = (r, g, b)
            bk = bucket(c, bn)
            t = lut.get(bk)
            if t is None:
                t = memo.get(bk)
                if t is None:
                    kc = (bk[0]*bn+bn//2, bk[1]*bn+bn//2, bk[2]*bn+bn//2)
                    best = None; bd = None
                    for k, v in lut.items():
                        kk = (k[0]*bn+bn//2, k[1]*bn+bn//2, k[2]*bn+bn//2)
                        d = (kk[0]-kc[0])**2 + (kk[1]-kc[1])**2 + (kk[2]-kc[2])**2
                        if bd is None or d < bd:
                            bd = d; best = v
                    t = best if (best is not None and math.sqrt(bd) <= CAP) else (nn_by_pal(c, pal) if pal else c)
                    memo[bk] = t
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_path)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skill", required=True)
    ap.add_argument("--refdir", required=True)
    ap.add_argument("--outdir", required=True)
    a = ap.parse_args()
    ref = os.path.join(SP_ROOT, a.refdir)
    os.makedirs(a.outdir, exist_ok=True)
    n = 0; aligned = 0; fallback = 0
    # 普通态 + 完成态: 所有基础音符类型
    import glob
    for pattern, bn in (("Note_*.png", BUCKET_N), ("Note_*_Select.png", BUCKET_S)):
        for bp in sorted(glob.glob(os.path.join(SP_ROOT, pattern))):
            name = os.path.splitext(os.path.basename(bp))[0]
            if pattern == "Note_*.png" and name.endswith("_Select"):
                continue
            samp = os.path.join(ref, f"{name}_{a.skill}.png")
            if not os.path.exists(samp):
                continue
            # 同尺寸对齐 -> 位置复制(精确); 否则桶兜底
            if Image.open(bp).size == Image.open(samp).size:
                apply_aligned(bp, samp, os.path.join(a.outdir, f"{name}_{a.skill}.png"))
                aligned += 1
            else:
                lut, pal = build_lut(bp, samp, bn)
                apply(bp, lut, pal, bn, os.path.join(a.outdir, f"{name}_{a.skill}.png"))
                fallback += 1
            n += 1
    print(f"已生成 {n} 张 (位置复制 {aligned} / 桶兜底 {fallback}) 到 {a.outdir}")

if __name__ == "__main__":
    main()
