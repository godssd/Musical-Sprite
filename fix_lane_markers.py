import re
import shutil

SCENE_PATH = 'Assets/Scenes/SampleScene.unity'
BACKUP_PATH = 'Assets/Scenes/SampleScene.unity.bak.20261003'

# 红方 4 lane 运行时调好的位姿（从对话记录读取）
LEFT_LANE_OFFSETS = {
    0: {'pos': (0.07, -0.288, -0.848), 'rot': (75.825, 0.0, 0.0), 'scale': (1.0, 1.0, 1.0)},
    1: {'pos': (0.17, -0.209, -0.98),  'rot': (62.569, 0.0, 0.0), 'scale': (1.0, 1.0, 1.0)},
    2: {'pos': (0.32, -0.34, -0.79),   'rot': (45.074, 0.0, 0.0), 'scale': (1.0, 1.0, 1.0)},
    3: {'pos': (0.42, -0.41, -0.66),   'rot': (17.881, 0.0, 0.0), 'scale': (1.0, 1.0, 1.0)},
}

# CharacterCubeMarker 脚本 guid
MARKER_GUID = '8660dc6d68348004f929749e81304ad1'
# CharacterSlotPose 脚本 guid
SLOTPOSE_GUID = 'a7138b80fe754e6fb243de002498a4f1'

# 给 8 个 lane 分配新的 fileID（避开之前的冲突区间）
# 顺序：Left0, Left1, Left2, Left3, Right0, Right1, Right2, Right3
LANE_ORDER = [
    ('LeftBand_Member_Lane0',  0, 0),
    ('LeftBand_Member_Lane1',  0, 1),
    ('LeftBand_Member_Lane2',  0, 2),
    ('LeftBand_Member_Lane3',  0, 3),
    ('RightBand_Member_Lane0', 1, 0),
    ('RightBand_Member_Lane1', 1, 1),
    ('RightBand_Member_Lane2', 1, 2),
    ('RightBand_Member_Lane3', 1, 3),
]
MARKER_FILEIDS = [9120000300 + i for i in range(8)]


def parse_scene(path):
    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    # 找到每个 lane 的 GameObject 块、SlotPose 组件块
    lane_info = {}
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith('--- !u!1 &'):
            go_id = int(line.split('&')[1].strip())
            j = i + 1
            name = None
            comps = []
            in_comps = False
            comp_end = j
            while j < len(lines) and not lines[j].startswith('---'):
                if lines[j].startswith('  m_Name:'):
                    name = lines[j].split(':', 1)[1].strip()
                if lines[j].startswith('  m_Component:'):
                    in_comps = True
                    comp_end = j
                elif in_comps and lines[j].startswith('  - component:'):
                    cid = int(re.search(r'fileID: (\d+)', lines[j]).group(1))
                    comps.append(cid)
                    comp_end = j
                elif in_comps and lines[j].strip() != '' and not lines[j].startswith('  - component:'):
                    in_comps = False
                j += 1
            if name and 'Band_Member_Lane' in name:
                lane_info[name] = {
                    'go_id': go_id,
                    'go_start': i,
                    'go_end': j,
                    'comps': comps,
                    'comp_end_line': comp_end,
                }
            i = j
        else:
            i += 1

    # 找到 SlotPose 组件块（按 guid 匹配）
    slotpose_blocks = {}
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith('--- !u!114 &'):
            block_id = int(line.split('&')[1].strip())
            j = i + 1
            go_id = None
            guid = None
            while j < len(lines) and not lines[j].startswith('---'):
                if lines[j].startswith('  m_GameObject:'):
                    go_id = int(re.search(r'fileID: (\d+)', lines[j]).group(1))
                if lines[j].startswith('  m_Script:'):
                    m = re.search(r'guid: ([a-f0-9]{32})', lines[j])
                    if m:
                        guid = m.group(1)
                j += 1
            if guid == SLOTPOSE_GUID and go_id is not None:
                # 反查属于哪个 lane
                for lname, info in lane_info.items():
                    if info['go_id'] == go_id:
                        slotpose_blocks[lname] = {
                            'block_id': block_id,
                            'start': i,
                            'end': j,
                        }
                        break
            i = j
        else:
            i += 1

    return lines, lane_info, slotpose_blocks


def make_marker_yaml(file_id, go_id, side, lane_index, fallback_pos, fallback_rot, fallback_scale):
    # 右侧蓝方：fallback 旋转 Y 水平镜像（绕 Y 转 180）以面向中心，位置 x 取反
    if side == 1:
        fp = (-fallback_pos[0], fallback_pos[1], fallback_pos[2])
        fr = (fallback_rot[0], fallback_rot[1] + 180.0, fallback_rot[2])
    else:
        fp = fallback_pos
        fr = fallback_rot
    fs = fallback_scale

    return f"""--- !u!114 &{file_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {MARKER_GUID}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
  side: {side}
  laneIndex: {lane_index}
  popScale: 1.25
  colorBoost: 0.6
  flashDuration: 0.18
  growScale: 1.5
  releaseGlow: {{r: 1, g: 0.85, b: 0.2, a: 1}}
  jumpHeight: 0.6
  jumpDuration: 0.3
  sleepShrinkScale: 0.6
  modelPrefab: {{fileID: 0}}
  forceKeepPose: 1
  fallbackPositionOffset: {{x: {fp[0]:.6f}, y: {fp[1]:.6f}, z: {fp[2]:.6f}}}
  fallbackRotationOffset: {{x: {fr[0]:.6f}, y: {fr[1]:.6f}, z: {fr[2]:.6f}}}
  fallbackScaleOffset: {{x: {fs[0]:.6f}, y: {fs[1]:.6f}, z: {fs[2]:.6f}}}
  flipFacing: 1
"""


