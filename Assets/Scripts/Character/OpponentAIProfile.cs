using UnityEngine;

/// <summary>
/// 对手（AI）难度参数表（ScriptableObject）。
/// 由 OpponentInput 读取，决定「音符打击」与「主动技能判定」两类行为。
/// 4 份难度资产（Easy / Medium / Hard / Nightmare）放在 Assets/Data/AI/，
/// 在 OpponentInput 组件的 profile 字段拖入即可切换难度（GameManager 自动接线 spawner/conductor/beatmap）。
///
/// 设计要点（来自用户方案表）：
/// - 音符打击：noteHitRate 只决定「是否按下按键」；偏移 offsetFrac∈[offsetMinMul,offsetMaxMul] 决定偏离程度，
///   符号 50% 提前 / 50% 延后；|dt|≤有效窗口才命中，否则「点出但未命中(MISS)」。
///   小音符(小型点击)有效窗口 = 0.6 × goodWindow，故 offsetFrac>0.6 必 MISS（AI 侧按此缩放时间线偏移实现）。
/// - 主动技能：大前提(能量满[无能量技能=视为满] + 不在 CD) → 专属条件 → 每个候选独立掷 releaseProbability
///   → 从发动里随机抽 1 个 → 主技能=大狗叫/炸弹雨 时按概率追加全体进攻（额外判定，不要求领先 1300），
///   重排为先放全体进攻、再放主技能（一次只输入一个技能，但先后捆绑）。
/// </summary>
[CreateAssetMenu(fileName = "OpponentAIProfile", menuName = "Musical Sprite/Opponent AI Profile")]
public class OpponentAIProfile : ScriptableObject
{
    [Header("标识")]
    public string difficultyName = "中等";

    [Header("音符打击")]
    [Range(0f, 1f), Tooltip("命中概率：只决定 AI 是否按『正确时机』按下按键（不按=无输入，音符自然 MISS）")]
    public float noteHitRate = 0.6f;
    [Tooltip("偏移下限（× goodWindow）。0 = 完美时机附近")]
    public float offsetMinMul = 0f;
    [Tooltip("偏移上限（× goodWindow）。>1 表示有概率点出但未命中（超出 GOOD 窗口即 MISS）")]
    public float offsetMaxMul = 1f;

    [Header("主动技能判定")]
    [Tooltip("技能评估间隔（秒）：每隔这么久评估一次是否发动主动技能")]
    public float evaluateInterval = 5f;
    [Range(0f, 1f), Tooltip("发动概率：每个满足大前提+专属条件的候选技能各自独立掷一次")]
    public float releaseProbability = 0.4f;
    [Tooltip("模拟玩家输入整个序列所用的秒数（越大越像真人手速）")]
    public float inputSpeed = 1f;

    [Header("各技能具体条件")]
    [Tooltip("大狗叫：对方(side0)连击数超过此阈值才发动")]
    public int dogHowlOppComboThreshold = 30;
    [Range(0f, 1f), Tooltip("牛角包治疗：自身血量比例低于此阈值才发动")]
    public float healHpRatioThreshold = 0.35f;
    [Tooltip("小黑清屏：可清音符数(自身侧清屏带内)超过此阈值才发动")]
    public int clearScreenNoteThreshold = 2;
    [Tooltip("全体进攻：自身分 - 对方分 超过此阈值才发动")]
    public int offenseScoreLeadThreshold = 1300;

    [Header("追加全体进攻（仅当主技能=大狗叫/炸弹雨）")]
    [Tooltip("主技能=大狗叫时，按此概率追加一个全体进攻（先放进攻、再放狗叫）")]
    [Range(0f, 1f)] public float offenseAfterDogHowlChance = 0.05f;
    [Tooltip("主技能=炸弹雨时，按此概率追加一个全体进攻（先放进攻、再放炸弹）")]
    [Range(0f, 1f)] public float offenseAfterBombChance = 0.1f;
}
