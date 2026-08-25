#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Characters.xlsx（角色录入模板，含功能需求表内容 + 技能库 skillId 引用）。
零依赖（zipfile + 手写 OOXML），生成结果可被 Assets/Editor/SimpleXlsx.cs 解析。

列（导入器兼容，见 CharacterImporterWindow.FindHeader）：
  编号 / 角色 / 职业 / hp / 战斗力 / 能力1(必杀) / 能量需求 / 过热状态(过热+超级过热)
  / 输入方式(A/B/C) / 备注 / activeSkillId / passiveSkillId

其中 编号 1~5 为飞书原表 5 个角色；6~9 为「未来角色模板」(编号留空，导入器会跳过)，
保留功能需求表里的必杀描述，方便照着录入。

Side: 玩家队（side=0）= 编号 1 + 2 + 3 + 4 + 5 全上；对面（side=1）由 CharacterBattleSystem
按需要复制一份（同样的编号与数值，但 lane=对手的 0..3）。
"""
import zipfile
import os
import shutil
import tempfile
from xml.sax.saxutils import escape

OUT = r"D:/unity/plan go/Musical Sprite/Assets/Data/Characters/Characters.xlsx"
TMP = os.path.join(tempfile.gettempdir(), "characters_new.xlsx")

# ---- Sheet1: 角色（飞书原表 + 技能库 skillId） ---------------------------
headers = [
    "编号",                              # A  int      characterId
    "角色",                              # B  string   displayName
    "职业",                              # C  string   profession
    "hp\n（1hp=1hp换为全队血量）",         # D  int      maxHP
    "战斗力\n（100点战斗力视为10%分数换位比）",  # E float  combatPower
    "能力1（必杀）\n（飞书原文）",          # F  string   activeSkillDescription
    "能量需求\n（10分=1能量）",            # G  string   activeEnergyCost（"无" 或 整数）
    "过热状态\n（过热+超级过热）",          # H  string   passiveSkillDescription
    "输入方式\n（← / ↓ / → 三键，飞书原表为 A/B/C；A=←, B=↓, C=→）",   # I  string   参考：大狗←←←/嘟嘟→↓→/爱格←↓←/小黑↓→←
    "备注",                              # J  string   notes
    "activeSkillId\n（技能库引用ID）",     # K  string   反查 SkillSO（Skill Maker 生成）
    "passiveSkillId\n（技能库引用ID）",    # L  string   反查 SkillSO（被动/过热）
]

rows = [
    # 1) 宝宝 — 玩家自身，演奏者
    [1, "宝宝（玩家自己角色）", "演奏者", 100, 25.0,
     "（1）全体防御（获取分数能力下降（80%）来抵御一切负面效果）持续 4s",
     "无",
     "获得 5% 的伤害减少；超级过热：获得 15% 的伤害减少",
     "↓↓←（能力1）/ ←←↓（能力2），玩家主动技能走 PlayerCommand",
     "玩家自身（isPlayer=true，lane=-1，无能量充能）。能力 = 玩家指令/防御类普攻，无需能量，触发后 4s 内全体减伤 + 分数获取 -80%。",
     "", "baby_damage_reduce"],

    # 2) 大狗 — 队伍 lane0，必杀：大狗叫（已实装）
    [2, "大狗", "", 25, 35.0,
     "（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
     "300",
     "狗叫后追加一次狗叫；超级过热：狗叫后追加两次狗叫",
     "←←←",
     "lane 0（最底轨）。能量蓄满后释放：大狗叫 → 附魔接下来 6 个音符（黄色），每完成（非MISS）一个闪烁一次 → 全部命中/消失后持续发光并发射音波，按 完成数×3 降低对手连击数，随后缩小回常态。",
     "dog_howl", ""],

    # 3) 嘟嘟 — 队伍 lane1，必杀治疗型
    [3, "嘟嘟", "", 88, 3.0,
     "（4）（将即将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命））（必杀）",
     "200",
     "获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×(1) 回复生命，持续一段时间（9s））；超级过热：出现更多附魔（+4）并进入缓慢回复",
     "→↓→",
     "lane 1。能量蓄满后释放：附魔 6 个音符，每个 +3 点生命；9s 内每 3s 按收集音符数 ×1 缓慢回复。",
     "dudu_heal", ""],

    # 4) 爱格 — 队伍 lane2，必杀炸弹型
    [4, "爱格", "", 45, 20.0,
     "（5）炸弹雨（将即将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
     "280",
     "生成更多音符 (+2)；超级过热：进一步生成更多（+4）附魔音符",
     "←↓←",
     "lane 2。能量蓄满后释放：附魔 3 个音符 → 每完成一个投射炸弹（10 点伤害直接扣对手血）。释放后额外生成 2 个音符。",
     "aige_bomb", ""],

    # 5) 小黑 — 队伍 lane3，必杀清屏
    [5, "小黑", "", 68, 15.0,
     "（6）将身前区域的所有音符全部电没（视为完成最佳击中自己获得所有大招充能）之后陷入 3 秒沉睡",
     "330",
     "范围加大，此后一段时间（30s）全队战斗力提升 30%；超级过热：范围加大，此后一段时间（30s）全队战斗力提升 80%",
     "↓→←",
     "lane 3（最顶轨）。能量蓄满后释放：消除身前音符 + 自身 3s 沉睡；过热状态下范围加大 + 战斗力提升。",
     "xiaohei_clear", ""],

    # 6) 未来角色模板（编号留空 → 导入器跳过）
    ["", "", "", "", "",
     "（将即将出现的音符附魔，每成功完成一个音符，额外增加连击数）（必杀）",
     "",
     "生成更多附魔音符；超级过热：进一步生成更多附魔音符",
     "",
     "未来角色模板（待填 角色/职业/hp/战斗力/能量需求/输入方式）。",
     "", ""],

    # 7) 未来角色模板（冰洁/冰冻系）
    ["", "", "", "", "",
     "（对即将出现的音符附魔，每完成一个音符，就增加冰洁强度，结算完后根据冰洁强度对对方随机角色施加冰冻（冰洁强度越高持续时间越久））（必杀）",
     "",
     "额外追加一个对象；超级过热：额外追加两个对象",
     "",
     "未来角色模板（冰洁/冰冻系，待填基础数值与输入方式）。",
     "", ""],

    # 8) 未来角色模板（音波强化系）
    ["", "", "", "", "",
     "（对即将出现的音符附魔，每完成一个音符会增加一点力量，结算完毕后会全方位提升整个乐队的音波威力，持续一段时间）（必杀）",
     "",
     "施加的更加持久；超级过热：施加的提升变得更大",
     "",
     "未来角色模板（音波强化系，待填基础数值与输入方式）。",
     "", ""],

    # 9) 未来角色模板（吞噬/消化系）
    ["", "", "", "", "",
     "将对方吞食进入消化（自身和对方都进入被控制状态，对象所持有的必杀技能量越多消化越慢），并对即将出现的音符附魔，每完成一个音符将会增加消化速度，直到消化完毕（消化时间越久获得能量流失越多）后会将对方吐出并将消化得到的能量随机分发给队友（必杀）",
     "",
     "（无）",
     "",
     "未来角色模板（吞噬/消化系，待填基础数值与输入方式）。",
     "", ""],
]

# ---- 共用字符串表 -------------------------------------------------------
shared = []
shared_index = {}


def sidx(text):
    if text in shared_index:
        return shared_index[text]
    i = len(shared)
    shared.append(text)
    shared_index[text] = i
    return i


def col_letter(col0):
    s = ""
    n = col0
    while True:
        s = chr(ord('A') + (n % 26)) + s
        n = n // 26 - 1
        if n < 0:
            break
    return s


def cell_xml(row1, col0, value):
    ref = col_letter(col0) + str(row1)
    if isinstance(value, str):
        idx = sidx(value)
        return '<c r="' + ref + '" t="s"><v>' + str(idx) + '</v></c>'
    if isinstance(value, bool):
        return '<c r="' + ref + '" t="b"><v>' + ('1' if value else '0') + '</v></c>'
    if isinstance(value, float) and value.is_integer():
        v = str(int(value))
    else:
        v = str(value)
    return '<c r="' + ref + '"><v>' + v + '</v></c>'


def build_sheet_xml(headers_, rows_):
    parts = []
    parts.append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    parts.append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
    parts.append('<cols>')
    # 列宽：能力描述 / 过热状态 / 备注 给宽一些，方便阅读
    widths = {"A": 6, "B": 22, "C": 10, "D": 9, "E": 11, "F": 70, "G": 10,
              "H": 50, "I": 12, "J": 40, "K": 14, "L": 14}
    for cl, w in widths.items():
        col_idx = 0
        for ch in cl:
            col_idx = col_idx * 26 + (ord(ch) - ord('A') + 1)
        col_idx -= 1
        parts.append('<col min="' + str(col_idx + 1) + '" max="' + str(col_idx + 1) + '" width="' + str(w) + '"/>')
    parts.append('</cols>')
    # 行高
    parts.append('<sheetFormatPr defaultRowHeight="36"/>')
    parts.append('<sheetData>')
    parts.append('<row r="1" ht="32" customHeight="1">')
    for c, h in enumerate(headers_):
        parts.append(cell_xml(1, c, h))
    parts.append('</row>')
    for ri, row in enumerate(rows_):
        r = ri + 2
        parts.append('<row r="' + str(r) + '" ht="80" customHeight="1">')
        for c, val in enumerate(row):
            parts.append(cell_xml(r, c, val))
        parts.append('</row>')
    parts.append('</sheetData>')
    parts.append('</worksheet>')
    return "".join(parts)


sheet1 = build_sheet_xml(headers, rows)

ss_parts = []
ss_parts.append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
ss_parts.append('<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="'
                + str(len(shared)) + '" uniqueCount="' + str(len(shared)) + '">')
for t in shared:
    # 把可能破坏属性的 XML 字符转义
    ss_parts.append('<si><t xml:space="preserve">' + escape(t) + '</t></si>')
ss_parts.append('</sst>')
shared_xml = "".join(ss_parts)

wb = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
    'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
    '<sheets>'
    '<sheet name="' + escape("角色") + '" sheetId="1" r:id="rId1"/>'
    '</sheets></workbook>'
)

wb_rels = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
    '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
    '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>'
    '</Relationships>'
)

ct = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
    '<Default Extension="xml" ContentType="application/xml"/>'
    '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
    '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
    '<Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>'
    '</Types>'
)

root_rels = (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
    '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
    '</Relationships>'
)

with zipfile.ZipFile(TMP, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("[Content_Types].xml", ct)
    z.writestr("_rels/.rels", root_rels)
    z.writestr("xl/workbook.xml", wb)
    z.writestr("xl/_rels/workbook.xml.rels", wb_rels)
    z.writestr("xl/worksheets/sheet1.xml", sheet1)
    z.writestr("xl/sharedStrings.xml", shared_xml)

# 覆盖前先尝试释放可能被锁的旧文件（如果 Excel 还没关，让覆盖请求失败由用户重试）
try:
    # 用 os.replace 原子覆盖；Windows 下若目标被锁会抛 PermissionError → 退回到提示并保留 temp 输出
    os.replace(TMP, OUT)
    print("OK wrote", OUT)
except (PermissionError, OSError) as e:
    print("[提示] 目标 xlsx 仍被占用（可能 Excel 还开着），请先关闭后再重跑。")
    print("本次结果已写到:", TMP)
    raise SystemExit(0)
print("shared strings:", len(shared))
print("角色/模板行数:", len(rows), "（1~5 实装角色 + 6~9 未来模板）")
print("总战斗力(1~5) =", sum(r[4] for r in rows[:5] if isinstance(r[4], (int, float))),
      "（玩家25+大狗35+嘟嘟3+爱格20+小黑15）")
print("总HP(1~5)      =", sum(r[3] for r in rows[:5] if isinstance(r[3], (int, float))),
      "（玩家100+大狗25+嘟嘟88+爱格45+小黑68）")
