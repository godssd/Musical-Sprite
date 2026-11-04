# V2 生成提示词记录

使用内置 imagegen，基于用户场景与已选 UI 参考图进行编辑。最终图保存于 DesignPreviews/battle-ui-approved-ingame-v2.png 与 DesignPreviews/battle-ui-approved-motion-board-v2.png。

## 整局合成

Use case: compositing. Create ONE latest in-game art-direction mockup by precisely editing input image 1.
INPUT ROLES: image 1 is the EDIT TARGET and immutable scene/layout baseline. Image 2 is the APPROVED exact hand-painted UI design to composite into the existing layout. Image 3 is the REQUIRED judgement-segment glow reference, not a background reference.

CRITICAL PRESERVATION: Keep image 1's original terrain texture and original irregular olive-green, muted brick-red and mauve painted patches. Preserve ALL existing ground props, the center stone stump, tufts, shrubs, flowers, standing plants, and every character's identity, pose, scale and location. Preserve all note shapes and positions, small yellow/silver energy bars under characters, central red/blue divide, isometric camera, arena shape and geometry. Preserve image 1's forest backdrop, lanterns and lighting; do not invent a new sunny garden, a new path, sky opening, buildings, more flowers, a new palette, or fog. This is a restrained UI overlay update, not an environment repaint. Keep the terrain under the UI as close as possible to original pixels. Full landscape frame at the same aspect ratio.

REPLACE ONLY THESE OLD UI ELEMENTS with image 2's actual designed shapes:
TOP HUD: remove capsule white borders and old green/yellow score fonts. At left top, a tapered sweeping coral ribbon HP shape exactly like image 2, cream handmade HP label and coral '326', score '5842' in large cream rounded hand-painted digits with small leaf accents. At right top mirrored periwinkle ribbon, cream HP label and blue '176', cream '4580' score beside it toward the center. Show right HP fill roughly 54 percent, left full, preserve visual separation of remaining fill and low-contrast empty ribbon. Broad rounded brush body transitioning to a very fine curved tail, not a thin underline, no dark outline. Copy the leaf and punctuation accents from image 2, no generic fantasy framing.
COMBO: at the two existing combo locations, replace the old outlined digits with image 2's coral '55' and blue '3', cream 'COMBO' lettering, small separated side accent arcs, and the EXACT small botanical base of leaves, white daisies and stones from image 2. Scale the entire decoration to fit modestly, the vegetation base is a low compact UI motif, not a replacement for real arena plants. Keep the footprint around the old combo area so nearby notes remain legible. No heavy black outline, no generic font.
SKILL ARROWS: replace only the three existing translucent square buttons on the right edge with image 2's cream hand-drawn left/down/right arrows in coral open brush rings, with the delicate flowing linking stroke. Keep original VERTICAL placement; adapt connecting stroke vertically. All three are coral because these are local red player inputs. Keep low opacity/transparent interior with no opaque disk; rounded ends, tasteful paint texture, a few small leaf-like ticks. One middle ring may show a subtle cream arc for accepted input. Do not move or redraw characters underneath.
JUDGEMENT LINES: add the ALREADY APPROVED glow from image 3 to the existing rails. Each side has four contiguous track-height judgement segments. Preserve their positions and perspective. Most segments remain a very thin subdued coral or blue hand-drawn rail, no thick black backing, no lamp fixture. Show one segment on the left side and one segment on the right side in hit state: the ENTIRE track-height rail segment glows with a narrow near-white core and soft red/pink or cyan/blue halo. From the segment's two ends, top and bottom edges of THAT SAME lane extend inward toward the center, fading completely within about one-third of the half-arena width. These edge ribbons must match image 3: wide extremely soft colored haze around fine near-white roots, subtly hue-shift toward violet, narrowing and fading in depth, not equally bright straight bars or hard rectangles. A broad translucent soft wash occupies the near portion of that lane. No mid-lane horizontal laser. Do not light every track at once. Remove obsolete heavy black rail only in the exact rail footprints.
JUDGEMENT TEXT: near the highlighted right segment, replace old spaced PERFECT with a compact handwritten cream 'PERFECT', a fine tapered blue-violet underline and only two small leaf flecks. No banner or badge, restrained size, no thick dark outline.

Style: literally the visual language of approved UI image 2, brushy rounded forms, cream ink, coral/periwinkle accents, tiny leaves. Crisp readable and finely soft lighting. No labels, arrows explaining design, storyboard borders, watermarks, or additional prose. Deliver one faithful scene composite.

## 判定线精修

Use case: precise-object-edit. Image 1 is the current finished game mockup; image 2 is the required soft-blue lane glow reference. Make a tiny localized correction ONLY to the judgement rails and their light. Preserve every other pixel as closely as possible: the new top HUD, combo art, arrow UI, characters, original ground texture and vegetation, forest, center boundary, notes and energy bars must remain unchanged.
The right-side judgement rail's unlit/idle segments are incorrectly coral/red: recolor these idle thin segments to subdued periwinkle BLUE with no red. Keep right team's active bottom segment's luminous cyan-white core. For that bottom lane, render the two inward-edge light ribbons like image 2: wider but much lower-opacity soft blue-to-violet falloff, fade fully before halfway to central divide; remove overly hard long thin laser edges. Top and bottom lane bounds should not appear as a rigid rectangle. On the left, keep coral-white active judgement segment but only one full track-height segment bright (second lane from top); other segments remain a narrow muted coral rail. Give the active left lane an extremely soft pink-violet wash near the rail and softly fading top/bottom edge ribbons. No separate lamp object, no horizontal central lane laser, no new objects, no text changes. The source screenshot is otherwise finished and should be retained exactly.

