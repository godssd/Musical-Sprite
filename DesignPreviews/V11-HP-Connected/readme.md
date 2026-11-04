# 连体月牙 HP / 分数组合素材 V5

## 本次确认

按用户修改图制作：HP 与血量字样缩小；月牙下沿和分数底线连接；分数略放大；红方向右、蓝方向左延伸。现有手绘 0–9 字库继续复用，无需重画成完整数字串。

## 素材目录

`Assets/Art/UI/V11-Handpainted/ConnectedV5/`

- `hp-connected-neutral.png`：完整连体月牙与底线，中性母版。
- `hp-connected-left-neutral.png`、`hp-connected-middle-neutral.png`、`hp-connected-right-neutral.png`：同一母版的精确三段切片，不是三次重新生成。
- `hp-frame-red.png`、`hp-frame-blue.png`：着色后的完整轮廓，蓝版已镜像。
- `hp-capsule-neutral.png`：独立血量遮罩。
- `hp-fill-red.png`、`hp-fill-blue.png`：红蓝满血主体，蓝版已镜像。
- `hp.png`、`leaves.png`、`hp-strokes.png`、`hp-dot.png`：复用现有已制作素材的独立副本，便于这一版整体管理。
- `layout.json`：裁切坐标、横向伸展方向及边界。

原始生成画布保存在本目录 `frame-source.png` 与 `fill-source.png`。生产切片按 alpha>32 的有效边界裁切，移除远离主体的极低透明像素边界；源图保留。所有 Sprite 为单图、Full Rect、Bilinear、Clamp、无 mipmap、无压缩，最大尺寸 4096。中性母版保留轻微纸感，并非纯色二值遮罩。

## 伸缩与布局约定

连体轮廓裁切尺寸 1829×265，横向三段：左 600，中 949，右 280 像素。完整红图已设置左右 border 为 600 / 280；蓝图为 280 / 600。接入可选 Image Sliced，或直接使用三个独立切片。采用统一缩放确定高度与左右端大小，再只延长中段；不要整体横向缩放月牙和卷钩。蓝图本身已镜像，不可再次镜像。

数字按图片实际有效宽度和固定字距计算分数总宽度；红方分数左端固定，蓝方分数右端固定，叶片与卷钩跟随数字末端。数字不足位时不补零。达到中央安全间距时应对两侧整体布局施加上限；不允许两侧叶片或数字互相重叠。动态平滑延长与安全区限制属于后续接入工作。

组合稿使用参考布局单位：HP 标签字高 30、血量数字 38、分数字高 72、血槽 286×37；分数与血量相对层级可调。血槽和下方装饰线留缝，底线本身不随血量减少。半血/残血以同一主体遮罩裁切，空槽保持低对比。治疗绿色虚血沿用已确认逻辑。

`connected-materials-preview.png` 由实际素材、现有字库与三段切片合成，演示三种分数长度；均为满血，非 Unity 实机截图。图中的排版并非已修改的运行代码。

## 范围

素材制作阶段先新增生产素材及静态组合说明；用户随后批准接入，目前已绑定游戏运行入口，详见 [runtime-integration.md](runtime-integration.md)。旧版隐藏保留，Combo 位置和用户判定参数未改。

新轮廓与血槽通过内置 imagegen 生成；后续仅作透明边界裁切、三段切片、镜像、着色和预览排版。完整提示见 `prompts.md`。制作脚本为 `build-materials.ps1`，仍需后续运行时适配和用户实机审查。

