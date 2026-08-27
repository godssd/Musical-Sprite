#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Characters_TEMPLATE.xlsx（角色录入模板，与真实 Characters.xlsx 一致的 41 列布局）。

⚠️ 重要：本脚本只生成「模板」文件，不会覆盖你正在使用的 Characters.xlsx。
   真实数据在 D:/unity/plan go/Musical Sprite/Assets/Data/Characters/Characters.xlsx，
   导入器也是读那个文件。本模板仅供「重新对照列结构 / 新角色照抄」使用。

零依赖（zipfile + 手写 OOXML），生成结果可被 Assets/Editor/SimpleXlsx.cs 解析。

列布局（导入器兼容，见 CharacterImporterWindow）：
   基础 6 列：编号 / 角色 / 角色类型 / 职业 / hp / 战斗力
   能力1~能力5，每组 7 子列：
       能力N（能力名称/描述）
       技能冷却（秒；空/无 = 无冷却）
       技能引用ID（技能库引用ID，反查 SkillSO）
       能量需求（10分=1能量；无/空/0 = 无能量）
       过热状态（过热描述）
       超级过热状态（超级过热描述）
       输入方式（←/↓/→ 或 A/B/C；空 = 被动技能）

合计 6 + 5×7 = 41 列。

Side: 玩家队（side=0）= 编号 1 + 2 + 3 + 4 + 5 全上；对面（side=1）由 CharacterBattleSystem
按需要复制一份（同样的编号与数值，但 lane=对手的 0..3）。
"""
import zipfile
import os
import shutil
import tempfile
from xml.sax.saxutils import escape

# 输出到「模板」文件，绝不覆盖真实 Characters.xlsx
OUT = r"D:/unity/plan go/Musical Sprite/Assets/Data/Characters/Characters_TEMPLATE.xlsx"
TMP = os.path.join(tempfile.gettempdir(), "characters_template_new.xlsx")

# ---- 表头（41 列）---------------------------------------------------------
# 基础 6 列
base_headers = [
    "编号",                                                  # A  int      characterId
    "角色",                                                  # B  string   displayName
    "角色类型",                                              # C  string   玩家自己角色 / 队伍角色
    "职业",                                                  # D  string   profession（仅标注）
    "hp\n（1hp=1hp换为全队血量）",                            # E  int      maxHP
    "战斗力\n（100点战斗力视为10%分数换位比）",                # F  float     combatPower
]

# 每组能力 7 子列
def ability_header(n):
    return [
        f"能力{n}\n（能力名称/描述）",
        "技能冷却\n（秒；空/无=无冷却）",
        "技能引用ID\n（技能库引用ID）",
        "能量需求\n（10分=1能量；无/空/0=无能量）",
        "过热状态\n（过热描述）",
        "超级过热状态\n（超级过热描述）",
        "输入方式\n（←/↓/→ 或 A/B/C；空=被动）",
    ]

headers = list(base_headers)
for n in range(1, 6):
    headers += ability_header(n)
assert len(headers) == 41, f"表头列数应为 41，实际 {len(headers)}"


def ability(name="", cooldown="", skill_id="", energy="", overheat="",
            super_oh="", input_=""):
    """构造一组能力（7 列）。全空 = 该能力不存在（导入器视为无技能）。"""
    return [name, cooldown, skill_id, energy, overheat, super_oh, input_]


def make_row(char_id, name, role_type, profession, hp, combat, abilities):
    """abilities: 长度为 5 的列表，每项是 ability() 返回的 7 元组。"""
    row = [char_id, name, role_type, profession, hp, combat]
    for a in abilities:
        row += list(a)
    assert len(row) == 41, f"行长度应为 41，实际 {len(row)}"
    return row


EMPTY5 = [ability() for _ in range(5)]

rows = [
    # 1) 宝宝 — 玩家自身，演奏者（被动减伤，无能量、无输入）
    make_row(1, "宝宝（玩家自己角色）", "玩家自己角色", "演奏者", 100, 25.0, [
        ability(
            name="（1）全体防御（获取分数能力下降（80%）来抵御一切负面效果）持续 4s",
            skill_id="baby_damage_reduce", energy="无",
            overheat="获得 5% 的伤害减少", super_oh="获得 15% 的伤害减少",
            input_="",
        ),
        *EMPTY5[1:],
    ]),

    # 2) 大狗 — 队伍 lane0，必杀：大狗叫（已实装）
    make_row(2, "大狗", "队伍角色", "", 25, 35.0, [
        ability(
            name="（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
            skill_id="dog_howl", energy="300",
            overheat="狗叫后追加一次狗叫", super_oh="狗叫后追加两次狗叫",
            input_="←←←",
        ),
        *EMPTY5[1:],
    ]),

    # 3) 嘟嘟 — 队伍 lane1，必杀治疗型
    make_row(3, "嘟嘟", "队伍角色", "", 88, 3.0, [
        ability(
            name="（4）将即将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命）（必杀）",
            skill_id="dudu_heal", energy="200",
            overheat="获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×1 回复生命，持续 9s）",
            super_oh="出现更多附魔（+4）并进入缓慢回复",
            input_="→↓→",
        ),
        *EMPTY5[1:],
    ]),

    # 4) 爱格 — 队伍 lane2，必杀炸弹型
    make_row(4, "爱格", "队伍角色", "", 45, 20.0, [
        ability(
            name="（5）炸弹雨（将即将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
            skill_id="aige_bomb", energy="280",
            overheat="生成更多音符 (+2)", super_oh="进一步生成更多（+4）附魔音符",
            input_="←↓←",
        ),
        *EMPTY5[1:],
    ]),

    # 5) 小黑 — 队伍 lane3，必杀清屏
    make_row(5, "小黑", "队伍角色", "", 68, 15.0, [
        ability(
            name="（6）将身前区域的所有音符全部电没（视为完成最佳击中自己获得所有大招充能）之后陷入 3 秒沉睡",
            skill_id="xiaohei_clear", energy="330",
            overheat="范围加大，此后一段时间（30s）全队战斗力提升 30%",
            super_oh="范围加大，此后一段时间（30s）全队战斗力提升 80%",
            input_="↓→←",
        ),
        *EMPTY5[1:],
    ]),

    # 6) 未来角色模板（编号留空 → 导入器跳过）
    make_row("", "", "", "", "", "",
             [ability(name="（将即将出现的音符附魔，每成功完成一个音符，额外增加连击数）（必杀）"), *EMPTY5[1:]]),

    # 7) 未来角色模板（冰洁/冰冻系）
    make_row("", "", "", "", "", "",
             [ability(name="（对即将出现的音符附魔，每完成一个音符，就增加冰洁强度，结算完后根据冰洁强度对对方随机角色施加冰冻（冰洁强度越高持续时间越久））（必杀）"), *EMPTY5[1:]]),

    # 8) 未来角色模板（音波强化系）
    make_row("", "", "", "", "", "",
             [ability(name="（对即将出现的音符附魔，每完成一个音符会增加一点力量，结算完毕后会全方位提升整个乐队的音波威力，持续一段时间）（必杀）"), *EMPTY5[1:]]),

    # 9) 未来角色模板（吞噬/消化系）
    make_row("", "", "", "", "", "",
             [ability(name="将对方吞食进入消化（自身和对方都进入被控制状态，对象所持有的必杀技能量越多消化越慢），并对即将出现的音符附魔，每完成一个音符将会增加消化速度，直到消化完毕（消化时间越久获得能量流失越多）后会将对方吐出并将消化得到的能量随机分发给队友（必杀）"), *EMPTY5[1:]]),
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
    # 列宽：角色/能力描述/过热状态给宽一些，方便阅读
    widths = {}
    for c in range(len(headers_)):
        widths[col_letter(c)] = 14
    widths["A"] = 6
    widths["B"] = 22
    widths["C"] = 12
    widths["D"] = 10
    widths["E"] = 9
    widths["F"] = 11
    # 每组的 能力N 名字列（索引 6,13,20,27,34）与 过热/超级过热 列（索引 +4/+5）加宽
    for n in range(5):
        base = 6 + n * 7
        widths[col_letter(base)] = 40       # 能力N 名字
        widths[col_letter(base + 4)] = 32   # 过热状态
        widths[col_letter(base + 5)] = 32   # 超级过热状态

    parts = []
    parts.append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    parts.append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
    parts.append('<cols>')
    for cl, w in widths.items():
        col_idx = 0
        for ch in cl:
            col_idx = col_idx * 26 + (ord(ch) - ord('A') + 1)
        col_idx -= 1
        parts.append('<col min="' + str(col_idx + 1) + '" max="' + str(col_idx + 1) + '" width="' + str(w) + '"/>')
    parts.append('</cols>')
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

# 原子写入到「模板」文件（不会触碰真实 Characters.xlsx）
try:
    os.replace(TMP, OUT)
    print("OK wrote", OUT)
except (PermissionError, OSError) as e:
    print("[提示] 目标 xlsx 仍被占用（可能 Excel 还开着），请先关闭后再重跑。")
    print("本次结果已写到:", TMP)
    raise SystemExit(0)
print("shared strings:", len(shared))
print("角色/模板行数:", len(rows), "（1~5 实装角色 + 6~9 未来模板）")
print("总列数:", len(headers), "（基础 6 + 能力1~5 各 7 = 41）")
