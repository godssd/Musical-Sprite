# -*- coding: utf-8 -*-
from PIL import Image
import os
SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
V9 = "D:/unity/plan go/Musical Sprite/Tools/_gen_v9"
V10 = "D:/unity/plan go/Musical Sprite/Tools/_gen_v10"
SAMPLES = {"Note_Tap_skill_3_howl.png","Note_Tap_Select_skill_3_howl.png"}
def blotch(p):
    im = Image.open(p).convert("RGBA"); w,h=im.size; px=im.load(); n=0; tot=0
    for y in range(h):
        for x in range(w):
            r,g,b,a=px[x,y]
            if a<128: continue
            tot+=1
            if abs(r-173)<26 and abs(g-147)<26 and abs(b-80)<26: n+=1
    return n, (n*100//tot if tot else 0)
for f in sorted(os.listdir(SP+"/003")):
    if not f.endswith(".png"): continue
    p = f"{SP}/003/{f}"
    if f in SAMPLES: src="样本(不动)"
    elif os.path.exists(f"{V10}/{f}"):
        src="=v10" if open(p,'rb').read()==open(f"{V10}/{f}",'rb').read() else "≠v10"
    elif os.path.exists(f"{V9}/{f}"):
        src="=v9" if open(p,'rb').read()==open(f"{V9}/{f}",'rb').read() else "≠v9"
    else:
        src="(无v9/v10源)"
    n,pc = blotch(p)
    print(f"{f:44s} {src:14s} 斑点{n}px({pc}%)")
