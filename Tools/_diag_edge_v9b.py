# -*- coding: utf-8 -*-
# 诊断：Note_Wide / Note_Tap_Small 基础图 alpha 边界 vs 可见边界 vs BFS 距离
from PIL import Image
from collections import deque

SP = "D:/unity/plan go/Musical Sprite/Assets/Art/UI/sprites/"

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

for name, ring_t in [("Note_Wide", 15), ("Note_Tap_Small", 3)]:
    im = Image.open(SP + name + ".png").convert("RGBA")
    w, h = im.size; px = im.load()
    D = edge_dist(px, w, h)
    print(f"===== {name}  size={w}x{h}  ring_t={ring_t} =====")
    y = h // 2
    print(f"-- 中行 y={y}: 从左往右前22个 alpha>0 像素 (x, a, RGB, D) --")
    cnt = 0
    for x in range(w):
        r, g, b, a = px[x, y]
        if a > 0:
            print(f"   x={x:3d} a={a:3d} rgb=({r},{g},{b}) D={D[y][x]}")
            cnt += 1
            if cnt >= 22: break
    x = w // 2
    print(f"-- 中列 x={x}: 从上往下前16个 alpha>0 像素 (y, a, RGB, D) --")
    cnt = 0
    for yy in range(h):
        r, g, b, a = px[x, yy]
        if a > 0:
            print(f"   y={yy:3d} a={a:3d} rgb=({r},{g},{b}) D={D[yy][x]}")
            cnt += 1
            if cnt >= 16: break
    # 统计：不透明区域里 D<=ring_t 的像素分布（按列位置分左侧/右侧/顶/底）
    import collections
    stat = collections.Counter()
    for yy in range(h):
        for xx in range(w):
            if px[xx, yy][3] >= 128 and D[yy][xx] <= ring_t:
                if yy < h*0.2: stat["top"] += 1
                elif yy > h*0.8: stat["bottom"] += 1
                elif xx < w*0.2: stat["left"] += 1
                elif xx > w*0.8: stat["right"] += 1
                else: stat["mid"] += 1
    print(f"-- D<=ring_t 像素分布: {dict(stat)}")
    print()
