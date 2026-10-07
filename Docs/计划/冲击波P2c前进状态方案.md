# 冲击波 P2c 前进状态方案（3 秒窗口 + 累积距离）

> 状态：**第一步已落地（2026-10-06）——检测阶段**；第二步「清理无用代码」待检测无误后进行。  
> 前置：P2 弹簧阻尼、P2b 前进预备、P3 放大保持已落地。  
> 落地范围：`ShockwavePreview.cs` 新增 P2c 前进状态机（`advanceStateEnable` 总开关，关闭即回退旧逻辑），
> 旧代码（P2b 的 `UpdateAnticipTrigger`、P3 的 Hold 等）**暂未删除**，保留便于 A/B 对比与回退。

---

## 0. 结论先行

把当前「每帧独立判定剩余距离 → 每帧都可能触发后弹」的节奏，改成**前进状态机**：

- 每侧（红/蓝）独立维护一个 **3 秒前进窗口**。
- 第一次进入前进方向移动时，触发一次 P2b 后弹蓄力，并记录窗口起点 `baseX`。
- 窗口期内继续同向推进：**不后弹**，把 `|currentX - baseX|` 累积为前进距离，并按现有倍率表查得目标倍率。
- 窗口期内每次推进刷新 3 秒计时；3 秒内无推进 → 进入 **衰退态**，目标倍率缓慢回到 1.0。
- 衰退期间或之后对方/自己的其它移动**不影响本侧**的视觉大小，两侧完全解耦。
- 扣血补分（被推回）**不视为前进**，不累积；优势方保持其前进状态与累积值，自然维持大小。

这样「对方失误就后撤一下」的抽搐感会被合并成一次连续、有节奏的「蓄力 → 推进 → 保持 → 衰退」。

---

## 1. 问题诊断（基于录屏反馈）

### 1.1 当前逻辑

`DriveRuntime` 每帧做的事：

```text
dist  = |currentX - targetX|
targetScale = LookupScale(dist)            // 按剩余距离查表
redTarget   = (targetX > currentX) ? targetScale : 1f
blueTarget  = (targetX < currentX) ? targetScale : 1f

P2b 后弹触发条件：target - curScale >= forwardAnticipationMinGain
```

### 1.2 导致的症状

| 症状 | 根因 |
|---|---|
| **对方每次失误都后撤一下，像抽搐** | 每个小分差都会让 `targetX` 变化，只要 `target - curScale` 够门槛就触发 P2b 后弹 |
| **高频低数值前进没有节奏** | `dist` 只有 0.05~0.1 时倍率 1.05，但仍触发一次完整的 0.18s 后弹 + 弹簧前冲 |
| **连续前进没有第二次后弹，但也缺乏累积感** | 目标倍率只由「当前剩余距离」决定，连续命中只是把这个距离从 0.1 慢慢拉到 0.3，倍率从 1.05 到 1.1，视觉上几乎无差别 |
| **扣血补分把优势方推回时缩小** | 之前靠 P3 `enlargeHoldTime` 冻结解决；用户现在希望用前进状态本身来维持 |

### 1.3 核心洞察

**「前进」应该是一个持续状态，而不是每一帧的剩余距离。**  
一次连续进攻 = 3 秒内多次推进的合并；一次后弹 = 这次进攻的开始仪式。

---

## 2. 三种状态定义（与截图对齐）

| 状态 | 判定 | 视觉表现 | 是否后弹 |
|---|---|---|---|
| **对峙态（Idle）** | 两侧都没有明显位移，`currentX` 基本不动，或只有微小抖动 | 静止；弹簧已回到 1.0；可播对峙呼吸 | 否 |
| **变化态（Advancing / Decay）** | 某侧检测到连续同向推进 | 开始后弹一次，然后按累积距离持续放大；停止推进后进入衰退 | 仅 Advancing 开始时一次 |
| **阈值态（ScoreAdjustPush）** | `diff > catchUpDiffThreshold` → `ApplyCatchUp` 触发 | 劣势方只闪白、不变大；优势方保持前进状态 | 否 |

### 2.1 变化态内部再拆分

```text
Idle ──[检测到同向推进]──> Advancing ──[3秒内继续推进]──> Advancing（刷新计时）
                              │
                              └──[3秒内无推进]──> Decay ──[scale回到1.0]──> Idle
                              │
                              └──[衰退期间新的同向推进]──> Advancing（重新后弹）
```

- **Advancing**：窗口内，累积距离决定倍率。
- **Decay**：窗口结束，倍率缓慢回到 1.0。

---

## 3. 前进状态机详细设计

### 3.1 每侧独立状态

```csharp
private enum AdvanceState { Idle, Advancing, Decay }
private AdvanceState _redAdvState = AdvanceState.Idle;
private AdvanceState _blueAdvState = AdvanceState.Idle;
```

### 3.2 运行时字段

