/// <summary>
/// 过热状态机。
/// None →（连击 ≥ feverComboThreshold）→ Fever →（连击 ≥ superFeverComboThreshold）→ SuperFever。
/// 连击中断（MISS）直接回到 None。
/// </summary>
public enum FeverState
{
    None = 0,
    Fever = 1,
    SuperFever = 2
}
