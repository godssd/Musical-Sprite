# HP 曲带 V2 素材修订

日期：2026-10-25。

依据用户指定的头部裁图重画曲带：左端开放弯钩、独立圆润填充主体、由宽到细的支撑色带、尾端上卷。红蓝共用中性图层，蓝方镜像换色，避免两侧造型不一致。

## 文件

素材目录：`Assets/Art/UI/V11-Handpainted/RibbonV2/`。

- `hp-fill-aligned-v2.png`：中性填充主体。
- `hp-fixed-neutral-aligned-v2.png`：中性固定弯钩与支撑色带。
- `hp-composite-red-v2.png`、`hp-composite-blue-v2.png`：上述图层着色合成，供轮廓审查。
- `hp-master-red-v2.png`：完整红方造型母稿。
- `hp-fixed-red-aligned-v2.png`：红方拆层过程稿。
- `layer-layout-v2.json`：图层透明边界与源文件摘要。

所有源图层保留同一 2172×724 透明画布。接入时保持共同坐标、等比缩放；不可分别拉伸或独立裁边后按整图对齐。填充主体有效范围为左上原点坐标 x=234、y=282、width=851、height=149；后续血量进度须映射到该有效范围，不能直接使用整个画布宽度。固定层不随血量裁切。导入设置为 Sprite、无压缩、无 mipmap，最大尺寸 4096。

## 范围与检查

本批仅新增素材与预览，未替换游戏内 V1 绑定，未修改运行代码、血量逻辑或 Combo 定位。旧素材完整保留。后续旧 UI 采用隐藏方式保留回退，不删除。

已查看透明图层在深色底上的合成轮廓。`ribbon-v2-material-review.png` 是素材预览，不是 Unity 实机截图；游戏中的 Shader、缩放与实际摆放仍需接入后审查。

图稿通过内置 imagegen 生成；完整四次生成提示见 [prompts.md](prompts.md)。红蓝最终合成为已有透明图层的机械着色与叠合。
