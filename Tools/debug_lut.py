import sys
sys.path.insert(0, "D:/unity/plan go/Musical Sprite/Tools")
from enchant_skin import build_luts, map_color, load
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"

n_refs, n_body, n_cnt = build_luts(SP + "/Note_Tap.png", SP + "/003/Note_Tap_skill_3_howl.png")
print("普通态 主体LUT条目数:", len(n_body), " 爪印LUT条目数:", len(n_refs))

def q(c): return (c[0]//16, c[1]//16, c[2]//16)
refpx, (rw, rh) = load(SP + "/003/Note_Tap_skill_3_howl.png")
refset = set()
for y in range(rh):
    for x in range(rw):
        r, g, b, a = refpx[x, y]
        if a >= 128: refset.add(q((r, g, b)))
print("普通样本调色板 bin 数:", len(refset))

# Repeat_5 base -> map_color
bpx, (bw, bh) = load(SP + "/Note_Repeat_5.png")
tot = dirty = 0
samples = []
hits = 0
for y in range(bh):
    for x in range(bw):
        r, g, b, a = bpx[x, y]
        if a < 128: continue
        tot += 1
        exact = (r, g, b) in n_body
        out = map_color((r, g, b), n_body)
        if exact: hits += 1
        if q(out) not in refset:
            dirty += 1
            if len(samples) < 8:
                samples.append((exact, (r, g, b), out))
print(f"Repeat_5: tot={tot} 精确命中={hits} 脏(out不在样本bin)={dirty}")
for exact, base, out in samples:
    print(f"  {'命中' if exact else '兜底'} base={base} -> out={out}  in_refset={q(out) in refset}")

# 检查: 主体LUT 里的值是否都在样本bin内
lut_dirty = sum(1 for c, sc in n_body.items() if q(sc) not in refset)
print("主体LUT中 值不在样本bin的条目数:", lut_dirty, "/", len(n_body))
