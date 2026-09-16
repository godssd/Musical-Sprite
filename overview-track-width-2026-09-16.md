# 阶段 A 执行：音轨宽度 / 判定线 / 触控区 / 提示灯（2026-09-16）

> 依据用户报参（左侧玩家为标准）执行，先对齐后执行。备份：`Backup/2026-09-16-track-width/`

## 一、改动清单

| 对象 | 字段 | 旧值 | 新值 | 备注 |
|---|---|---|---|---|
| 左/右 NoteSpawner | `laneSpacing` | 1.5 | **1.7** | 两侧各 1；触控区/长按段宽/跨轨宽度自动跟随 |
| LeftHitLine / RightHitLine | `scale.z`（判定线视觉长） | 7.5 | **6.5** | 对齐新带宽 = 1.7×3 + 2×0.7 |
| 8× LaneIndicator（提示灯） | `position.z` | ±2.25 / ±0.75 | **±2.55 / ±0.85** | 移到新 lane 中心 |
| 8× LaneIndicator（提示灯） | `scale.z`（灯宽） | 1.275 | **1.4** | 对齐 lane 实际宽度（比例 0.85×1.7） |
| TouchZoneBuilder（红方） | `centerX` | -7 | **-6** | 触控区中心对齐判定线 |
| TouchZoneBuilder（红方） | `halfWidth` | 1 | **3** | X∈[-9, -3]（判定线身前3/身后3） |
| TouchZoneBuilder（红方） | `halfDepth` | 0.65 | **0.75** | 随 1.7 间距比例 |
| NoteSpawner.cs（代码默认） | `laneSpacing` | 1f | **1.7f** | 仅新实例一致（场景已序列化） |
| TouchZoneBuilder.cs（代码默认） | centerX/halfWidth/halfDepth | -7f/1f/0.65f | **-6f/3f/0.75f** | 同上 |

未动：`z:0` 两条非提示灯对象；判定线 X 位置（本来就是 ∓6，与报参一致）；CenterLine 粉线（push 杠，与判定线无关）；蓝方触控区（网络驱动，无）。

## 二、落地验证（python 重解析场景）
- `laneSpacing` 出现 2 次，均为 1.7 ✓
- HitLine `scale.z` = 6.5 ✓
- 8 个提示灯 z = ±2.55/±0.85、thick=1.4 ✓
- TouchZone 字段 centerX=-6 / halfWidth=3 / halfDepth=0.75 ✓

## 三、回 Unity 验证清单
1. 进场景/编译无报错（场景 YAML 已原子改写，计数校验通过）
2. 红方 4 条轨道变宽，提示灯与判定线视觉长度对齐到新带宽
3. 触屏点击范围覆盖 X∈[-9, -3]（判定线身前3到身后3）
4. 蓝方（网络驱动）表现不受影响
5. 地面 camo 若画死轨道线，1.7 后会错位——顺带目测一下

## 四、回退方式
`Backup/2026-09-16-track-width/` 含 `SampleScene.unity` 与 `NoteSpawner.cs` 改前副本，整文件覆盖即可回退。

## 五、下一批
音符大小 live 旋钮（`noteSizeScale`，原计划第 2 点）尚未做，等你确认后单独推进。
