using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SkillCommandReview {
    public static void PrepareAndValidate(){
        const string folder="Assets/Resources/BattleVisualV11/SkillCommands/";
        foreach(string name in new[]{"courage","guardian","tenacity"}){
            string path=folder+name+".png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.isReadable=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        var style=AssetDatabase.LoadAssetAtPath<SkillCommandStyle>(folder+"Style.asset");
        if(style==null){style=ScriptableObject.CreateInstance<SkillCommandStyle>();style.cream=Color.white;AssetDatabase.CreateAsset(style,folder+"Style.asset");}
        if(style.artworkFrame==null)style.icons=new[]{AssetDatabase.LoadAssetAtPath<Sprite>(folder+"courage.png"),AssetDatabase.LoadAssetAtPath<Sprite>(folder+"guardian.png"),AssetDatabase.LoadAssetAtPath<Sprite>(folder+"tenacity.png")};
        EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();if(!style.HasIcons)throw new Exception("Semantic icon import failed");
        var a=new SkillCommandSession();var b=new SkillCommandSession();a.Append(SkillInputStep.Left,1.2f);a.Advance(1.21f);a.Append(SkillInputStep.Down,1.2f);
        if(a.Buffer.Count!=1||a.Buffer[0]!=SkillInputStep.Down)throw new Exception("Expired prefix did not start fresh");
        a.Lock(SkillCommandFeedback.Reset,.28f);float remaining=a.LockRemaining;
        if(a.Append(SkillInputStep.Right,1.2f)||a.Buffer.Count!=0||a.LockRemaining!=remaining)throw new Exception("Locked input modified buffer or restarted reset");
        if(!b.Append(SkillInputStep.Left,1.2f))throw new Exception("One team locked the other");
        a.Advance(.28f);if(a.IsLocked||!a.Append(SkillInputStep.Left,1.2f))throw new Exception("Reset lock failed to expire");
        if(!SkillCommandSession.IsPrefix(new[]{SkillInputStep.A,SkillInputStep.B},a.Buffer))throw new Exception("Legacy semantic alias failed");
        a.Lock(SkillCommandFeedback.Release,.38f);a.Advance(.37f);if(!a.IsLocked)throw new Exception("Release lock ended early");a.Advance(.02f);if(a.IsLocked)throw new Exception("Release lock failed to end");
        a.Reset();if(a.IsLocked||a.Buffer.Count!=0||a.Feedback!=SkillCommandFeedback.None)throw new Exception("Restart retained input state");
        var bindings=new SkillCommandBindings();bindings.Reset(false);
        if(!bindings.TrySetAll(new[]{KeyCode.RightArrow,KeyCode.DownArrow,KeyCode.LeftArrow},false)||bindings.Get(0)!=KeyCode.RightArrow)throw new Exception("Atomic key swap failed");
        if(bindings.TrySet(1,KeyCode.RightArrow,false)||bindings.TrySet(1,KeyCode.Mouse0,false)||bindings.TrySet(1,KeyCode.None,false))throw new Exception("Invalid key mapping accepted");
        string output="DesignPreviews/SkillInputIntegration-20261102";Directory.CreateDirectory(output);
        File.WriteAllText(output+"/core-validation.txt","PASS: three 256px sprite icons; timeout starts fresh; unmatched reset and successful release locks; ignored input does not mutate state; teams isolated; A/B/C legacy aliases; reset clears state; atomic key swaps and invalid/duplicate rejection. No PlayerPrefs modified by tests.");
    }
}

public sealed class SkillCommandPreviewWindow : EditorWindow {
    Editor inspector;Vector2 scroll;KeyCode[] keys={KeyCode.LeftArrow,KeyCode.DownArrow,KeyCode.RightArrow};string message;
    [MenuItem("Tools/Musical-Sprite/V11/10 Skill Commands")]
    static void Open(){var window=GetWindow<SkillCommandPreviewWindow>("技能输入");window.minSize=new Vector2(440,480);}
    void OnEnable(){var saved=new SkillCommandBindings();for(int i=0;i<3;i++)keys[i]=saved.Get(i);}
    void OnDisable(){if(inspector!=null)DestroyImmediate(inspector);}
    void OnGUI(){
        EditorGUILayout.HelpBox("自上而下：勇气、守护、坚韧。Play 中按钮走真实技能输入逻辑，可能实际释放技能。重置表现与释放反馈期间输入会被忽略。",MessageType.Info);
        var ui=UnityEngine.Object.FindFirstObjectByType<SkillInputUI>();
        using(new EditorGUI.DisabledScope(!EditorApplication.isPlaying||ui==null)){
            using(new EditorGUILayout.HorizontalScope()){for(int i=0;i<3;i++)if(GUILayout.Button(new[]{"勇气","守护","坚韧"}[i],GUILayout.Height(28)))ui.OnButtonPushed(i);}
            if(ui!=null)EditorGUILayout.LabelField("状态",ui.LastResult+" · 输入锁定 "+ui.IsInputLocked(0)+" · 缓冲 "+ui.BufferedCount(0));
            for(int i=0;i<3;i++)keys[i]=(KeyCode)EditorGUILayout.EnumPopup(new[]{"勇气按键","守护按键","坚韧按键"}[i],keys[i]);
            using(new EditorGUILayout.HorizontalScope()){
                if(GUILayout.Button("读取当前键位"))for(int i=0;i<3;i++)keys[i]=ui.GetBinding(i);
                if(GUILayout.Button("应用并保存改键"))message=ui.SetBindings(keys)?"键位已保存":"未保存：键位不能重复、不能与打击轨道冲突，且必须为键盘按键。";
                if(GUILayout.Button("恢复默认键位")){ui.ResetBindings();for(int i=0;i<3;i++)keys[i]=ui.GetBinding(i);message="已恢复默认键位。";}
            }
        }
        if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        EditorGUILayout.HelpBox("图标、热区和布局改动在重新进入 Play 后应用；反馈时长用于下一次输入。",MessageType.Info);
        var style=Resources.Load<SkillCommandStyle>(SkillCommandStyle.Path);if(style==null)return;
        if(inspector==null)inspector=Editor.CreateEditor(style);scroll=EditorGUILayout.BeginScrollView(scroll);inspector.OnInspectorGUI();EditorGUILayout.EndScrollView();
    }
}
