using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Opt-in batch play-mode smoke test. Never runs in the user's normal editor session.</summary>
[InitializeOnLoad]
public static class FullRibbonGameSmoke
{
    private const string Key="MusicalSprite.FullRibbon.BatchSmoke";
    private static int phase;
    private static double begin,checkpoint;
    private static ScoreManager owner;
    private static GameManager game;
    private static FullRibbonBattleHUD hud;
    private static string DirectoryPath=>Path.GetFullPath(Path.Combine(Application.dataPath,"../DesignPreviews/FullRibbonLive-20261028"));
    static FullRibbonGameSmoke(){
        EditorApplication.playModeStateChanged+=Changed;
        EditorApplication.update+=Tick;
        if(SessionState.GetString(Key,"")=="finished")EditorApplication.delayCall+=Quit;
    }
    public static void Batch(){
        SessionState.SetString(Key,"running");SessionState.SetBool(Key+".pass",false);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    public static void BatchWithCapture(){SessionState.SetBool(Key+".capture",true);Batch();}
    public static void BatchSceneryCapture(){SessionState.SetBool(Key+".sceneryCapture",true);BatchWithCapture();}
    public static void BatchIterationCapture(){SessionState.SetBool(Key+".iteration",true);BatchSceneryCapture();}
    private static void Changed(PlayModeStateChange state){
        if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetString(Key,"")=="running"){
            phase=0;begin=checkpoint=EditorApplication.timeSinceStartup;
        }
        if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetString(Key,"")=="finished")Quit();
    }
    private static void Tick(){
        if(SessionState.GetString(Key,"")!="running"||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try {
            double now=EditorApplication.timeSinceStartup;
            if(begin==0)begin=checkpoint=now;
            if(now-begin>45)throw new Exception("Timed out waiting for runtime HUD / result sequence");
            if(owner==null)owner=UnityEngine.Object.FindFirstObjectByType<ScoreManager>();
            if(game==null)game=UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if(owner==null||game==null)return;
            if(hud==null)hud=owner.GetComponent<FullRibbonBattleHUD>();
            if(hud==null||!hud.IsReady||!hud.EntranceComplete)return;
            if(phase==0){
                int hidden=0;
                foreach(var bar in UnityEngine.Object.FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None)){
                    if(bar.gameObject.scene!=owner.gameObject.scene)continue;
                    var group=bar.GetComponent<CanvasGroup>();if(group==null||group.alpha!=0||bar.enabled)throw new Exception("Old HP UI was not retained and hidden");hidden++;
                }
                if(hidden<2)throw new Exception("Expected retained legacy bars for both teams");
                foreach(var score in new[]{owner.leftScoreDisplay,owner.rightScoreDisplay})
                    if(score==null||score.GetComponent<CanvasGroup>()==null||score.GetComponent<CanvasGroup>().alpha!=0)throw new Exception("Old score UI remains visible");
                if(SessionState.GetBool(Key+".capture",false)) {
                    owner.AddScore(0,5842);owner.AddScore(1,4580);
                    var battle=UnityEngine.Object.FindFirstObjectByType<BattleVisualsController>();
                    battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
                    foreach(var rail in UnityEngine.Object.FindObjectsByType<UnifiedJudgementRail>(FindObjectsSortMode.None))rail.Hit(1);
                    Time.timeScale=0;hud.Advance(.4f);
                    string captures=Path.GetFullPath(SessionState.GetBool(Key+".iteration",false)?"DesignPreviews/VisualIterationV2-20261029":"DesignPreviews/FourVisualCorrections-20261029");Directory.CreateDirectory(captures);
                    CaptureScene(captures);
                    if(SessionState.GetBool(Key+".iteration",false))CaptureRuntimeEffects(captures,battle);
                    if(SessionState.GetBool(Key+".sceneryCapture",false))CaptureScenery(captures);
                    phase=5;checkpoint=now;return;
                }
                owner.TakeDamage(0,Mathf.Max(1,owner.GetMaxHP(0)/10));phase=1;checkpoint=now;return;
            }
            if(phase==5&&now-checkpoint>.5) {
                Time.timeScale=1;SessionState.SetBool(Key+".capture",false);
                owner.TakeDamage(0,Mathf.Max(1,owner.GetMaxHP(0)/10));phase=1;checkpoint=now;return;
            }
            if(phase==1&&now-checkpoint>.8){
                CheckFill(0);owner.Heal(0,Mathf.Max(1,owner.GetMaxHP(0)/20));phase=2;checkpoint=now;return;
            }
            if(phase==2&&now-checkpoint>.8){
                CheckFill(0);owner.TakeDamage(0,int.MaxValue/8);
                if(!game.isGameOver||hud.ZeroImpactReady(0)||game.winnerDisplayRoot!=null)throw new Exception("Result sequence did not wait for the delayed zero impact");
                phase=3;checkpoint=now;return;
            }
            if(phase==3&&hud.ZeroImpactReady(0)&&game.winnerDisplayRoot!=null){
                game.RestartGame();
                if(game.isGameOver||hud.ZeroImpactReady(0))throw new Exception("Restart retained the defeated state");
                phase=4;checkpoint=now;return;
            }
            if(phase==4&&now-checkpoint>.3){
                if(game.isGameOver)throw new Exception("A stale defeat callback ended the restarted match");
                Complete(true,"PASS: SampleScene Play mode automatically installed the new HUD; both old bars/scores were retained and hidden; live TakeDamage/Heal settled correctly; result waited for zero impact; restart cleared the pending defeat and replayed entrance.");
            }
        }catch(Exception e){Complete(false,"FAIL: "+e);}
    }
    private static void CheckFill(int side){
        float expected=owner.GetHP(side)/(float)Mathf.Max(1,owner.GetMaxHP(side));
        if(Mathf.Abs(hud.DisplayedFill01(side)-expected)>.001f)throw new Exception("Live HUD did not track ScoreManager HP");
    }
    private static Color32[] CaptureScene(string directory,string filename="sample-scene-runtime.png") {
        var camera=Camera.main;var host=GameObject.Find("FullRibbonBattleHUD_V1");
        if(camera==null||host==null)throw new Exception("Scene capture is missing battle camera / HUD");
        var canvas=host.GetComponent<Canvas>();var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;float distance=canvas.planeDistance;
        var oldTarget=camera.targetTexture;var oldRect=camera.rect;var previous=RenderTexture.active;
        var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);target.Create();
        var image=new Texture2D(1920,1080,TextureFormat.RGBA32,false);
        try {
            camera.targetTexture=target;camera.rect=new Rect(0,0,1,1);
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=camera.nearClipPlane+1;
            Canvas.ForceUpdateCanvases();hud.Advance(0);hud.RebuildForPreview();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            var pixels=image.GetPixels32();int cream=0;
            for(int y=930;y<1040;y++)for(int x=180;x<820;x++){var p=pixels[y*1920+x];if(p.r>230&&p.g>210&&p.b>180)cream++;}
            if(cream<500)throw new Exception("Capture missed new HUD: "+filename);
            File.WriteAllBytes(Path.Combine(directory,filename),image.EncodeToPNG());return pixels;
        } finally {
            RenderTexture.active=previous;camera.targetTexture=oldTarget;camera.rect=oldRect;
            canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=distance;
            UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
    private static void CaptureRuntimeEffects(string directory,BattleVisualsController battle) {
        owner.TakeDamage(1,130);hud.Advance(.035f);CaptureScene(directory,"scene-loss-flash.png");
        hud.Advance(.22f);CaptureScene(directory,"scene-loss-tick.png");hud.Advance(1);
        CaptureScene(directory,"scene-idle-tick.png");
        owner.Heal(1,50);hud.Advance(.27f);CaptureScene(directory,"scene-heal-tick.png");hud.Advance(1);
        hud.PreviewReset(.15f);hud.Advance(.6f);CaptureScene(directory,"scene-danger-dark.png");
        hud.ResetBattle();hud.Advance(1);
        battle.leftComboDisplay.SetComboImmediate(55);battle.rightComboDisplay.SetComboImmediate(55);
    }
    private static void CaptureScenery(string directory) {
        var layers=UnityEngine.Object.FindObjectsByType<MusicalSprite.Terrain.FeverSceneryController>(FindObjectsSortMode.None);
        var report=new System.Text.StringBuilder();var camera=Camera.main;
        foreach(var state in new[]{FeverState.Fever,FeverState.SuperFever,FeverState.None}) {
            hud.PreviewFever(state==FeverState.SuperFever?2:state==FeverState.Fever?1:0);hud.Advance(.4f);
            foreach(var layer in layers) {
                layer.StopAllCoroutines();layer.SendMessage("ApplyImmediately",state);
                foreach(var card in new[]{layer.feverLayer,layer.superFeverLayer}) {
                    var renderer=card.GetComponent<SpriteRenderer>();
                    var sprite=renderer.sprite;Rect rect=sprite.rect;Vector2 pivot=sprite.pivot;
                    var bounds=new Bounds();bool first=true;
                    foreach(float x in new[]{0f,rect.width})foreach(float y in new[]{0f,rect.height}) {
                        Vector3 world=card.TransformPoint(new Vector3((x-pivot.x)/sprite.pixelsPerUnit,(y-pivot.y)/sprite.pixelsPerUnit,0));
                        Vector3 p=camera.WorldToViewportPoint(world);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                    }
                    report.AppendLine(state+" side="+layer.side+" "+card.name+" world="+card.position+" viewport="+bounds.min+".."+bounds.max);
                    bool expected=card==layer.feverLayer?state!=FeverState.None:state==FeverState.SuperFever;
                    foreach(var part in card.GetComponentsInChildren<Renderer>(true))if(part.enabled!=expected)throw new Exception("Scenery visibility is inconsistent at "+state+" "+card.name);
                    if(expected&&layer.fitRisingLayersToArena) {
                        if(bounds.max.y>layer.topViewportLimit+.002f)throw new Exception("Scenery crown escaped screen framing");
                        if(bounds.min.x<-.002f||bounds.max.x>1.002f)throw new Exception("Scenery extended outside screen");
                        if(layer.side==0&&bounds.max.x>.502f||layer.side==1&&bounds.min.x<.498f)throw new Exception("Scenery crossed the other team's half");
                    }
                }
            }
            CaptureScene(directory,"scenery-"+state+".png");
            if(state==FeverState.SuperFever&&SessionState.GetBool(Key+".iteration",false)) {
                void SetTime(float time){foreach(var layer in layers)foreach(var card in new[]{layer.feverLayer,layer.superFeverLayer})card.GetComponent<SpriteRenderer>().sharedMaterial.SetFloat("_ReviewTime",time);}
                foreach(var layer in layers) {
                    var a=layer.feverLayer.GetComponent<SpriteRenderer>();var b=layer.superFeverLayer.GetComponent<SpriteRenderer>();
                    if(a.sprite==b.sprite||a.sharedMaterial.shader.name!="MusicalSprite/FeverCanopy")throw new Exception("Production scenery did not replace both placeholders");
                }
                SetTime(0);CaptureScene(directory,"scenery-loop-warmup.png");var a0=CaptureScene(directory,"scenery-loop-0.png");
                SetTime(1.5f);var a1=CaptureScene(directory,"scenery-loop-1.5.png");
                SetTime(6);var a6=CaptureScene(directory,"scenery-loop-6.png");
                int changed=0,loopError=0;
                for(int k=0;k<a0.Length;k++){int Delta(Color32 x,Color32 y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b);if(Delta(a0[k],a1[k])>6)changed++;if(Delta(a0[k],a6[k])>6)loopError++;}
                if(changed<30||loopError>100)throw new Exception("Scenery loop pixel check failed: changed="+changed+" closure="+loopError);
                report.AppendLine("Loop check: changed="+changed+", 6s closure error pixels="+loopError);SetTime(-1);
            }
        }
        report.AppendLine("PASS: ordinary/super crowns remain in frame and each team half; all inactive layers and their backdrops are disabled.");
        File.WriteAllText(Path.Combine(directory,"scenery-projection.txt"),report.ToString());SessionState.SetBool(Key+".sceneryCapture",false);
    }
    private static void Complete(bool passed,string text){
        Directory.CreateDirectory(DirectoryPath);File.WriteAllText(Path.Combine(DirectoryPath,"play-mode-smoke.txt"),text);
        if(SessionState.GetBool(Key+".iteration",false)){string output=Path.GetFullPath("DesignPreviews/VisualIterationV2-20261029");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"play-mode-smoke.txt"),text);}
        SessionState.SetBool(Key+".pass",passed);SessionState.SetString(Key,"finished");
        if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();else Quit();
    }
    private static void Quit(){
        if(!Application.isBatchMode)return;
        bool passed=SessionState.GetBool(Key+".pass",false);SessionState.EraseString(Key);EditorApplication.Exit(passed?0:1);
    }
}
