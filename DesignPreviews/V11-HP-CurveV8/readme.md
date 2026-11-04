# V8 装饰线平滑修订

使用内置 imagegen 对 V7 整体稿局部迭代，改善下方连体线的局部起伏和粗细接续，保留向分数抬升的走势。只作造型审查；未替换游戏绑定。生成稿仍不是精确矢量母版，正式制作需统一轮廓，保证红蓝镜像、满血 / 半血共用同一形状。

输出：curve-review-v8.png。

2026-10-27：用户授权更新素材，已完成独立生产素材和运行图片引用切换；所有数值参数保留。后续状态以 [Production/readme.md](Production/readme.md) 为准。

## 完整生成提示

Use case precise-object-edit. Edit the supplied game HUD sheet with ONE targeted correction: smooth the LOWER DECORATIVE CRESCENT-TO-SCORE UNDERLINE on all four header examples. Preserve all health bands, HP values, score digits, typography, leaves, tiny dash accents, navy background and positions.
The present lower stroke has subtle uneven wobble and thickness bumps under the HP band and the HP-to-score junction. Redraw that entire continuous decorative stroke as an expertly constructed smooth calligraphic curve with perfectly fair upper and lower contours. From the red OUTER LEFT crescent bowl flow into a single gently rising sweep toward the score RIGHT, ending at the existing small downward curl; exact mirrored direction on BLUE (crescent RIGHT, score LEFT). Think clean vector-path quality, continuous tangent and continuous curvature, no visible kinks, ripples, steps, cusps, repeated inflections, random waviness, or sudden thick/thin blobs. Keep crescent broad at outside bowl, smoothly taper into fine underline by HP band midpoint, then maintain nearly consistent fine width through rising long sweep, clean rounded terminal. Overall sweep retains its shallow elegant rising trajectory; do NOT flatten into a horizontal ruler, do not introduce a sinusoidal S wave. Use flat solid team color with clean antialiased edges; paper texture may remain in health/text but must not disturb line contours. Leave clear continuous navy breathing gap between underline and health band exactly as the original. The same smooth decorative silhouette in both 100% and50% states, blue mirror of red geometrically.
Do not add outlines, shadows, glow, highlights, symbols, labels or extra panels. Keep title and all unchanged design intact. This is geometric refinement of the stroke, not a style redesign.

