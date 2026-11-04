# 整局效果图 V4：四音轨与绿色回血虚血

2026-10-23。本轮修改设计逻辑及效果图，未修改 Unity 运行时代码或场景。

## 最新效果图

`battle-final-four-lanes-healing-v4.png`

沿用已选手绘 HUD、原地面与场景布局，合入平面判定带、音轨辅助线、红蓝同形式命中。蓝方血条展示从 176 回复至 226 的早期动画帧，绿色短段表示新增血量的虚血。

## 回血规则（覆盖此前的单纯填充方案）

- 逻辑确认回血后，HP 数字立即显示新的真实 HP；绿色不是护盾，也不是尚未生效的治疗。
- 绿色虚血先覆盖旧血量到新目标血量之间的新增区间；原有红 / 蓝填充仍从当前显示进度平滑追上。
- 主色填充覆盖过的区域恢复本方红 / 蓝色，绿色可见区间随之缩小，完全追上时绿色消失。
- 不把整条血条或已有血量改绿，不把绿色画成常驻边框；两方都使用同一绿色回复规则，填充方向镜像。
- 扣血虚血仍为黄色，保留现有扣血反馈。回血不触发受伤闪白和受击回弹。
- 连续回血合并新目标，从当前画面继续；治疗被扣血打断时取消过时绿色区间，切换至最新真实 HP 的受伤反馈，不并列残留两种方向矛盾的虚血。
- 满血、实际回复量为零、初始化状态同步均不播放绿色虚血。
- 绿色区间以真实最大 HP 归一化裁切，不能按概念图的像素长度直接制作。填充时间先沿用现有动画配置作为比较起点。

## 音轨数量

整场为四条横向音轨，红蓝为对应两端。判定带有四个响应区间，只有三条内部音轨分隔；加上上下外边界共五个边界位置。上外边界在合成图中通过判定带起点表示，不另画额外分隔带。

最终 Unity 边界必须从真实四轨位置推导，不能照生成图中的非均匀间距落地。图像生成经过局部删线校正，用于展示四轨整体关系；不改变音符位置和判定范围。

## 光效

- 常态是平面本方色判定带和低透明直线辅助线，没有塑料高光或立体包边。
- 左右命中采用相同亮芯、上下两侧直线渐变和衰减，仅方向镜像、颜色不同。
- 光线越向中央越淡，不新增音轨中央光条；辅助线命中增强后恢复常态。
- 该图是设计合成，地面、角色、轮廓仍可能有生成局部偏差；实际工程沿用原素材。

## 生成记录

使用内置 imagegen；编辑底图为 `battle-ui-approved-ingame-v2.png`，平面判定设计参考为 `judgement-flat-lane-guides-v3.png`。提示词如下。

### 初次整局合成

Use case: precise-object-edit / compositing. Make ONE final full-game concept screenshot by editing image 1. Image 2 is the new flat judgement rail and lane-guide STYLE reference ONLY; its lane count is incorrect and MUST NOT be copied.

PRESERVE image 1: original terrain texture, olive/mauve/red painted patches, all ground plants, stones, center stump, bushes, flowers, all characters with their exact identity, poses, locations and scale, yellow/silver energy gauges, paw notes and their positions, camera angle, central energy divide, forest backdrop, lighting, approved top curved HP/score HUD, botanical combo 55 and 3, right-side coral skill arrow buttons. Do not invent an environment, repaint the floor, move props, add fog, redesign characters or add UI panels. Same complete landscape framing, not a design sheet. Only update rails, lane guides and one HP healing-overlay demonstration.

CRITICAL TRACK COUNT:
The arena has EXACTLY FOUR horizontal gameplay lane rows. Left and right are the two teams' ends of those SAME four rows, not eight stacked lanes.
There are EXACTLY FIVE boundary levels total: top outer edge, THREE interior separators, bottom outer edge. Five boundary lines define FOUR spaces. NOT six boundaries, NOT five lane spaces. Each of the two outer judgement rails is divided into EXACTLY FOUR consecutive lane-height segments. Use the four existing characters per team as alignment anchors. No extra band above or below. There must be no center-of-lane horizontal lines or extra ticks that could be counted as tracks.

NEW FLAT RAILS: Replace the existing black-backed/glowing outer judgement rails with narrow, geometrically straight, UNIFORM-width matte coral on left and soft periwinkle on right. Roughly 6–8 pixels at full gameplay size; no plastic highlights, no white perimeter outline at rest, no bevels, no black border, no cast shadows, no cap ornaments, no rounded capsules, no bamboo joints, no curving long edge. The three interior joins are subtle hairline breaks. No separate lamp object. Follow original perspective and hit coordinates.

LANE GUIDES: On each of the FIVE lane boundary levels, one thin low-opacity straight guide extends inward from each outer rail. Original ground remains clearly visible. Guides fade away approaching center, do not cross the central energy wall. Muted warm cream/coral on left, cool pale periwinkle on right; thin and subdued (approximately15 percent opacity), not a bright grid. No extra horizontal markings inside lane spaces. These should read as four lanes integrated with the rails.

