# 血条细节补齐

日期：2026-10-25。依据用户新提供的头部 HUD 参考补齐小装饰，沿用第四批手绘主体、数字、HP 字样和既有回血 / 扣血逻辑。

后续游戏组合布局已接入，见 `game-header-layout.md`。最终摆放以游戏中新 Header 容器为准，本页的素材拼版只用于素材查看。

## 已接入

- 外侧两枚小叶。
- 内侧三枚短笔触与小圆点。
- 向分数位置方向延长的固定曲线尾饰，末端配小叶。
- 红蓝共用配置，位置与方向镜像；装饰不参与血量区间裁切。

`hp-details-material-preview-v1.png` 是真实素材合成的排版示意，非 Unity 截图，不作为实际容器位置证明。实际分数字体仍待后续批次替换。

## 查看与保存

重新进入 Play，或在 03 血条小样中重新载入。新增 **Tools → Musical-Sprite → V11 → 05 HP Detail Parameters** 会选中持久配置：

`Assets/Resources/BattleVisualV11/HPDecorationStyle.asset`

| 中文参数 | 作用 |
|---|---|
| 显示装饰 | 开关本轮小叶 / 短笔触 / 圆点；关闭时细尾恢复原长度 |
| 向分数延伸长度 | 固定细尾向场内延长的像素数，初始 190；可按实际分数位置细调 |
| 细尾透明度 / 高度 / 上下偏移 | 调整细尾的视觉层级、厚薄和高度 |
| 外侧叶片尺寸 / 偏移 | X 从场外一端向内计算，Y 从血条根底边向上 |
| 三笔触尺寸 / 偏移 | X 从内侧一端向外计算，Y 从根底边向上 |
| 圆点大小 / 偏移 | 小点的尺寸和同一内侧坐标偏移 |
| 末端叶片尺寸 / 偏移 | X 相对细尾内侧端点，Y 相对血条根底边 |

X/Y 均为原 Canvas 像素；红蓝自动镜像，不用填写两套相反符号。点击 Inspector 中“保存血条细节参数（退出 Play 后保留）”写入资产。改变配置会刷新游戏和 03 小样；未修改判定调参资产。

## 素材与生成记录

新独立透明 PNG：

- `Assets/Art/UI/V11-Handpainted/hp-strokes.png`
- `Assets/Art/UI/V11-Handpainted/hp-dot.png`

运行图集与切分矩形：`Assets/Resources/BattleVisualV11/HudAssets/hp-marks-v1.png`、`hp-details-v1.json`。复用已有 `leaves.png` 与 `hp-tail.png`。裁切基于 alpha 外接框，仅切分原图，没有重画旧数字、字样和主体。

使用内置 imagegen，参考 `DesignPreviews/battle-ui-option-c-rhythm.png`，透明背景。原输出 `exec-3d5e7d42-1c3c-4df8-bcfb-cb9003845f09.png`，项目已保存副本。提示词如下：

> Create TWO actual production HUD accent sprites, transparent background, wide 1024x512 canvas, exactly TWO equal square cells side by side. Match reference matte hand-painted forest rhythm game UI, reference STYLE ONLY. LEFT CELL: a group of exactly three small solid short rounded teardrop brush dashes, each tilted slightly forward, arranged in one horizontal row with even generous gaps; all ivory white, very subtle paper paint texture, can be tinted coral or soft blue at runtime. The three dashes slightly decrease in size left to right. RIGHT CELL: exactly one small round matte ivory-white painted dot, almost circular with just a gentle organic edge, no ring and no hole. Objects centered in each cell with wide transparent margins. NO words, NO digits, NO leaves, NO UI panels, NO halos, NO glow, NO blur, NO shadows, NO bevels, NO background, NO lighting rays. Fully opaque actual painted marks, transparent everywhere outside each mark except clean antialias edge. These are tiny restrained supplementary details to an existing elegant brush-ribbon health bar; NOT a complete health bar or concept board.

## 检查记录

Runtime / Editor 离线 C# 编译通过；素材合成图已查看。未运行游戏测试，不宣称实机位置已验证。备份：`.codex-backups/v11-hp-details-20261025-092707/manifest.json`。
