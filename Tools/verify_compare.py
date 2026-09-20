"""验证 v4 落地到 003 的效果: 兽爪区域均值对比(old vs new) + 旧/新并排 montage + 兽爪放大裁切。"""
import os, sys
from PIL import Image

ROOT = "D:/unity/plan go/Musical Sprite"
SP = os.path.join(ROOT, "Assets/Art/UI/sprites")
BK = os.path.join(ROOT, "Tools/_backup_003_v4")
OUT = os.path.join(ROOT, "Tools/_compare_003")
os.makedirs(OUT, exist_ok=True)

def load(p):
    return Image.open(p).convert("RGBA")

def is_claw(r, g, b):
    return r >= 222 and 118 <= g <= 200 and 55 <= b <= 155 and (r - b) >= 95

def claw_region_stats(name):
    """在 NEW 输出的兽爪(亮金/鲑鱼)区域, 对比 old vs new 的均值与褐化程度。"""
    new = load(os.path.join(SP, "003", name)); npx = new.load(); w, h = new.size
    old = load(os.path.join(BK, name)); opx = old.load()
    r_n=g_n=b_n=0
    r_o=g_o=b_o=0
    cnt=0
    minx=miny=10**9; maxx=maxy=-1
    for y in range(h):
        for x in range(w):
            r,g,b,a = npx[x,y]
            if a>=128 and is_claw(r,g,b):
                cnt+=1
                r_n+=r; g_n+=g; b_n+=b
                ro,go,bo = opx[x,y][0],opx[x,y][1],opx[x,y][2]
                r_o+=ro; g_o+=go; b_o+=bo
                minx=min(minx,x); maxx=max(maxx,x); miny=min(miny,y); maxy=max(maxy,y)
    if cnt==0:
        return None
    avg_n=(r_n//cnt,g_n//cnt,b_n//cnt)
    avg_o=(r_o//cnt,g_o//cnt,b_o//cnt)
    # 褐化指标: 越"褐"则 r-g 与 g-b 都大且饱和低; 越"金"则 r-b 大、g 居中
    def brown(t): return (t[0]-t[1]) + (t[1]-t[2])  # 越大越暗褐
    return dict(n=name, cnt=cnt, avg_new=avg_n, avg_old=avg_o,
                brown_new=brown(avg_n), brown_old=brown(avg_o),
                bbox=(minx,miny,maxx,maxy))

print("=== 兽爪区域均值对比 (OLD 备份 vs NEW 003) ===")
for name in ["Note_Wide_skill_3_howl.png", "Note_Wide_Select_skill_3_howl.png",
             "Note_Tap_Small_skill_3_howl.png", "Note_Tap_Small_Select_skill_3_howl.png"]:
    s = claw_region_stats(name)
    if not s:
        print(f"  {name}: 未检测到兽爪区域(可能该音符无爪印)"); continue
    print(f"  {name}")
    print(f"    兽爪像素数={s['cnt']}")
    print(f"    OLD 均值={s['avg_old']}  褐化={s['brown_old']}")
    print(f"    NEW 均值={s['avg_new']}  褐化={s['brown_new']}  (褐化越小越金/越亮)")

# ---------- 旧/新并排 montage ----------
def side_by_side(old_im, new_im, label=""):
    w1,h1=old_im.size; w2,h2=new_im.size
    W=max(w1,w2); H=max(h1,h2)
    canvas=Image.new("RGBA",(W*2+20,H),(40,40,46,255))
    canvas.paste(old_im,(0,0))
    canvas.paste(new_im,(W+20,0))
    return canvas

def stack(images, gap=16):
    W=max(im.size[0] for im in images)
    H=sum(im.size[1] for im in images)+gap*(len(images)-1)
    canvas=Image.new("RGBA",(W,H),(30,30,36,255))
    y=0
    for im in images:
        canvas.paste(im,(0,y)); y+=im.size[1]+gap
    return canvas

# 普通态 montage (类型后缀, 拼接成 Note_{nm}_skill_3_howl.png)
normal_names=["Wide","Tap_Small","Repeat_3"]
sel_names=["Wide_Select","Tap_Small_Select","Repeat_3_Select"]
rows=[]
for nm in normal_names:
    f=f"Note_{nm}_skill_3_howl.png"
    old=load(os.path.join(BK,f)); new=load(os.path.join(SP,"003",f))
    rows.append(side_by_side(old,new))
montage_n=stack(rows)
montage_n.save(os.path.join(OUT,"montage_Normal_old_left_new_right.png"))

rows=[]
for nm in sel_names:
    f=f"Note_{nm}_skill_3_howl.png"
    old=load(os.path.join(BK,f)); new=load(os.path.join(SP,"003",f))
    rows.append(side_by_side(old,new))
montage_s=stack(rows)
montage_s.save(os.path.join(OUT,"montage_Select_old_left_new_right.png"))

# 兽爪放大裁切 (Note_Wide 普通态)
s=claw_region_stats("Note_Wide_skill_3_howl.png")
if s:
    x0,y0,x1,y1=s["bbox"]
    pad=8
    x0=max(0,x0-pad); y0=max(0,y0-pad); x1=min(load(os.path.join(SP,"003","Note_Wide_skill_3_howl.png")).size[0],x1+pad); y1=min(load(os.path.join(SP,"003","Note_Wide_skill_3_howl.png")).size[1],y1+pad)
    old=load(os.path.join(BK,"Note_Wide_skill_3_howl.png")).crop((x0,y0,x1,y1))
    new=load(os.path.join(SP,"003","Note_Wide_skill_3_howl.png")).crop((x0,y0,x1,y1))
    new=new.resize((new.size[0]*3,new.size[1]*3),Image.NEAREST)
    old=old.resize((old.size[0]*3,old.size[1]*3),Image.NEAREST)
    side_by_side(old,new).save(os.path.join(OUT,"claw_zoom_Note_Wide.png"))

print("\n已生成对比图:")
print(" ", os.path.join(OUT,"montage_Normal_old_left_new_right.png"))
print(" ", os.path.join(OUT,"montage_Select_old_left_new_right.png"))
print(" ", os.path.join(OUT,"claw_zoom_Note_Wide.png"))
print("\n003/ 当前文件数(含样本):", len([f for f in os.listdir(os.path.join(SP,'003')) if f.endswith('.png')]))
