# 连点音符（ChainTap）运动模型 + 公式批改动 — 执行概述

日期：2026-09-16 | 项目：Musical Sprite（Unity 6000 + URP 17 + spine-unity）

## 一、改动目标
1. **修复充能异常**：连点音符此前每次点击都按 PERFECT(100分) 结算 → 一个 N 击连点 = N×10 能量，量异常。
2. **落地新运动模型**：命中后极快后退 1 单位、匀减速 1s 速度归零，随后以正常前进速度回到判定线，重新可点；越过未点 = Miss。

## 二、最终拍板（覆盖早期“完成时结算一次”草案）
- 连点音符：**每次点击 = YES(10分)**，最后一下 = **CLEAR(50分)**；分数不缩；过热连击仍逐击 +1；底层“获得即结算（actual/10 充能）”保留。
- 结果：一个 N 击连点 = (N-1) 次 YES + 1 次 CLEAR，充能量回归正常。

## 三、具体改动（均已落盘）
| 文件 | 改动 |
|------|------|
| `Assets/Scripts/NoteMover.cs` | 字段区加 `chainTapHoldDuration=1f` / `chainRetreatDist=1f` + 运动态 `chainTapDeadline/chainHitTime/chainNextContactTime/normalSpeed/goodWindow`；`Init` 加 goodWindow 参数并按曲速算 normalSpeed；`Update` 连点分支按 te 做「匀减速后退 → 正常速前进」插值，去掉命中钉死；`RegisterChainTapHit` 记录 hitTime / nextContactTime(=hitTime+hold+retreat/normalSpeed) / deadline(=nct+goodWindow) |
| `Assets/Scripts/NoteSpawner.cs` | `chainTapHoldDuration` 默认 1f;`mover.Init` 传 goodWindow;`TryHitTap` 连点分支(absDt=0)→按 `ChainTapNextContactTime` 判定(‖songTime−nct‖≤goodWindow 才有效，否则 continue);rank 计算加 `if(best.isChainTap) rank=(chainTapRemaining<=1)?CLEAR:YES` |
| `Assets/Scripts/ScoreManager.cs` | 加 `public int yesScore = 10;` 与 `case "YES": delta = yesScore;` |

## 四、备份
`Backup/2026-09-16-chain-motion/` 含三份**改前副本**：NoteMover.cs / NoteSpawner.cs / ScoreManager.cs（可直接回退）。

## 五、自检结论
- 字段可见性：`chainTapRemaining` / `IsChainTapExpired` / `RegisterChainTapHit` / `ChainTapNextContactTime` 均为 public，链路自洽。
- 首击走普通 hitTime 路径(remaining=N>1→YES)，末击 remaining=1→CLEAR 后 cleared 移除，中途 miss 走 `IsChainTapExpired`→Miss 流程。
- 沙箱无 Unity，未编译验证。

## 六、待你在 Unity 验证
1. 编译通过；
2. 连点每次点击数字 −1、末击变大消失、中途 miss 缩小消失；
3. 充能量正常（(N−1)×YES + 1×CLEAR，不再每击满能量）；
4. 命中后后退 1 单位匀减速 1s，再正常速回到判定线可再点（旋钮：`chainRetreatDist` / `chainTapHoldDuration` 在 Inspector 可调）。