```csharp
// 红蓝各自前进窗口数据
private float _redAdvTimer,  _blueAdvTimer;   // 窗口剩余时间
private float _redAdvAccum,  _blueAdvAccum;   // 当前累积前进距离
private float _redAdvBaseX,  _blueAdvBaseX;   // 窗口起点 currentX
private float _redAdvLastX,  _blueAdvLastX;   // 上一帧 currentX，用于检测方向
```

### 3.3 检测规则

```csharp
// 一帧内 currentX 的变化
float delta = curX - lastX;

// 红方前进 = currentX 向 +x 移动（即向蓝方推进）
bool redAdvancingNow = delta > advanceAccumThreshold;
// 蓝方前进 = currentX 向 -x 移动
bool blueAdvancingNow = delta < -advanceAccumThreshold;
```

> 不用 `targetX` 判断方向，因为 targetX 在扣血补分瞬间会被改到反方向，导致误判。用 `currentX` 的实际移动方向最稳。

### 3.4 状态转换

| 上一状态 | 当前帧检测 | 处理 |
|---|---|---|
| Idle | 本侧未前进 | 无操作，保持 Idle |
| Idle | 本侧前进 | 进入 Advancing：`baseX = curX`，`accum = 0`，`timer = advanceWindowTime`，**触发一次 P2b 后弹** |
| Advancing | 本侧继续推进 | `accum = |curX - baseX|`，`timer = advanceWindowTime`，不后弹 |
| Advancing | 本侧停止/回退 | `timer -= dt`；窗口未结束期间保持当前 `accum` 不衰减；窗口结束 → Decay |
| Decay | 本侧前进 | 重新进入 Advancing（新 baseX = curX），**再次后弹** |
| Decay | 本侧未前进 | 目标倍率按 `shrinkTime` / 弹簧缓慢回到 1.0；回到 1.0 后 → Idle |

### 3.5 累积距离 → 目标倍率

```csharp
float targetScale = 1f;
if (state == AdvanceState.Advancing)
    targetScale = LookupScale(accum);   // 复用现有倍率表
```

复用现有 `LookupScale`：

| 累积距离 | 倍率 |
|---|---|
| 0 | 1.0 |
| < 0.2 | 1.05 |
| < 0.3 | 1.1 |
| < 0.5 | 1.2 |
| < 1.0 | 1.3 |
| >= 1.0 | 1.5 |

用户给的两个例子刚好对应上：累积 0.5 → 1.3；累积 1.0 → 1.5。

### 3.6 对峙呼吸的判定

对峙呼吸只在「两侧都处于 Idle 且 scale 都回到 1.0」时才播放。  
Decay 期间不播呼吸，因为 scale 还在变化；Advancing 期间也不播。

---

## 4. 与现有机制的兼容关系

### 4.1 P2 弹簧阻尼

继续保留。`targetScale` 由前进状态机输出，弹簧只负责把当前 `scale` 追到 `target`。

### 4.2 P2b 前进预备

后弹触发点从「每帧判定目标 - 当前」改为「Idle→Advancing 状态转换」。  
其它逻辑不变：sin 曲线弹开、回缩蓄力、预备结束后猛冲。

### 4.3 P3 放大保持

`enlargeHoldTime` 可以保留作为 Advancing 内的「防瞬间回缩」兜底，但**主保持机制已经变成 3 秒前进窗口**。  
建议把 `enlargeHoldTime` 与 `advanceWindowTime` 合并，或把 `enlargeHoldTime` 仅用于 Decay 前的宽限期。具体见下方参数表。

### 4.4 越界禁放大

仍然只压「被推回」的位移。  
在阈值态：

- 劣势方被补分推回 → `isPushedBack = true` → 该侧 `targetScale = 1.0`，不会进入 Advancing。
- 优势方中线也被往回推一点，但它处于 Advancing 状态，保持 `accum` 与 `timer` 不重置，视觉大小不掉。

### 4.5 扣血补分 `OnScoreAdjustPush`

```csharp
public void OnScoreAdjustPush(int side)   // side = 劣势方（被补分/扣血侧）
{
    // 劣势方：闪白，退出前进状态（不累积）
    if (side == 0) { _redFlashing = true; _redAdvState = AdvanceState.Idle; _redAdvAccum = 0; }
    else           { _blueFlashing = true; _blueAdvState = AdvanceState.Idle; _blueAdvAccum = 0; }

    // 优势方：保持当前 Advancing 状态与累积值，并刷新窗口计时（让它继续 Hold 住）
    if (side == 0) _blueAdvTimer = advanceWindowTime;
    else           _redAdvTimer = advanceWindowTime;
}
```

这样就不需要在 `OnScoreAdjustPush` 里单独刷新 `enlargeHoldTime`（用户明确说冻结机制不再需要）。

---