## 关键帧板

Use case: ui-mockup. Create a carefully designed game UI MOTION KEYFRAME SHEET, using attached image 1 as the exact approved UI art source. Maintain its hand-painted cream round numerals, coral and periwinkle tapered brush ribbons, leaves, plant-and-daisy-and-stone combo base, connected open arrow rings. Do not replace with generic vector UI, slim line charts, typed Arial numerals, badges, thick black outlines, boxes or solid circular buttons.
A spacious landscape art direction sheet with dark muted teal paper backdrop, organized into FIVE horizontal strips and THREE columns of before / action peak / settled frames. Thin quiet separators only. Large readable UI details. Each strip has a small clear Chinese heading at left. This is a static storyboard of the ACTUAL designed components, not a new alternative style. Label columns exactly "起始", "动作峰值", "收尾". Title exactly "UI 动效关键帧". Minimal labels only. Make all stage components have consistent anchors and scale; show deformations small but readable with tiny motion arcs only when useful.

ROW 1 label "扣血 · 保留并优化":
Show the approved coral curved HP ribbon, cream HP with coral 326 initially. Middle snapshot HP246: ribbon has a bright cream flash concentrated near lost-fill region and soft glow, subtle squash/rebound, the actual fill receded and a GOLDEN YELLOW trailing damage segment still occupies the lost portion; a small leaf bends. Final HP246: stable hand-painted coral ribbon about three-quarters fill, yellow trail has disappeared, leaf settles. Under states small times "0 ms", "80 ms", "950 ms". Preserve the original bold tapered curved outer silhouette, dark tinted empty tail, no white capsule frame. No cross or medical icon.

ROW 2 label "分数 · 字形接力":
Original approved cream hand-lettered 5842 and leaf accent, center 5942 with only changing digits gently rising 2-3 pixels with faint outgoing glyph trace, final 5942 clean with leaf back in place. Avoid slot machine cylinders or plaques. Times 0/80/160 ms. Make the settled digits the same visual size as original.

ROW 3 label "COMBO · 分层回弹":
Copy the approved exact combo design including coral 55, cream COMBO, separated curved accent marks, low botanical base of leaves, tiny white daisies and rocks. Middle 56 with small 7% numeral rebound and slightly open side strokes, leaf tips lag behind but base anchors remain fixed. Final 56 baseline, no large bounce, no particle explosion. Times0/100/220ms.

ROW 4 label "评价 · 一笔落定":
A restrained cream hand-lettered PERFECT above a tapered periwinkle brush underline and two tiny leaf flecks. Initial at low opacity slightly compressed; peak at full readability, barely larger and 5-degree handwritten slant; last is gently faded and a few pixels higher, not flying across screen. Times0/90/540ms. Keep font rounded handmade compatible with UI source, no badge no rectangular backplate no dark chunky outline. Use GREAT/GOOD nowhere; actual text must be PERFECT only in this row.

ROW 5 label "技能 · 输入到释放":
Each frame uses original three connected coral OPEN rings with cream ← ↓ → glyphs, delicate flowing linking stroke and tiny petal ticks.
Initial label "按下": left arrow/ring gently compressed with warm cream arc.
Middle label "输入接受": left and down ring have small highlighted arcs, right ring steady, no universal cooldown or progress bar.
Final label "完整释放": all three linking strokes gently glow in a traveling wave, ring tails extend slightly, exactly two tiny leaf flecks. Arrow centers stay transparent dark-teal like the source, not opaque disks.
Preserve source art's personality and foliage, show practical subtle animation changes, not dramatic ornamental redesign. No characters and no game arena in this sheet. This is a professional art-direction storyboard.

## 关键帧校正

Use case: precise-object-edit. Edit this motion keyframe sheet minimally. Preserve all existing art, layout, brush style, text and five-row organization EXCEPT these three accuracy corrections:
1) Second row center panel currently lacks the first digit 5 and shows a confusing number. Render complete readable cream '5942', exactly four digits, same baseline and size as the right panel. Only the second digit 9 is slightly raised with a very faint outgoing 8 trace directly behind it. Keep the leading 5, 4 and 2 perfectly stable. No extra golden starburst.
2) First row right settled frame 'HP246' must show a clearly shorter red filled ribbon than the left HP326 frame: reduce the bright red filled body to 75 percent of its initial bright red filled length, leaving the rest as the existing darker red track. The overall curved ribbon outline and tapered tail must not shorten. Center frame should show this same shorter red fill, with a golden yellow TRAILING damage section occupying the portion from 75 to100 percent of the initial bright filled body, no extension beyond the old fill position. Keep exact HP326 and HP246 numbers.
3) Bottom skills row has misleading millisecond times for a sequence of actual inputs. Replace bottom '0 ms', '100 ms', '220 ms' labels with '按键反馈', '前缀匹配', '释放反馈' respectively; keep top labels above button clusters unchanged. Don't alter other times.
No other changes.

## 数字最终校正

Make exactly ONE correction to the attached image: in row 2 (分数), MIDDLE column 动作峰值, replace the entire number group currently reading ghost 8 + 942 with the full number 5942. It MUST have FOUR clearly visible cream digits: FIVE NINE FOUR TWO. Copy the clean 5942 number group from the RIGHT column of the SAME row into the middle column, including the leading 5. Remove ALL ghost digits and all fading number traces from this middle group. Keep the four digits aligned in the same hand-painted cream font as the right column. Only slightly illuminate the 9. Leave every other pixel, title, all five rows, HP, combo, ratings, arrows and captions unchanged. No other edits.

