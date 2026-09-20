"""
附魔音符生成器 v7 —— 结构角色映射（修复 v5/v6 亮度映射的方向性错误）

v5/v6 根本缺陷:
  map_lum 按"亮度等值"映射: 基础身体棕(L114) -> 样本调色板里 L114 的颜色。
  但 skill_3 样本把 明暗关系反了: 身体棕(L114)->奶油(L192), 外圈是新画的深棕(L83)。
  结果: 整个身体错配到样本的深棕圈色 -> 全图深棕糊, 外圈结构丢失, 爪印颜色跑偏。

v7 方法(从对齐成对图学"结构角色"):
  基础图与样本同尺寸同轮廓(样本是在基础图上重绘的), 可逐像素学:
  普通态:
    - 环带区(到边距离 d<=13, 由样本环色占比标定): 基础色桶 -> 样本色众数  => 深棕圈
    - 内部区(d>13): 基础色桶 -> 样本色众数 => 奶油身体/金爪填充/金爪描边/白高光
    - 特例(依据 004 手绘参照):
        数字描边(222,116,85)族 -> 深棕圈色; 数字象牙填充(252,241,198)族 -> 奶油
        Slide_Link 长条 -> 深棕圈色, 条上鲑鱼爪 -> 浅金; Slide_Judgment 保持原样(004 无参照)
  完成态(果冻质感, 平滑渐变):
    - 1-NN 颜色迁移: /4 桶记忆化, 基础色 -> 对齐位置样本色(保留深边+辉光+奶油爪)
    - 数字: 金描边 -> 样本橙金, 象牙填充 -> 样本亮黄; Slide_Link: 条 -> 亮黄, 爪 -> 浅奶油

用法:
  python enchant_skin_v7.py --skill skill_3_howl --refdir 003 --outdir <目录>
"""
import os, sys, math, argparse
from collections import Counter, deque
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP_ROOT = os.path.join(PROJ, "Assets/Art/UI/sprites")

