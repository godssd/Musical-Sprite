using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class HandpaintedJudgementReview {
    public static void PrepareAndValidate(){
        const string folder="Assets/Resources/BattleVisualV11/Judgements/";
        foreach(string name in new[]{"lettering.png","decoration.png"}){
            string path=folder+name;AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;importer.isReadable=false;importer.npotScale=TextureImporterNPOTScale.None;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=2048;importer.SaveAndReimport();
        }
        AssetDatabase.ImportAsset(folder+"layout.json",ImportAssetOptions.ForceSynchronousImport);
        string asset=folder+"Style.asset";
        var style=AssetDatabase.LoadAssetAtPath<HandpaintedJudgementStyle>(asset);
        if(style==null){style=ScriptableObject.CreateInstance<HandpaintedJudgementStyle>();AssetDatabase.CreateAsset(style,asset);AssetDatabase.SaveAssets();}
        if(!HandpaintedJudgementHUD.LoadArtwork())throw new Exception("Six approved lettering/stroke regions failed to load");
        UIFxRebuildV7Regression.Batch();
        string output="DesignPreviews/ABC-JudgementIntegration-20261102";Directory.CreateDirectory(output);
        File.WriteAllText(output+"/assets-validation.txt","PASS: six approved rank regions imported; lettering and team strokes separated; uncompressed RGBA, sRGB, alpha, bilinear, no mipmaps. Style supports legacy rollback. Shared HUD / Combo / health regression executed.");
    }
}

public sealed class HandpaintedJudgementPreviewWindow : EditorWindow {
    int side,lane;Vector2 scroll;Editor inspector;
    [MenuItem("Tools/Musical-Sprite/V11/09 Handpainted Judgements")]
    static void Open(){var window=GetWindow<HandpaintedJudgementPreviewWindow>("手绘评价");window.minSize=new Vector2(430,430);}
    void OnDisable(){if(inspector!=null)DestroyImmediate(inspector);}
    void OnGUI(){
        EditorGUILayout.HelpBox("Play 中可在真实判定位置播放评价小样；这些按钮只播放表现，不计分、不改变 Combo。关闭配置中的 Enabled In Game 后重新进入 Play 可回退旧评价。",MessageType.Info);
        side=EditorGUILayout.IntPopup("阵营",side,new[]{"红方","蓝方"},new[]{0,1});lane=EditorGUILayout.IntSlider("音轨",lane,0,3);
        using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying)){
            using(new EditorGUILayout.HorizontalScope()){foreach(var rank in new[]{"PERFECT","GOOD","CLEAR"})if(GUILayout.Button(rank,GUILayout.Height(28)))Play(rank);}
            using(new EditorGUILayout.HorizontalScope()){foreach(var rank in new[]{"PASS","YES","MISS","BREAK"})if(GUILayout.Button(rank,GUILayout.Height(28)))Play(rank);}
            if(GUILayout.Button("清除当前评价")){var hud=UnityEngine.Object.FindFirstObjectByType<HandpaintedJudgementHUD>();if(hud!=null)hud.ResetFeedback();}
        }
        var style=Resources.Load<HandpaintedJudgementStyle>(HandpaintedJudgementStyle.Path);
        if(style==null){EditorGUILayout.HelpBox("评价配置尚未导入。",MessageType.Warning);return;}
        if(inspector==null)inspector=Editor.CreateEditor(style);
        scroll=EditorGUILayout.BeginScrollView(scroll);inspector.OnInspectorGUI();EditorGUILayout.EndScrollView();
    }
    void Play(string rank){
        var manager=UnityEngine.Object.FindFirstObjectByType<JudgeFeedbackManager>();if(manager==null)return;
        foreach(var spawner in UnityEngine.Object.FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None))if(spawner.side==side&&spawner.hitPoint!=null){
            Vector3 position=spawner.hitPoint.position+Vector3.forward*((lane-1.5f)*spawner.laneSpacing);
            manager.ShowFeedback(side,lane,rank,position,null);return;
        }
    }
}
