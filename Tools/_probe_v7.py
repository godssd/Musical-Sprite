"""v7 验证探针: 检查体部深色侵断是否消除 + 结构色是否正确"""
import os
from collections import Counter
from PIL import Image
import enchant_skin_v7 as E

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v7"

def lum(c): return 0.299*c[0]+0.587*c[1]+0.114*c[2]

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def edge_dist(px, w, h):
    return E.edge_dist(px, w, h)

def probe_capsule(name):
    bp = os.path.join(SP, f"Note_{name}.png")
    gp = os.path.join(GEN, f"Note_{name}_skill_3_howl.png")
    if not os.path.exists(bp) or not os.path.exists(gp):
        print(f"  [skip] {name}: 缺 base/gen"); return
    bpx, (w, h) = load(bp); gpx, _ = load(gp)
    D = edge_dist(bpx, w, h)
    dom = E.dominant_color(bpx, w, h)
    dark_in_body = 0          # 体部区(d>RING_D)生成色暗(lum<120)
    body_cream = 0; body_other = 0   # 体部区(d>RING_D)是否奶油
    ring_deep = 0; ring_total = 0    # 环带区(d<=RING_D)是否深棕
    cnt = Counter()
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = bpx[x, y]
            rg, gg, bg, ag = gpx[x, y]
            if ab < 128: continue
            d = D[y][x]
            if d > E.RING_D:
                # 基础图上是身体(贴近主色)的像素
                if E.dist((rb, gb, bb), dom) <= 35:
                    if lum((rg, gg, bg)) < 120: dark_in_body += 1
                    if lum((rg, gg, bg)) > 170: body_cream += 1
                    else: body_other += 1
            else:
                ring_total += 1
                # 深棕圈判定: 偏暗(样本 ring_mode 通常 L<120)
                if lum((rg, gg, bg)) < 140: ring_deep += 1
    tot_body = body_cream + body_other
    print(f"  {name:12s}: 体部暗色像素={dark_in_body:4d} (期望≈0) | 体部奶油占 {body_cream}/{tot_body} = {100*body_cream/max(1,tot_body):.0f}% | 环带深棕占 {ring_deep}/{ring_total} = {100*ring_deep/max(1,ring_total):.0f}%")

print("=== 胶囊类体部深色侵断探针 (v7) ===")
for t in ["Wide", "Tap_Small", "Repeat", "Repeat_5"]:
    probe_capsule(t)

print("\n=== 完成态(Select)重建误差 (vs 样本) ===")
def selftest_err(base_name, gen_suffix):
    bp = os.path.join(SP, f"{base_name}.png")
    sp = os.path.join(SP, "003", f"{base_name}_skill_3_howl.png")
    if not os.path.exists(sp):
        print(f"  [skip] {base_name}: 无样本"); return
    genp = os.path.join(GEN, f"_selftest_{gen_suffix}.png")
    if not os.path.exists(genp):
        print(f"  [skip] {base_name}: 无自检输出"); return
    bpx, (w, h) = load(bp); spx, _ = load(sp); gpx, _ = load(genp)
    err = 0; n = 0
    for y in range(h):
        for x in range(w):
            rg, gg, bg, ag = gpx[x, y]
            rs, gs, bs, as_ = spx[x, y]
            if ag < 128: continue
            err += abs(rg-rs)+abs(gg-gs)+abs(bg-bs); n += 1
    print(f"  {base_name:22s}: 平均色差 {err/max(1,n):.1f} (Reconstruction 误差, 越低越好)")
selftest_err("Note_Tap_Select", "Tap_Select")
selftest_err("Note_Tap", "Tap")
