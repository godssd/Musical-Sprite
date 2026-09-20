# -*- coding: utf-8 -*-
"""
enchant_skin_v9.py  —  技能3(howl) 音符换皮 v9（百分位亮度曲线迁移版）
======================================================================
前提（用户确认 2026-09-19）：
  003/ 里 ONLY 两个文件是美术师真参照（取色样本）：
      Note_Tap_skill_3_howl.png           （普通态风格源）
      Note_Tap_Select_skill_3_howl.png    （完成态/果冻风格源）
  其余 29 个是 AI 旧产物（斑点/缺深边），全部重新生成。

方法：
  1. 调色板取自 2 个真样本：RING(深棕) / BODY(奶油) / PAW(金) / PAW_HI(亮金)
  2. 普通态胶囊/滑条：内部色 = 「Tap 对齐对」学到的 百分位亮度曲线
     （保留基础图 3D 光影与爪印浮雕，只换色相）；深边按 004 圈厚。
  3. 数字（Repeat_0..9）：基础图自带 浅填充+橙描边 结构，
     按亮度 s∈[0,1] 直接 lerp(RING→BODY)：描边→深棕、填充→奶油。
  4. Select 果冻态：内部 = Tap_Select 学到的果冻曲线（无深边，按用户图2目标）；
     Select 数字按亮度 lerp(暗琥珀→亮琥珀)。
  5. Slide_* 在 004 无深边 -> 不加深边。
"""
import os, argparse, glob
from collections import deque, Counter
from PIL import Image

SP_ROOT = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
REF = os.path.join(SP_ROOT, "003")
SAMPLES_KEEP = {"Note_Tap_skill_3_howl.png", "Note_Tap_Select_skill_3_howl.png"}

# ---------- 工具 ----------
def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def edge_dist(px, w, h):
    D = [[10**9]*w for _ in range(h)]; q = deque()
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

def edge_dist_outside(px, w, h):
    """画圈专用：与 edge_dist 相同，但把【画布外】也视为透明。
    基础图是紧贴胶囊裁切的（左右侧没有透明像素种子），不补这条的话
    BFS 距离在侧边会暴涨（Note_Wide 中行 D=125+），导致深边只出现在上下端。
    边界上的不透明像素距离记 1（相邻画布外=透明）。"""
    D = [[10**9]*w for _ in range(h)]; q = deque()
    for y in range(h):
        for x in range(w):
            if px[x, y][3] < 128:
                D[y][x] = 0; q.append((x, y))
    for x in range(w):
        for y in (0, h-1):
            if px[x, y][3] >= 128 and D[y][x] > 1:
                D[y][x] = 1; q.append((x, y))
    for y in range(h):
        for x in (0, w-1):
            if px[x, y][3] >= 128 and D[y][x] > 1:
                D[y][x] = 1; q.append((x, y))
    while q:
        x, y = q.popleft()
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nx, ny = x+dx, y+dy
            if 0 <= nx < w and 0 <= ny < h and D[ny][nx] > D[y][x]+1:
                D[ny][nx] = D[y][x]+1; q.append((nx, ny))
    return D

def dist_hv(px, w, h):
    """水平 / 垂直两个方向到『外部(透明像素或画布外)』的步数。
    用于方向性圈厚：长边看 DH、端帽看 DV。"""
    DH = [[10**9] * w for _ in range(h)]
    DV = [[10**9] * w for _ in range(h)]
    for y in range(h):
        d = 1                                    # 画布外视为 1 步
        for x in range(w):
            if px[x, y][3] < 128: d = 0
            DH[y][x] = min(DH[y][x], d)
            d += 1
        d = 1
        for x in range(w - 1, -1, -1):
            if px[x, y][3] < 128: d = 0
            DH[y][x] = min(DH[y][x], d)
            d += 1
    for x in range(w):
        d = 1
        for y in range(h):
            if px[x, y][3] < 128: d = 0
            DV[y][x] = min(DV[y][x], d)
            d += 1
        d = 1
        for y in range(h - 1, -1, -1):
            if px[x, y][3] < 128: d = 0
            DV[y][x] = min(DV[y][x], d)
            d += 1
    return DH, DV

def lum(c): return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

