# 老版曲带 + 完整血槽 + 队伍头像

2026-10-27，内置 imagegen 静态样式迭代。回到用户原版手绘曲带，不使用上一批的大底板风格。

用户新定义：从月牙内侧到分数下方末端的整段都作为有效血槽；必须整段填充，不再使用“短血条 + 永久发亮的长尾”。新增一个玩家可设置图片的队伍头像槽，橡果 / 叶片仅占位。分数第一、生命值第二。

输出 full-ribbon-avatar-review.png，三排依次为满血、半血与15%血量概念示意。尚未制作运行素材、未改逻辑或游戏绑定、未设计动效。

概念图尚非准确遮罩：正式生产需整段统一 UV / 填充区间，蓝方内侧端卷钩在低血量生成稿中仍偏亮，应与空槽一同变暗；数值比例与精确镜像也需同母版控制。原图外侧月牙和头像圈是身份装饰，不参与血量。

## 主提示

Use case: precise-object-edit / static cartoon team HUD redesign.
Use the attached OLD forest HUD crop as the AUTHORITATIVE style and silhouette reference. User wants to RETURN to this light elegant handpainted style, adding a customizable team avatar and making the ENTIRE long curved ribbon UNDER BOTH HP AND SCORE the actual HEALTH BAR. No rectangular backing card, no wooden plaque, no sticker panel. Keep cream brush typography, coral/periwinkle colors, sparse fresh green leaf accents, flat matte fine paper grain.
Critical health interpretation: the whole continuous ribbon starting just inside the outer crescent, running under 'HP' and ALL THE WAY UNDER THE SCORE to the inner small curled endpoint is health-bearing. Not just a short fat pill under HP. NO separate foreground capsule, NO thin permanently bright decorative tail mistaken for health. All the active ribbon from outer start to inner endpoint is one unified fill mask. Full health=100% of THIS ENTIRE length bright. Half=outer half bright and inner half muted. 15%=only outermost15% bright, entire remaining continuous ribbon muted. Outer crescent surround may be a separate modest identity accent, but do not leave a bright copy of the long lower ribbon at half/low. Main track has readable moderate thickness along its WHOLE length (approx16–22px at600px-header scale), gently changing thickness like the reference sweep. It may be slightly fuller under HP, but MUST NOT become an almost invisible hairline under score. Smooth shallow arc, calm intentional trend, no twisting or sinusoidal wave, no abrupt thickness jumps.
Add ONE customizable team avatar socket at OUTER end integrated with crescent curve: a small round frame opened/embraced by the crescent rather than a giant hanging medallion. Red sample a simple acorn emblem, blue sample a simple leaf emblem; these are placeholder pictures, not fixed characters. Avatar visually secondary, diameter roughly HP text+bar combined height, smaller than score. Preserve breathing space between avatar and HP text, no overlap. Crescent, avatar and health ribbon feel like one naturally connected team identifier. A tiny paired green leaf at avatar/inner tip allowed, sparse only, no foliage bouquet.
Information hierarchy: SCORE biggest '5842' red, '4580' blue, cream handpainted numbers, life next 'HP 326' at100%, 'HP 163' at50%, 'HP 49' at15%. Both team examples use same MAX326 to permit matching comparison; other info subdued. No combo, plus-gain popup, animation/effects, labels claiming implementation.
Composition: landscape1536x1024 STATIC design sheet, plain deep navy backdrop. Three evenly spaced rows, each a complete RED header LEFT and BLUE header RIGHT. Exact mirrored layout on blue: custom avatar FAR RIGHT, HP near right, SCORE toward inner LEFT; text normal readable and blue health remaining anchored RIGHT. Red avatar FAR LEFT, HP near left, scoreRIGHT toward center, fill anchoredLEFT. Center row labels '100%', '50%', '15%'. Small top title '整段曲带血条 · 队伍头像'. All three rows use SAME full ribbon geometry and SAME header size, only HP number and fill proportion differ. At100% both long bands visibly bright to their very last inner end. At50% and15% inactive band is subdued charcoal tinted team color, still visible enough to understand actual full track shape.
Refine old reference into coherent functional health HUD, not a new style. Flat no shiny highlights, neon, beveled rims, thick black outline or big background panels. Keep large readable clean examples with equal margins.

## 字号与末端修正提示

Edit supplied STATIC HUD sheet with two precise corrections, keep its old handpainted forest ribbon identity and 3row red/blue layout.
1. HEALTH correctness: on50% and15% rows, ALL of the depleted ribbon INCLUDING the tiny inner END CURL under the SCORE must be muted charcoal team tint, not bright coral/blue. There must be no bright end tip at far end of depleted track. Red depleted inner end is on RIGHT, blue depleted inner end LEFT. Outer crescent/avatar surround may remain team color as identity frame. Full100% row remains bright along entire long track. Keep track geometry identical in all rows. No secondary bright decorative long line.
2. Hierarchy: make the cream SCORE numbers '5842'/'4580' about15% larger, and reduce HP label and life digits about10%, preserving readability and clean spacing. Score should dominate, life second. Do not move or mirror text incorrectly. Scores remain at inner side toward center, HP next to outer avatar.
Leave avatars placeholders, leaves, title, navy background, colors, and quiet flat texture unchanged. No animations, flash or extra labels. No new ornamental shapes, no shadows/bevels. This is a constrained refinement, not a new layout.

