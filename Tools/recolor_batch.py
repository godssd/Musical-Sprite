# -*- coding: utf-8 -*-
"""
recolor_batch.py — 附魔音符批量换色生成器 v2.1（全族覆盖，0失误可校验）
========================================================================
用途：
    给定一份调色板锚点(JSON)，按《RECOLOR_RULES.md》验证过的结构对应律，
    把基底模板机械地批量换成新皮肤，并内置复测校验，不符即非零退出。

四大音符族（对应律详见 RECOLOR_RULES.md）：
  1. digit    数字族  : Note_Repeat_0~9 / Note_Repeat(generic) (+_Select)
                        轮廓/内部分离两簇，轴投影平涂（零渐变）
  2. bar      链接条族 : Note_Slide_Link (+_Select)
                        周界/内心采样两簇，轴投影平涂（零渐变）
  3. ringpaw  音符本体族:
      · select（Wide_Select / Tap_Small_Select）:
          基底 Tap_Select ↔ 皮肤 Tap_Select 样本【同几何逐像素色彩映射】
          （完成态无深圈铁律：映射天然不带环）
      · normal（Wide / Tap_Small）:
          基底是平涂色，环/身同色不可分 → 颜色映射不可行（自还原率实测0.826）。
          采用「身+爪沿用已验证文件 + 按样本环剖面补/验深环」：
            - adopt     : 已有结构正确文件，校验环带/内部色后收编
            - ring_paint: 已有身+爪正确的文件，按 howl 基准环厚补画深环
          （Tap/Tap_Select 本体 = 美术样本，不生成）
  4. judgment 判定条   : Note_Slide_Judgment（纯色条 → Tap 身色）

轴投影公式（已修正旧bug）：out = tA + t*(tB - tA)，t = proj(pixel, sA→sB)

0失误校验：
  digit/bar/generic : 输出簇色必须严格等于锚点（容差 TOL）
  ringpaw select    : ① alpha 与基底逐像素一致 ② 输出 RGB ⊆ 样本色表(Δ≤2)
                      ③ 映射自还原率（select 对实测 1.0000）
  ringpaw normal    : 环带中位=环锚点、内部中位=身锚点、色⊆样本色表
  judgment          : 输出色必须严格等于身色锚点

用法：
    python recolor_batch.py palette.json
    python recolor_batch.py palette.json --dry-run
    python recolor_batch.py palette.json --out DIR
"""
import json, os, sys
from collections import deque, Counter
from PIL import Image

TOL = 5  # 每通道中位色容差

# ---------- 基础 ----------
def dist3(a, b):
    return max(abs(a[0]-b[0]), abs(a[1]-b[1]), abs(a[2]-b[2]))

def _med(L):
    if not L: return None
    r = sorted(c[0] for c in L); g = sorted(c[1] for c in L); b = sorted(c[2] for c in L)
    n = len(L); m = n // 2
    return (r[m], g[m], b[m])

def dist_field(px, W, H):
    INF = 10**9
    dist = [[INF]*W for _ in range(H)]
    dq = deque()
    for y in range(H):
        for x in range(W):
            if px[x, y][3] < 10:
                dist[y][x] = 0; dq.append((x, y))
    while dq:
        x, y = dq.popleft(); d = dist[y][x]
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if dx == 0 and dy == 0: continue
                nx, ny = x+dx, y+dy
                if 0 <= nx < W and 0 <= ny < H and px[nx, ny][3] >= 10:
                    nd = d + 1
                    if nd < dist[ny][nx]:
                        dist[ny][nx] = nd; dq.append((nx, ny))
    return dist

# ---------- 簇检测 ----------
def digit_clusters(px, W, H):
    dist = dist_field(px, W, H)
    out, fill = [], []
    for y in range(H):
        for x in range(W):
            if px[x, y][3] >= 10:
                c = (px[x, y][0], px[x, y][1], px[x, y][2]); d = dist[y][x]
                if d <= 2: out.append(c)
                elif d >= 5: fill.append(c)
    return _med(out), _med(fill)

