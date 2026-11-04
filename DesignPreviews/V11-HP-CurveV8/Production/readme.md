# V8 生产素材更新与绑定

日期：2026-10-27。使用内置 imagegen，从最新 V8 整体稿分别提取连体装饰线和中性血槽；属于生成式重绘提取，不声称与概念图逐像素相同。

## 当前素材

`Assets/Art/UI/V11-Handpainted/CurveV8/`：

- hp-frame-red.png / hp-frame-blue.png：单图连体月牙和细线，蓝方由同一母版镜像换色。
- hp-capsule-neutral.png / hp-capsule-blue-neutral.png：红蓝中性血槽遮罩，供原 HP Shader 着色和裁切。
- hp-connected-neutral.png，以及 left / middle / right 中性切片：保留调形与伸缩用分层。
- hp-fill-red.png / hp-fill-blue.png：着色主体供美术查看，游戏实际使用中性遮罩。
- layout.json：源画布有效边界、三段 border 参数。

连体源有效尺寸 1864×322，Sprite border 左600 / 右280；蓝方交换为左280 / 右600。保持现有运行时 Sliced Image，按 frameHeight 自动匹配端部比例，中段随分数长度延伸。最终不同长度下的曲线仍可通过 06 的轮廓高度、分数后余量及分数位置调整；本次不新增曲线生成算法。

生成源图保存在本目录 frame-source.png / fill-source.png。装饰线转换为白色 RGB、保留 alpha 再着色，避免红方色相污染蓝方；机械裁边、切片、镜像、着色在 build-materials.ps1 中记录。

## 已切换运行绑定

`Assets/Resources/BattleVisualV11/ConnectedHeaderStyle.asset` 仅修改 redFrame / blueFrame / body / blueBody 四个图片引用。所有现有数值系数、数字布局、整体缩放、边距保持原值；没有重置用户参数，没有修改 C#、场景或 Combo 位置。

Unity 导入后重新进入 Play。参数入口仍为 Tools > Musical-Sprite > V11 > 06 Connected HP And Score Parameters。修改后用面板保存按钮保留。重点可调 bodyOffset / bodySize、frameTop / frameHeight、scoreOffset。具体视觉间距由用户作最后调整。

旧素材目录保留。改前配置完整备份：`.codex-backups/v11-curve-v8-materials-20261027/ConnectedHeaderStyle.asset`。回退时恢复这份配置即可。

materials-preview.png 是实际切片和已有图片字库的静态组合，不是实机图。已目视查看素材组合，未运行 Unity 或测试。配置差异检查仅四个图片引用发生变化。

## 提取提示：装饰线

Use case precise-object-edit, production sprite extraction. Input image is latest APPROVED HUD curve design. Extract ONLY the continuous CORAL CRESCENT + LOWER UNDERLINE + small end CURL belonging to the TOP LEFT RED full-health header. Remove every other item: health capsule, digits, label, green leaves, dash accents, title, other rows, blue UI, background. GENUINELY TRANSPARENT background. Preserve the exact graceful shape and proportions of this latest line: broad open crescent left, its lower bowl seamlessly becoming a clean thin gently rising curve toward the right, ending in the tiny downward curl. Smooth continuous tangent and curvature, no wiggles or thickness lumps, NOT a ruler-straight middle. Copy its outline faithfully, don't redesign, don't make ends bigger or introduce a wave. Flat solid coral red as reference, no highlights, shadow, glow, black outline or 3D. Center the isolated single component with transparent margin in a wide3:1canvas, maximum useful size, front flat orthographic. Nothing except that one connected decorative sprite.

## 提取提示：血槽

Use case precise-object-edit, production sprite extraction. Extract ONLY the FULL HEALTH CAPSULE from TOP LEFT RED header of the supplied design. Remove the crescent, lower underline, text, numbers, leaves, accents, title, all other headers and backdrop. Genuinely transparent background. Preserve this quiet silhouette: broad short gently curved band, very subtle shallow bow with softly rounded ends and only tiny thickness variation. NO repeated rise-fall-rise, no sinusoidal wave, no leaf-shaped point, no wiggling. It is still a calm readable almost-capsule, following ONE gentle arc. Recolor the entire extracted band to neutral white for runtime shader tinting, with only barely perceptible fine matte paper texture. No red or cream tint, no bevel, glow, shadow, outline or bright highlight. Full white filled shape, clean antialiasing, large on wide3:1canvas with transparent margins. EXACTLY ONE isolated blood-fill-mask sprite, faithful proportions about7:1 actual shape aspect.

