"""
附魔音符生成器 v6 —— v5 + 爪印描边吸收（修复爪印周围深棕糊块）

v5 遗留问题(用户截图指认):
  基础图爪印周围有一圈深橄榄描边/阴影(如 (146,73,58)/(122,98,69)),
  不是金色 -> 走主体调色板按亮度映射 -> 命中样本最暗边缘色 -> 亮金爪印中间出现深棕糊块。
  而样本里爪印周围是通体亮金, 没有深色斑点。

v6 方法(v5 基础上仅加一步):
  - 爪印描边吸收: 从金色爪印像素出发 BFS, 只经过 [亮度<=165 且 距主体主色>30] 的像素(即深橄榄描边),
    深度<=6px, 吸收进爪印区 -> 这些像素改走爪印调色板按亮度映射 -> 变成样本爪印系的金色调。
  - 亮度<=165 止步: 不会碰到奶油主垫(高亮)与数字(远离爪印); 距主色>30 止步: 不会吃掉主体本身。
  - 其余逻辑与 v5 完全一致(区域感知 + 亮度保真 + 输出必在样本调色板内)。

用法:
  python enchant_skin_v6.py --skill skill_3_howl --refdir 003 --outdir <目录> [--maskdir <目录>]
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

def dist(c1, c2):
    return math.sqrt((c1[0]-c2[0])**2 + (c1[1]-c2[1])**2 + (c1[2]-c2[2])**2)

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

def dominant_color(px, w, h):
    """主体主色 = 出现最多的不透明颜色"""
    from collections import Counter
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128: c[(r, g, b)] += 1
    return c.most_common(1)[0][0] if c else (0, 0, 0)

def absorb_outline(px, w, h, claw_mask, max_depth=6, l_max=165, dom_thresh=30):
    """从金色爪印像素 BFS 吸收深橄榄描边: 只走 亮度<=l_max 且 距主体主色>dom_thresh 的像素, 深度<=max_depth"""
    dom = dominant_color(px, w, h)
    seed = [(x, y) for y in range(h) for x in range(w) if claw_mask[y][x]]
    seen = set(seed)
    q = deque((x, y, 0) for x, y in seed)
    while q:
        x, y, d = q.popleft()
        if d >= max_depth: continue
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nx, ny = x+dx, y+dy
            if not (0 <= nx < w and 0 <= ny < h): continue
            if (nx, ny) in seen: continue
            r, g, b, a = px[nx, ny]
            if a < 128: continue
            if lum((r, g, b)) > l_max: continue          # 止步于亮色(奶油垫/高光)
            if dist((r, g, b), dom) <= dom_thresh: continue  # 止步于主体本身
            seen.add((nx, ny))
            claw_mask[ny][nx] = True
            q.append((nx, ny, d+1))
    return claw_mask

NORMAL_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

def quantize_pal(pal):
    """把调色板颜色按 /8 量化去重, 用于快速最近邻"""
    s = set()
    for _, c in pal:
        s.add((c[0]//8, c[1]//8, c[2]//8))
    return list(s)

def nearest_dist(c, qpal):
    dq = (c[0]//8, c[1]//8, c[2]//8)
    best = None
    for q in qpal:
        d = (q[0]-dq[0])**2 + (q[1]-dq[1])**2 + (q[2]-dq[2])**2
        if best is None or d < best:
            best = d
    return math.sqrt(best) if best is not None else 1e9

def apply_v6(base_path, claw_pal, body_pal, mask_px, out_path, absorb=True):
    px, (w, h) = load(base_path)
    # 1) 初始爪印掩码: 金色像素(或外部 mask)
    claw_mask = [[False]*w for _ in range(h)]
    n_gold = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128: continue
            g_ = (mask_px is not None and mask_px[x, y][0] >= 128) or is_claw_color(r, g, b)
            claw_mask[y][x] = g_
            if g_: n_gold += 1
    # 2) v6 新增: 吸收爪印周围的深色描边/阴影(仅完成态启用; 深度30)
    n_absorbed = 0
    if absorb and claw_pal:
        claw_mask = absorb_outline(px, w, h, claw_mask, max_depth=30)
        n_absorbed = sum(row.count(True) for row in claw_mask) - n_gold
    # 3) 映射(与 v5 一致; 被吸收像素亮度托底到爪印调色板最暗, 保证不出暗斑)
    #    完成态额外结构规则: 贴近本图主色(距离<=35)的表皮色 一律归爪印板 —— 表皮阴影(如 (237,115,77),
    #    距主色仅9但因 g<118 未过金色判定)必须与表皮同板, 否则会命中主体最暗橄榄色形成糊块
    claw_min_L = claw_pal[0][0] if claw_pal else 0
    dom = dominant_color(px, w, h) if absorb else None
    out = Image.new("RGBA", (w, h)); op = out.load()
    memo = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            isc = claw_mask[y][x]
            if not isc and absorb:
                if dist((r, g, b), dom) <= 35:
                    isc = True
            key = (r, g, b, isc)
            t = memo.get(key)
            if t is None:
                if isc:
                    L = max(lum((r, g, b)), claw_min_L)
                    t = map_lum(L, claw_pal)
                else:
                    t = map_lum(lum((r, g, b)), body_pal)
                t = t if t is not None else (r, g, b)
                memo[key] = t
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_path)
    return n_gold, n_absorbed

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
    print(f"普通样本: 爪印色{len(clawN)}种(最暗L={clawN[0][0]:.0f}) 主体色{len(bodyN)}种 | "
          f"完成样本: 爪印色{len(clawS)}种(最暗L={clawS[0][0]:.0f}) 主体色{len(bodyS)}种")
    # 自检: 用普通调色板重建 Tap 自身(普通态不吸收)
    apply_v6(os.path.join(SP_ROOT, "Note_Tap.png"), clawN, bodyN, None,
             os.path.join(a.outdir, "_selftest_Tap.png"), absorb=False)
    n = 0
    for t in NORMAL_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}.png")
        if not os.path.exists(bp): continue
        mask = None
        if a.maskdir:
            mp = os.path.join(a.maskdir, f"Note_{t}_pawmask.png")
            if os.path.exists(mp): mask, _ = load(mp)
        ng, na = apply_v6(bp, clawN, bodyN, mask, os.path.join(a.outdir, f"Note_{t}_{a.skill}.png"), absorb=False)
        print(f"  Note_{t}: 金{ng} 吸收描边{na}")
        n += 1
    for t in SELECT_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}_Select.png")
        if not os.path.exists(bp): continue
        mask = None
        if a.maskdir:
            mp = os.path.join(a.maskdir, f"Note_{t}_pawmask.png")
            if os.path.exists(mp): mask, _ = load(mp)
        ng, na = apply_v6(bp, clawS, bodyS, mask, os.path.join(a.outdir, f"Note_{t}_Select_{a.skill}.png"), absorb=True)
        print(f"  Note_{t}_Select: 金{ng} 吸收描边{na}")
        n += 1
    print(f"已生成 {n} 张到 {a.outdir}")

if __name__ == "__main__":
    main()