## 5. 新增/调整参数（全部 Inspector 可调）

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `advanceWindowTime` | float | 3.0 | 前进状态窗口期（秒）。窗口内再次推进只累积距离、不后弹、刷新计时 |
| `advanceAccumThreshold` | float | 0.01 | 一帧内 `currentX` 变化超过该值才算「前进」，防抖动 |
| `advanceDecayTime` | float | 1.2 | 衰退态从当前倍率缩回 1.0 的时间（复用/替代现有 `shrinkTime`） |
| `forwardAnticipationEnable` | bool | true | P2b 总开关（保留） |
| `forwardAnticipationTime` | float | 0.18 | 后弹时长 |
| `forwardAnticipationPullback` | float | 0.45 | 后弹幅度 |
| `forwardAnticipationScaleRatio` | float | 0.92 | 预备期回缩比例 |
| `forwardAnticipationMinGain` | float | 0.03 | 进入 Advancing 的最小倍率增益门槛 |

> 原 `enlargeHoldTime` 可考虑废弃或与 `advanceWindowTime` 合并。保留的话只作为「窗口结束后仍拖延一小会儿再 Decay」的宽限，避免手感太紧。

---

## 6. 关键代码改动点

### 6.1 替换目标倍率计算

当前：

```csharp
float dist = Mathf.Abs(curX - tgtX);
float targetMag = LookupScale(dist);
float redTarget = (tgtX > curX + 1e-4f) ? targetMag : 1f;
float blueTarget = (tgtX < curX - 1e-4f) ? targetMag : 1f;
```

改为：

```csharp
UpdateAdvanceState(ref _redAdvState, ref _redAdvTimer, ref _redAdvAccum, ref _redAdvBaseX,
                   ref _redAdvLastX, ref _redAnticipT, curX, 1);
UpdateAdvanceState(ref _blueAdvState, ref _blueAdvTimer, ref _blueAdvAccum, ref _blueAdvBaseX,
                   ref _blueAdvLastX, ref _blueAnticipT, curX, -1);

float redTarget  = GetAdvanceTarget(_redAdvState, _redAdvAccum);
float blueTarget = GetAdvanceTarget(_blueAdvState, _blueAdvAccum);
```

### 6.2 新增核心方法

```csharp
/// <summary>
/// 更新单侧前进状态机。
/// directionSign: 红=+1（前进方向为 currentX 增大），蓝=-1（前进方向为 currentX 减小）。
/// </summary>
private void UpdateAdvanceState(ref AdvanceState state, ref float timer, ref float accum,
                                ref float baseX, ref float lastX, ref float anticipT,
                                float curX, int directionSign)
{
    float delta = curX - lastX;
    bool advancingNow = directionSign > 0 ? delta > advanceAccumThreshold
                                          : delta < -advanceAccumThreshold;

    switch (state)
    {
        case AdvanceState.Idle:
            if (advancingNow)
            {
                state = AdvanceState.Advancing;
                baseX = curX;
                accum = 0f;
                timer = advanceWindowTime;
                // 触发 P2b 后弹
                anticipT = Mathf.Max(0.01f, forwardAnticipationTime);
            }
            break;

        case AdvanceState.Advancing:
            if (advancingNow)
            {
                accum = Mathf.Abs(curX - baseX);
                timer = advanceWindowTime;
            }
            else
            {
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    state = AdvanceState.Decay;
                    accum = 0f;
                }
            }
            break;

        case AdvanceState.Decay:
            if (advancingNow)
            {
                state = AdvanceState.Advancing;
                baseX = curX;
                accum = 0f;
                timer = advanceWindowTime;
                anticipT = Mathf.Max(0.01f, forwardAnticipationTime); // 衰退后重新前进再次后弹
            }
            break;
    }

    lastX = curX;
}

private float GetAdvanceTarget(AdvanceState state, float accum)
{
    if (state == AdvanceState.Advancing)
        return LookupScale(accum);
    return 1f;  // Idle / Decay 都回到 1.0（Decay 由弹簧/缓动自然回去）
}
```

### 6.3 对峙呼吸移动判定

当前 `moving` 判定包含 `dist > 1e-4f` 等。改为以两侧状态为主：

```csharp
bool moving = _redAdvState != AdvanceState.Idle
           || _blueAdvState != AdvanceState.Idle
           || _redAnticipT > 0f || _blueAnticipT > 0f
           || Mathf.Abs(_redScale - 1f) > 1e-3f
           || Mathf.Abs(_blueScale - 1f) > 1e-3f;
```

去掉 `dist > 1e-4f`，因为当前 `currentX` 与 `targetX` 的微小差异不再直接驱动 scale。

### 6.4 P3 ApplyEnlargeHold 的处置

保留但降低优先级：

- 仍可在 Advancing 内防止 `targetScale` 因为回弹抖动而下调（实际新状态机已处理，可作为双保险）。
- 或完全由 `advanceWindowTime` 替代，把 `enlargeHoldTime` 删掉/隐藏。

建议落地时先保留，验收后若冗余再删。

---

## 7. 调参顺序

