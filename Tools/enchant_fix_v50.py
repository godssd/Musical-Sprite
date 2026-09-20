# -*- coding: utf-8 -*-
# 第50轮修正(终): Small=howl结构换色+厚环; Repeat=平涂身+厚环(对齐005样本风格)
from PIL import Image
from collections import Counter, deque
import os

ROOT = r"D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites"
OUT  = r"D:/unity/plan go/Musical Sprite/Tools/_gen_recolor/bomb_rain_full"
CMP  = r"D:/unity/plan go/Musical Sprite/Tools/_cmp_v9"

tA = (110, 107, 86)    # 环 / 深胶
tB = (225, 225, 213)   # 体 / 亮胶

def load(p): return Image.open(os.path.join(ROOT, p)).convert("RGBA")

def axis_remap(im, sA, sB):
    W,H = im.size; s = im.load(); d = Image.new("RGBA",(W,H)); dp = d.load()
    ax,ay,az = sB[0]-sA[0], sB[1]-sA[1], sB[2]-sA[2]
    dn = ax*ax+ay*ay+az*az
    bx,by,bz = tB[0]-tA[0], tB[1]-tA[1], tB[2]-tA[2]
    for y in range(H):
        for x in range(W):
            r,g,b,a = s[x,y]
            if a==0: dp[x,y]=(0,0,0,0); continue
            t = ((r-sA[0])*ax+(g-sA[1])*ay+(b-sA[2])*az)/dn
            t = 0.0 if t<0 else (1.0 if t>1 else t)
            dp[x,y]=(round(tA[0]+t*bx),round(tA[1]+t*by),round(tA[2]+t*bz),a)
    return d

def dist_field(px,W,H):
    """透明像素=0;图像边界视为"外部",紧贴边界的body像素=dd=1(多源BFS起点),
       其余=到最近透明/图像边界的曼哈顿距离。这样齐边音符也能整圈成环。"""
    INF=10**9; d=[[INF]*W for _ in range(H)]
    q=deque()
    for y in range(H):
        for x in range(W):
            if px[x,y][3]==0:
                d[y][x]=0; q.append((x,y))
            elif x==0 or y==0 or x==W-1 or y==H-1:
                d[y][x]=1; q.append((x,y))
    while q:
        x,y=q.popleft()
        nd=d[y][x]+1
        for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nx,ny=x+dx,y+dy
            if 0<=nx<W and 0<=ny<H and px[nx,ny][3]>0 and d[ny][nx]>nd:
                d[ny][nx]=nd; q.append((nx,ny))
    return d

def ring_paint_over(im, ring=(110,107,86), solid=10, fade=2):
    """在已有图(im)上,仅把距透明<=solid+fade 的环带重绘为ring色,内部不动(保留alpha)"""
    W,H=im.size; s=im.load()
    df=dist_field(s,W,H)
    d=im.copy(); dp=d.load()
    for y in range(H):
        for x in range(W):
            if s[x,y][3]==0: continue
            dd=df[y][x]
            if dd<=solid:
                dp[x,y]=(ring[0],ring[1],ring[2],s[x,y][3])
            elif dd<=solid+fade:
                k=(dd-solid)/fade
                r=int(round(s[x,y][0]*(1-k)+ring[0]*k))
                g=int(round(s[x,y][1]*(1-k)+ring[1]*k))
                b=int(round(s[x,y][2]*(1-k)+ring[2]*k))
                dp[x,y]=(r,g,b,s[x,y][3])
    return d

def flat_with_alpha(alpha_im, color):
    W,H=alpha_im.size; a=alpha_im.load(); d=Image.new("RGBA",(W,H)); dp=d.load()
    for y in range(H):
        for x in range(W):
            if a[x,y][3]>0: dp[x,y]=(color[0],color[1],color[2],a[x,y][3])
    return d

# ---- Small: 结构换色 + 厚环 ----
howl_small = load("003/Note_Tap_Small_skill_3_howl.png")
small_colored = axis_remap(howl_small, (116,78,35), (222,189,126))
small_final = ring_paint_over(small_colored, ring=tA, solid=10, fade=2)
small_final.save(os.path.join(OUT,"Note_Tap_Small_skill_5_bomb_rain.png"))