def update_slotpose_block(lines, block, pos, rot, scale):
    start, end = block['start'], block['end']
    new_lines = []
    for idx in range(start, end):
        line = lines[idx]
        if line.startswith('  positionOffset:'):
            new_lines.append(f'  positionOffset: {{x: {pos[0]:.6f}, y: {pos[1]:.6f}, z: {pos[2]:.6f}}}\n')
        elif line.startswith('  rotationOffset:'):
            new_lines.append(f'  rotationOffset: {{x: {rot[0]:.6f}, y: {rot[1]:.6f}, z: {rot[2]:.6f}}}\n')
        elif line.startswith('  scaleOffset:'):
            new_lines.append(f'  scaleOffset: {{x: {scale[0]:.6f}, y: {scale[1]:.6f}, z: {scale[2]:.6f}}}\n')
        else:
            new_lines.append(line)
    return new_lines


def main():
    shutil.copy(SCENE_PATH, BACKUP_PATH)
    lines, lane_info, slotpose_blocks = parse_scene(SCENE_PATH)

    # 先生成要插入的 Marker YAML 和要替换的 SlotPose 块
    marker_yamls = []
    replacements = []  # list of (start, end, new_lines)

    for idx, (lname, side, lane_index) in enumerate(LANE_ORDER):
        if lname not in lane_info:
            print(f'WARNING: {lname} not found in scene')
            continue
        info = lane_info[lname]
        go_id = info['go_id']
        marker_fid = MARKER_FILEIDS[idx]

        # 红方有运行时调好的参数；蓝方暂时默认（后续用户报数再改）
        if side == 0 and lane_index in LEFT_LANE_OFFSETS:
            off = LEFT_LANE_OFFSETS[lane_index]
            pos, rot, scale = off['pos'], off['rot'], off['scale']
        else:
            pos = (0.0, 0.0, 0.0)
            rot = (0.0, 0.0, 0.0)
            scale = (1.0, 1.0, 1.0)

        # 记录 SlotPose 替换
        if lname in slotpose_blocks:
            block = slotpose_blocks[lname]
            new_block_lines = update_slotpose_block(lines, block, pos, rot, scale)
            replacements.append((block['start'], block['end'], new_block_lines))
        else:
            print(f'WARNING: {lname} has no SlotPose block')

        marker_yamls.append(make_marker_yaml(marker_fid, go_id, side, lane_index, pos, rot, scale))

        # 在 GameObject 的 m_Component 列表最后追加 Marker 引用
        comp_end = info['comp_end_line']
        # 需要修改 lines 中这一行后面的内容
        # 实际处理放在下面统一重建

    # 为了简单，直接按行重建整个文本：
    # 1) 替换 SlotPose 块
    # 2) 在 GameObject 组件列表追加 Marker
    # 3) 在文件末尾追加 Marker YAML

    # 先处理替换（从后往前，避免行号偏移）
    replacements.sort(key=lambda x: x[0], reverse=True)
    for start, end, new_lines in replacements:
        lines[start:end] = new_lines

    # 重新解析 lane 位置（因为行号变了），在 GameObject 的 m_Component 最后插入 Marker 引用
    # 由于上面替换没有增删行数（只是替换），所以 lane_info 的 comp_end_line 仍然有效
    # 但安全起见重新解析
    _, lane_info2, _ = parse_scene_from_lines(lines)

    for idx, (lname, side, lane_index) in enumerate(LANE_ORDER):
        if lname not in lane_info2:
            continue
        info = lane_info2[lname]
        marker_fid = MARKER_FILEIDS[idx]
        insert_line = info['comp_end_line'] + 1
        # 找到这一行的缩进并插入
        lines.insert(insert_line, f'  - component: {{fileID: {marker_fid}}}\n')

    # 文件末尾追加 Marker YAML
    if not lines[-1].endswith('\n'):
        lines[-1] += '\n'
    lines.append('\n')
    lines.extend(marker_yamls)

    with open(SCENE_PATH, 'w', encoding='utf-8') as f:
        f.writelines(lines)

    print('Done. Added CharacterCubeMarker to 8 lanes and updated SlotPose offsets.')


def parse_scene_from_lines(lines):
    """仅从 lines 解析 lane 信息（不读文件）。"""
    lane_info = {}
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith('--- !u!1 &'):
            go_id = int(line.split('&')[1].strip())
            j = i + 1
            name = None
            comps = []
            in_comps = False
            comp_end = j
            while j < len(lines) and not lines[j].startswith('---'):
                if lines[j].startswith('  m_Name:'):
                    name = lines[j].split(':', 1)[1].strip()
                if lines[j].startswith('  m_Component:'):
                    in_comps = True
                    comp_end = j
                elif in_comps and lines[j].startswith('  - component:'):
                    cid = int(re.search(r'fileID: (\d+)', lines[j]).group(1))
                    comps.append(cid)
                    comp_end = j
                elif in_comps and lines[j].strip() != '' and not lines[j].startswith('  - component:'):
                    in_comps = False
                j += 1
            if name and 'Band_Member_Lane' in name:
                lane_info[name] = {
                    'go_id': go_id,
                    'go_start': i,
                    'go_end': j,
                    'comps': comps,
                    'comp_end_line': comp_end,
                }
            i = j
        else:
            i += 1
    return lines, lane_info, None


if __name__ == '__main__':
    main()
