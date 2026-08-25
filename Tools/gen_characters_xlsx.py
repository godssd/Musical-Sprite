#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Characters.xlsx（按飞书原表 1:1 格式：编号 / 角色 / 职业 / hp / 战斗力 / 能力1 / 能量需求 / 过热状态 / 备注）。
零依赖（zipfile + 手写 OOXML），生成结果可被 Assets/Editor/SimpleXlsx.cs 解析。

Side: 玩家队（side=0）= 编号 1 + 2 + 3 + 4 + 5 全上；对面（side=1）由 CharacterBattleSystem.AutoSeedOpponent()
按需要复制一份（同样的编号与数值，但 lane=对手的 0..3）。
"""
import zipfile
import os
import shutil
import tempfile
from xml.sax.saxutils import escape

OUT = r"D:/unity/plan go/Musical Sprite/Assets/Data/Characters/Characters.xlsx"
TMP = os.path.join(tempfile.gettempdir(), "characters_new.xlsx")

# ---- Sheet1: 角色池（飞书原表 1:1） -------------------------------------
headers = [
    "编号",          # A  int      characterId，飞书原"编号"
    "角色",          # B  string   displayName，飞书原"角色"
    "职业",          # C  string   profession（玩家=演奏者；其他留空）
    "hp\n（1hp=1hp换为全队血量）",  # D  int  maxHP
    "战斗力\n（100点战斗力视为10%分数换位比）",  # E  float combatPower
    "能力1",         # F  string   activeSkillDescription（长文本）
    "能量需求\n（10分=1能量）",  # G  string  activeEnergyCost（"无" 或 整数）
    "过热状态",      # H  string   passiveSkillDescription（被动 / 过热期间效果）
    "备注",          # I  string   notes
]

rows = [
    # 1) 宝宝 — 玩家自身，演奏者；全体防御（P0：玩家普攻型角色，用 PlayerCommand 类，不需要能量，启用时给全队减伤）
    [
        1, "宝宝（玩家自己角色）", "演奏者", 100, 25.0,
        "全体防御（获取分数能力下降 80%）来抵御一切负面效果）持续 4s",
        "无",
        "获得 5% 的伤害减少",
        "玩家自身（isPlayer=true，lane=-1，无能量充能）。能力 = 玩家指令 / 防御类普攻，无需能量，触发后 4s 内全体减伤 + 分数获取 -80%。",
    ],
    # 2) 大狗 — 队伍角色，必杀型：附魔将出现的音符（数量待定）
    [
        2, "大狗", "", 25, 35.0,
        "（3）大狗叫（将即将出现的音符附魔，每成功完成一个音符，增加分贝，结算完后发出狗叫按照分贝惊吓对手降低对方连击数）（必杀）",
        "300",
        "狗叫后追加一次狗叫",
        "lane 0（最底轨）。能量蓄满后释放：大狗叫 → 附魔音符逐渐加分贝 → 结算按分贝降低对手连击数。",
    ],
    # 3) 嘟嘟 — 队伍角色，必杀治疗型：附魔 6 个音符
    [
        3, "嘟嘟", "", 88, 3.0,
        "（4）（即将将出现的音符（6 个）附魔，每成功完成一个音符，就对自己进行一点生命治愈（3 点生命））（必杀）",
        "200",
        "获得治疗后进入缓慢回复（大招之后每三秒根据收集音符数量 ×(1) 回复生命，持续一段时间（9s））",
        "lane 1。能量蓄满后释放：附魔 6 个音符，每个 +3 点生命；9s 内每 3s 按收集音符数 ×1 缓慢回复。",
    ],
    # 4) 爱格 — 队伍角色，必杀炸弹型：附魔 3 个音符 → 随机小型炸弹（原表误写为“爱情”）
    [
        4, "爱格", "", 45, 20.0,
        "（5）炸弹雨（即将将出现的音符（3 个）附魔，每完成一个音符就朝对手随机投射一颗小型炸弹（10 点伤害），造成直接生命伤害直到结算完毕）（必杀）",
        "280",
        "生成更多音符 (+2)",
        "lane 2。能量蓄满后释放：附魔 3 个音符 → 每完成一个投射炸弹（10 点伤害直接扣对手血）。释放后额外生成 2 个音符。",
    ],
    # 5) 小黑 — 队伍角色：清屏 + 沉睡
    [
        5, "小黑", "", 68, 15.0,
        "（6）将身前区域的所有音符全部电没（视力完成最佳击中己方获得所有大招充能）之后陷入 3 秒沉睡",
        "330",
        "范围加大，此后一段时间（30s）全队战斗力提升",
        "lane 3（最顶轨）。能量蓄满后释放：消除身前音符 + 自身 3s 沉睡；过热状态下范围加大 + 30s 内全队战斗力提升。",
    ],
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
    widths = {"A": 6, "B": 22, "C": 10, "D": 9, "E": 11, "F": 70, "G": 10, "H": 50, "I": 38}
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
    if os.path.exists(OUT):
        os.remove(OUT)
except (PermissionError, OSError) as e:
    print("[提示] 目标 xlsx 仍被占用，请先关闭 Excel 再重跑。本次结果已写到:", TMP)
    raise SystemExit(0)
shutil.copy2(TMP, OUT)
print("OK wrote", OUT)
print("shared strings:", len(shared))
print("5 个角色，total HP  =", sum(r[3] for r in rows), "（玩家 100 + 大狗 25 + 嘟嘟 88 + 爱格 45 + 小黑 68）")
print("           总战斗力 =", sum(r[4] for r in rows), "（玩家 25 + 大狗 35 + 嘟嘟 3 + 爱格 20 + 小黑 15）")