| 步骤 | 调什么 | 验收标准 |
|---|---|---|
| Step 1 | `advanceWindowTime = 3`，`advanceAccumThreshold = 0.01` | 连续命中不会反复后弹；第一次命中后弹明显 |
| Step 2 | `forwardAnticipationPullback / Time` | 后弹幅度/时长有「蓄力感」 |
| Step 3 | `LookupScale` 表 | 累积 0.5 → 1.3、累积 1.0 → 1.5 准确对应 |
| Step 4 | `advanceDecayTime` | 停止推进后缓慢缩回 1.0，不突兀 |
| Step 5 | 阈值态测试 | 扣血补分触发时劣势方只闪白不放大；优势方不掉回 1.0 |

---

## 8. 风险与回退

| 风险 | 表现 | 处理 |
|---|---|---|
| **前进检测阈值太严** | 玩家正常命中但不触发前进 | 降低 `advanceAccumThreshold` |
| **前进窗口太长** | 优势方一直不掉下来 | 缩短 `advanceWindowTime` |
| **窗口结束瞬间缩小太快** | 有硬切感 | 加长 `advanceDecayTime`，或保留弹簧阻尼 |
| **对峙呼吸被抑制过久** | Decay 期间一直看不到呼吸 | 检查 `moving` 判定，Decay 回到 1.0 后应能呼吸 |
| **扣血补分误判为前进** | 被推回时反而放大 | 用 `currentX` 实际移动方向判断，不用 `targetX`；并在 `OnScoreAdjustPush` 清空劣势方状态 |

**回退方式**：把 `UpdateAdvanceState` 的两行调用注释掉，恢复旧的 `redTarget/blueTarget` 计算即可。

---

## 9. 落地步骤建议

| 步骤 | 内容 | 验收标准 |
|---|---|---|
| Step A | 加状态机和 `currentX` 方向检测，替换 `redTarget/blueTarget` 计算 | 单次推进有后弹，连续命中只后弹一次 |
| Step B | 调 `advanceWindowTime` / `advanceAccumThreshold` | 3 秒内多次命中合并为持续放大 |
| Step C | 调 `advanceDecayTime` | 停止后缓慢回 1.0 |
| Step D | 阈值态回归测试 | 扣血补分劣势方不变大、优势方不掉 |
| Step E | 对峙呼吸回归测试 | 静止后正常呼吸 |

每步完成后建议 commit，确保随时可回退。

---

## 10. 与用户描述的逐条对齐

| 用户原话 | 方案对应 |
|---|---|
| 「设置三秒的检测间隔，放 ShockwavePreview 里」 | 新增 `advanceWindowTime`，Inspector 可调 |
| 「第一次前进 3 秒内再次触发前进就不再后弹」 | Advancing 状态内不触发 `anticipT`，只刷新 timer |
| 「直接前进并将分数距离累积」 | `accum = |curX - baseX|`，按 `LookupScale(accum)` 得倍率 |
| 「累积 0.5 变成 1.3 倍，累积 1 单位变成 1.5 倍」 | 直接复用现有 `scaleAbove0_5=1.3`、`scaleAbove1_0=1.5` |
| 「刷新 3 秒持续时间重新计时」 | 每次推进 `timer = advanceWindowTime` |
| 「3 秒内没有发生前进才开始衰减缓慢恢复大小到 1 倍」 | `timer <= 0` 进入 Decay，目标回 1.0 |
| 「衰退期间或者之后对方移动都不会影响自己这边」 | 每侧状态独立，只由本侧 `currentX` 方向决定 |
| 「扣血补分机制的大小冻结不再需要」 | 优势方保持 Advancing 状态与累积值即可自然维持 |
| 「衰退期间或之后再次前进又要重新后弹」 | Decay→Advancing 转换时再次设置 `anticipT` |

---

## 11. 待用户拍板 → **已全部拍板（见第 14 节）**

1. ✅ **P3 `enlargeHoldTime` 是否废弃？** → **已删除**，由 `advanceWindowTime` 完全接管（14.3）。
2. ✅ **Decay 回 1.0 用 `shrinkTime` 还是新增 `advanceDecayTime`？** → **新增**，且进一步拆出 `advanceDecayHoldTime` + `advanceDecayShape`（14.2）。
3. ✅ **窗口内 `currentX` 回退但未反向是否仍保持 Advancing？** → **是**，保持到 timer 结束；回退只是不再续期，不立即 Decay。
4. ✅ **是否按本方案开始执行？** → 已执行（第 12 节第一步 + 第 14 节第二步清理）。

---

## 12. 落地记录（第一步：检测，2026-10-06 已完成）

用户指令：分两步执行 —— ① 落地检测是否存在问题；② 确认无误后再优化简化并清理无用代码。  
第一步已完成，改动全部在 `Assets/Scripts/ShockwavePreview.cs`。

### 12.1 实际落地内容

