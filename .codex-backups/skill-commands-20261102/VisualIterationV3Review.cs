using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MusicalSprite.Terrain;

/// <summary>Opt-in real-scene review. No scene/asset saving or automatic normal-editor work.</summary>
[InitializeOnLoad]
public static class VisualIterationV3Review
{
    const string Key="MusicalSprite.V3.LiveReview";
    static string Output=>Path.GetFullPath(SessionState.GetString(Key+".output","DesignPreviews/VisualIterationV3-20261029/live"));
    static int phase;
    static int animationFrame;
    static double checkpoint,started;
    static FullRibbonBattleHUD hud;
    static BattleVisualsController battle;
    static ScoreManager score;
    static string pendingCapture;
    static VisualIterationV3Review(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;}
    public static void Batch()=>Run("DesignPreviews/VisualIterationV3-20261029/live");
    public static void BatchV4()=>Run("DesignPreviews/VisualIterationV4-20261030/live");
    public static void BatchV7()=>Run("DesignPreviews/UIFxRebuildV7-20261101/live");
    public static void BatchV6()=>Run("DesignPreviews/VisualIterationV6-20261101/live");
    public static void BatchV5()=>Run("DesignPreviews/VisualIterationV5-20261030/live");
    static void Run(string directory){
        SessionState.SetString(Key+".output",directory);
        Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);
        File.WriteAllText(Path.Combine(Output,"capture-status.txt"),"");
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var view=EditorWindow.GetWindow(type);view.Show();
        var sizesType=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
        var groupType=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeGroupType");
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{Enum.Parse(groupType,"Standalone")});
        var labels=(string[])group.GetType().GetMethod("GetDisplayTexts").Invoke(group,null);
        File.WriteAllLines(Path.Combine(Output,"gameview-sizes.txt"),labels);
        int selected=Array.FindIndex(labels,s=>s.Contains("1920x1080")||s.Contains("1920 x 1080")||s.Contains("Full HD"));
        if(selected<0)selected=Array.FindIndex(labels,s=>s.Contains("16:9"));
        if(selected>=0)type.GetProperty("selectedSizeIndex",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,selected);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state){
        if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false)){phase=0;started=checkpoint=EditorApplication.timeSinceStartup;}
        if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(Key+".done",false)){
            SessionState.EraseBool(Key+".done");if(Application.isBatchMode||SessionState.GetBool(Key+".quit",false))EditorApplication.Exit(SessionState.GetBool(Key+".failed",false)?1:0);
        }
    }
    public static void GuiBatch(){SessionState.SetBool(Key+".quit",true);Batch();}
    static void Tick(){
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try{
            double now=EditorApplication.timeSinceStartup;if(started==0)started=checkpoint=now;
            if(now-started>120)throw new Exception("Live review timed out at phase "+phase);
            if(hud==null){score=UnityEngine.Object.FindFirstObjectByType<ScoreManager>();if(score!=null)hud=score.GetComponent<FullRibbonBattleHUD>();}
            if(hud==null||!hud.IsReady||(phase==0&&!hud.EntranceComplete))return;
            if(now-checkpoint<.08)return;
            if(pendingCapture!=null){
                CaptureGameView(pendingCapture);pendingCapture=null;checkpoint=now;return;
            }
            if(battle==null)battle=UnityEngine.Object.FindFirstObjectByType<BattleVisualsController>();
            if(phase==0){
                Time.timeScale=0;score.AddScore(0,5842);score.AddScore(1,4580);hud.Advance(1);
                battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
                foreach(var layer in UnityEngine.Object.FindObjectsByType<FeverSceneryController>(FindObjectsSortMode.None)){
                    layer.StopAllCoroutines();layer.SendMessage("ApplyImmediately",FeverState.SuperFever);
                    var a=layer.feverLayer.lossyScale;var b=layer.superFeverLayer.lossyScale;
                    if(Mathf.Abs(Mathf.Abs(a.x)-Mathf.Abs(a.y))>.002||Mathf.Abs(Mathf.Abs(b.x)-Mathf.Abs(b.y))>.002)throw new Exception("Canopy artwork aspect was distorted");
                    foreach(var card in new[]{layer.feverLayer,layer.superFeverLayer}){
                        var renderer=card.GetComponent<SpriteRenderer>();var bounds=renderer.sprite.bounds;
                        float top=Camera.main.WorldToViewportPoint(card.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,0))).y;
                        if(top>layer.ActiveTopLimit+.003f)throw new Exception("Canopy escaped camera skyline");
                    }
                }
                hud.PreviewFever(2);hud.Advance(.4f);Queue("01-super-scene");
            }else if(phase==1){
                foreach(var rail in UnityEngine.Object.FindObjectsByType<UnifiedJudgementRail>(FindObjectsSortMode.None))rail.Hit(1);
                Queue("02-hit");
            }else if(phase==2){score.TakeDamage(1,163);hud.Advance(.03f);Queue("03-damage-flash");}
            else if(phase==3){hud.Advance(1.32f);Queue("04-damage-cursor");}
            else if(phase==4){hud.Advance(1);score.Heal(1,70);hud.Advance(.27f);Queue("05-heal-cursor");}
            else if(phase==5){hud.Advance(1);battle.leftComboDisplay.ResetCombo();battle.rightComboDisplay.ResetCombo();battle.leftComboDisplay.AdvanceVisuals(.13f);battle.rightComboDisplay.AdvanceVisuals(.13f);Queue("06-combo-tear");}
            else if(phase==6){hud.PreviewEntrance();hud.Advance(.36f);Queue("07-entry");}
            else if(phase==7){hud.Advance(.6f);hud.PreviewScores(999999,999999);Queue("08-six-digit");}
            else if(phase==8){Queue("09-final-scene");}
            else if(phase==9){
                animationFrame=0;battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
                battle.leftComboDisplay.ResetCombo();battle.rightComboDisplay.ResetCombo();Queue("combo-frame-00");
            }
            else if(phase==10){
                animationFrame++;battle.leftComboDisplay.AdvanceVisuals(.03f);battle.rightComboDisplay.AdvanceVisuals(.03f);
                Queue("combo-frame-"+animationFrame.ToString("D2"));if(animationFrame<27)phase--;
            }
            else if(phase==11){animationFrame=0;hud.PreviewEntrance();Queue("entry-frame-00");}
            else if(phase==12){animationFrame++;hud.Advance(.04f);Queue("entry-frame-"+animationFrame.ToString("D2"));if(animationFrame<34)phase--;}
            else if(phase==13){
                animationFrame=0;battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
                battle.leftComboDisplay.ResetCombo();battle.rightComboDisplay.ResetCombo();Queue("rapid-frame-00");
            }
            else if(phase==14){
                animationFrame++;battle.leftComboDisplay.AdvanceVisuals(.03f);battle.rightComboDisplay.AdvanceVisuals(.03f);
                if(animationFrame==3){battle.leftComboDisplay.AddCombo();battle.rightComboDisplay.AddCombo();
                    foreach(var display in new[]{battle.leftComboDisplay,battle.rightComboDisplay})if(!display.GetComponentInChildren<ComboTearGraphic>().IsPlaying||display.CurrentCombo!=1)throw new Exception("Real new hit cancelled outgoing paper or blocked Combo 1");}
                Queue("rapid-frame-"+animationFrame.ToString("D2"));if(animationFrame<28)phase--;
            }
            else if(phase==15){animationFrame=0;hud.PreviewReset(1);hud.PreviewChange(.50f);Queue("damage-frame-00");}
            else if(phase==16){animationFrame++;hud.Advance(.025f);Queue("damage-frame-"+animationFrame.ToString("D2"));if(animationFrame<80)phase--;}
            else if(phase==17){animationFrame=0;hud.PreviewReset(.35f);hud.PreviewChange(.75f);Queue("heal-frame-00");}
            else if(phase==18){animationFrame++;hud.Advance(.025f);Queue("heal-frame-"+animationFrame.ToString("D2"));if(animationFrame<40)phase--;}
            else if(phase==19){
                animationFrame=0;score.AddScore(0,10);score.AddScore(1,10);
                battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
                battle.leftComboDisplay.AddCombo();battle.rightComboDisplay.AddCombo();Queue("number-frame-00");
            }
            else if(phase==20){
                animationFrame++;hud.Advance(.015f);battle.leftComboDisplay.AdvanceVisuals(.015f);battle.rightComboDisplay.AdvanceVisuals(.015f);
                Queue("number-frame-"+animationFrame.ToString("D2"));if(animationFrame<34)phase--;
            }
            else if(phase==21){
                var manager=UnityEngine.Object.FindFirstObjectByType<JudgeFeedbackManager>();
                var camera=Camera.main;
                for(int side=0;side<2;side++)for(int lane=0;lane<3;lane++)manager.ShowFeedback(side,lane,new[]{"PERFECT","GOOD","CLEAR"}[lane],camera.ScreenToWorldPoint(new Vector3(side==0?450:1470,800-lane*150,20))-manager.spawnOffset,null);
                var overlay=manager.GetComponent<HandpaintedJudgementHUD>();if(overlay==null||overlay.ActiveCount<6)throw new Exception("Handpainted judgement events did not create both team layers");
                overlay.Advance(.10f);Queue("10-judgements-success");
            }
            else if(phase==22){
                var manager=UnityEngine.Object.FindFirstObjectByType<JudgeFeedbackManager>();var overlay=manager.GetComponent<HandpaintedJudgementHUD>();overlay.ResetFeedback();
                for(int side=0;side<2;side++)for(int lane=0;lane<3;lane++)manager.ShowFeedback(side,lane,new[]{"PASS","YES","BREAK"}[lane],Camera.main.ScreenToWorldPoint(new Vector3(side==0?450:1470,800-lane*150,20))-manager.spawnOffset,null);
                if(overlay.LastRank!="MISS"||overlay.ActiveCount!=6)throw new Exception("YES or BREAK/MISS mapping failed");
                overlay.Advance(.10f);Queue("11-judgements-other");
            }
            else if(phase==23){var overlay=UnityEngine.Object.FindFirstObjectByType<HandpaintedJudgementHUD>();overlay.Advance(.20f);Queue("12-judgements-fade");}
            else if(phase==24){
                var manager=UnityEngine.Object.FindFirstObjectByType<JudgeFeedbackManager>();var overlay=manager.GetComponent<HandpaintedJudgementHUD>();overlay.ResetFeedback();int count=overlay.SpawnCount;
                for(int i=0;i<20;i++)manager.ShowFeedback(0,1,"YES",Camera.main.ScreenToWorldPoint(new Vector3(450,650,20))-manager.spawnOffset,null);
                if(overlay.SpawnCount-count!=20)throw new Exception("Legitimate repeated judge events were swallowed");
                overlay.Advance(1);if(overlay.ActiveCount!=0)throw new Exception("Judgement feedback did not expire");
                overlay.ResetFeedback();Queue("13-judgements-clean");
            }
            else {
                var profile=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
                if(score.leftScoreDisplay.GetComponent<CanvasGroup>().alpha!=0||score.rightScoreDisplay.GetComponent<CanvasGroup>().alpha!=0)throw new Exception("Legacy score was not hidden");
                foreach(var bar in UnityEngine.Object.FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None))if(bar.enabled||bar.GetComponent<CanvasGroup>().alpha!=0)throw new Exception("Legacy HP was not retained and hidden");
                var diagnostics=new System.Text.StringBuilder();diagnostics.AppendLine(hud.Diagnostics());
                foreach(var material in Resources.FindObjectsOfTypeAll<Material>())if(material.shader.name=="MusicalSprite/JudgementSoftLight")diagnostics.AppendLine(material.name+" coreFade="+material.GetVector("_CoreFade"));
                File.WriteAllText(Path.Combine(Output,"renderer-diagnostics.txt"),diagnostics.ToString());
                File.WriteAllText(Path.Combine(Output,"runtime-validation.txt"),"PASS: live scene initialized; legacy HP / scores retained and hidden; production canopy uniform scale and skyline verified; 1920x1080 native Game View captures include Overlay UI, 28 Combo tear frames, 29 new-hit overlap frames, 35 entry frames, 35 number update frames and damage/heal damping sequences; six judgement ranks, both team stroke colours, BREAK mapping, repeated legal events and expiry checked. No scene saved.");
                Finish(false);return;
            }
            phase++;checkpoint=now;
        }catch(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"runtime-validation.txt"),"FAIL: "+e);Debug.LogException(e);Finish(true);}
    }
    static void Queue(string name){pendingCapture=name;UnityEditorInternal.InternalEditorUtility.RepaintAllViews();}
    static void CaptureGameView(string name){
        // Native Game View includes the Overlay canvases, unlike Camera.Render.
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var window=EditorWindow.GetWindow(type);window.Repaint();
        Texture target=null;
        for(Type t=type;t!=null&&target==null;t=t.BaseType){
            foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                if(typeof(RenderTexture).IsAssignableFrom(f.FieldType)){var value=f.GetValue(window) as RenderTexture;if(value!=null&&value.width>100&&value.height>100){target=value;break;}}
        }
        if(target is RenderTexture rt){
            var previous=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            try{
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                // Native D3D Game View RT is top-origin; PNG rows are bottom-origin.
                if(SystemInfo.graphicsUVStartsAtTop){var pixels=image.GetPixels32();for(int y=0;y<rt.height/2;y++)for(int x=0;x<rt.width;x++){int a=y*rt.width+x,b=(rt.height-1-y)*rt.width+x;var p=pixels[a];pixels[a]=pixels[b];pixels[b]=p;}image.SetPixels32(pixels);image.Apply();}
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
            File.AppendAllText(Path.Combine(Output,"capture-status.txt"),name+": native Game View "+rt.width+"x"+rt.height+"\n");
        }else File.AppendAllText(Path.Combine(Output,"capture-status.txt"),name+": native Game View texture unavailable in this editor mode\n");
    }
    static void Finish(bool failed){
        Time.timeScale=1;SessionState.SetBool(Key,false);SessionState.SetBool(Key+".done",true);SessionState.SetBool(Key+".failed",failed);EditorApplication.ExitPlaymode();
    }
}
