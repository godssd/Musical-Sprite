# 手绘素材生成记录

方法：内置 imagegen，透明背景；非 CLI。风格参考：`DesignPreviews/battle-ui-option-c-rhythm.png`。以下为最终提示词；其他源文件为生成器默认保存的原始 PNG，项目内已有副本，不依赖外部绝对路径运行。

## 1. 数字初稿

Create a PRODUCTION bitmap glyph atlas for the selected hand-painted forest rhythm game UI in the reference. Reference is STYLE ONLY. Output genuinely transparent PNG, wide canvas preferably 1536x1024. Exactly TEN large separate digits, reading 0 1 2 3 4 on top row and 5 6 7 8 9 on bottom row. Strict regular 5-column by 2-row grid: each digit completely contained inside its equal cell, centered, generous transparent padding (at least 15% cell width and height), consistent cap height and common baseline within each row. All digits warm near-white ivory, neutral enough for runtime coral-red or periwinkle-blue tinting. Match reference's chunky rounded hand-painted brush numerals: broad confident soft matte silhouettes, gentle organic edge imperfections, very subtle light paper paint tonal variation, friendly slight forward lean but no wildly tilted glyphs. Each digit clearly readable, 1 has substantial width, 4 open unmistakable, 6 and 9 distinguishable, 0 closed oval, 8 two clear counters. NO lettering, labels, guide grid, shadows, glow, black outlines, bevels, plastic effects, stones, flowers or background. NOT a concept board: only the ten actual usable isolated glyphs in the specified grid. Keep nearly white opaque interiors, antialiased alpha edges, holes fully transparent.

原始输出：`exec-3288aa1c-7a55-473c-8e22-e2f852b6e72f.png`。未选作最终项目数字。

## 2. 数字透明边缘确认稿（项目采用）

Technical cleanup of THIS production numeral atlas. Keep all ten glyph shapes, placements, ivory fill texture and grid exactly. REMOVE ALL exterior glow, blur, soft atmospheric haze, bloom and color outside glyph silhouettes. Transparent background must be fully alpha=0 everywhere except the actual painted numeral strokes and a 1-pixel antialias border. The holes of 0 4 6 8 9 MUST be fully transparent too. Very clear crisp matte edges. No luminous outline. No shadows. No low-opacity fog. Numerals opaque warm ivory paint. Preserve exact 5x2 order 01234 / 56789, same 1536x1024 composition. This is a usable tightly bounded sprite atlas, never a glowing presentation image.

编辑目标为数字初稿；采用输出 `exec-4c30d620-0a69-4561-abed-6b829563603e.png`，保存为 `Assets/Resources/BattleVisualV11/HudAssets/digits-v1.png`。实际透明合成见素材拼版。

## 3. 字样与简化装饰

Production game HUD sprite sheet on genuinely transparent background, matching reference's matte rounded handpainted typography. Reference STYLE ONLY. 1536x1024 canvas, strict 2 columns x 2 rows with generous padding, all items centered in their own quadrant and not touching. TOP LEFT: only the uppercase letters HP, warm opaque ivory, broad brush letterforms. TOP RIGHT: only the uppercase word COMBO, warm ivory, loose confident playful brush letters, readable at small sizes, same as reference title. BOTTOM LEFT: a minimal PAIR of small simple olive green leaves, two separated brush leaf silhouettes leaning together, no stem tangles. BOTTOM RIGHT: two separated short warm near-white tapered curved brush accent strokes arranged vertically, for one side of a Combo number, minimal and elegant. All elements flat matte paint, subtle paper pigment texture, no shadows, no glow, no bevel, no outline. No ground, no stones, no flowers, no extra words, no grid, no panel, no backdrop. Transparent alpha everywhere outside actual painted marks. This is four separable usable HUD assets, not a presentation composition.

采用输出 `exec-99fe5b28-84bf-4a9c-a3bc-d79a491e179b.png`，保存为 `Assets/Resources/BattleVisualV11/HudAssets/labels-v1.png`。

## 4. 主体与固定尾饰

Production HP ribbon components for the selected reference forest rhythm game UI. Real transparent PNG, 1536x1024, strict TWO HORIZONTAL ROWS, each separate element has very generous padding from row boundary. TOP ROW: ONE wide horizontal filled brush-ribbon health bar BODY ONLY, opaque neutral warm white ivory for runtime team tint. Ratio of painted body roughly 7:1, broad even readable fill area, smoothly domed gentle upper curve and gentle lower curve, rounded ends, very subtle organic hand-painted edges and paper pigment. It must support a fill reveal from left to right, no holes in the body and no attached tails. BOTTOM ROW: ONE separate minimal long sweeping thin brush underline / decorative ribbon tail in same warm near-white, inspired by reference: elegant left-hand hooked turn sweeping down then to the right into a thin pointed upward flick. Ratio about 8:1. This fixed decoration is not the fillable body. Flat matte, understated sophisticated handpainted shape. NO text, NO digits, NO icons, NO outline frames, NO container capsule, NO glow, NO shadow, NO bevel, NO flowers or rocks or leaves. Only two actual separable ivory painted sprites, transparent elsewhere. Preserve reference's simple flowing ribbon language without plastic gloss.

采用输出 `exec-19f33134-be26-4ea9-8368-859ac8f55d8b.png`，保存为 `Assets/Resources/BattleVisualV11/HudAssets/ribbon-v1.png`。
