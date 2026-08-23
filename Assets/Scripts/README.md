# Musical Sprite 对战音游系统使用说明

## 一、脚本清单

| 脚本 | 作用 | 挂载位置 |
|---|---|---|
| `Conductor.cs` | 音乐时钟 | `GameManager` |
| `BeatmapSO.cs` | 谱面 ScriptableObject 模板 | 谱面资源 |
| `NoteData.cs` | 单个音符数据 | — |
| `Note.cs` | 音符实例组件 | 音符 Prefab |
| `NoteMover.cs` | 音符移动 + 过中线显示 | 音符 Prefab |
| `NoteSpawner.cs` | 发射器 + 判定器 | `LeftSpawner`、`RightSpawner` |
| `BattleCenterLine.cs` | 中间粉色竖杠对战移动 | `CenterLine` |
| `GameManager.cs` | 总控 + 事件接线 | `GameManager` |
| `TouchInputManager.cs` | 移动端触摸/鼠标输入 | `GameManager` |
| `TouchZone.cs` | 触控区标识 | 运行时生成 |
| `TouchZoneBuilder.cs` | 自动生成 8 个触控区 | `TouchInputManager` 同级或 GameManager 下 |
| `OpponentInput.cs` | AI 对手输入 | `GameManager` 或 `Opponent` |
| `JudgeFeedbackManager.cs` | 判定文字管理器 | `GameManager` |
| `JudgeFeedbackItem.cs` | 判定文字飘动动画 | 判定文字 Prefab |
| `OpponentVisualFeedback.cs` | AI 按键时轨道高亮 | `RightSpawner` |
| `LaneIndicator.cs` | 轨道指示灯（竖直线，点击发光） | 每轨指示灯 |
| `ComboDisplay.cs` | 连击数显示 | `ComboDisplay` |
| `BattleVisualsController.cs` | 乐队/指示灯/连击总控 | `GameManager` |
| `ScoreManager.cs` | 计分板管理 | `ScoreManager` |
| `ScoreDisplay.cs` | 单个玩家分数显示 | `ScoreLeft` / `ScoreRight` |
| `SceneSetupWindow.cs` | 一键搭建场景（编辑器工具） | — |
| `DemoBeatmapGenerator.cs` | 生成测试谱面（编辑器工具） | — |

## 二、场景搭建步骤

### 推荐：使用一键搭建工具

1. 打开 Unity，等待脚本编译完成。
2. 顶部菜单：`Tools > Musical Sprite > Setup Demo Scene`。
3. 按顺序点击：
   - **创建材质与文件夹**
   - **生成测试谱面**
   - **搭建完整场景**
4. 工具会自动创建：红蓝场地（面积随中线移动）、中线、判定线、发射器、音符预制体、触控区、AI 对手、乐队阵容、轨道指示灯（竖直线）、连击数显示、顶部计分板（左绿右黄），并连好所有引用。
5. 选中 `GameManager`，在 `AudioSource` 里拖入一首 `AudioClip`（可选，没有也能跑）。
6. 按 Play 测试。

### 手动搭建（可选）

#### 1. 创建 `GameManager`

- `Create Empty`，命名 `GameManager`。
- 位置 `(0, 0, 0)`。
- 挂载组件：
  - `Conductor`
  - `GameManager`
  - `TouchInputManager`
  - `TouchZoneBuilder`
  - `JudgeFeedbackManager`
- 给 `GameManager` 再挂一个 `AudioSource`：
  - 拖入音乐 AudioClip。
  - **取消勾选 `Play On Awake`**。
  - 把 AudioSource 拖到 `Conductor.musicSource`。

### 2. 创建中线 `CenterLine`

- Cube，命名 `CenterLine`。
- Scale `(0.2, 1, 9)`，粉色材质。
- 位置 `(0, 0.5, 0)`。
- 挂载 `BattleCenterLine`。
- 拖到 `GameManager.centerLine`。

### 3. 创建判定线与发射点

建 4 个空物体：

| 名称 | 作用 | 推荐位置 |
|---|---|---|
| `LeftHitPoint` | 左判定线 | `(-5, 0.5, 0)` |
| `LeftSpawnPoint` | 左玩家音符生成点 | `(7, 0.5, 0)` |
| `RightHitPoint` | 右判定线 | `(5, 0.5, 0)` |
| `RightSpawnPoint` | 右玩家音符生成点 | `(-7, 0.5, 0)` |

判定线处可放一个扁 Cube 当黄色视觉线。

### 4. 创建音符 Prefab `NotePrefab`

- 建 Cube，Scale `(0.4, 0.2, 0.4)`，给一个醒目材质。
- 拖进 Project 做成 Prefab，命名 `NotePrefab`。
- 删除场景里的原始 Cube。
- 把 Prefab 拖到两个 NoteSpawner 的 `Note Prefab`。

### 5. 创建左发射器 `LeftSpawner`

- 空物体 `LeftSpawner`。
- 挂载 `NoteSpawner`。
- 参数：
  - `Conductor` ← `GameManager`
  - `Beatmap` ← `DemoBeatmap`（先生成谱面，见下文）
  - `Center Line` ← `CenterLine`
  - `Side` = `0`
  - `Spawn Point` ← `LeftSpawnPoint`
  - `Hit Point` ← `LeftHitPoint`
  - `Note Prefab` ← `NotePrefab`
  - `Lead Time` = `2`
  - `Hit Window` = `0.15`
  - `Lane Count` = `4`
  - `Lane Spacing` = `1`
  - `Keys` = `A, S, D, F`

### 6. 创建右发射器 `RightSpawner`

