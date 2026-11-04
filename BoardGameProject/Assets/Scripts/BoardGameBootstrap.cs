using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BoardGameBootstrap : MonoBehaviour
{
    private const int MaxRounds = 8;
    private readonly string[] eventNames = { "丰收季：本轮探索额外获得 1 粮食", "星尘潮汐：本轮建造少花 1 木材", "贸易集市：贸易额外获得 1 金币", "平静航道：没有额外效果" };
    private readonly List<string> log = new List<string>();
    private PlayerState human;
    private PlayerState rival;
    private int round;
    private int eventIndex;
    private bool gameOver;
    private string status = "欢迎来到新星港。选择你的行动，赢得建设竞赛！";

    private sealed class PlayerState
    {
        public string Name;
        public int Coins = 4, Wood = 2, Food = 2, Prestige;
        public int Actions = 2, Buildings;
        public PlayerState(string name) { Name = name; }
    }

    private void Start() { NewGame(); }

    private void NewGame()
    {
        human = new PlayerState("拓荒者") { Prestige = 1 };
        rival = new PlayerState("星际公会") { Prestige = 1 };
        round = 1; gameOver = false; log.Clear();
        eventIndex = UnityEngine.Random.Range(0, eventNames.Length);
        status = "第 1 轮开始。你有 2 个行动点。";
    }

    private void OnGUI()
    {
        GUI.backgroundColor = new Color(.08f, .12f, .19f);
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);
        float pad = Mathf.Max(24, Screen.width * .055f);
        GUILayout.BeginArea(new Rect(pad, 22, Screen.width - pad * 2, Screen.height - 44));
        GUILayout.Label("✦  星港拓荒", TitleStyle());
        GUILayout.Label("STARPORT FRONTIER   •   策略经营桌游原型", SubStyle());
        GUILayout.Space(14);
        GUILayout.BeginHorizontal();
        Card("回合", gameOver ? "结算" : round + " / " + MaxRounds, "行动点：" + (human == null ? 0 : human.Actions));
        Card("公共事件", eventNames[eventIndex], "影响本轮所有玩家");
        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.BeginHorizontal();
        PlayerCard(human, true);
        PlayerCard(rival, false);
        GUILayout.EndHorizontal();
        GUILayout.Space(14);
        GUILayout.Label(status, BodyStyle());
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        ActionButton("探索星域", "获得木材与粮食", Explore);
        ActionButton("建造前哨", "消耗 2 木材 + 1 粮食，获得 3 声望", Build);
        ActionButton("贸易补给", "消耗 1 粮食，获得 3 金币", Trade);
        GUILayout.EndHorizontal();
        GUILayout.Space(8);
        if (gameOver)
        {
            if (GUILayout.Button("再开一局", GUILayout.Height(42))) NewGame();
        }
        GUILayout.Label("航行日志", HeaderStyle());
        foreach (string line in log) GUILayout.Label("•  " + line, BodyStyle());
        GUILayout.EndArea();
    }

    private void PlayerCard(PlayerState p, bool isHuman)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(108));
        GUILayout.Label((isHuman ? "◉  " : "◇  ") + p.Name + (isHuman ? "（你）" : "（对手）"), HeaderStyle());
        GUILayout.Label("声望  " + p.Prestige + "      金币  " + p.Coins, BodyStyle());
        GUILayout.Label("木材  " + p.Wood + "      粮食  " + p.Food + "      前哨  " + p.Buildings, BodyStyle());
        GUILayout.EndVertical();
    }

    private void Card(string title, string value, string detail)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(76));
        GUILayout.Label(title, SubStyle()); GUILayout.Label(value, HeaderStyle()); GUILayout.Label(detail, SubStyle());
        GUILayout.EndVertical();
    }

    private void ActionButton(string title, string detail, Action action)
    {
        GUI.enabled = !gameOver && human.Actions > 0;
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(76));
        if (GUILayout.Button(title, GUILayout.Height(34))) action();
        GUILayout.Label(detail, SubStyle()); GUILayout.EndVertical();
        GUI.enabled = true;
    }

    private void Explore()
    {
        int wood = UnityEngine.Random.Range(1, 3), food = UnityEngine.Random.Range(0, 2);
        if (eventIndex == 0) food++;
        human.Wood += wood; human.Food += food;
        SpendAction("你探索星域，找到 " + wood + " 木材和 " + food + " 粮食。");
    }

    private void Build()
    {
        int cost = eventIndex == 1 ? 1 : 2;
        if (human.Wood < cost || human.Food < 1) { status = "资源不足：建造需要 " + cost + " 木材和 1 粮食。先探索或贸易。"; return; }
        human.Wood -= cost; human.Food--; human.Prestige += 3; human.Buildings++;
        SpendAction("你建成一座前哨，获得 3 点声望！");
    }

    private void Trade()
    {
        if (human.Food < 1) { status = "粮食不足：贸易需要 1 粮食。"; return; }
        human.Food--; human.Coins += eventIndex == 2 ? 4 : 3;
        SpendAction("你完成补给贸易，金币增加。");
    }

    private void SpendAction(string message)
    {
        human.Actions--; status = message; log.Insert(0, "第 " + round + " 轮：" + message);
        if (human.Actions <= 0) EndRound();
    }

    private void EndRound()
    {
        if (round < MaxRounds)
        {
            rival.Wood += 1 + UnityEngine.Random.Range(0, 2);
            rival.Food += 1;
            if (rival.Wood >= 2 && rival.Food >= 1 && (rival.Prestige < human.Prestige + 2 || UnityEngine.Random.value > .35f))
            {
                int cost = eventIndex == 1 ? 1 : 2;
                if (rival.Wood >= cost) { rival.Wood -= cost; rival.Food--; rival.Prestige += 3; rival.Buildings++; }
            }
            else rival.Prestige++;
            round++; human.Actions = 2;
            eventIndex = UnityEngine.Random.Range(0, eventNames.Length);
            status = "星际公会结束了行动。第 " + round + " 轮开始，新的公共事件已揭晓。";
        }
        else
        {
            gameOver = true;
            status = human.Prestige == rival.Prestige ? "平局！新星港由双方共同建设。" : human.Prestige > rival.Prestige ? "你赢得了星港建设竞赛！" : "星际公会领先一步。再试一次，改写结果！";
            log.Insert(0, "终局：拓荒者 " + human.Prestige + " 声望，星际公会 " + rival.Prestige + " 声望。");
        }
    }

    private GUIStyle TitleStyle() { return new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.95f, .78f, .42f) } }; }
    private GUIStyle HeaderStyle() { return new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }; }
    private GUIStyle SubStyle() { return new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(.65f, .75f, .85f) } }; }
    private GUIStyle BodyStyle() { return new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(.87f, .91f, .96f) } }; }
}
