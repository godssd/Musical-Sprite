"""
对比分析: 当前 003 生成图 vs 对应样本调色板(只读, 不改文件)。
指标:
  - 调色板贴合度 = 生成图中"量化色落在样本调色板 bin 内"的像素比例(越高越干净)
  - 脏色占比 = 落在样本 bin 外(不在样本调色板中)的像素比例(越高=混色/脏色越多)
  - 分爪印区 / 主体区分别统计, 定位是哪套 LUT 出问题
"""
import os, sys, math, argparse
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP = os.path.join(PROJ, "Assets/Art/UI/sprites")
sys.path.insert(0, os.path.join(PROJ, "Tools"))
from enchant_skin import segment_claw

STEP = 16  # 量化步长
ap = argparse.ArgumentParser()
ap.add_argument("--gen", default="Assets/Art/UI/sprites/003",
                help="生成图目录(相对工程根, 默认 003)")
A = ap.parse_args()
GEN = os.path.join(PROJ, A.gen)

def load(p):
    im = Image.open(p).convert("RGBA")
    return im.load(), im.size

def q(c):
    return (c[0]//STEP, c[1]//STEP, c[2]//STEP)

def palette_set(px, w, h):
    s = set()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128:
                s.add(q((r, g, b)))
    return s

def palset(path):
    px, (w, h) = load(path)
    return palette_set(px, w, h)

def qdist(qc, refset):
    best = 10**9
    for rc in refset:
        d = (qc[0]-rc[0])**2 + (qc[1]-rc[1])**2 + (qc[2]-rc[2])**2
        if d < best: best = d
    return math.sqrt(best) * STEP  # 近似欧氏距离

def analyze(name, ref_path):
    gen_px, (gw, gh) = load(os.path.join(GEN, name))
    ref_px, (rw, rh) = load(ref_path)
    refset = palette_set(ref_px, rw, rh)
    # 用生成图自身尺寸做爪印分割(基于基础base图? 生成图已上色, 用 base 更准)
    base_name = name.replace("_skill_3_howl", "").replace("_Select", "")
    base_path = os.path.join(SP, base_name + ".png")
    if os.path.exists(base_path):
        bpx, (bw, bh) = load(base_path)
        claw = segment_claw(bpx, bw, bh)
        # 注意: base 与 gen 尺寸需一致
        if (bw, bh) != (gw, gh):
            claw = None
    else:
        claw = None
    tot = dirty = dirty_claw = dirty_body = 0
    for y in range(gh):
        for x in range(gw):
            r, g, b, a = gen_px[x, y]
            if a < 128:
                continue
            tot += 1
            if q((r, g, b)) not in refset:
                dirty += 1
                if claw is not None and claw[y][x]:
                    dirty_claw += 1
                else:
                    dirty_body += 1
    return tot, dirty, dirty_claw, dirty_body, len(refset)

def dump_dirty(name, ref_path, topn=8):
    gen_px, (gw, gh) = load(os.path.join(GEN, name))
    ref_px, (rw, rh) = load(ref_path)
    refset = palette_set(ref_px, rw, rh)
    # 最近样本色(精确, 用未量化距离)
    refcols = list(refset)
    cnt = {}
    for y in range(gh):
        for x in range(gw):
            r, g, b, a = gen_px[x, y]
            if a < 128:
                continue
            qc = q((r, g, b))
            if qc not in refset:
                cnt[qc] = cnt.get(qc, 0) + 1
    if not cnt:
        print(f"  {name}: 无脏色")
        return
    ranked = sorted(cnt.items(), key=lambda kv: -kv[1])[:topn]
    print(f"  {name}  TOP脏色(量化bin, 计数):")
    for qc, c in ranked:
        # 反量化到 approximate 颜色
        approx = (qc[0]*STEP+8, qc[1]*STEP+8, qc[2]*STEP+8)
        # 最近样本色
        best = None; bd = 10**9
        for rc in refcols:
            d = (qc[0]-rc[0])**2+(qc[1]-rc[1])**2+(qc[2]-rc[2])**2
            if d < bd: bd = d; best = rc
        near = (best[0]*STEP+8, best[1]*STEP+8, best[2]*STEP+8)
        print(f"    ~{approx}  x{c:4d}   最近样本色~{near}")

NORMAL = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

refN = os.path.join(SP, "003", "Note_Tap_skill_3_howl.png")
refS = os.path.join(SP, "003", "Note_Tap_Select_skill_3_howl.png")

print(f"样本调色板 bin 数: 普通={len(palset(refN))}  完成={len(palset(refS))}")
print("\n=== 普通态: 脏色占比(落在普通样本调色板外) ===")
rows = []
for t in NORMAL:
    nm = f"Note_{t}_skill_3_howl.png"
    if not os.path.exists(os.path.join(GEN, nm)):
        continue
    tot, d, dc, db, rb = analyze(nm, refN)
    pct = 100.0*d/tot if tot else 0
    rows.append((nm, tot, d, pct, dc, db))
rows.sort(key=lambda r: -r[3])
for nm, tot, d, pct, dc, db in rows:
    print(f"  {nm:40s} 不透明={tot:5d} 脏色={d:5d} ({pct:5.1f}%)  爪印脏={dc:4d} 主体脏={db:4d}")

print("\n=== 普通态脏色 TOP 颜色 dump(最差4个) ===")
for t in ["Slide_Judgment", "Repeat_7", "Repeat_6", "Repeat_5"]:
    dump_dirty(f"Note_{t}_skill_3_howl.png", refN)

print("\n=== 完成态: 脏色占比(落在完成样本调色板外) ===")
rows = []
for t in SELECT:
    nm = f"Note_{t}_Select_skill_3_howl.png"
    if not os.path.exists(os.path.join(GEN, nm)):
        continue
    tot, d, dc, db, rb = analyze(nm, refS)
    pct = 100.0*d/tot if tot else 0
    rows.append((nm, tot, d, pct, dc, db))
rows.sort(key=lambda r: -r[3])
for nm, tot, d, pct, dc, db in rows:
    print(f"  {nm:40s} 不透明={tot:5d} 脏色={d:5d} ({pct:5.1f}%)  爪印脏={dc:4d} 主体脏={db:4d}")

print("\n=== 普通态脏色 TOP 颜色 dump(最差4个) ===")
for t in ["Slide_Judgment", "Repeat_7", "Repeat_6", "Repeat_5"]:
    dump_dirty(f"Note_{t}_skill_3_howl.png", refN)
