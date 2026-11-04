# ABC 细节、真实血量刻度与手绘评价接入

## 当前行为

- 刻度始终读取真实HP Target，不读取残影/待兑现游标Cursor。扣血、回血都立即更新刻度位置；满血标在终点，真实HP归零触发爆炸后不保留刻度。
- 刻度细核心仍为近白色，外光固定按阵营：红方红光、蓝方蓝光；不随扣血/回血改成黄光/绿光。黄绿变化区自身的轻外光仍跟随变化区颜色，二者是独立光层。
- 黄条仍按末次扣血后1.2秒（含边界）暂停、再次受伤暂停刷新；绿条叠加与先抵扣规则保持。真实HP归零立即死亡、爆炸与结算，不等黄条。

## ABC

A：以原素材为基础增加很弱的宽幅纸感色差，强度默认0.025；生命值与分数数字增加轻微下部渐暗，默认数字暗部0.06，HP更弱。没有重画血槽、头像或字形。

B：保留无连续亮边的平面黄绿条和柔和外光；刻度几何及辉光独立，光色按最新要求改为阵营色。

C：分数变化后，内侧小叶片和分数旁短笔触延后0.03秒做1–2px、约2–3度的轻跟随，0.24秒内归位。高频更新合并到当前响应，不排队。数字原有压缩、回弹、阻尼保留；头像、主血条不跟着抖动。

参数位于 Assets/Resources/BattleVisualV11/FullRibbonHUDStyle.asset 的ABC部分。

## 评价实装

已选透明字稿经确定性分层切图，分离奶油文字与可染色短笔触；清除孤立Alpha杂点，保留字形。资源位于 Assets/Resources/BattleVisualV11/Judgements/，layout.json保存每项区域与逻辑字高。

- 覆盖PERFECT、GOOD、CLEAR、PASS、YES、MISS；BREAK映射MISS。
- 文字保持原奶油色；短笔触按阵营染红/蓝，MISS灰紫。
- 采用实际判定位置投影到Overlay Canvas，保持屏幕字高，避免世界透视造成字号忽大忽小。位置跟随相机投影，少量上移并渐隐。
- 成功反馈轻压回弹，MISS较弱收束；新同轨评价只缩短旧尾迹，不吞掉合法判定、长条节点或连点事件。
- 预热16个显示实例，按需复用；重开清空旧反馈。没有修改评分、分数、Combo或过热计算。
- 修复旧路径YES映射，以及JudgeFeedbackItem在最终文字颜色设置前读取旧颜色的问题。
- JudgeFeedbackManager旧字段、预制体、旧Text动画保留。关闭Judgements/Style.asset的Enabled In Game，重新进入Play可回退旧评价；也可使用Manager的useHandpaintedJudgements开关。

## 审查入口

Unity菜单 Tools → Musical-Sprite → V11 → 09 Handpainted Judgements。

Play时可选红/蓝方与0–3音轨，单独播放各评价和BREAK。这些审查按钮仅调用表现，不产生计分、Combo、过热或玩法事件。窗口可调整字号、寿命、上移距离与笔触色。

## 验证

- PrepareAndValidate：实际资源导入；全部六种素材、共享HUD Shader、真实HP目标刻度位置（损血/回血）、红蓝光色；旧血条规则、立即死亡、数字阻尼和Combo撕碎回归。
- 原生GameView：六种评价、两队笔触、BREAK映射、20个合法重复判定不被吞、显示实例可到期清空；ABC、血条与Combo实际截图/序列。
- Play smoke：扣血/回血收敛、真实HP归零立即结算而黄条仍在、重开清理。
- 评价全类型验证图为了比较样式，会同时排开几种字样；不是录制到的一次自然判定。每种实例走的是实际JudgeFeedbackManager和新显示路径。

没有保存SampleScene，没有删除旧UI，没有修改角色/地面/场景件/轨道或技能输入。备份位于 .codex-backups/hud-abc-judgement-20261102/。
