# 判定带直线版 V2

2026-10-23。V1 的弯曲、收腰、竹节式造型被用户否定，不作为后续实现依据。本轮仍为生成设计图，未修改 Unity。

## 修正原则

- 判定带沿原音轨边界笔直排列，宽度一致；四段长度根据实际音轨间隔与透视投影确定，不能照概念图不均匀的分段长度落地。
- 圆角仅用于端部，不能改变长边和判定位置。取消竹节节点、外翻尖角、弯尾和收腰。
- 卡通感通过珊瑚红 / 柔蓝、少量奶油色笔触与细微材质体现；游戏实际尺寸的描边、阴影应弱于放大示意图。
- 两侧边光的中心线和边界方向沿真实音轨直线延伸。远端渐淡、变窄、轻微变色，不弯曲、不下垂、不做曲线扫光。
- 柔和来自光强和透明度衰减、边缘羽化，而非扭曲几何。
- 保持常态、按下、命中使用同一判定带，只有对应段改变亮度。延续已讨论的触发逻辑。

效果图：`unified-judgement-straight-v2.png`。

## 生成方式与提示词

内置 imagegen；使用原场景截图及已选 UI 色彩参考，没有使用被否定造型作为编辑底图。

Use case: ui-mockup. Design a NEW clean elegant unified judgement rail for a hand-painted cartoon rhythm game, correcting a rejected wobbly bamboo-like version. Produce one carefully composed landscape design sheet with three state columns, NOT three alternative designs.
Reference image 1 is gameplay terrain and rail context only: preserve the original muted olive / mauve patchy terrain in demonstration swatches.
Reference image 2 is color and finish reference for the game's approved UI: matte coral, periwinkle, cream, fine hand-painted texture. Do NOT transfer its curved swooshes to the judgement rail.

TOP PRIORITY: ALL rail centerlines, long silhouettes, and emitted lane-edge light paths must be geometrically STRAIGHT. No bending, curling, wavy lines, S curves, crooked widths, pinched bamboo joints, tapering blades, pointed tips, horns, hooks, ribbons that turn, vine forms, disconnected bulky panels or black backing rods. Soft glow means smooth intensity and alpha falloff, NOT a curved light path. This is a precise rhythm-game input surface.

SHEET: restrained dark teal background, small cream title "判定带 · 直线版", three state headings "常态", "按下", "命中". No huge title art or decorative underlines. Large unobstructed views. The normal column takes 45% width and shows a coral and periwinkle mirror pair, the other two 27.5% each. Each rail lies on a recognizable small flat painted-ground swatch; ground remains muted to show UI clearly. No characters, no HUD, no change to terrain style.
RAIL SHAPE: one slim UNIFORM-WIDTH flat colored strip, four equal-length contiguous zones with 3 extremely thin transverse cream seams. Seam thickness 1px at game size, absolutely no bulges or nodes. Constant perfectly straight parallel sides from top to bottom. Top and bottom ends have tiny consistent corner rounding, like a carefully cut flat UI strip, not pill-shaped rounded ends. No large exterior border, no dark perimeter, no metal bevel or tube. A restrained narrow matte cream inner-edge accent, delicately softer darker-team-color shading inside the strip on outermost edge only. Smooth flat printed paint appearance. It must look meticulously designed, calm and modern, cartoon-compatible through color and subtle surface finish. Approximately 8–12 px gameplay width at 1080p, enlarged in the sheet. Perspective may incline whole straight rails but NEVER warp them. For mirror team pair, the coral rail tilts slightly to the right toward its top, the blue rail slightly to the left toward its top. They are straight trapezoidal arena boundaries. No width fluctuation except uniform perspective scaling.
IDLE: colored strip with low contrast fine cream accent. No broad bloom, each equal lane zone is distinguishable by tiny seams only. Clean modest silhouette.
PRESS: same red rail geometry, activate ONLY second zone from top, the entire zone becomes softly cream-white and emits wide very faint warm white wash horizontally into arena on the right. Do not change strip size or shape; no extra housing underneath, no separate lamp object.
HIT: same blue rail geometry, activate ONLY second zone from top in near-white pale cyan with a fine bright core and wide low-alpha diffusion. From the exact TOP and BOTTOM endpoints of this active zone, emit TWO geometrically straight horizontal light bands LEFT into the arena. Both are ruler-straight parallel rays, zero curvature at any point, no hooked corners. Near rail they have fine bright centers with a broad delicate cyan halo; with distance from rail, fade continuously through soft blue to a slight violet hint and disappear by half the swatch width. The diffuse rays may taper symmetrically around their STRAIGHT centerlines, but do not sag, bow or swoosh. The interior wash is broad and very transparent, with no hard rectangular far boundary. Nothing emits from the middle of the lane. The active segment stays in identical alignment and width with idle segments.
Use restrained delicate finish, ample space, very readable state comparison, precise matching proportions across panels. The new design should feel intentionally aligned and polished, not hand-drawn wonkiness. Text only the short title, three headings and red/blue tiny team labels.
