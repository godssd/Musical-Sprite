# 视觉迭代 V3 · 2026-10-29

范围：依据 QQ20261029-182535.mp4 及补充意见，修复已落地的血条、Combo、过热布景和四轨光效。未保存 SampleScene；原场景件、地面、角色、中央能量墙、技能与评价未改。旧血条与分数仍保留并隐藏。

## 本轮改动

- 血条：基础 PNG 曲带、头像、月牙和字形保留。空槽恢复两队独立低饱和底色，端部卷钩保留队伍色。填充、黄绿实色区和刻度使用同一个曲面截面，避免弧形刻度压在竖直切口上。归零不残留微小实色片。红方右扣、蓝方左扣、实色追赶、满血隐藏刻度、濒危暗闪及延迟归零结算时序保留。
- 排版：HP 字高 38→34；分数按真实字形宽度排布，并为六个最宽数字预留固定空间，避免装饰点挤入 HP。分数 1–6 位保持同一字高、固定血条长度；位数变化仍平滑移动。超过六位只对 HUD 显示封顶，不改真实计分。
- Combo：移除 Voronoi 径向碎块，改为每个数字两条连续不规则主裂口、三块主体与边角小纸片。保留数字轮廓和原位置；先轻拉扯再打开纸边，灰化、错位、分散淡出。纹理切口确定，不每帧随机抖动。零连击不播，新命中仍清掉旧碎片。
- 过热布景：使用现有正式透明原画，不再按固定卡片宽高分别拉伸；世界显示比例保持原画比例。正式层有独立的相机顶部范围，普通与超级的原画轮廓差异得以保留。补充窗口 / 相机视口变化后的重新适配。未移动用户场景里的树或前景物件。
- 判定光：上下边细亮核心具有独立的长度和衰减，外围色光继续向内铺开、变色并淡出；降低近端重复叠亮，判定段外晕补柔和收边。保留四轨、直线中心路径和红蓝统一结构。
- 入场：身份区先蓄亮、中段快速推进、末端短收束，白光强度和推进头高亮随阶段变化；完成时头像和全套颜色一起还原，碎光加白色亮芯与淡光晕。初始完全不可见，不提前显示分数。

## 实景审查

`live/` 是直接读取 Unity 原生 1920×1080 Game View 渲染纹理的截图，包含真实场景和 ScreenSpaceOverlay UI。为便于对照，在 Play 中暂停并使用运行组件定格指定状态；不是效果图拼贴，也不代表整局自然输入录屏。

- `live/01-super-scene.png`：超级过热与常态 HUD 同屏。
- `live/02-hit.png`：双侧单轨命中。
- `live/03-damage-flash.png`、`04-damage-cursor.png`、`05-heal-cursor.png`：蓝方真实 ScoreManager 扣血 / 回血事件驱动。
- `live/06-combo-tear.png`：双侧断连 0.13 秒。
- `live/08-six-digit.png`：双侧六位数字占位。
- `combo-runtime.gif`、`entry-runtime.gif`：运行截图裁切形成的动图，分别按 0.03 / 0.04 秒采样；末帧停留便于循环观看。
- `combo-runtime-keyframes.jpg`、`entry-runtime-keyframes.jpg`：上述运行帧的时间对照。

## 验证

- `component-check.log`、`validation.txt`、`hud/unity-validation.txt`：Unity 6000.0.82f1 编译和 shader 渲染通过；损血 / 回血停留、反向打断、归零时序、实际计分事件、1–6 位排版、不同数字和长 Combo 网格、新命中打断及零连击检查通过。
- `live/runtime-validation.txt`：完整场景初始化、原 UI 隐藏保留、正式布景等比显示 / 顶部边界通过。
- `live/capture-status.txt`：各张原生 Game View 截图的尺寸。
- `live/renderer-diagnostics.txt`：实际运行 HUD 和材质状态；判定边光的独立参数为 0.58 / 1.70。
- `gameplay-regression.log`、`play-mode-smoke.txt`：真实 TakeDamage / Heal 结算、等待刻度归零再出结果、重开清除待处理失败状态通过。

## 入口及回退

退出 Play、等待 Unity 导入后重新运行。

- 血条参数：`Assets/Resources/BattleVisualV11/FullRibbonHUDStyle.asset`，新增红 / 蓝空槽底色；其余入口保持原样。
- 判定线调参工具新增“上下边细芯延伸比例”和“上下边细芯独立衰减”，与宽光晕分开。
- 正式过热层的高度由原画比例及相机限制决定；旧占位卡片高度参数只用于回退路径。正式层顶部上限 `productionTopLimit` 默认 0.985。
- 修改前源文件和血条配置备份：`.codex-backups/visual-iteration-v3-20261029/`。
- `Tools → Musical-Sprite → V11 → 11 Capture Visual Iteration V3`：重新生成组件审查图；原生 Game View 全场景定格工具为显式批处理 `VisualIterationV3Review.Batch`，不在普通编辑器启动时自动运行。

视觉仍以完整运行画面的用户审查为准。编译与状态测试通过不替代最终美术定稿。
