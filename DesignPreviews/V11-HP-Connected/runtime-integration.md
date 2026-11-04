# 连体血条与动态分数：运行接入

## 查看方法

1. 等待 Unity 导入 / 编译完成，重新进入 Play。ScoreManager 自动选择 `Resources/BattleVisualV11/ConnectedHeaderStyle.asset`，无需手动更换场景引用。
2. 新节点为 `ConnectedBattleHeader_V5/BattleCameraSafeArea/Header_Red_V5` 与 `Header_Blue_V5`。
3. 参数入口：`Tools > Musical-Sprite > V11 > 06 Connected HP And Score Parameters`。Inspector 显示中文字段；修改即时影响运行布局，点击保存跨 Play 保留。新版开关须重新进入 Play 生效。

## 已接入

- 实际新生产 PNG 直接由配置资产引用，不再使用旧曲带轮廓。
- 连体月牙 / 底线为 Sliced Image，按轮廓高度同步 pixelsPerUnitMultiplier；左右边界按原比例显示，只延长中段。
- 红方向右，蓝方向左；两侧纹理和叶片镜像，文字正常阅读。
- HP 标签约 30、血量数字 38、分数 72 设计像素。血槽保持 286×37，装饰线不参与血量填充。
- 按实际字库宽度计算分数长度。新增宽度即时展开，避免末端叶片遮挡新数字；收短按 0.12 秒系数平滑回收，卷钩和叶片跟随末端。
- 分数沿靠 HP 端固定排版；既有轻微数字弹动从该端向外伸展，并预留宽度。
- 相机 pixelRect 与 Screen.safeArea 取交集。单侧最多占半个安全区域减中央间距；较长数字用红蓝共同等比缩放避免互相重叠。无限增长不可在有限屏幕内维持固定字高，因而达到安全区上限后才缩放整体。
- 实际 HPBarLiquid 实例保留；主血量、绿色回血区间、黄色损血区间与闪白层换为同一中性遮罩，继续使用现有 Shader 和时间曲线。蓝方使用镜像中性遮罩，填充方向仍由原 `_Flip` 控制。
- 旧轮廓、旧装饰、旧 Text 仅禁用显示，不删除。旧绿色 / 黄色 IMGUI 分数在手绘分数可用时隐藏；`ScoreManager.forceLegacyScoreOverlay` 可强制显示旧层供回退诊断。Combo 代码与位置、判定参数、场景、角色和地面未修改。

## 回退与工具区分

取消新版配置中的“使用连体新版”，重新进入 Play，即使用原 `BattleHeaderLayout` 和 `HPDecorationStyle`。旧素材 / 配置未覆盖。

05 旧参数面板仍保留，现会提示当前新版使用 06。03 血量小样继续用于旧曲带的回血 / 扣血逻辑审查，并明确标注其外观不是当前 V5。新版实际组合请在游戏中审查。

## 证据与边界

运行 / 编辑器 C# 离线编译通过（退出码均为 0）；有工程既有弃用 API 等警告。不运行测试、不启动第二个 Unity，不把静态组合图当作实机证明。Unity 资源导入、Shader 实际呈现、不同分辨率和连续得分后的效果仍由用户逐步实机审查。

改前源码备份：`.codex-backups/v11-connected-header-20261025/`。修改不包含旧资源清理。
