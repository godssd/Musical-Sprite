# 平面判定带与音轨辅助线 V3

2026-10-23 · 造型提案，未改 Unity。

## 本轮调整

- 红蓝命中统一采用蓝方参考的整段亮芯、上下边光和低透明内侧光晕；两方镜像，仅换配色。后续实现共用几何、光强包络、宽度、长度与渐变配置，避免分别调参导致表现不一致。
- 常态改为平面纯色色带，移除立体高光、倒角、投影和发亮外框。颜色为珊瑚红 / 柔蓝；真实游戏宽度初始按 1080p 下 6–8 px 比较，不直接照大图粗细制作。
- 每侧四条音轨使用五条边界辅助线（包含最外侧两条）。辅助线从判定带各分界向场内延伸；保持直线，靠近判定带较清楚，远端变淡，不横穿中央分界。
- 辅助线初始宽度约 1 px，近端透明度 15–20%，远端降至零；设计图为可读性展示，实际强度结合原地面校准。
- 不加入音轨中央光条、网格封框、刻度、方向箭头或新的地面贴图。地面、场景件、角色保持原资源。

## 状态关系

1. 常态：色带与低对比辅助线形成完整音轨 UI，无 Bloom。
2. 按下：对应整段中性柔白光与较弱场内渐变，红蓝规则相同。
3. 有效命中：整段变成偏本方色的亮白芯，同轨上下两条辅助线原位增强成柔光；越远越淡、越窄，中心线保持笔直。
4. 命中结束：增强部分回到常态辅助线强度；若仍按住，判定段回柔白输入态。
5. 红方采用暖白→珊瑚红/粉→淡紫，蓝方采用冷白→青蓝→淡紫。几何、视觉强度目标及衰减节奏一致。
6. 相邻音轨共用一条边界。同时命中时该边界取较强包络并限制峰值，不把光强简单翻倍。

本次效果图上方为常态，下方左右均为命中，不再用红方按下与蓝方命中作同状态对照。图是静态生成概念，辅助线条数、分段长度和透视位置需按实际四音轨锚点准确生成。

## 文件与生成记录

效果图：`judgement-flat-lane-guides-v3.png`。

使用内置 imagegen，以用户红蓝对照片中的蓝方为命中参考、原场景为地面参考。

### 完整提示词

Use case: ui-mockup. Create one refined game UI design sheet for a FLAT MINIMAL JUDGEMENT RAIL WITH LANE GUIDES. This is a new revision, not the old glossy rail.
Reference 1: ONLY the BLUE RIGHT HALF is the approved HIT light structure. The RED LEFT HALF is NOT a hit reference. Both teams must use IDENTICAL hit geometry, brightness distribution and soft fading, mirrored spatially, with ONLY red versus blue hue difference.
Reference 2: original game scene for its existing muted olive, reddish and mauve painted ground texture. Use this ground as swatches, do not redesign the terrain.

Layout: tasteful compact landscape sheet with small cream title "判定带与音轨辅助线". One wide TOP panel occupying about 50 percent height labeled "常态 · 平面简约". Two equal-sized BOTTOM panels labeled "红方命中" and "蓝方命中". Both bottom panels depict the SAME active lane, same scale and same camera, mirrored. No press-state panel at all. Labels outside art samples. Restrained dark teal sheet background, no ornamental boxes, no mock dashboard.

TOP normal panel: show an open rhythm arena portion, left red judgement rail and right blue judgement rail, and the original ground texture visible between. Both slim straight rails flank FOUR clear horizontal tracks, with FIVE geometrically straight track-boundary GUIDE LINES at the top, bottom and 3 interior divisions. Perspective stays consistent: outer rail may tilt as a straight line but never bends. Guides start at exact lane boundaries at each rail and extend inward toward the center, fading well before reaching the center, never forming an enclosed grid. Left/right lane boundaries align across arena.
Normal rails are perfectly FLAT solid MUTED coral or soft muted periwinkle strips, 6–8 gameplay pixels wide at 1080p, with no bright rim, no cream outline, no bevel, no glossy highlights, no reflected shine, no plastic tube, no shadow, no black or dark backing, no raised depth, no double borders, no physical housing. All long edges straight and width constant. Four equal projected lane-height zones separated only by 1px hairline quiet breaks. Top and bottom ends almost square with only a tiny corner radius, NOT capsules. Normal color is subdued but clear against terrain; zero bloom at rest.
GUIDES: thin 1px apparent width, warm gray/cream with a slight team tint, about 15–20 percent opacity near the rail and zero at their distant ends. Clear enough in this large sheet to show all four lane intervals, but much weaker than the rail. One continuous straight finely anti-aliased stroke per boundary, no dash pattern, no arrows, no center-of-lane marks, no giant ticks, no ruler, no grid boxes, no full-screen field tint. Their function is to link the rail into an elegant sparse track UI. Thin simple flat typography outside. This must feel quiet, graphic, professional and non-plastic.

BOTTOM HIT pair:
Use the exact same rail silhouette and the exact same guide layout as top. Only the SECOND lane from top becomes active.
The entire corresponding segment of the same rail lights in a near-white core with soft colored glow.
The two existing guide lines bounding that active lane intensify into straight thin bright cores plus wide extremely soft LOW-OPACITY feathered halos. These run inward, gradually decreasing brightness and width, hue-shifting slightly, then fading to transparent. A very subtle broader interior wash stays near the rail. All light paths mathematically STRAIGHT, no swoosh, curved edges, hook or arc. Near-root bright core must not become a hard rectangular outline. No far vertical edge. No mid-lane beam.
LEFT bottom coral version: rail at left, light extends RIGHT. Near-white peach core, coral and soft rose halo, a subtle muted mauve far fade. Avoid yellow spotlight cone.
RIGHT bottom blue version: rail at right, light extends LEFT. Near-white ice-blue core, cyan and soft blue halo, subtle periwinkle far fade.
They must look like the SAME effect recolored, same length, same softness, same halo breadth, same peak brightness, same fade. Show other idle lane guides faintly visible so the active state belongs to a complete UI.
Do not add plants, flowers, character drawings, energy bars, floating notes or HUD to this design sheet; use original ground swatches only. Prioritize exact geometry, refined quiet normal state, and consistent red/blue hit design. No explanatory prose inside image.
