"""核查: 爪印在基础图里到底是什么颜色? 目标音符(含 Wide)是否带棕色爪印?
以及普通样本里 爪印(金) vs 主体(棕) 各有哪些代表色。"""
from collections import Counter
from PIL import Image

PROJ = "D:/unity/plan go/Musical Sprite"
SP = PROJ + "/Assets/Art/UI/sprites"

def load(p):
    im = Image.open(p).convert("RGBA"); return im.load(), im.size

def top(px, w, h, n=12):
    c = Counter()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a >= 128: c[(r, g, b)] += 1
    return c.most_common(n)

def is_gold(r, g, b):
    return r >= 222 and 118 <= g <= 200 and 55 <= b <= 155 and (r - b) >= 95

for nm in ["Note_Tap.png", "Note_Wide.png", "Note_Tap_Small.png", "Note_Repeat_5.png", "Note_Slide_Link.png"]:
    px, (w, h) = load(SP + "/" + nm)
    t = top(px, w, h, 14)
    gold = [col for col, _ in t if is_gold(*col)]
    print(f"\n{nm} TOP14 基色:")
    for col, cnt in t:
        tag = " [金]" if is_gold(*col) else ""
        print(f"   {col} x{cnt}{tag}")
    print(f"  -> 含金色像素? {'是' if gold else '否(爪印为其它色)'}")

# 普通样本: 爪印(金) vs 主体 代表色
px, (w, h) = load(SP + "/003/Note_Tap_skill_3_howl.png")
claw = Counter(); body = Counter()
for y in range(h):
    for x in range(w):
        r, g, b, a = px[x, y]
        if a < 128: continue
        (claw if is_gold(r, g, b) else body)[(r, g, b)] += 1
print("\n=== 普通样本 爪印(金) TOP 代表色 ===")
for col, cnt in claw.most_common(8): print(f"   {col} x{cnt}")
print("=== 普通样本 主体(非金) TOP 代表色 ===")
for col, cnt in body.most_common(8): print(f"   {col} x{cnt}")