| 改动 | 说明 |
|---|---|
| 新增 `AdvanceState` 枚举 | `Idle / Advancing / Decay` |
| 新增 6 组状态字段 | `_redAdvState/_blueAdvState`、`_redAdvTimer/_blueAdvTimer`、`_redAdvAccum/_blueAdvAccum`、`_redAdvBaseX/_blueAdvBaseX`、`_redAdvLastX/_blueAdvLastX` |
| 新增 `UpdateAdvanceState()` | 单侧前进状态机；红 `directionSign=+1`（currentX 增大），蓝 `-1`（currentX 减小） |
| 新增 `GetAdvanceTarget()` | Advancing 返回 `LookupScale(accum)`，其余返回 `scaleAtRest` |
| 新增 `ExitAdvance()` | 退出前进状态并清空累积（suppress 命中 / `OnScoreAdjustPush` 时调用） |
| `SpringScale()` 加重载 | 新增带 `stiffness/dampingRatio` 覆盖的版本，供 Decay 用较软刚度 |
| 替换 `redTarget/blueTarget` 计算 | P2c 模式下由前进状态机输出；`advanceStateEnable=false` 走旧逻辑（A/B 对比用） |
| 删除 `dist` 变量 | 目标倍率不再由每帧剩余距离决定 |
| `UpdateAnticipTrigger` 不再调用 | 后弹触发点改由状态机的 `Idle→Advancing` / `Decay→Advancing` 转换负责 |
| `moving` 判定改写 | 由两侧前进状态决定，不再用 `currentX≠targetX` |
| `OnScoreAdjustPush` 改写 | 劣势方 `ExitAdvance`（不累积、不变大）；优势方刷新 `_advTimer` 保持状态 |
| `CaptureBase` 初始化 | `_redAdvLastX/_blueAdvLastX` 初始化为当前 currentX，防首帧巨大 delta 误判 |

### 12.2 与方案的三处差异（落地时按实际需要调整）

1. **Decay 的"缓慢恢复"用刚度而非时长**：方案原计划新增 `advanceDecayTime`，实际改为 `advanceDecayStiffness`（默认 20，正常 80）+ `advanceDecayDamping`（默认 1.0）。原因：Decay 仍走弹簧积分，用较低刚度比另起一套时长插值更简单，且与现有弹簧状态无缝衔接。**若验收时觉得恢复太快/太慢，直接调这两个值。**
2. **新增 `advanceStateEnable` 总开关**：默认 true。关闭即回退到旧的"每帧剩余距离"逻辑，便于 A/B 对比与紧急回退。
3. **P3 Hold 在 Decay 时立即释放**：`ApplyEnlargeHold` 加了 `state != Decay` 的条件。否则窗口结束后 target 虽已是 1.0，仍会被 Hold 再冻结 2 秒才开始衰减（实际会变成 3+2=5 秒）。

### 12.3 自查发现并修掉的隐患

| 隐患 | 表现 | 修复 |
|---|---|---|
| Decay 永不转回 Idle | `moving` 恒为真，对峙呼吸永远不播 | 在状态机后加判断：Decay 且 `scale` 已回到 1.0 → 转 Idle |
| `CaptureBase` 未初始化 `lastX` | 首帧 delta 巨大，被误判为一次前进并触发后弹 | 初始化为当前 currentX |

### 12.4 待 Unity 检测项（第二步清理前必须确认）

| # | 检测项 | 预期表现 |
|---|---|---|
| 1 | 连续得分推进 | 只在第一次推进时后弹一次，之后不再抽搐 |
| 2 | 停止得分 3 秒后 | 倍率缓慢回到 1.0，无硬切 |
| 3 | 衰退后再得分 | 重新后弹、重新累积 |
| 4 | 对峙静止 | 正常播呼吸开合 |
| 5 | 触发扣血补分 | 劣势方只闪白不变大；优势方不掉回 1.0 |
| 6 | `advanceStateEnable=false` | 回退到旧行为（对照用） |

### 12.5 第二步待清理清单（检测无误后再动）

| 对象 | 状态 | 处理建议 |
|---|---|---|
| `UpdateAnticipTrigger()` 方法 | 已无调用（死代码） | 删除 |
| `forwardAnticipationMinGain` 参数 | 仅被上述死方法使用 | 删除 |
| P3 `enlargeHoldTime` / `holdRefreshThreshold` / `_redHoldT` | 保持职责已被 3 秒窗口接管 | 待确认后删除或保留为兜底 |
| `_dispStartX` / `_prevTargetX` 越界判定 | 仍服务于"扣血方不变大"，与 `ExitAdvance` 有重叠 | 待确认是否合并 |
| 旧的 `else` 回退分支（`distOld`） | 仅 A/B 对比用 | 验收后可删 |

**注意：第一步的所有改动都是新增/替换计算路径，旧字段与方法均未删除，随时可回退。尚未 commit。**

---

## 13. 细节调整（2026-10-07，检测后反馈）

用户反馈：① 衰退变小时间过快，需要一个能直接调的参数；② 衰退期间重新前进应「结束衰退、维持当前大小、以当前累积值为起点继续累积」。

### 13.1 衰退改为【时长插值】，参数是秒数

原实现用弹簧刚度 `advanceDecayStiffness=20` 驱动衰退，不直观（刚度越小越慢）。现改为**按固定时长插值**：

