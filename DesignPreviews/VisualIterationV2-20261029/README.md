# 四项视觉迭代 V2 · 2026-10-29

依据用户录屏 QQ20261029-123732.mp4 的分析，经用户授权实施。仅处理已落地的过热、血条、Combo、音轨；没有保存 SampleScene，没有修改原地面、场景件、角色、中央能量分界、技能和评价 UI。

## 本轮接入内容

### 血条

- 原 PNG 造型、血量计算、左右方向、先闪白再实黄色/绿色追赶顺序保留。
- 刻度改为贴合锁定曲带中心线的小弧形，加入像素抗锯齿；常态宽2.4、移动宽4.2逻辑像素，移动发光强度2、柔晕范围11。
- 只有满血/归零时隐藏刻度；由满血开始损血的停留段也允许显示边界。
- 危险暗闪继续运行，受击不再把它整体停掉。变化区实色、白闪和刻度叠在预警之上，避免黄色/绿色被暗闪压成不可辨识的颜色。
- 没有重画 HP 字形、分数字形或基础曲带。`hud/` 为实际运行渲染器的隔离截图，`scene-loss-*.png`、`scene-heal-tick.png` 和 `scene-idle-tick.png` 为 SampleScene 运行相机加新血条的核对图。

### Combo

- 每个数字分别安排3块中心主体与6处外围分块，不再把主要碎块布局浪费在整串数字的间隙。
- 同串中不同位置使用确定性的不同切口，避免两个5完全同形撕碎；无每帧随机抖动。
- 保持约0.30秒：先整串轻拉扯，再裂开，随后灰化分离，最后淡出。提高纸边在真实屏幕尺寸下的可见宽度，灰片更明亮。
- 字体、装饰、位置函数均保留；零连击不播放，新命中取消旧碎片。
- `combo/` 是放大观察切口的组件小样；`combo-actual-size/` 是同一组件在实景背景上按1逻辑像素=1输出像素渲染，数字高度约110px，用于看正常尺寸下的纸边和灰片对比。该组是编辑器组件小样，不冒充完整Play截图；位置只用于小样展示，没有写入实际Combo锚点。
- 覆盖1、2、8、55、100、12345、67890及int.MaxValue，检查渲染、不空播和新命中打断。

### 音轨

- 保留四轨五边界、镜像逻辑、既有按下/成功/失败路由。
- 减少细芯相对占比，扩大软晕与内侧色光；近端交接削减重复叠亮，辅助线边缘补抗锯齿。
- 没有整体降低成功反馈强度。`rails/` 使用真实场地相机、原地面与运行网格渲染常态、按下、单轨、相邻轨、全轨五态。

### 过热分层

- 参考已选 `DesignPreviews/fever-animated-backdrop-v2.png`，用 imagegen 制作并接入两张实际透明生产图：`Assets/Resources/BattleVisualV11/FeverSceneryV2/fever-canopy.png`（低冠林叶拱桥）与 `super-branches.png`（花灯枝条增量）。两侧可共享图层，但每侧的状态、升降、循环相位独立。
- 这两张内容不同的资源替代运行时的过热占位图；正式路径跳过旧裁底部30%、队伍全图染色和程序色块底衬。原占位资源和原场景引用仍保留，未覆盖或删除。
- `FeverCanopy.shader` 提供6秒闭合循环：根部固定，叶冠和枝条小幅摆动，暖色花灯轻微明暗变化；不使用雾片、不改变整场光照。
- 保留后沿定位、所属半场范围与顶部限制，普通/超级两层独立升降、收起后关闭 renderer。自然暖色原画保留，不把蓝方整层染蓝。
- 分数火苗固定5个潜在位置：普通显示中央3簇，超级逐渐显出外侧2簇，避免3→5簇时重排跳位。火星也逐渐加入。

## 审查图及其边界

- `scenery-Fever.png`、`scenery-SuperFever.png`、`scenery-None.png`：实际Play场景相机与新血条/火苗；新布景替换、遮挡和收起核对。
- `scenery-loop-0.png` / `1.5.png` / `6.png`：固定场景下同一运行材质的循环采样；6秒回到同一画面。最近像素检查：中段改变33040像素，周期末差异超过阈值的像素为0。
- 这些运行相机图只临时把新血条 Canvas 转为相机 Canvas，其他 Overlay UI 未捕获，因此不能通过它们判断实际Combo/技能按钮是否出现。Combo使用上述实际尺寸组件图判断；完整联动仍以用户编辑器Game视图为准。
- `discarded-capture-attempts/` 保留尝试其他离屏叠加路径时产生的不完整截图和日志，明确不作为视觉完成证据。最终验证工具已恢复稳定路径，没有给游戏新增UI相机或改变生产渲染顺序。

## 验证

- `final-visual-proof.log`、`validation.txt`、`hud/unity-validation.txt`：Unity6000.0.82f1编译、运行材质/网格、血量时序与反向事件、Combo、1—6位分数布局检查通过。
- `runtime-verified.log`、`play-mode-smoke.txt`：SampleScene独立进程Play回归通过；旧HP/分数仍存在并隐藏；TakeDamage/Heal、等待归零再结算、重开清理通过。
- `scenery-projection.txt`：普通/超级均不出屏、不跨越所属半场，非活动层关闭；循环变化及6秒闭合像素检查通过。
- 最后的小改动只调整了Combo确定性切口及危险状态实色叠加，已由最终隔离测试重新编译和渲染；运行态血量事件路由没有更改。

## 参数、回退与使用

退出Play、等待Unity完成导入/编译后重新进入Play即可查看。

- 血条：`Assets/Resources/BattleVisualV11/FullRibbonHUDStyle.asset`。
- 音轨：`Assets/Resources/BattleVisualV11/RailStyle.asset`。
- 布景：实际场景的FeverSceneryController中，`useProductionArtwork`控制正式资源路径；`foliageLoopSeconds`控制周期，定位参数保持原入口。关闭正式资源后重新进入Play可回退旧图。
- 本轮修改前的9份源文件与参数资产：`.codex-backups/visual-iteration-v2-20261029/`。本轮没有清理旧素材、旧UI或用户工作区其他改动。
- Tools → Musical-Sprite → V11 → 08 Full Ribbon HUD And FX：血条逐状态审查。
- Tools → Musical-Sprite → V11 → 04 Combo Growth And Tear Review：撕碎与新命中打断。
- Tools → Musical-Sprite → V11 → 10 Capture Visual Iteration V2：重新生成隔离检查图（实际尺寸Combo背景使用本目录 `scene-background.png`）。