- 空物体 `RightSpawner`。
- 挂载 `NoteSpawner`。
- 参数：
  - `Side` = `1`
  - `Spawn Point` ← `RightSpawnPoint`
  - `Hit Point` ← `RightHitPoint`
  - `Keys` = `H, J, K, L`
  - 其余同 `LeftSpawner`。

### 7. 把发射器填进 GameManager

- `Left Spawner` ← `LeftSpawner`
- `Right Spawner` ← `RightSpawner`

### 8. 创建 AI 对手输入

- 空物体 `Opponent`，挂在 `GameManager` 下或同级。
- 挂载 `OpponentInput`。
- `GameManager.opponentInput` 会自动接线，不用手动填。

### 9. 配置 `TouchInputManager`

- `Game Camera` 留空会自动用 `Camera.main`。
- `Left Spawner` ← `LeftSpawner`
- `Right Spawner` ← `RightSpawner`
- `Touch Layer`：
  - 打开 `Edit > Project Settings > Tags and Layers`。
  - 把 `Layer 6` 命名为 `TouchZone`。
  - 在 `TouchInputManager` 的 `Touch Layer` 勾选 `TouchZone`。

### 10. 配置 `TouchZoneBuilder`

- `Left Spawner` ← `LeftSpawner`
- `Right Spawner` ← `RightSpawner`
- `Touch Layer` 填 `6`（对应 Project Settings 里的 TouchZone）。
- `Show Debug Visual` 建议先勾上，确认位置后再取消。
- 如果勾上 debug，给一个半透明材质（见下文材质制作）。

### 11. 配置 `JudgeFeedbackManager`

- 参数默认即可，不需要 Prefab 也会动态生成 UI Text 飘字。
- 如果想自定义样式，创建世界空间 Canvas + Text 预制体，拖到 `Perfect Prefab` 等字段。

## 三、生成测试谱面

1. 打开 Unity，等待脚本编译完成。
2. 顶部菜单：`Tools > Musical Sprite > Create Demo Beatmap`。
3. 工程里会生成 `Assets/ScriptableObjects/DemoBeatmap.asset`。
4. 把 `DemoBeatmap` 拖到 `LeftSpawner` 和 `RightSpawner` 的 `Beatmap` 字段。

谱面内容：
- BPM 128，2 秒前奏。
- 120 拍基础节奏，左右轮流。
- 每 8 拍加双押，每 16 拍加一个小连打。
- 最后 16 拍双方同时对打。

## 四、相机设置

选中 `Main Camera`：
- `Projection` = `Orthographic`
- `Position` = `(0, 12, -10)`
- `Rotation` = `(45, 0, 0)`
- `Size` = `8`

如果是透视相机：`Position (0, 15, -18)`，`Rotation (55, 0, 0)`。

## 五、调试材质（可选）

用于显示触控区位置：
1. Project 右键 → `Create > Material`，命名 `TouchDebugMat`。
2. Shader 选 `Universal Render Pipeline/Lit`。
3. Albedo 颜色 `RGBA(1, 1, 1, 0.3)`。
4. 勾选 `Surface Type = Transparent`。
5. 拖到 `TouchZoneBuilder.debugMaterial`。

## 六、运行

1. 按 Play。
2. 左玩家用键盘 `A/S/D/F`，右玩家由 AI 自动按键。
3. 鼠标左键点击场景里的触控区也能触发输入。
4. 音符过中线后显示，到达判定线时按键。
5. 命中会出现 `PERFECT`/`GOOD` 飘字，Miss 会出现 `MISS`。
6. 命中会推动中线。
7. 按键时对应轨道的蓝色指示灯会亮起（玩家和 AI 都会）。
8. 画面中央绿色数字为当前连击数，Miss 会清零并显示 `MISS`。
9. 按 `R` 重新开始。

## 七、移动端测试

1. 切到 Android 或 iOS 平台（`File > Build Profiles`）。
2. 真机上手指按屏幕两侧的触控区即可。
3. 触控区在黄线到屏幕边缘之间，按 Z 轴分成 4 条轨道。

## 八、后续扩展

- 把 `NotePrefab` 换成真正的音符模型。
- 给 AI 按键加轨道高亮/音效/粒子。
- 用 Canvas + UI Text 做世界空间 HUD（血量、连击）。
- 网络对战：把真人右玩家的输入通过网络传给 `RightSpawner.TriggerLaneInput()`。

## 九、常见问题

### 触控区点不动
- 确认 `TouchZoneBuilder` 的 `Touch Layer` 和 `TouchInputManager` 的 `Touch Layer` 一致。
- 确认该 Layer 在 `Edit > Project Settings > Physics` 里没有被禁用射线检测。
- 确认主相机 `Main Camera` 标签是 `MainCamera`。

### 判定文字不显示
- 确认场景里有 `JudgeFeedbackManager`。
- 确认 `GameManager` 的 `Judge Feedback` 字段已引用。
- 确认音符 Prefab 能正常被 Destroy（如果音符被提前销毁，位置可能不对）。

### 音符一直不可见
- 检查 `NotePrefab` 的 Renderer 材质是否支持透明度。
- URP 默认使用 `_BaseColor`，已适配。如果是 Built-in，把 `NoteMover.cs` 里的 `_BaseColor` 改成 `_Color`。

### 中线不动
- 确认 `NoteSpawner.centerLine` 已引用 `CenterLine`。
- 确认 `CenterLine` 挂有 `BattleCenterLine` 脚本。

## 十、关键参数说明

- `Lead Time`：音符提前多久出现。手机端建议 `2`~`2.5`。
- `Hit Window`：判定容差。手机端建议 `0.15`~`0.2`。
- `Song Offset`（在 Conductor 上）：音频延迟校准，正数提前，负数延后。