| 参数 | 位置 | 默认 | 说明 |
|---|---|---|---|
| **`advanceDecayTime`** | Inspector → `ShockwavePreview` → 分组「P2c 前进状态（3 秒窗口…）」 | **2.0** | **衰退速度主旋钮，单位是秒**。调大=衰退更慢（如 2.5 / 3.0），调小=更利落 |

- 曲线：`SmoothStep`（ease-in-out），起步与收尾都柔和，无硬切。
- 衰退期间**不走弹簧**（`_redVel` 清零，避免切回弹簧时残留冲量造成突跳）。
- 衰退结束自动转回 `Idle`（不再依赖额外的 scale 检测）。
- `advanceDecayStiffness` / `advanceDecayDamping` 已标注废弃、不再生效，待第二步清理。

### 13.2 累积距离随衰退同步下降（不再瞬间清零）

进入 Decay 时记录 `decayStartScale` / `decayStartAccum`，衰退过程中：

```csharp
scale = Lerp(decayStartScale, 1.0, s);
accum = decayStartAccum * (1 - s);   // s: 0→1 同步推进
```

即倍率与累积距离严格同步下降，衰退结束两者同时归零。

### 13.3 衰退中途重新前进：维持当前大小、从当前值继续累积

`Decay → Advancing` 转换时不再重置 `accum = 0` / `baseX = curX`，改为**重新锚定 baseX 使 `|curX - baseX|` 恰好等于当前 accum**：

```csharp
baseX = curX - directionSign * accum;
```

效果：

| 阶段 | 行为 |
|---|---|
| 衰退中 | 倍率与累积距离同步下降 |
| 重新前进瞬间 | 立刻停止变小，维持当前大小 |
| 重新前进后 | accum 从**当前值**继续增长（不再从头累积） |
| 累积足够后 | 继续变大（如从 0.5 涨到 1.0 → 1.3 涨到 1.5） |
| 再次退出前进 | 重新进入衰退，倍率与累积同步下降 |

红方（`directionSign=+1`）`baseX = curX - accum`；蓝方（`-1`）`baseX = curX + accum`，两种情况下 `|curX - baseX|` 都恰等于 `accum`，后续推进即从该值继续增长。

---

## 14. 第二步：清理 + 衰退手感（2026-10-07）

用户指令：「现在可以把旧的无用的那些系数和代码清理了」+ 两个新问题：① 衰退曲线太平均；② 衰退期间进入前进后冻结、无法变大。

### 14.1 ⚠ 根因：累积距离被单帧阈值「冻结」（问题 2 的真正原因）

`BattleCenterLine` 用指数逼近：

```csharp
_currentX = Mathf.Lerp(_currentX, targetX, Time.deltaTime * smoothSpeed);   // smoothSpeed = 5
```

60fps 下每帧只吃掉剩余距离的 `dt*5 ≈ 8.33%`。一次 PERFECT = 100 分 → `targetX` 移动 `100 × pushPerHit(0.001) = 0.1` 单位，逐帧位移：

| 帧 | 剩余距离 | 本帧位移 |
|---|---|---|
| 1 | 0.1000 | **0.00833** |
| 2 | 0.0917 | **0.00764** |
| 3 | 0.0840 | **0.00700** |
| 4 | 0.0770 | 0.00642 |
| … | … | … |
| ~10 | ~0.046 | **0.00383** ← 跌破旧阈值 0.01 之前就已停止 |

旧实现 `accum` 只在「单帧位移 > `advanceAccumThreshold`(0.01)」那一帧才更新，所以推进尚未走完就被**永久冻结在起步那 3 帧的小量（≈0.03）**，查表只有 `scaleTiny = 1.05` —— 表现就是「进了前进状态却再也长不大」，衰退期重新前进时尤其明显（因为起点更小）。

**修复**（`UpdateAdvanceState` 的 `Advancing` 分支）：

```csharp
// 每帧无条件跟踪真实推进量，不再依赖单帧阈值
float travelled = Mathf.Max(0f, directionSign * (curX - baseX));
bool movedForward = travelled > accum + 1e-6f;   // 累积距离还在涨 = 自己还在推进
accum = travelled;

if (movedForward) timer = advanceWindowTime;     // 续期
else { timer -= dt; if (timer <= 0f) { /* 进入 Decay */ } }
```

配套：`advanceAccumThreshold` **默认 0.01 → 0.003**，且**只用于 Idle / Decay 的起跑判定**（防抖），不再参与 Advancing 内的累积与续期。

阈值取值依据（一次判定的 `targetX` 位移 vs Lerp 首帧位移）：

| 判定 | 分数 | targetX 位移 | 首帧位移 | 是否起跑 |
|---|---|---|---|---|
| PERFECT | 100 | 0.100 | 0.00833 | ✅ |
| PASS | 80 | 0.080 | 0.00667 | ✅ |
| GOOD | 60 | 0.060 | 0.00500 | ✅ |
| CLEAR | 50 | 0.050 | 0.00417 | ✅ |
| YES | 10 | 0.010 | 0.00083 | ❌（连点中间击，本就不该算推进） |