# ---- generic Repeat: 平涂奶油身 + 厚环 ----
base_rep = load("Note_Repeat.png")
rep_cream = flat_with_alpha(base_rep, tB)
rep_final = ring_paint_over(rep_cream, ring=tA, solid=12, fade=2)
rep_final.save(os.path.join(OUT,"Note_Repeat_skill_5_bomb_rain.png"))

# ---- 校验 ----
def report(im,name):
    c=Counter((r,g,b) for r,g,b,a in im.getdata() if a>=200)
    print(f"[{name}] 主色: {c.most_common(6)}")
report(small_final,"新Small"); report(rep_final,"新Repeat")

# 环厚复核(四边)
def ring4(im,body,is_small):
    W,H=im.size; px=im.load(); cx,cy=W//2,H//2; res={}
    for tag,rng in [("top",((cx,y) for y in range(H))),("bot",((cx,y) for y in range(H-1,-1,-1))),
                    ("lef",((x,cy) for x in range(W))),("rig",((x,cy) for x in range(W-1,-1,-1)))]:
        n=0
        for x,y in rng:
            r,g,b,a=px[x,y]
            if a<200: continue
            if body(r,g,b): break
            n+=1
        res[tag]=n
    print(f"  环厚: {res}")
ring4(small_final, lambda r,g,b:r>180 and g>180, True)
ring4(rep_final, lambda r,g,b:r>180 and g>180, False)

# alpha 一致性
for new,ref,nm in [(small_final,howl_small,"Small"),(rep_final,base_rep,"Repeat")]:
    pn,pr=new.load(),ref.load(); W,H=new.size
    bad=sum(1 for y in range(H) for x in range(W) if pn[x,y][3]!=pr[x,y][3])
    print(f"[{nm}] alpha不一致={bad}")

# ---- 对照图 ----
from PIL import ImageDraw, ImageFont
CELL_W,CELL_H,PAD,LAB=140,180,16,24
img=Image.new("RGB",(CELL_W*3+PAD*4,(CELL_H+LAB)*2+PAD*3),(245,245,242))
dr=ImageDraw.Draw(img)
try: font=ImageFont.truetype("C:/Windows/Fonts/msyh.ttc",13)
except: font=ImageFont.load_default()
def cell(path,col,row,label):
    if path:
        im=Image.open(path).convert("RGBA"); im.thumbnail((CELL_W-10,CELL_H-10))
        cx=PAD+col*(CELL_W+PAD)+(CELL_W-im.size[0])//2
        cy=PAD+row*(CELL_H+LAB+PAD)+(CELL_H-im.size[1])//2
        img.paste(im,(cx,cy),im)
    dr.text((PAD+col*(CELL_W+PAD)+4,PAD-2+row*(CELL_H+LAB+PAD)+CELL_H+6),label,fill=(40,40,40),font=font)
cell(os.path.join(ROOT,"003/Note_Tap_Small_skill_3_howl.png"),0,0,"howl参照(结构)")
cell(os.path.join(ROOT,"005/Note_Tap_Small_skill_5_bomb_rain.png"),1,0,"旧生成(打回)")
cell(os.path.join(OUT,"Note_Tap_Small_skill_5_bomb_rain.png"),2,0,"新v50(厚环+爪)")
cell(os.path.join(ROOT,"003/Note_Repeat_skill_3_howl.png"),0,1,"howl参照(结构)")
cell(os.path.join(ROOT,"005/Note_Repeat_skill_5_bomb_rain.png"),1,1,"旧生成(打回)")
cell(os.path.join(OUT,"Note_Repeat_skill_5_bomb_rain.png"),2,1,"新v50(平涂+厚环)")
dr.text((2,PAD+20),"Small",fill=(120,30,30),font=font)
dr.text((2,PAD+(CELL_H+LAB+PAD)+20),"Repeat",fill=(120,30,30),font=font)
cmp=os.path.join(CMP,"_v50_fix_compare.png"); img.save(cmp)
print("对照图:",cmp)
