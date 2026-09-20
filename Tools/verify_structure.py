"""结构核查: 修正版 vs 旧003
 1) 明暗结构保持度: 基础图亮度 vs 生成图亮度的 Pearson 相关(高=暗->暗,亮->亮,结构未搅乱)
 2) Slide_Judgment 特殊音符: 基色调色板 -> 生成色调色板 对照
"""
import os, sys, math
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP = os.path.join(PROJ, "Assets/Art/UI/sprites")
FIXED = os.path.join(PROJ, "Tools", "_gen_fixed")
OLD = os.path.join(SP, "003")

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def lum(c): return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def pearson(La, Lb):
    n = len(La)
    if n == 0: return 1.0
    ma = sum(La)/n; mb = sum(Lb)/n
    num = sum((La[i]-ma)*(Lb[i]-mb) for i in range(n))
    da = math.sqrt(sum((x-ma)**2 for x in La)); db = math.sqrt(sum((x-mb)**2 for x in Lb))
    if da == 0 or db == 0: return 1.0
    return num/(da*db)

def top_colors(px, w, h, topn=8):
    from collections import Counter
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128: c[(r, g, b)] += 1
    return c.most_common(topn)

NORMAL = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

print("=== 明暗结构保持度 (Pearson r, 基础亮度~生成亮度) ===")
print("注: r 接近1=暗部仍暗/亮部仍亮, 结构正确; 接近0或负=明暗被搅乱")
for label, types, sel in [("普通态", NORMAL, ""), ("完成态", SELECT, "_Select")]:
    print(f"-- {label} --")
    for t in types:
        nm = f"Note_{t}{sel}_skill_3_howl.png"
        gp = os.path.join(FIXED, nm)
        bp = os.path.join(SP, f"Note_{t}{sel}.png")
        if not (os.path.exists(gp) and os.path.exists(bp)): continue
        gpx, (gw, gh) = load(gp); bpx, (bw, bh) = load(bp)
        La = []; Lb = []
        # 用生成图坐标, base 同尺寸
        for y in range(gh):
            for x in range(gw):
                r, g, b, a = gpx[x, y]
                if a < 128: continue
                La.append(lum((r, g, b)))
                rb, gb, bb, ab = bpx[x, y]
                Lb.append(lum((rb, gb, bb)))
        r = pearson(La, Lb)
        flag = "OK" if r > 0.8 else "⚠结构可能异常"
        print(f"  {nm:40s} r={r:.3f}  {flag}")

print("\n=== Slide_Judgment 基色->生成色 对照 ===")
bpx, (bw, bh) = load(os.path.join(SP, "Note_Slide_Judgment.png"))
gpx, (gw, gh) = load(os.path.join(FIXED, "Note_Slide_Judgment_skill_3_howl.png"))
bc = top_colors(bpx, bw, bh)
gc = dict(top_colors(gpx, gw, gh))
print("基础图 TOP 基色 -> 对应生成色:")
for col, cnt in bc:
    # 在生成图同位置取色
    found = None
    for y in range(min(bh, gh)):
        for x in range(min(bw, gw)):
            if bpx[x, y][:3] == col:
                found = gpx[x, y][:3]; break
        if found: break
    print(f"  基色{col} (x{cnt}) -> 生成{found}")