### 14.2 衰退曲线：加「僵持余韵」+ 形状旋钮（问题 1）

旧曲线是标准 `SmoothStep`（S 形，中段最快）→ 观感「平均」。改为两段：

| 参数 | 默认 | 作用 |
|---|---|---|
| **`advanceDecayHoldTime`** | **0.4s** | 退出前进状态后**先完全维持当前大小**这么久（余韵），之后才开始下降。**「不要一停就缩」的主旋钮**，0 = 立即开始降 |
| **`advanceDecayTime`** | 2.0s | 下降段时长（秒）。调大=更慢 |
| **`advanceDecayShape`** | **1.6** | 形状：1 = 旧的标准 S 形；**>1 = 先撑住后卸力**（重心靠后，推荐 1.5~2.2）；<1 = 先快后慢（泄气感） |

```csharp
float k = Mathf.Clamp01((decayT - advanceDecayHoldTime) / Mathf.Max(0.01f, advanceDecayTime));
float s = Mathf.SmoothStep(0f, 1f, Mathf.Pow(k, advanceDecayShape));
```

`SmoothStep` 保证两端速度恒为 0，**任何 shape 都不会出现硬切或速度突变**。衰减比例对比（`k` = 下降段进度）：

| k | shape=1（旧） | shape=1.6（新默认） |
|---|---|---|
| 0.25 | 15.6% | **3.3%** |
| 0.50 | 50.0% | **25.5%** |
| 0.75 | 84.4% | **69.2%** |

即前 1/4 时间几乎不掉（撑住），后半段才卸力，层次明显强于旧的匀速观感。

### 14.3 清理清单（已执行）

| 删除对象 | 原用途 | 删除理由 |
|---|---|---|
| `advanceDecayStiffness` / `advanceDecayDamping` | 衰退弹簧刚度/阻尼 | 衰退改时长插值后不再生效（13.1 已废弃） |
| `UpdateAnticipTrigger()` | 旧的「每次目标变大就后弹」触发 | P2c 状态机接管触发点后成为死代码；且旧逻辑会让连续得分反复后弹（抽搐） |
| `forwardAnticipationMinGain` | 仅被上述死方法使用 | 同上 |
| P3 整套：`enlargeHoldTime` / `holdRefreshThreshold` / `_redHoldT` / `_blueHoldT` / `ApplyEnlargeHold()` | 2 秒放大冻结 | 3 秒前进窗口已完全接管「维持住就不缩小」；旧机制会在 Decay 时额外挡一道（曾导致 3+2=5 秒才开始衰减）。扣血补分保优势方改由 `OnScoreAdjustPush` 直接刷新 `_advTimer` 实现 |
| `SpringScale()` 的 5 参重载 | Decay 用较软刚度 | 衰退不再走弹簧，重载无调用 |
| `_redSuppress` / `_blueSuppress` 字段 | 越界禁放大中间量 | 改为 `DriveRuntime` 内局部变量即可，无需跨帧状态 |

**保留未删**（仍在生效 / 属于回退开关）：

- `advanceStateEnable`（P2c 总开关，紧急 A/B 回退）
- `useSpringModel` + `AnimateScale()` + `enlargeTime/shrinkTime/...`（P2 弹簧的回退路径，用户落地时明确要求可开关）
- 越界禁放大（`autoSuppressEdge` / `enlargeSuppressEdge` / `_dispStartX` / `isPushedBack`）—— 服务于「扣血方不变大、只闪白」，与 `ExitAdvance` 是双保险，`OnScoreAdjustPush` 之外的场景仍需它
- `autoClash` / `previewTargetX` 等编辑器预览参数

文件：`Assets/Scripts/ShockwavePreview.cs` 1015 → **978 行**，括号自检通过。**未 commit。**

### 14.4 验收要点

| # | 场景 | 预期 |
|---|---|---|
| 1 | 连续 PERFECT | 第一次后弹；累积距离每帧持续增长（不再冻结在 1.05） |
| 2 | 3 秒窗口内打满 5 个音符 | 累积 ≈0.5 → 1.3 倍；10 个 → 1.0 → 1.5 倍 |
| 3 | 停止得分 | 先**完全维持 0.4s**，再用 2.0s 平滑回 1.0，前段几乎不掉 |
| 4 | 衰退中途再命中 | 立刻停止变小 → 从当前累积值继续增长 → 能继续变大 |
| 5 | 对峙静止 | 衰退结束后转 Idle，正常播呼吸 |
| 6 | 扣血补分 | 劣势方只闪白不变大；优势方不掉回 1.0 |

---

## 15. 第三轮迭代（2026-10-07 已执行）

用户录屏反馈「未断连、不到 3 秒却突然快速衰减」，并给出四条修正指令。本轮全部已落地。

### 15.1 越界禁放大整体删除 → 改用 ignoreAdvance

