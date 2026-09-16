using UnityEngine;
using UnityEditor;

/// <summary>
/// 一键生成 4 份 AI 难度参数资产（Easy / Medium / Hard / Nightmare）到 Assets/Data/AI/。
/// 用 Unity 原生 AssetDatabase.CreateAsset 生成，避免手写 YAML 的 GUID 错配风险。
/// 菜单：Tools > Musical Sprite > 生成 AI 难度资产
///
/// 幂等：已存在的资产不会被覆盖（避免误清用户调过的参数）；如需重置请先删除对应 .asset。
/// </summary>
public class AICreateProfiles
{
    // 2026-09-14 隐藏：难度资产已生成完毕，菜单暂时不需要显示。保留代码以便日后恢复。
    // [MenuItem("Tools/Musical Sprite/生成 AI 难度资产")]
    public static void Generate()
    {
        string dir = "Assets/Data/AI";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/Data", "AI");

        Create("OpponentAIProfile_Easy", "Easy",
            hitRate: 0.35f, offsetMin: 0f, offsetMax: 2.0f,
            evaluateInterval: 8f, releaseProb: 0.15f, inputSpeed: 1.6f,
            dogHowlCombo: 45, healHpRatio: 0.25f, clearScreenNotes: 4, offenseLead: 1800,
            offAfterDog: 0.02f, offAfterBomb: 0.03f);

        Create("OpponentAIProfile_Medium", "Medium",
            hitRate: 0.6f, offsetMin: 0f, offsetMax: 1.0f,
            evaluateInterval: 5f, releaseProb: 0.4f, inputSpeed: 1.0f,
            dogHowlCombo: 30, healHpRatio: 0.35f, clearScreenNotes: 2, offenseLead: 1300,
            offAfterDog: 0.05f, offAfterBomb: 0.1f);

        Create("OpponentAIProfile_Hard", "Hard",
            hitRate: 0.8f, offsetMin: 0f, offsetMax: 0.6f,
            evaluateInterval: 3.5f, releaseProb: 0.6f, inputSpeed: 0.7f,
            dogHowlCombo: 20, healHpRatio: 0.45f, clearScreenNotes: 1, offenseLead: 900,
            offAfterDog: 0.12f, offAfterBomb: 0.2f);

        Create("OpponentAIProfile_Nightmare", "Nightmare",
            hitRate: 0.95f, offsetMin: 0f, offsetMax: 0.35f,
            evaluateInterval: 2.5f, releaseProb: 0.8f, inputSpeed: 0.5f,
            dogHowlCombo: 12, healHpRatio: 0.55f, clearScreenNotes: 1, offenseLead: 500,
            offAfterDog: 0.2f, offAfterBomb: 0.3f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AICreateProfiles] 已生成/校验 4 份难度资产于 " + dir +
                  "。把其中一份拖到场景中 OpponentInput 组件的 profile 字段即可切换难度。");
    }

    private static void Create(string fileName, string difficultyName,
        float hitRate, float offsetMin, float offsetMax,
        float evaluateInterval, float releaseProb, float inputSpeed,
        int dogHowlCombo, float healHpRatio, int clearScreenNotes, int offenseLead,
        float offAfterDog, float offAfterBomb)
    {
        string path = "Assets/Data/AI/" + fileName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<OpponentAIProfile>(path) != null)
        {
            Debug.Log("[AICreateProfiles] 已存在，跳过：" + path);
            return;
        }
        var p = ScriptableObject.CreateInstance<OpponentAIProfile>();
        p.difficultyName = difficultyName;
        p.noteHitRate = hitRate;
        p.offsetMinMul = offsetMin;
        p.offsetMaxMul = offsetMax;
        p.evaluateInterval = evaluateInterval;
        p.releaseProbability = releaseProb;
        p.inputSpeed = inputSpeed;
        p.dogHowlOppComboThreshold = dogHowlCombo;
        p.healHpRatioThreshold = healHpRatio;
        p.clearScreenNoteThreshold = clearScreenNotes;
        p.offenseScoreLeadThreshold = offenseLead;
        p.offenseAfterDogHowlChance = offAfterDog;
        p.offenseAfterBombChance = offAfterBomb;
        AssetDatabase.CreateAsset(p, path);
        Debug.Log("[AICreateProfiles] 已生成：" + path);
    }
}
