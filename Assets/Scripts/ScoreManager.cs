using UnityEngine;
using System;

/// <summary>
/// 计分板管理器。
/// 订阅 NoteSpawner.OnJudge，根据 PERFECT/GOOD/MISS 更新左右玩家分数。
/// </summary>
public class ScoreManager : MonoBehaviour
{
    [Header("分数权重")]
    public int perfectScore = 100;
    public int goodScore = 50;
    public int missScore = 0;

    [Header("玩家分数显示（可选，会自动查找）")]
    public ScoreDisplay leftScoreDisplay;
    public ScoreDisplay rightScoreDisplay;

    [Header("发射器（可选，会自动查找）")]
    public NoteSpawner leftSpawner;
    public NoteSpawner rightSpawner;

    private int leftScore = 0;
    private int rightScore = 0;

    public event Action<int, int> OnScoreChanged;

    void Start()
    {
        if (leftSpawner == null) leftSpawner = FindLeftSpawner();
        if (rightSpawner == null) rightSpawner = FindRightSpawner();

        if (leftSpawner != null)
            leftSpawner.OnJudge += HandleJudge;
        else
            Debug.LogWarning("[ScoreManager] 未找到左发射器");

        if (rightSpawner != null)
            rightSpawner.OnJudge += HandleJudge;
        else
            Debug.LogWarning("[ScoreManager] 未找到右发射器");

        if (leftScoreDisplay == null)
            Debug.LogWarning("[ScoreManager] 未分配左分数显示");

        if (rightScoreDisplay == null)
            Debug.LogWarning("[ScoreManager] 未分配右分数显示");

        UpdateDisplays();
    }

    void OnDestroy()
    {
        if (leftSpawner != null)
            leftSpawner.OnJudge -= HandleJudge;

        if (rightSpawner != null)
            rightSpawner.OnJudge -= HandleJudge;
    }

    private void HandleJudge(int side, int lane, string rank, Vector3 position)
    {
        int delta = 0;
        switch (rank)
        {
            case "PERFECT": delta = perfectScore; break;
            case "GOOD": delta = goodScore; break;
            case "MISS": delta = missScore; break;
        }

        if (side == 0)
        {
            leftScore += delta;
        }
        else
        {
            rightScore += delta;
        }

        UpdateDisplays();
        OnScoreChanged?.Invoke(leftScore, rightScore);
        Debug.Log($"[ScoreManager] Side {side} {rank} +{delta} | Left={leftScore} Right={rightScore}");
    }

    private void UpdateDisplays()
    {
        if (leftScoreDisplay != null) leftScoreDisplay.SetScore(leftScore);
        if (rightScoreDisplay != null) rightScoreDisplay.SetScore(rightScore);
    }

    public void ResetScores()
    {
        leftScore = 0;
        rightScore = 0;
        UpdateDisplays();
    }

    public int GetLeftScore() => leftScore;
    public int GetRightScore() => rightScore;

    private NoteSpawner FindLeftSpawner()
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all)
            if (s.side == 0) return s;
        return null;
    }

    private NoteSpawner FindRightSpawner()
    {
        var all = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in all)
            if (s.side == 1) return s;
        return null;
    }
}
