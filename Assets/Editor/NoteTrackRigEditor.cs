using UnityEngine;
using UnityEditor;

/// <summary>
/// NoteTrackRig 的编辑器扩展：在 Scene 视图提供可拖动手柄 + 数值标签。
///   - 轨道带 Z 边缘手柄：拖动 -> 改 laneSpacing（= 正式音轨宽度）
///   - 判定线手柄：拖动 -> 改 judgeLineX（= 正式判定线 X）
/// 另提供菜单项一键在场景中创建 Rig GameObject。
/// </summary>
[CustomEditor(typeof(NoteTrackRig))]
public class NoteTrackRigEditor : Editor
{
    void OnSceneGUI()
    {
        NoteTrackRig rig = (NoteTrackRig)target;
        SerializedProperty propSpacing = serializedObject.FindProperty("laneSpacing");
        SerializedProperty propJudgeX = serializedObject.FindProperty("judgeLineX");

        float bandHalf = rig.BandHalfWidth;
        float xHandle = (rig.playerStartX + rig.judgeLineX) * 0.5f;
        float y = rig.groundY;
        float hs = HandleUtility.GetHandleSize(new Vector3(xHandle, y, 0f)) * 0.4f;

        // ---- 轨道带 Z 边缘手柄（正/负各一） ----
        Vector3 posPos = new Vector3(xHandle, y, bandHalf);
        Vector3 posNeg = new Vector3(xHandle, y, -bandHalf);
        EditorGUI.BeginChangeCheck();
        Vector3 nPos = Handles.Slider(posPos, Vector3.forward, hs, Handles.CubeHandleCap, 0);
        Vector3 nNeg = Handles.Slider(posNeg, Vector3.back, hs, Handles.CubeHandleCap, 0);
        if (EditorGUI.EndChangeCheck())
        {
            float newHalf = Mathf.Max(Mathf.Abs(nPos.z), Mathf.Abs(nNeg.z));
            float newSpacing = (newHalf - rig.laneHalfWidth) * 2f / Mathf.Max(1, rig.laneCount - 1);
            propSpacing.floatValue = Mathf.Max(0.2f, newSpacing);
            serializedObject.ApplyModifiedProperties();
        }

        // ---- 判定线手柄（沿 X） ----
        Vector3 jPos = new Vector3(rig.judgeLineX, y, 0f);
        EditorGUI.BeginChangeCheck();
        Vector3 nj = Handles.Slider(jPos, Vector3.right, hs, Handles.SphereHandleCap, 0);
        if (EditorGUI.EndChangeCheck())
        {
            propJudgeX.floatValue = nj.x;
            serializedObject.ApplyModifiedProperties();
        }

        // ---- 数值标签 ----
        Handles.Label(posPos + Vector3.up * 0.35f,
            $"laneSpacing = {rig.laneSpacing:F2}\nbandHalf = {rig.BandHalfWidth:F2}\n总带宽 = {(rig.BandHalfWidth * 2f):F2}");
        Handles.Label(jPos + Vector3.up * 0.35f + Vector3.right * 0.2f,
            $"judgeLineX = {rig.judgeLineX:F2}");
    }
}

public static class NoteTrackRigMenu
{
    [MenuItem("Tools/音符/创建音轨调参 Rig")]
    static void CreateRig()
    {
        var go = new GameObject("TrackTuningRig");
        go.AddComponent<NoteTrackRig>();
        // 放到原点（与场景坐标对齐），并选中方便立即拖
        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create TrackTuningRig");
    }
}