**前提纠正**：原以为「扣血补分已被 `ExitAdvance` 排除出前进状态」，实际并非如此 ——
`ExitAdvance` 把 state 设为 Idle，但下一帧 `currentX` 仍朝劣势方前进方向移动，会立刻重新进入 Advancing 并后弹。
真正的原因是：

```
diff = 左分 - 右分；targetX = diff × pushPerHit；红 = 左 = side0，前进方向 = currentX 增大
红劣势 → targetX < 0 → ApplyCatchUp 给红加分 → diff 增大 → targetX 增大 → currentX 朝 +x → 红方"前进"
```

所以补分**会**让劣势方变大。改用 `ignoreAdvance`：

- `OnScoreAdjustPush(lowerSide)`：劣势方闪白 + `_ignoreT = ignoreAdvanceDuration`
- `UpdateAdvanceState`：`ignoring` 时不计累积 / 不刷新 timer / 不后弹 / 不起跑；timer 照常递减、超时照常转 Decay
- 不是"冻结"（状态不被卡住），而是「这段位移压根不算前进」
- ⛔ `ignoreAdvanceDuration` 默认 **1.2s**，必须 > `ScoreManager.catchUpInterval`（1s），否则连续保底间隙里的 Lerp 残余仍会被计入

### 15.2 累积距离单调递增（双方独立）

`accum += Mathf.Max(0f, directionSign * (curX - lastX))`，被推回加 0。
**`baseX` 整套删除**（无需重新锚定）；Decay 重进 Advancing 直接 `accum += stepFwd` 续接。

### 15.3 衰退改「按倍率档位」计时

| 档位区间 | 倍率差 | 耗时 |
|---|---|---|
| 1.5 → 1.3 | 0.2 | `advanceDecayStepTime` |
| 1.3 → 1.2 | 0.1 | 同上 |
| 1.2 → 1.1 | 0.1 | 同上 |
| 1.1 → 1.05 | 0.05 | 同上 |
| 1.05 → 1.0 | 0.05 | 同上 |

每档耗时相同、倍率差越来越小 ⇒ 下降速度自然先快后慢，**不需要任何 shape 曲线**。
`1.5→1.0 = 5s`、`1.3→1.0 = 4s`、`1.2→1.0 = 3s`、`1.1→1.0 = 2s`、`1.05→1.0 = 1s`。

实现：`LevelProgress(s)` 把倍率映射为档位进度（`1.0→0 … 1.5→5`，档间线性）+ `ScaleFromProgress(p)` 逆运算；
`UpdateDecay` 中 `p = decayStartProgress - decayT / step`，再反查倍率。
废弃 `advanceDecayTime` / `advanceDecayHoldTime` / `advanceDecayShape`。

### 15.4 对峙循环改「中线静止 1 秒」

`idleBreathDelay = 1.0f`；`centerStill = |curX-tgtX|<1e-3 && |curX-_lastCenterX|<1e-4`。
与前进状态 / 衰退 / 后弹**完全无关** —— 衰退期间中线静止照样播呼吸。

⚠ 连带修复：early return 从 `if (!moving && _breathFade<=1e-3f) return;` 改为 `needWrite`
（`_breathFade>1e-3 || |scale-_lastAppliedScale|>1e-5 || anticipOff!=0`），
否则「中线静止 + 呼吸关闭/淡出」时衰退的缩放根本写不进 Transform，墙会卡住不动。

### 15.5 亮度与附带项

- `enableBrightnessFx = false` 总开关（P1 落地前）：关时 `ApplyWallGlow` 只写基础 Opacity；**扣血闪白保留**
- `SetWallFloat` / `SetWallFlash` 运行时改用 `mr.material`（编辑模式仍 sharedMaterial），防红蓝串台 + 防写盘污染 .mat
- `_redAnticipFromDecay`：Decay→Advancing 只弹开、**不回缩蓄力**（否则先缩一下再涨，与"立刻停止变小"矛盾）

### 15.6 本轮验收要点

| # | 场景 | 预期 |
|---|---|---|
| 1 | 连续 PERFECT | 第一次后弹；累积持续爬升（1.3 / 1.5 倍可达） |
| 2 | 前进 1 单位后被推回 | 累积仍是 1，墙**不缩** |
| 3 | 停止推进 3 秒 | 按档位衰退：1.5→1.0 约 5s，1.2→1.0 约 3s，先快后慢 |
| 4 | 衰退中途再命中 | 立刻停止变小 → 从当前值继续涨 → 能继续变大 |
| 5 | 双方静止 | 1 秒后播对峙呼吸（即使正在衰退也播） |
| 6 | 触发保底扣血 | 劣势方**只闪白、完全不变大**；优势方不受影响 |
| 7 | 亮度 | 无前进/撞击加亮；扣血闪白仍可见 |

文件：`Assets/Scripts/ShockwavePreview.cs` → **986 行**，括号自检通过；`SampleScene.unity` 已同步清理旧字段、补入新字段。**未 commit。**
