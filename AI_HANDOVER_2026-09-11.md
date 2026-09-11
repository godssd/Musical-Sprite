# Musical-Sprite · AI 任务交接文档（2026-09-11）

> 用途：副机 WorkBuddy 会话无法跟随到主机，本文档导出当前进度与后续计划，供主机 AI 继续推进。
> 阅读后请从「六、下一步待办」开始执行。所有改动请遵守「二、铁律」。

---

## 一、项目身份

| 项 | 内容 |
|----|------|
| 名称 | 《Musical-Sprite》3D 锁视角对战音游 |
| 引擎 | Unity 6 LTS + URP 17.0.4 |
| 仓库 | `godssd/Musical-Sprite`（master 分支，已 push 到最新 `86f879e`） |
| 平台 | iOS / Android / PC |
| 美术 | 手绘卡通，参考《Cult of the Lamb》（高饱和、暗阴影、粗黑描边森林营地风） |
| 角色 | 小熊(主角·熊女孩) / 大狗(黄狗) / 小黑(黑猫·鱼骨吉他) / 布姆(炸弹沙锤) / 屎屎(牛角包蟹腿)，视觉已冻结不可改 |
| 视角 | 俯视锁视角，红蓝双方 5v5 镜像布阵，HUD 上方红/蓝血条 |
| 工程路径 | 副机 `D:\unity\plan go\Musical Sprite`；**主机 `D:\UGit\Musical-Sprite`** |

---

## 二、铁律（长期约束，违反会被纠正）

1. **效果「开放而非限制」**：贴图只答「是什么」(albedo)，系统答「看起来怎样」。地面/物体 shader 退为**中性画布**，氛围交全局系统（URP Volume 后处理 + 全局光照）。**绝不靠限制贴图内容（亮度/斑块/色相）来满足效果**——后续会有各种地面贴图，限制只会越加越多。
2. **改动可回退 + 清理残留**：旧实现隐藏(`_Old`/region)仅作临时可回退手段，方案确认废弃即**真正删除**，不长期堆 .bak / 死代码 / 注释块（防性能损耗）。
3. **verify-then-execute**：先给方案/参数表，用户说「执行」才碰文件；「继续/推进」≠ 执行。
4. **数值一律向上取整**为整数（伤害/回血/HP）。

---

## 三、Git 资产与操作约定（重要）

- **只排除两类**（既不 push 也不 pull）：`Assets/Beatmaps/`（谱面）、`Assets/Music/`（音乐）。其余正常上传。
- 自作美术（手绘贴图/材质/铺面等）**正常上传**，不进本地专属文件夹、不排进 .gitignore。
- pre-push hook 已改写：先拦截 Music/Beatmaps 推送，再链式 `git lfs pre-push` 保 LFS 上传。
- ⚠️ **WorkBuddy 自带 git 缺 git-lfs**：Bash 里直接 `git` 会因 LFS filter 失败（`git-lfs: command not found`）。解决：先
  ```bash
  export PATH="/c/Users/Administrator/AppData/Local/UGit/app-5.53.0/resources/app/git/mingw64/bin:$PATH"
  ```
  用 UGit 客户端自带 git+lfs（版本库自带 Studio 路径也可能不同，按实际调整）。

---

## 四、当前进度（已 push 到 master）

### M0 · 地面回归中性画布 ✅
- 文件：`Assets/Shaders/GroundEdge.shader`
- 删除了全部硬编码氛围：`_Focus*` / `_Ambient` / `_Mottle*` / `_EdgeAO*` / `_EdgeBrightness` / `_BottomColor`。
- **黑线根因 = edgeAO 同时压暗草边与台面内部**，M0 彻底移除后黑线消失。

### M1 · 晴天全局色彩分级 ✅
- 文件：`Assets/Settings/SampleSceneProfile.asset`
- ⚠️ **关键坑**：场景 `Global Volume` 引用的活动 Profile 是 `SampleSceneProfile.asset`（guid `10fc4df2da32a41aaa32d77bc913491c`），**不是** `DefaultVolumeProfile.asset`（guid `ab09877e2e707104187f6f83e2f62510`）。改错文件后处理不生效。
- 已含：ColorAdjustments（去黄、postExposure 0.15 / contrast 10 / colorFilter {1,1,0.96} / saturation 1.1）、Vignette（冷暗蓝边缘）、Bloom（0.8/0.4/0.6）、ShadowsMidtonesHighlights（暗冷紫蓝 / 亮暖黄绿）。

### Phase 1 · 实时阴影卡通化 ✅（已冻结，待全盘做完再迭代）
- 文件：`Assets/Shaders/GroundEdge.shader`
- 地面接收主光实时阴影：`MainLightRealtimeShadow(TransformWorldToShadowCoord(worldPos))`（需 `#include Lighting.hlsl` + 主光源阴影 multi_compile）。
- 阴影非死黑，改为有色半透明柔边：
  ```hlsl
  float shadowAtten = MainLightRealtimeShadow(shadowCoord);
  float softShadow = smoothstep(0.0, _ShadowSoftness, shadowAtten);
  float3 shadowedCol = finalCol * lerp(1.0, _ShadowColor.rgb, _ShadowIntensity);
  finalCol = lerp(shadowedCol, finalCol, softShadow);
  ```
