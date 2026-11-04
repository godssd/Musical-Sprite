# 头像尺寸修订 V2

2026-10-27。用户认可整段曲带方向，要求按圈注放大头像。通过内置 imagegen 局部编辑：增加头像内容占比，减少框内空白，边框稍扩并收细，红蓝三组同步要求；血槽和数字保持原方向。属于静态概念示意，未制作正式可换图头像组件、未修改游戏绑定。

输出 avatar-size-review.png。

基于本版的完整顶部 HUD 动效提案见 ../motion-design-v1.md；该文档是设计状态，尚未接入运行时。

2026-10-28 更新：以 ../motion-design-v2.md 为最新动效提案，补充 1～7 位分数布局与连续增长，取消头像及外框所有战斗动效。效果图位于 ../../TeamHUD-MotionBoards-20261028/，仍为待审查设计。

## 完整提示

Use case precise-object-edit. Image1 is the clean current approved HUD sheet. Image2 is user's annotation showing the OUTER EXTENT they want the avatar picture to occupy.
Make ONE localized change to Image1: enlarge the CUSTOMIZABLE AVATAR DISPLAY AREA and the placeholder picture inside it, in all SIX avatars (red acorn and blue leaf, all3rows). User says current portrait is too small. Treat the user circle as the intended larger display footprint, not as a new blue outline to reproduce.
Increase the usable clipped portrait diameter approx25–30%, make the acorn/leaf picture fill about85–90% of the portrait interior instead of sitting small in a large dark hole. Thin the circular frame slightly so the enlargement is primarily IMAGE AREA, not a bulky thicker ring. Adjust outer frame extent only modestly (about10–15%) as needed to make room; keep tasteful team-colored open crescent frame. Center each placeholder and keep small breathing gap to HP letters and ribbon start. Where needed expand outward into existing outer margin or make tiny positional accommodations, never overlap HP. All avatars stay aligned with their existing row and red/blue outer positions.
EVERYTHING ELSE LOCKED: keep exact score digits5842/4580, life values326/163/49, full-length ribbon geometry, 100/50/15% fill states, spacing between score and HP, typography, team colors, matte paper style, leaves and navy background, title, 3row layout, image aspect and composition. No new ornaments, no animation, no changes to blood bar, no card backing, no replacing placeholder identities. Do not draw user's cyan annotation. This should look like the exact approved sheet with more legible substantially larger avatar portraits and a restrained frame.

