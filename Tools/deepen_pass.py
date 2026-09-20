"""
后处理: 在 v8 精确版(≡技能3样本)之上, 按 004 每类音符的"深圈"圈厚, 给技能3对应类型
补 004 风格深描边。描边色用技能3自家的深棕 (114,77,34)(=Tap 现有圈色), 不借 004 的绿。

规则("根据004中不同类型的音符的处理方式处理"):
  对每类技能3音符, 读 004 同类型样本的圈厚 -> 在技能3边缘 d<=圈厚 处填深棕,
  向内 fade=3px 渐隐回原身色。004 无该类型则跳过(保持样本原样)。
"""
import os, sys, math
from collections import deque
from PIL import Image

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
GEN = "D:/unity/plan go/Musical Sprite/Tools/_gen_v8"
OUT = "D:/unity/plan go/Musical Sprite/Tools/_gen_v8d"
F4 = os.path.join(SP, "004")
os.makedirs(OUT, exist_ok=True)

OUTLINE = (114, 77, 34)   # 技能3 深棕描边(=Tap 圈色)
FADE = 3

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

def lum(c):
    return 0.299*c[0] + 0.587*c[1] + 0.114*c[2]

def ring_thickness(skill4_path):
    """读 004 同类型: 边缘亮度首次回升 +25 的距离 = 圈厚。无清晰圈返回 None。"""
    if not os.path.exists(skill4_path):
        return None
    px, (w, h) = load(skill4_path)
    D = edge_dist(px, w, h)
    acc = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128: continue
            d = D[y][x]
            if 1 <= d <= 30:
                acc.setdefault(d, []).append((r, g, b))
    prof = {d: tuple(round(sum(c[i] for c in cs)/len(cs)) for i in range(3)) for d, cs in acc.items() if cs}
    if not prof: return None
    edge = prof.get(1) or prof.get(2)
    if edge is None: return None
    le = lum(edge)
    for d in range(2, 31):
        if d in prof and lum(prof[d]) > le + 25:
            return d
    return None

def lerp(c1, c2, u):
    u = max(0.0, min(1.0, u))
    return tuple(round(c1[i]*(1-u) + c2[i]*u) for i in range(3))

def deepen(src_png, out_png, thickness):
    px, (w, h) = load(src_png)
    D = edge_dist(px, w, h)
    out = Image.new("RGBA", (w, h)); op = out.load()
    hard = max(1, thickness - FADE)
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            d = D[y][x]
            if d <= hard:
                t = OUTLINE
            elif d < thickness:
                t = lerp(OUTLINE, (r, g, b), (d - hard)/FADE)
            else:
                t = (r, g, b)
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_png)

def skill4_path(name):
    """004 样本命名可能有单/双下划线变体, 都试一下。"""
    for sep in ("_", "__"):
        p = os.path.join(F4, f"{name}{sep}skill_4_croissant_heal.png")
        if os.path.exists(p):
            return p
    return None

def main():
    n = 0
    for fn in sorted(os.listdir(GEN)):
        if not fn.endswith("_skill_3_howl.png"):
            continue
        name = fn[:-len("_skill_3_howl.png")]          # Note_Wide / Note_Wide_Select ...
        skill4 = skill4_path(name)
        thick = ring_thickness(skill4) if skill4 else None
        src = os.path.join(GEN, fn)
        if thick is None or thick < 3:
            # 004 无此类型或无清晰圈 -> 原样复制
            Image.open(src).save(os.path.join(OUT, fn))
            continue
        deepen(src, os.path.join(OUT, fn), thick)
        n += 1
        print(f"  {name:18s} 004圈厚={thick:2d} -> 加深深棕描边")
    print(f"已生成加深版 {n} 张到 {OUT} (其余原样复制)")

if __name__ == "__main__":
    main()