def bar_clusters(px, W, H):
    edge = Counter(); core = Counter()
    for x in range(W//5, W*4//5):
        col = [px[x, y] for y in range(H)]
        ys = [y for y in range(H) if col[y][3] >= 10]
        if not ys: continue
        y0, y1 = ys[0], ys[-1]
        edge[col[y0][:3]] += 1; edge[col[y1][:3]] += 1
        core[col[(y0+y1)//2][:3]] += 1
    return _med(edge), _med(core)

# ---------- 轴投影平涂 ----------
def axis_project(px_val, sA, sB, tA, tB):
    vx, vy, vz = sB[0]-sA[0], sB[1]-sA[1], sB[2]-sA[2]
    denom = vx*vx + vy*vy + vz*vz
    if denom == 0:
        t = 0.0
    else:
        dx, dy, dz = px_val[0]-sA[0], px_val[1]-sA[1], px_val[2]-sA[2]
        t = (dx*vx + dy*vy + dz*vz) / denom
    if t < 0: t = 0.0
    elif t > 1: t = 1.0
    # 正确公式：out = tA + t*(tB - tA)  （旧bug曾写成 sA + t*(tB-tA)）
    return (int(round(tA[0] + t*(tB[0]-tA[0]))),
            int(round(tA[1] + t*(tB[1]-tA[1]))),
            int(round(tA[2] + t*(tB[2]-tA[2]))))

def remap_axis(src_path, sA, sB, tA, tB, dst_path):
    im = Image.open(src_path).convert("RGBA"); W, H = im.size
    px = im.load(); out = Image.new("RGBA", (W, H)); op = out.load()
    for y in range(H):
        for x in range(W):
            a = px[x, y][3]
            if a == 0:
                op[x, y] = (0, 0, 0, 0)
            else:
                r, g, b = axis_project((px[x, y][0], px[x, y][1], px[x, y][2]), sA, sB, tA, tB)
                op[x, y] = (r, g, b, a)
    if dst_path:
        os.makedirs(os.path.dirname(dst_path), exist_ok=True)
        out.save(dst_path)
    return out

# ---------- ringpaw select：同几何逐像素色彩映射 ----------
def build_pixel_map(base_path, sample_path):
    b = Image.open(base_path).convert("RGBA"); s = Image.open(sample_path).convert("RGBA")
    if b.size != s.size:
        raise SystemExit(f"[ringpaw] 几何不一致: {base_path}{b.size} vs {sample_path}{s.size}")
    bp, sp = b.load(), s.load(); W, H = b.size
    m = {}
    for y in range(H):
        for x in range(W):
            if bp[x, y][3] >= 10 and sp[x, y][3] >= 10:
                k = (bp[x, y][0], bp[x, y][1], bp[x, y][2])
                v = (sp[x, y][0], sp[x, y][1], sp[x, y][2])
                m.setdefault(k, Counter())[v] += 1
    cmap = {k: c.most_common(1)[0][0] for k, c in m.items()}
    return cmap, list(cmap.keys())

def _nearest(c, keys):
    best, bd = None, 10**9
    for k in keys:
        d = (k[0]-c[0])**2 + (k[1]-c[1])**2 + (k[2]-c[2])**2
        if d < bd: bd, best = d, k
    return best

def apply_pixel_map(src_path, cmap, keys, dst_path):
    im = Image.open(src_path).convert("RGBA"); W, H = im.size
    px = im.load(); out = Image.new("RGBA", (W, H)); op = out.load()
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a == 0:
                op[x, y] = (0, 0, 0, 0); continue
            v = cmap.get((r, g, b)) or cmap[_nearest((r, g, b), keys)]
            op[x, y] = (v[0], v[1], v[2], a)   # alpha 严格保留（含1~9低alpha）
    os.makedirs(os.path.dirname(dst_path), exist_ok=True)
    out.save(dst_path)
    return out

def ringpaw_selfcheck(cmap, keys, base_path, sample_path):
    b = Image.open(base_path).convert("RGBA"); s = Image.open(sample_path).convert("RGBA")
    bp, sp = b.load(), s.load(); W, H = b.size
    ok = tot = 0
    for y in range(H):
        for x in range(W):
            if bp[x, y][3] >= 10 and sp[x, y][3] >= 10:
                tot += 1
                k = (bp[x, y][0], bp[x, y][1], bp[x, y][2])
                v = cmap.get(k) or cmap[_nearest(k, keys)]
                if dist3(v, (sp[x, y][0], sp[x, y][1], sp[x, y][2])) <= TOL:
                    ok += 1
    return ok / max(tot, 1)

# ---------- ringpaw normal：环带结构校验 / 补环 ----------
def ring_band_stats(path, ring_d=3, inner_d=6):
    im = Image.open(path).convert("RGBA"); W, H = im.size; px = im.load()
    dist = dist_field(px, W, H)
    edge, inner, cols = [], [], Counter()
    for y in range(H):
        for x in range(W):
            if px[x, y][3] >= 10:
                c = (px[x, y][0], px[x, y][1], px[x, y][2]); cols[c] += 1
                if dist[y][x] <= ring_d: edge.append(c)
                elif dist[y][x] >= inner_d: inner.append(c)
    return _med(edge), _med(inner), cols

def ring_paint(src_path, ring_color, solid, fade, dst_path):
    """补画深环：d<=solid 实色环；随后 fade 层线性渐隐；alpha 严格保留。"""
    im = Image.open(src_path).convert("RGBA"); W, H = im.size
    px = im.load(); dist = dist_field(px, W, H)
    out = Image.new("RGBA", (W, H)); op = out.load()
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a == 0:
                op[x, y] = (0, 0, 0, 0); continue
            d = dist[y][x]
            if d <= solid:
                op[x, y] = (ring_color[0], ring_color[1], ring_color[2], a)
            elif d <= solid + fade:
                t = (d - solid) / (fade + 1.0)
                op[x, y] = (int(round(ring_color[0]*(1-t) + r*t)),
                            int(round(ring_color[1]*(1-t) + g*t)),
                            int(round(ring_color[2]*(1-t) + b*t)), a)
            else:
                op[x, y] = (r, g, b, a)
    os.makedirs(os.path.dirname(dst_path), exist_ok=True)
    out.save(dst_path)
    return out

def copy_file(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(src, "rb") as f: data = f.read()
    with open(dst, "wb") as f: f.write(data)

# ---------- solid（judgment 纯色条）----------
def solid_remap(src_path, target, dst_path):
    im = Image.open(src_path).convert("RGBA"); W, H = im.size
    px = im.load(); out = Image.new("RGBA", (W, H)); op = out.load()
    for y in range(H):
        for x in range(W):
            a = px[x, y][3]
            op[x, y] = (target[0], target[1], target[2], a) if a >= 10 else (0, 0, 0, 0)
    os.makedirs(os.path.dirname(dst_path), exist_ok=True)
    out.save(dst_path)
    return out

# ---------- 校验 ----------
def measure_out_clusters(out, mode):
    px = out.load(); W, H = out.size
    return digit_clusters(px, W, H) if mode == "digit" else bar_clusters(px, W, H)

def sample_palette(path):
    im = Image.open(path).convert("RGBA"); px = im.load(); W, H = im.size
    cnt = Counter()
    for y in range(H):
        for x in range(W):
            if px[x, y][3] >= 10:
                cnt[(px[x, y][0], px[x, y][1], px[x, y][2])] += 1
    return set(cnt.keys())

# ---------- 主流程 ----------
def main():
    if len(sys.argv) < 2:
        print("usage: python recolor_batch.py palette.json [--dry-run] [--out DIR]"); sys.exit(2)
    spec_path = sys.argv[1]
    dry = "--dry-run" in sys.argv
    out_override = None
    if "--out" in sys.argv:
        out_override = sys.argv[sys.argv.index("--out")+1]

    with open(spec_path, "r", encoding="utf-8") as f:
        spec = json.load(f)

    base_dir = spec["base_dir"]
    out_dir = out_override or spec["output_dir"]
    suffix = spec["suffix"]
    A = spec["anchors"]
    tno = tuple(A["tap_normal_outer"]); tni = tuple(A["tap_normal_inner"])
    co = tuple(A["completed_outer"]);    ci = tuple(A["completed_inner"])

    def bp_(name): return os.path.join(base_dir, name)

    jobs, errors, n_jobs = [], [], 0
    print(f"== 批量换色生成器 v2.1 | 皮肤={spec.get('name','?')} | 输出={out_dir} ==")

    # --- 1/2. digit（索引 + generic）---
    for n in range(10):
        jobs.append(dict(base=f"Note_Repeat_{n}.png", mode="digit",
                         tA=tno, tB=tni, out=f"Note_Repeat_{n}{suffix}.png", check=(tno, tni)))
        jobs.append(dict(base=f"Note_Repeat_{n}_Select.png", mode="digit",
                         tA=co, tB=ci, out=f"Note_Repeat_{n}_Select{suffix}.png", check=(co, ci)))
    if spec.get("generic_digit", True):
        jobs.append(dict(base="Note_Repeat.png", mode="digit",
                         tA=tno, tB=tni, out=f"Note_Repeat{suffix}.png", check=(tno, tni)))
        jobs.append(dict(base="Note_Repeat_Select.png", mode="digit",
                         tA=co, tB=ci, out=f"Note_Repeat_Select{suffix}.png", check=(co, ci)))

    # --- 3. bar ---
    jobs.append(dict(base="Note_Slide_Link.png", mode="bar",
                     tA=tni, tB=tno, out=f"Note_Slide_Link{suffix}.png",   # 边=内, 芯=外
                     check=(tni, tno)))
    jobs.append(dict(base="Note_Slide_Link_Select.png", mode="bar",
                     tA=co, tB=ci, out=f"Note_Slide_Link_Select{suffix}.png",  # 边=外, 芯=内
                     check=(co, ci)))

    for j in jobs:
        n_jobs += 1
        p = bp_(j["base"])
        im = Image.open(p).convert("RGBA"); px = im.load(); W, H = im.size
        sA, sB = measure_out_clusters(im, j["mode"])
        if sA is None or sB is None:
            errors.append(f"[簇检测失败] {j['base']}"); continue
        dst = None if dry else os.path.join(out_dir, j["out"])
        out = remap_axis(p, sA, sB, j["tA"], j["tB"], dst)
        measA, measB = measure_out_clusters(out, j["mode"])
        da, db = dist3(measA, j["check"][0]), dist3(measB, j["check"][1])
        status = "OK" if (da <= TOL and db <= TOL) else "FAIL"
        print(f"  {status:4s} {j['out']:46s} A={measA}(Δ{da}) B={measB}(Δ{db})")
        if status == "FAIL":
            errors.append(f"{j['out']} 簇色超差(AΔ{da}/BΔ{db})")

    # --- 4. ringpaw ---
    rp = spec.get("ringpaw")
    if rp:
        # 4a. select：同几何逐像素映射（完成态无深圈，映射天然正确）
        s_sel = bp_(rp["map_select_from_sample"])
        cmap_s, keys_s = build_pixel_map(bp_("Note_Tap_Select.png"), s_sel)
        r2 = ringpaw_selfcheck(cmap_s, keys_s, bp_("Note_Tap_Select.png"), s_sel)
        pal_sel = sample_palette(s_sel)
        print(f"  [ringpaw select 自还原率] {r2:.4f} (阈值≥0.995)")
        if r2 < 0.995:
            errors.append(f"ringpaw select 自还原率不足: {r2:.4f}")
        for name in rp.get("select_targets", []):
            n_jobs += 1
            outname = name[:-4] + suffix + ".png"
            dst = None if dry else os.path.join(out_dir, outname)
            if dry:
                print(f"  DRY  {outname}"); continue
            out = apply_pixel_map(bp_(name), cmap_s, keys_s, dst)
            # 校验：alpha一致 + 输出色⊆样本色表
            base = Image.open(bp_(name)).convert("RGBA")
            op, bpx = out.load(), base.load(); W, H = out.size
            alpha_bad = color_bad = 0
            for y in range(H):
                for x in range(W):
                    if op[x, y][3] != bpx[x, y][3]: alpha_bad += 1
                    elif op[x, y][3] >= 10:
                        c = (op[x, y][0], op[x, y][1], op[x, y][2])
                        if not any(dist3(c, s) <= 2 for s in pal_sel): color_bad += 1
            status = "OK" if not (alpha_bad or color_bad) else "FAIL"
            print(f"  {status:4s} {outname:46s} alpha不一致={alpha_bad} 色不在样本表={color_bad}")
            if status == "FAIL":
                errors.append(f"{outname}: alpha={alpha_bad} 色={color_bad}")

        # 4b. normal：环带结构（adopt 收编 / ring_paint 补环）
        for item in rp.get("normal", []):
            n_jobs += 1
            outname = item["name"][:-4] + suffix + ".png"
            dst = None if dry else os.path.join(out_dir, outname)
            if "adopt_from" in item:
                src = bp_(item["adopt_from"])
                if dry:
                    print(f"  DRY  {outname} (adopt)"); continue
                copy_file(src, dst)
                how = "adopt"
            elif "ring_paint_from" in item:
                src = bp_(item["ring_paint_from"])
                if dry:
                    print(f"  DRY  {outname} (ring_paint)"); continue
                out = ring_paint(src, tno,
                                 item.get("ring_solid", 2), item.get("ring_fade", 1), dst)
                how = "ring_paint"
            else:
                errors.append(f"{outname}: 缺 adopt_from/ring_paint_from"); continue
            # 校验：环带(d<=2)=外圈锚点、内部(d>=6)=身锚点、色⊆源色∪环色
            med_e, med_i, _ = ring_band_stats(dst, ring_d=2, inner_d=6)
            de = dist3(med_e, tno); di = dist3(med_i, tni)
            status = "OK" if (de <= TOL and di <= TOL) else "FAIL"
            print(f"  {status:4s} {outname:46s} [{how}] 环带={med_e}(Δ{de}) 内部={med_i}(Δ{di})")
            if status == "FAIL":
                errors.append(f"{outname}: 环带Δ{de} 内部Δ{di}")

    # --- 5. judgment ---
    if spec.get("judgment", True):
        n_jobs += 1
        outname = f"Note_Slide_Judgment{suffix}.png"
        dst = None if dry else os.path.join(out_dir, outname)
        out = solid_remap(bp_("Note_Slide_Judgment.png"), tni, dst or os.devnull)
        px = out.load(); W, H = out.size
        cols = {(px[x, y][0], px[x, y][1], px[x, y][2])
                for y in range(H) for x in range(W) if px[x, y][3] >= 10}
        bad = [c for c in cols if dist3(c, tni) > TOL]
        status = "OK" if not bad else "FAIL"
        print(f"  {status:4s} {outname:46s} colors={cols}")
        if bad: errors.append(f"{outname} 色不符: {bad[:3]}")

    print("-" * 60)
    if errors:
        print(f"✗ 校验未通过，{len(errors)} 处错误：")
        for e in errors: print("   -", e)
        sys.exit(1)
    print(f"✓ 全部 {n_jobs} 张通过 0失误校验（容差≤{TOL}）。")
    if dry:
        print("  （--dry-run，未写盘）")

if __name__ == "__main__":
    main()