- 三 Pass（ForwardLit/DepthOnly/DepthNormals）CBUFFER 各 21 个成员，含 `_ShadowColor / _ShadowIntensity / _ShadowSoftness`，成员顺序/类型须完全一致（SRP Batcher 约束）。
- 材质 `Assets/Art/Materials/M_GroundEdge_Arena.mat`：`_ShadowColor:{0.35,0.32,0.45,1}`、`_ShadowIntensity:0.55`、`_ShadowSoftness:0.35`。
- **状态**：方向获认可，但「比参考还差很远」，已按用户要求**冻结**，先把其他模块做完再回头精修。

### BlobShadow · 脚下接触暗补充 ✅
- `Assets/Scripts/Effects/BlobShadow.cs` + `Assets/Shaders/BlobShadow.shader` + `Assets/Resources/Materials/M_BlobShadow.mat`
- 主阴影是实时阴影映射，BlobShadow 降级为「脚下接触暗补充」。
- `CharacterCubeMarker.Awake` 自动 `AddComponent<BlobShadow>()`。
- 编辑器工具：`Assets/Scripts/Editor/BlobShadowSetupEditor.cs`（菜单 `Tools/Musical Sprite/为角色方块添加 BlobShadow`，批量给 LeftBand_*/RightBand_* 挂）。
- 修复：原 `mpb` 仅在 Awake 初始化，编辑器/动态挂载场景为 null → `GetPropertyBlock(null)` 抛 `ArgumentNullException`。已在 `UpdateShadowPositionAndScale` 前加 `if (mpb == null) mpb = new MaterialPropertyBlock();`（commit `86f879e`）。

### 其它已确立架构
- 树贴图 tree_2~5.png（用户美术资产，已入库）。

---

## 五、本次会话修复的两类报错

### 1. 39 个 CS0246（类型找不到）
- 根因：commit `fce3e96`（「2333」）误删 **106 个文件**，含全部运行时脚本（`HPBarDisplay`/`SkillSO`/`NoteData`/`CharacterDataSO`/`ActiveSkillRuntime` 等），`Assets/Editor` 工具自然全部找不到类型。
- 修复：`git revert fce3e96 --no-edit` → 新 commit `06e7d2c`，恢复 106 文件并 push。
- 副作用：`fce3e96` 新增的 `Assets/Art/Textures/Mottle_Temp.png` / `GroundEdge_Template.png` 被一并 revert 删除（已备份 `/tmp/ms_texture_backup/`，但**倾向不恢复**——Mottle 在 M0 架构已明确清理）。缺失的 `GroundEdge_Template.png.meta` 已 `git checkout HEAD --` 恢复。

### 2. ArgumentNullException: Value cannot be null (dest)
- 见上「BlobShadow」修复段，commit `86f879e` 已 push。

> 主机拉取最新 master 后，Unity 菜单 `Assets → Refresh`（Ctrl+R）重新编译即可。

---

## 六、下一步待办（按用户定优先级）

| 编号 | 任务 | 说明 / 参考方向 |
|------|------|----------------|
| M2 | 全局 Toon/Cel Ramp 明暗分块 | 卡通味关键。倾向用 URP 后处理或 Lit 改造做二值/多值明暗分层，非地面 shader 内硬编码 |
| M4 | 全局动态光斑 + TerrainProfile 动态地面接口 | 用户关心的「两侧地面可切换系统」；动态斑驳做成全局后处理 / light cookie，由 TerrainProfile 驱动 |
| — | 树冠投影机制 | 上方摆树冠面片自动投射软影 |
| — | 雾效 | **用户自行用 Shader Graph 重做**（AI 不碰）。集成到 PC/Mobile_Renderer 的 FullScreenPassRendererFeature；移动端必须补雾 |
| 阴影 Phase 2 | 调 Light Shadow Resolution / Cascade / Distance / Bias | 让实时阴影轮廓更干净，减少硬切/锯齿 |
| 阴影 Phase 3 | BlobShadow 改作脚下接触暗补充的精修 | 当前已降级，待主阴影到位后微调 |

> 优先级建议：先把 M2 / M4 这类「全局系统」做完，再回头按参考精修阴影 Phase 2/3（用户原话：把其他都做完了再迭代这块）。

---

## 七、遗留待用户拍板

- **历史 .bak 清理**：版本库仍跟踪 09-10 的历史 `.bak` 备份若干（`tree_1.png.bak` / `SampleScene.unity.bak` / `PC_Renderer.asset.bak` / `GroundEdge.shader.bak` / `Fog.shadergraph.bak-20260910` 等），普通提交 `git rm` 清理即可，**不要 force push**，等用户确认是否清理。

---

## 八、用户协作偏好（给后续 AI）

- Unity 初学者，国内 Windows 环境，常遇 Unity 冻屏/卡死（严重时强制重启）。
- 偏好：分步骤、表格化、附代码示例与排错说明；先讲原理再讲用法；附对比表与编辑器工具。
- 对 AI 准确性高度敏感，遇到可疑结论会主动挑战并要求可验证来源。
- 反复在执行前纠正方向性理解错误：要求先确认模型再动手，**禁绝未确认就盲目改代码**。
- 对话风格简短口语化，常以「编号 + 成对约束」方式批量下发需求。
- 游戏之余玩《问道手游》私服 1.6（零氪五开 2法金+1体木+1敏水+1敏火，主号 60 级），常需截图验证 UI 并逐步讲解机制。

---

*文档生成时间：2026-09-11 15:08 (GMT+8) · 由副机 WorkBuddy (TechnicalArtist) 导出*