HIT DEMONSTRATION: Show only the SECOND lane from the top active on BOTH left and right simultaneously, so directly comparable. Entire segment's SAME flat silhouette becomes near-white with a soft team-colored halo. The TOP and BOTTOM boundary guides of that lane become brighter straight light ribbons, with fine near-white roots, wide soft low-opacity feathered halo, fading smoothly in opacity and width toward center after about one-third of each half-arena's width. The other THREE segments stay matte idle.
Red and blue are STRICTLY IDENTICAL geometry, width, halo spread, ray lengths and brightness falloff, mirrored spatially; ONLY hue differs. Red near-white peach → coral/rose → faint mauve, blue ice-white → cyan/blue → faint periwinkle. No yellow spotlight cone replacing red's two edge rays. No curved rays or drooping boundaries, no far-end rectangular edge. No independent middle-lane beam. Move existing small PERFECT judgement text beside the active blue SECOND segment if needed, keeping original type/art style and not covering characters or notes.

HEALING GHOST ON BLUE HP:
Demonstrate blue HP healing from176 to226 (max326), at an early animation frame. Update ONLY blue HP label to exact "HP 226". Keep HP label/number design and all other scores unchanged: red HP326, red score5842, blue score4580.
The existing blue main fill still shows the OLD visible amount176 and grows leftward from the right anchored end. A SHORT CLEAR GREEN healing ghost segment extends immediately LEFT of that blue fill frontier to the new target226. Green covers ONLY the newly gained interval between176 and226, not the whole bar, not the already filled blue portion, not the empty remaining portion. It is integrated inside the approved curved ribbon silhouette, like colored flat paint. Rest of empty track stays dark muted blue. Use readable fresh soft green, not neon poison green. No green rim around entire bar, no green HP text, no additional floating+50 text. The frame must visibly show BLUE filled section followed by adjacent GREEN gained-health section and then EMPTY muted track in the correct mirrored order. In actual animation blue will consume the green interval; here capture the brief green ghost preview state.

Polished final screenshot, all originally approved HUD shapes retained, delicate restrained flat lane graphics, terrain/characters faithful. No external titles, labels, track numbers, legends, collage, inset panels or explanatory text.

### 边界校正

Precisely edit the attached game screenshot. Only fix judgement rail segmentation and horizontal guide/glow overlay. Keep ALL scenery, floor texture, every character, plants, stones, notes, UI, HP226 green healing section, scores, combo, buttons and central divide unchanged.

The current screenshot accidentally has an extra lane above the first horizontal guide. Replace ALL existing horizontal lane guides and judgement-rail segment joins with the following EXACT FOUR-LANE layout. Image is 1647x955; scale coordinates proportionally if output resolution differs.
There must be EXACTLY FIVE horizontal boundary levels:
A y=320 (top outer boundary);
B y=460 (interior separator1);
C y=600 (interior separator2);
D y=745 (interior separator3);
E y=890 (bottom outer boundary).
FOUR lane intervals are A-B, B-C, C-D, D-E. The rails start at A and end at E, with ONLY THREE interior joins B,C,D. ALL other horizontal guide or beam levels from previous image (especially y400,y486,y586,y703) MUST BE REMOVED and underlying ground restored locally.
Red rail remains a thin flat straight coral strip from approximately (325,320) to(215,890). Blue rail a thin flat periwinkle straight strip from (1320,320) to(1430,890). Width and endpoints stable, no thick border or bevel.
Boundaries A,D,E are low-opacity fine straight guides extending inward from each rail with a long transparent fade, nearly invisible by halfway between rail and central divide. They do not reach or brighten the central divide.
Only interval B-C (SECOND lane from top) is in hit state for BOTH teams. Its ENTIRE red/blue rail segment (y460 to600) has a near-white core and soft team-color halo. Boundary B y460 and C y600 have stronger perfectly straight fading light ribbons. Highest intensity directly next to outer rails, fade substantially within 250 pixels inward and fully fade by 350 pixels inward. Red beams extend rightward; blue beams extend leftward; same lengths, shape and brightness in mirrored team colors. Wide low-opacity soft halo, no hard rectangular right/left ending, no red field flooding the whole arena. No additional beam through lane center. At the image's center, these lights should be invisible; keep central energy wall untouched.
Five boundary LEVELS and exactly four intervals; do not add any sixth line, label or numbering.
Relocate the existing small PERFECT label next to the blue SECOND active segment near x1430,y550, preserving its art style and avoiding the character face; remove the old bottom label.
Everything else is finished and must remain identical.

### 多余分隔线局部删除

Make ONE strictly localized deletion to the attached image. DELETE the UPPERMOST horizontal guide line on BOTH sides, the line around y=400 in this 1647x955 image, immediately ABOVE the COMBO headings and directly below the top characters. Delete that entire thin coral/blue horizontal line from left rail to center and from center to right rail, and remove its tiny seam across each outer rail. Restore the original terrain / vegetation underneath that thin deleted line. This is an EXTRA divider which creates a fifth lane.
After deletion there must be ONLY THREE interior divider levels: the existing bright upper hit edge around y485, the existing bright lower hit edge around y585, and the existing faint divider around y710. Preserve the bottom edge around y890. The top is defined by rail endpoints around y320; do NOT add a new line. Four intervals remain: rail top to y485, y485 to y585, y585 to y710, y710 to rail bottom. These correspond to the four characters per side.
DO NOT change anything else. No redesign, no moving lines, no recoloring other lines, no adding a replacement for the deleted line. Keep all characters, plants, numbers, the HP226 green healing strip, existing notes and EXACTLY the three retained interior levels unchanged. Only erase the extra top line and its seam.
