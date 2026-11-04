# Combo 撕裂修订 V10：保留选定样式，只调整碎裂程度

2026-10-23。只制作效果图与记录，未修改 Unity 实现。

## 当前美术依据

用户本次明确选择的图已保存为 `combo-tear-user-selected-style.png`。其数字字形、简化装饰、块状撕裂形态、材质和灰化方式均为依据。
V9 最后一张把中央保留成近乎完整数字、外圈剥落的效果已被用户否定，不能继续作为制作参考。

本次修订效果图：`combo-tear-keyframes-v10.png`。用户于 2026-10-23 明确回复“这次对了，加到落地方案里去”，此版碎裂程度及既有样式已获确认。完整执行范围、技术分析及素材制作要求统一收录于 `visual-implementation-master-v11.md`；本记录保留作为图稿依据和生成过程存档。

## 只改变这一项

在用户选定的原有大裂口和已分离碎块上，按位置增加或减少二次细分：
- 中央碎块较大，但仍是已经被撕开的数字片，不保留一个完整数字核心。
- 向外经过中等碎块，外缘部分继续细分为较小碎片。
- 大、中、小碎片继续采用参考图的同一种块状撕裂样式。
- 通过分片数量与大小形成程度层次，不改为毛边、羽化、剥蚀、向外喷射或完整数字周围撒屑。
- 原态、字形、COMBO 标题、侧弧、两枚小叶、整体排版和灰化节奏继续沿用用户选图。
- 不恢复已经删去的 UI 装饰地面、石头与密集花草；游戏场景中的地面与场景件保持原有保留约束。
- 红蓝实际实现共用分片模板；生成图中的微小差异不转成两套规则。
- 零连击不播放，新命中可以中断旧退场，其余 V8 逻辑继续有效。

本记录覆盖 `visual-implementation-v9.md` 中可被理解为“中央数字保持完整、外缘剥落”的部分。
生成的关键帧是美术评审图，不是实际 Unity 录屏或可直接使用的精确动画序列。

## 生成方式与最终提示词

使用内置 imagegen，直接编辑用户本次选择的图片。

Precise restrained edit of the attached approved storyboard. The user has CHOSEN THIS EXACT VISUAL STYLE, including the torn numeral chunks. Preserve it. Do NOT redesign the effect.

Keep the canvas, typography, all headings, five columns, times, background, row labels, colors, COMBO label, side arcs, two tiny leaves, resting 55 shapes and all layout unchanged. Keep the entire first column EXACTLY. Keep the final column empty. The approved motif is a number visibly TORN APART INTO CHUNKS, as in this attached image.

ONLY adjust the DEGREE OF FRAGMENTATION across the already-torn numeral pieces in columns 2–4:
- Preserve the existing primary torn gaps and the familiar irregular chunky silhouette of the exploded 55 in column 3. Each 5 must still be clearly broken through its main body into separate pieces, like in this reference. Do not keep a nearly intact 5 in the center.
- Keep the chunks nearest the center of the overall number group relatively large.
- Subdivide some outward-facing portions of the existing chunks into medium chunks. Subdivide their outermost tips again into smaller chips. Add only short subsidiary tears within the EXISTING painted stroke area. Gradually increasing break-up density from center outward.
- Size hierarchy should be readable but moderate and faithful to the original: large central broken pieces, medium pieces next to them, small irregular outer chips. The outer chips should be parts of the digit which have further broken off, not arbitrary new confetti.
- Preserve the same rounded hand-painted material and torn-edge treatment as the reference. No new paper-fiber aesthetic, no feathering, no shredded brush strokes, no pointed radial jets, no erosion of a mostly intact digit.
- Do not enlarge or reshape the numeral strokes, turn them into intact central cores, change the pull direction, or change the overall footprint. This is a local increase/decrease in subdivision, not a different tearing animation.

Column 2 at .05 seconds keeps the current primary pull-apart cracks, with only a few smaller secondary cracks near the numeral's outer regions.
Column 3 at .13 seconds keeps the original separated large chunks; adjust by splitting their outer parts into progressively smaller neighboring pieces. Still a clear broken-number silhouette.
Column 4 at .23 seconds uses the same pieces in the same hierarchy as column 3, slightly separated and fading, with small pieces fading a little sooner. Do not reassemble the digits.
Red and blue use the same tearing pattern and timing, only original team hue differs. Keep the current subtle gray conversion and fade progression. No extra decoration, labels, arrows, particles, smoke, rocks or plants.
The difference from the input should look like a precise art-director refinement of fragment size, NOT a new illustration style.