def med(cs):
    if not cs: return (0, 0, 0)
    s = sorted(cs, key=lambda c: c[0]*1_000_000 + c[1]*1000 + c[2])
    return s[len(s)//2]

def dom_color(px, w, h):
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128: c[(r, g, b)] += 1
    return c.most_common(1)[0][0]

# ---------- 调色板（来自 2 个真样本 + 已验证爪色） ----------
RING = (116, 78, 35)        # Tap 样本深棕圈
BODY = (222, 189, 126)      # Tap 样本奶油身
PAW  = (240, 185, 116)      # 已验证样本爪色
PAW_HI = (255, 240, 99)     # 爪高光
TAP_RING_T = 13             # Tap 样本自身圈厚（参考）
NBINS = 48                  # 百分位曲线桶数

# ---------- 百分位亮度曲线 ----------
def build_curve(base_path, sample_path, min_d):
    """对齐对 -> [(percentile_bin -> RGB)]。仅统计内部(d>min_d)像素。"""
    bpx, (w, h) = load(base_path); spx, _ = load(sample_path)
    D = edge_dist(bpx, w, h)
    pts = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = bpx[x, y]
            if a < 128: continue
            if D[y][x] <= min_d: continue
            pts.append((lum((r, g, b)), spx[x, y][:3]))
    if not pts: return None
    pts.sort(key=lambda t: t[0])
    n = len(pts); curve = []
    for i in range(NBINS):
        lo = int(i*n/NBINS); hi = max(int((i+1)*n/NBINS), lo+1)
        seg = [c for _, c in pts[lo:hi]]
        curve.append(med(seg))
    return curve

def apply_curve(coords, curve):
    """coords: [(x,y,lum)] -> 按百分位映射曲线色。返回 {(x,y):color}"""
    if not curve: return {}
    n = len(coords)
    out = {}
    for i, (x, y, L) in enumerate(coords):   # coords 已按 lum 升序
        b = min(NBINS-1, int(i * NBINS / max(1, n)))
        out[(x, y)] = curve[b]
    return out

# ---------- 类型分类 ----------
RING_THICK_CAPSULE = {      # 004 实测圈厚（均匀型）
    "Note_Wide": 15, "Note_Tap_Small": 3, "Note_Repeat": 10,
}
# 004 实测：Wide 的圈是「方向性」的 —— 上下端帽 15px，左右长边仅 8px。
# 旧实现按均匀 15px 画圈导致左右圈厚近一倍、身体被挤窄（用户 2026-09-19 反馈"wide范围不对"）。
RING_ANISO = {              # name -> (长边side_t, 端帽cap_t)
    "Note_Wide": (9, 14),   # 实测对齐 004：长边实量8px、端帽15/14（含1px渐隐的折算）
}
NORMAL_CAPSULE = ["Note_Wide", "Note_Tap_Small", "Note_Repeat"]
NORMAL_DIGIT   = [f"Note_Repeat_{i}" for i in range(10)]
NORMAL_SLIDE   = ["Note_Slide_Link", "Note_Slide_Judgment"]
SELECT_CAPSULE = ["Note_Wide_Select", "Note_Tap_Small_Select", "Note_Repeat_Select"]
SELECT_DIGIT   = [f"Note_Repeat_{i}_Select" for i in range(10)]
SELECT_SLIDE   = ["Note_Slide_Link_Select"]

def classify(name):
    if name in NORMAL_CAPSULE: return "ncap"
    if name in NORMAL_DIGIT:   return "ndig"
    if name in NORMAL_SLIDE:   return "nslide"
    if name in SELECT_CAPSULE: return "scap"
    if name in SELECT_DIGIT:   return "sdig"
    if name in SELECT_SLIDE:   return "sslide"
    return None

# ---------- 生成 ----------
def gen_normal(name, px, w, h, curve):
    kind = classify(name)
    ring_t = RING_THICK_CAPSULE.get(name, TAP_RING_T)   # nslide 用不到
    use_ring = kind in ("ncap",)
    aniso = RING_ANISO.get(name)          # (side_t, cap_t) 方向性圈厚
    if aniso:
        side_t, cap_t = aniso
        scale = cap_t / float(side_t)
        DH, DV = dist_hv(px, w, h)
    else:
        D = edge_dist_outside(px, w, h)   # 画圈用「画布外=透明」的距离场（修侧边漏圈）
    dom = dom_color(px, w, h); domL = lum(dom)
    out = Image.new("RGBA", (w, h)); op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            c = (r, g, b)
            if use_ring and aniso:
                # 方向性深边：dr = min(DH*cap_t/side_t, DV) —— 长边按 side_t、端帽按 cap_t
                dr = min(DH[y][x] * scale, DV[y][x])
                if dr <= cap_t:
                    t = RING if dr <= max(0, cap_t - 1) else lerp(RING, BODY, 0.5)
                    op[x, y] = (t[0], t[1], t[2], a); continue
            else:
                d = D[y][x]
                if use_ring and d <= ring_t:
                    # 深边：实心核 + 1px 渐隐
                    t = RING if d <= max(0, ring_t-1) else lerp(RING, BODY, 0.5)
                    op[x, y] = (t[0], t[1], t[2], a); continue
            if kind == "ndig":
                # 数字：亮度 s 直接 lerp(RING->BODY)（描边->深棕、填充->奶油）
                s = min(1.0, max(0.0, (lum(c) - 130) / 95.0))
                t = lerp(RING, BODY, s)
            else:
                # 角色判定平涂（美术师 Tap 样本内部为干净平涂奶油）：
                # 比体色亮一档的浮雕 = 爪印 -> 金；其余 -> 平奶油
                L = lum(c)
                if L > domL + 18:
                    u = min(1.0, (L - domL - 18) / 60.0)
                    t = lerp(PAW, PAW_HI, u)
                else:
                    t = BODY
            op[x, y] = (t[0], t[1], t[2], a)
    return out

def gen_select(name, px, w, h, curve):
    D = edge_dist(px, w, h)
    kind = classify(name)
    interior = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128: continue
            interior.append((x, y, lum((r, g, b))))
    interior.sort(key=lambda t: t[2])
    cmap = apply_curve(interior, curve)
    out = Image.new("RGBA", (w, h)); op = out.load()
    dark_sel = lerp(RING, BODY, 0.35)
    bright_sel = (min(255, BODY[0]+25), min(255, BODY[1]+20), min(255, BODY[2]+15))
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            cc = cmap.get((x, y))
            if kind == "sdig":
                s = min(1.0, max(0.0, (lum((r, g, b)) - 130) / 95.0))
                t = lerp(dark_sel, bright_sel, s)
            else:
                t = cc or BODY
            op[x, y] = (t[0], t[1], t[2], a)
    return out

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--only", default="", help="逗号分隔的音符名列表，只重新生成这些（空=全部）")
    a = ap.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    only = set(s.strip() for s in a.only.split(",") if s.strip()) or None
    # 曲线：普通态来自 Tap 对齐对（仅真内部 d>17，排除圈→身过渡带）；
    #       果冻来自 Tap_Select（内部 d>4，排除边缘抗锯齿）
    curve_n = build_curve(os.path.join(SP_ROOT, "Note_Tap.png"),
                          os.path.join(REF, "Note_Tap_skill_3_howl.png"), TAP_RING_T + 4)
    curve_s = build_curve(os.path.join(SP_ROOT, "Note_Tap_Select.png"),
                          os.path.join(REF, "Note_Tap_Select_skill_3_howl.png"), 4)
    print(f"curve_n={'OK' if curve_n else 'FAIL'} curve_s={'OK' if curve_s else 'FAIL'}")
    print(f"RING={RING} BODY={BODY} PAW={PAW}")
    bases = sorted(glob.glob(os.path.join(SP_ROOT, "Note_*.png")))
    n = 0
    for bp in bases:
        name = os.path.splitext(os.path.basename(bp))[0]
        out_name = f"{name}_skill_3_howl.png"
        if out_name in SAMPLES_KEEP:
            continue
        if only is not None and name not in only:
            continue
        kind = classify(name)
        if kind is None:
            continue
        px, (w, h) = load(bp)
        if kind in ("ncap", "ndig", "nslide"):
            out = gen_normal(name, px, w, h, curve_n)
        else:
            out = gen_select(name, px, w, h, curve_s)
        out.save(os.path.join(a.outdir, out_name))
        n += 1
    print(f"已生成 {n} 张到 {a.outdir}")

if __name__ == "__main__":
    main()