RING_D = 13          # 普通态环带厚度(由样本标定: d<=12 100%环色, d15 起 <50%)
BUCKET = 8           # 普通态颜色桶
BUCKET_S = 4         # 完成态颜色桶(平滑渐变需要更细)

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def lum(c):
    return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def bucket(c, n=BUCKET):
    return (c[0]//n, c[1]//n, c[2]//n)

def edge_dist(px, w, h):
    D = [[10**9]*w for _ in range(h)]
    q = deque()
    for y in range(h):
        for x in range(w):
            if px[x, y][3] < 128:
                D[y][x] = 0; q.append((x, y))
    while q:
        x, y = q.popleft()
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nx, ny = x+dx, y+dy
            if 0 <= nx < w and 0 <= ny < h and D[ny][nx] > D[y][x]+1:
                D[ny][nx] = D[y][x]+1; q.append((nx, ny))
    return D

def dist(c1, c2):
    return math.sqrt((c1[0]-c2[0])**2 + (c1[1]-c2[1])**2 + (c1[2]-c2[2])**2)

def is_digit_outline(c):
    r, g, b = c
    return r >= 205 and 100 <= g <= 132 and 60 <= b <= 105

def is_ivory(c):
    r, g, b = c
    return r >= 245 and g >= 225 and 150 <= b <= 230

def is_digit_gold(c):
    r, g, b = c
    return r >= 245 and 150 <= g <= 215 and b <= 60

def dominant_color(px, w, h):
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128: c[(r, g, b)] += 1
    return c.most_common(1)[0][0] if c else (0, 0, 0)

# ---------------- 普通态 ----------------

def build_normal_luts(base_path, sample_path):
    bpx, (w, h) = load(base_path)
    spx, _ = load(sample_path)
    D = edge_dist(bpx, w, h)
    ring_acc = {}; body_acc = {}
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rs, gs, bs, as_ = spx[x, y]
            if ab < 128 or as_ < 128: continue
            bk = bucket((rb, gb, bb))
            acc = ring_acc if D[y][x] <= RING_D else body_acc
            acc.setdefault(bk, Counter())[(rs, gs, bs)] += 1
    lut_ring = {k: v.most_common(1)[0][0] for k, v in ring_acc.items()}
    lut_body = {k: v.most_common(1)[0][0] for k, v in body_acc.items()}
    ring_cnt = Counter(); body_cnt = Counter()
    for v in ring_acc.values(): ring_cnt.update(v)
    for v in body_acc.values(): body_cnt.update(v)
    ring_mode = ring_cnt.most_common(1)[0][0]
    body_mode = body_cnt.most_common(1)[0][0]
    # 深一号奶油: 体部第二常见的浅色(排除主奶油), 对应基础图偏暗的结构线(面板线)
    cream2 = None
    for c, n in body_cnt.most_common(30):
        if c != body_mode and lum(c) > 170:
            cream2 = c; break
    ring_pal = sorted({c for v in ring_acc.values() for c in v})
    body_pal = sorted({c for v in body_acc.values() for c in v})
    return lut_ring, lut_body, ring_mode, body_mode, ring_pal, body_pal, cream2

def nn_in_lut(c, lut, cap=60):
    """最近基础色桶(桶中心距离<=cap), 返回其样本色; 找不到返回 None"""
    bk = bucket(c)
    best = None; bd = None
    for k, v in lut.items():
        kc = (k[0]*BUCKET+BUCKET//2, k[1]*BUCKET+BUCKET//2, k[2]*BUCKET+BUCKET//2)
        d = (kc[0]-c[0])**2 + (kc[1]-c[1])**2 + (kc[2]-c[2])**2
        if bd is None or d < bd:
            bd = d; best = v
    if best is not None and math.sqrt(bd) <= cap:
        return best
    return None

def nn_by_pal(c, pal):
    """调色板里最近色(兜底, 保证输出仍在样本调色板内)"""
    best = None; bd = None
    for v in pal:
        d = (v[0]-c[0])**2 + (v[1]-c[1])**2 + (v[2]-c[2])**2
        if bd is None or d < bd:
            bd = d; best = v
    return best

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

def apply_normal(base_path, luts, out_path, mode="capsule"):
    """mode: capsule | slidelink(条->圈色) | digit(数字: 描边->圈色 填充->奶油 AA插值) | keep(原样)"""
    lut_ring, lut_body, ring_mode, body_mode, ring_pal, body_pal, cream2 = luts
    px, (w, h) = load(base_path)
    out = Image.new("RGBA", (w, h)); op = out.load()
    if mode == "keep":
        for y in range(h):
            for x in range(w):
                op[x, y] = px[x, y]
        out.save(out_path); return
    D = edge_dist(px, w, h)
    dom = dominant_color(px, w, h)
    domL = lum(dom)
    cream = body_mode
    # 深一号奶油 = 样本浅色簇中第二常见的(L>170), 对应基础图偏暗的结构线(面板线)
    light = Counter()
    for v in lut_body.values():
        if lum(v) > 170: light[v] += 1
    cream2 = None
    if light:
        top2 = [c for c, _ in light.most_common(3)]
        cream2 = top2[1] if len(top2) > 1 else None
    gold = lut_body.get(bucket((241, 143, 119))) or body_mode
    memo = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            c = (r, g, b)
            bk = bucket(c)
            if mode == "digit":
                if r >= 245 and g >= 225:
                    branch, t = "iv", body_mode
                elif r >= 200 and g <= 132:
                    branch, t = "ol", ring_mode
                else:  # 描边<->填充 之间的抗锯齿: 按g线性插值
                    branch, t = "aa", lerp(ring_mode, body_mode, (g-132)/93.0)
                key = (bk, branch)
            elif mode == "slidelink":
                if c == dom:
                    branch, t = "bar", ring_mode
                elif g >= 135 and r >= 240:
                    branch, t = "paw", gold
                else:  # 条<->爪 之间的抗锯齿
                    branch, t = "aa", lerp(ring_mode, gold, (g-95)/55.0)
                key = (bk, branch)
            else:
                # 胶囊类: 贴近本图主色 = 身体(语义规则), 不依赖空间位置
                if dist(c, dom) <= 35:
                    t = (cream2 if (cream2 and lum(c) < domL - 8) else cream)
                    branch = "body"
                else:
                    branch = "other"
                    t = lut_body.get(bk) or nn_in_lut(c, lut_body) or nn_by_pal(c, body_pal)
                d = D[y][x]
                # 环带: 深棕, 11~16px 渐隐过渡到身体色
                if d <= 11:
                    t = ring_mode
                elif d <= 16 and branch == "body":
                    t = lerp(ring_mode, t, (d-11)/5.0)
                key = (bk, branch) if d > 16 else None
            if key is not None:
                t2 = memo.get(key)
                if t2 is not None:
                    t = t2
                else:
                    memo[key] = t
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_path)

# ---------------- 完成态 ----------------

def build_select_refs(base_path, sample_path):
    """1-NN 颜色迁移参照: /4 桶 -> 该桶对齐位置样本色列表"""
    bpx, (w, h) = load(base_path)
    spx, _ = load(sample_path)
    acc = {}
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rs, gs, bs, as_ = spx[x, y]
            if ab < 128 or as_ < 128: continue
            acc.setdefault(bucket((rb, gb, bb), BUCKET_S), Counter())[(rs, gs, bs)] += 1
    refs = {k: v.most_common(1)[0][0] for k, v in acc.items()}
    # 样本簇(给数字/长条特调用)
    samp = Counter()
    for v in refs.values(): samp[v] += 1
    samp_px, (sw, sh) = load(sample_path)
    allc = Counter()
    for y in range(sh):
        for x in range(sw):
            r, g, b, a = samp_px[x, y]
            if a >= 128: allc[(r, g, b)] += 1
    orange = Counter(); bright = Counter(); pale = Counter()
    for c, n in allc.items():
        r, g, b = c; L = lum(c)
        if b < 105 and L < 200: orange[c] += n
        if L >= 225: bright[c] += n
        elif 210 <= L < 225 and b >= 130: pale[c] += n
    f = lambda cc: cc.most_common(1)[0][0] if cc else (0, 0, 0)
    return refs, f(orange), f(bright), f(pale)

def nn_ref(c, refs, cap=80):
    bk = bucket(c, BUCKET_S)
    if bk in refs: return refs[bk]
    best = None; bd = None
    for k, v in refs.items():
        kc = (k[0]*BUCKET_S+BUCKET_S//2, k[1]*BUCKET_S+BUCKET_S//2, k[2]*BUCKET_S+BUCKET_S//2)
        d = (kc[0]-c[0])**2 + (kc[1]-c[1])**2 + (kc[2]-c[2])**2
        if bd is None or d < bd:
            bd = d; best = v
    if best is not None and math.sqrt(bd) <= cap:
        return best
    return None

def apply_select(base_path, refs, orange, bright, pale, out_path, mode="capsule"):
    px, (w, h) = load(base_path)
    out = Image.new("RGBA", (w, h)); op = out.load()
    dom = dominant_color(px, w, h) if mode == "slidelink" else None
    memo = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            c = (r, g, b)
            if mode == "digit":
                if is_digit_gold(c):
                    branch, t = "ol", orange
                elif r >= 245 and g >= 225:
                    branch, t = "iv", bright
                else:  # 金描边<->象牙填充 之间的抗锯齿
                    branch, t = "aa", lerp(orange, bright, (g-192)/57.0)
                key = (bucket(c, BUCKET_S), branch)
                t2 = memo.get(key)
                if t2 is not None: t = t2
                op[x, y] = (t[0], t[1], t[2], a)
                continue
            special = None
            if mode == "slidelink" and c == dom: special = bright
            elif is_digit_gold(c): special = orange
            elif is_ivory(c): special = bright
            bk = bucket(c, BUCKET_S)
            key = (bk, special is not None, special)
            t = memo.get(key)
            if t is None:
                if special is not None:
                    t = special
                else:
                    t = nn_ref(c, refs)
                    if t is None: t = c
                memo[key] = t
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_path)

# ---------------- 主流程 ----------------

NORMAL_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skill", required=True)
    ap.add_argument("--refdir", required=True)
    ap.add_argument("--outdir", required=True)
    a = ap.parse_args()
    ref = os.path.join(SP_ROOT, a.refdir)
    os.makedirs(a.outdir, exist_ok=True)
    sampN = os.path.join(ref, f"Note_Tap_{a.skill}.png")
    sampS = os.path.join(ref, f"Note_Tap_Select_{a.skill}.png")
    luts = build_normal_luts(os.path.join(SP_ROOT, "Note_Tap.png"), sampN)
    refs, orange, bright, pale = build_select_refs(os.path.join(SP_ROOT, "Note_Tap_Select.png"), sampS)
    print(f"普通LUT: 环{len(luts[0])}桶 体{len(luts[1])}桶 | 完成refs: {len(refs)}桶 "
          f"橙{orange} 亮{bright} 浅{pale}")
    # 自检: Tap 应近乎精确重建
    apply_normal(os.path.join(SP_ROOT, "Note_Tap.png"), luts, os.path.join(a.outdir, "_selftest_Tap.png"))
    apply_select(os.path.join(SP_ROOT, "Note_Tap_Select.png"), refs, orange, bright, pale,
                 os.path.join(a.outdir, "_selftest_Tap_Select.png"))
    n = 0
    for t in NORMAL_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}.png")
        if not os.path.exists(bp): continue
        if t == "Slide_Judgment": mode = "keep"
        elif t == "Slide_Link": mode = "slidelink"
        elif t.startswith("Repeat_"): mode = "digit"
        else: mode = "capsule"
        apply_normal(bp, luts, os.path.join(a.outdir, f"Note_{t}_{a.skill}.png"), mode)
        n += 1
    for t in SELECT_TYPES:
        bp = os.path.join(SP_ROOT, f"Note_{t}_Select.png")
        if not os.path.exists(bp): continue
        mode = "slidelink" if t == "Slide_Link" else ("digit" if t.startswith("Repeat_") else "capsule")
        apply_select(bp, refs, orange, bright, pale,
                     os.path.join(a.outdir, f"Note_{t}_Select_{a.skill}.png"), mode)
        n += 1
    print(f"已生成 {n} 张(+2自检) 到 {a.outdir}")

if __name__ == "__main__":
    main()
