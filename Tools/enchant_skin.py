"""
附魔音符生成器 v4 —— 区域感知版（修复兽爪专门色相偏移）

核心规律(用户口述+实测):
  普通态颜色来自普通样本(Tap普通), 完成态颜色来自完成样本(Tap完成)。
  但兽爪图案(趾豆/主垫/金黄描边)在样本里做过【专门的色相偏移】,
  与主体映射不同 → 同一底色在爪印内/外映射到不同技能色,
  纯逐色 LUT 必然混淆。必须按区域分割:
    1) 金黄/鲑鱼色像素 + 被其包围的封闭区 = 爪印区
    2) 爪印区像素 → 爪印 LUT ; 其余 → 主体 LUT
  两套 LUT 均为 k近邻逆距离插值(从 Tap 样本对学习), 保留 alpha 与渐变。

用法:
  python enchant_skin.py --skill skill_3_howl --refdir 003 --outdir <目录>
"""
import os, argparse, math
from collections import deque
from PIL import Image

SP_ROOT = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"

# ---------- 爪印色判定(金黄描边 + 鲑鱼填充) ----------
def is_claw_color(r, g, b):
    return r >= 222 and 118 <= g <= 200 and 55 <= b <= 155 and (r - b) >= 95

def segment_claw(px, w, h):
    """返回 claw[y][x] 布尔掩码: 金黄/鲑鱼像素 + 被它们包围的封闭区。"""
    claw = [[False]*w for _ in range(h)]
    n_claw = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128 and is_claw_color(r, g, b):
                claw[y][x] = True; n_claw += 1
    if n_claw == 0:
        return claw  # 该音符无爪印(数字等)
    # 屏障膨胀1px, 防抗锯齿缝隙泄漏
    barrier = [[False]*w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if claw[y][x]:
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = y+dy, x+dx
                        if 0 <= ny < h and 0 <= nx < w:
                            barrier[ny][nx] = True
    # 从主体种子 BFS(只走 非屏障、不透明 像素)
    seed = None
    for sy in (h-4, h//2, 3, h-8):
        for sx in (w//2, w//4, 3*w//4, 2, w-3):
            if 0 <= sx < w and 0 <= sy < h:
                r, g, b, a = px[sx, sy]
                if a >= 128 and not barrier[sy][sx]:
                    seed = (sx, sy); break
        if seed: break
    if not seed:
        return claw
    seen = [[False]*w for _ in range(h)]
    seen[seed[1]][seed[0]] = True
    dq = deque([seed])
    while dq:
        x, y = dq.popleft()
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nx, ny = x+dx, y+dy
            if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx]:
                r, g, b, a = px[nx, ny]
                if a < 128:
                    seen[ny][nx] = True; continue
                if barrier[ny][nx]:
                    continue
                seen[ny][nx] = True; dq.append((nx, ny))
    # 不透明且没被走到 = 封闭在爪印里
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128 and not seen[y][x]:
                claw[y][x] = True
    return claw

# ---------- 精确逐色 LUT（不平均，保证输出落在样本调色板内） ----------
from collections import Counter

def build_refs(base_px, skill_px, w, h, claw):
    acc = {True: {}, False: {}}
    for y in range(h):
        for x in range(w):
            rb, gb, bb, ab = base_px[x, y]
            rs, gs, bs, at = skill_px[x, y]
            if ab < 128 or at < 128:
                continue
            acc[claw[y][x]].setdefault((rb, gb, bb), []).append((rs, gs, bs))
    refs = {}
    for k, m in acc.items():
        d = {}
        for c, v in m.items():
            # 取出现最多的真实技能色(众数), 避免对边缘抗锯齿技能色求均值混出样本外中间色
            d[c] = Counter(v).most_common(1)[0][0]
        refs[k] = d
    return refs[True], refs[False]

def map_color(c, refs):
    """精确逐色 LUT：命中直接返回；未命中取最近基色(k=1, 不平均) -> 输出必在样本调色板内。"""
    if not refs:
        return c
    t = refs.get(c)
    if t is not None:
        return t
    best = None; bd = 10**9
    for bc, sc in refs.items():
        dd = (c[0]-bc[0])**2 + (c[1]-bc[1])**2 + (c[2]-bc[2])**2
        if dd < bd:
            bd = dd; best = sc
    return best

# ---------- 主流程 ----------
NORMAL_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link", "Slide_Judgment"] + [f"Repeat_{i}" for i in range(10)]
SELECT_TYPES = ["Wide", "Tap_Small", "Repeat", "Slide_Link"] + [f"Repeat_{i}" for i in range(10)]

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def build_luts(tap_base_p, tap_skill_p):
    """从 Tap 样本对学习 两套 LUT 参考表。"""
    bpx, (w, h) = load(tap_base_p)
    spx, _ = load(tap_skill_p)
    claw = segment_claw(bpx, w, h)
    claw_refs, body_refs = build_refs(bpx, spx, w, h, claw)
    return claw_refs, body_refs, sum(1 for y in range(h) for x in range(w) if claw[y][x])

def apply_to(target_base_p, claw_refs, body_refs, out_p):
    """对目标 base 图应用区域感知 LUT，输出 out_p。返回 (爪印px, 是否有爪印)。"""
    px, (w, h) = load(target_base_p)
    claw = segment_claw(px, w, h)
    n_claw = sum(1 for y in range(h) for x in range(w) if claw[y][x])
    memo = {}  # 颜色级缓存: (r,g,b,is_claw) -> mapped（扁平手绘色重复率极高）
    out = Image.new("RGBA", (w, h)); op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128:
                op[x, y] = (r, g, b, a); continue
            isc = claw[y][x] and bool(claw_refs)
            key = (r, g, b, isc)
            t = memo.get(key)
            if t is None:
                refs = claw_refs if isc else body_refs
                t = map_color((r, g, b), refs)
                memo[key] = t
            op[x, y] = (t[0], t[1], t[2], a)
    out.save(out_p)
    return n_claw

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--skill", required=True)
    ap.add_argument("--refdir", required=True)
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--sproot", default=SP_ROOT)
    a = ap.parse_args()
    ref = os.path.join(a.sproot, a.refdir)
    os.makedirs(a.outdir, exist_ok=True)
    # 学习两套 LUT(普通态 + 完成态)，返回顺序: (爪印refs, 主体refs, 爪印px数)
    n_refs, n_body, n_cnt = build_luts(os.path.join(a.sproot, "Note_Tap.png"),
                                       os.path.join(ref, f"Note_Tap_{a.skill}.png"))
    s_refs, s_body, s_cnt = build_luts(os.path.join(a.sproot, "Note_Tap_Select.png"),
                                       os.path.join(ref, f"Note_Tap_Select_{a.skill}.png"))
    print(f"普通态 LUT: 爪印px={n_cnt} 爪印refs={len(n_refs)} 主体refs={len(n_body)}")
    print(f"完成态 LUT: 爪印px={s_cnt} 爪印refs={len(s_refs)} 主体refs={len(s_body)}")
    # 自检: 用普通态 LUT 重建 Tap 本身
    apply_to(os.path.join(a.sproot, "Note_Tap.png"), n_refs, n_body,
             os.path.join(a.outdir, "_selftest_Tap.png"))
    n_total = 0
    for t in NORMAL_TYPES:
        bp = os.path.join(a.sproot, f"Note_{t}.png")
        if not os.path.exists(bp):
            print(f"  跳过(无base) {t}"); continue
        apply_to(bp, n_refs, n_body, os.path.join(a.outdir, f"Note_{t}_{a.skill}.png"))
        n_total += 1
    for t in SELECT_TYPES:
        bp = os.path.join(a.sproot, f"Note_{t}_Select.png")
        if not os.path.exists(bp):
            print(f"  跳过(无base Select) {t}"); continue
        apply_to(bp, s_refs, s_body, os.path.join(a.outdir, f"Note_{t}_Select_{a.skill}.png"))
        n_total += 1
    print(f"已生成 {n_total} 张到 {a.outdir}")

if __name__ == "__main__":
    main()
