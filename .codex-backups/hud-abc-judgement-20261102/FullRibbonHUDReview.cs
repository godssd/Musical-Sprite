using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Isolated sample using the actual runtime renderer and shader.</summary>
public sealed class FullRibbonHUDReview : EditorWindow
{
    private Scene scene;
    private Camera camera;
    private FullRibbonBattleHUD hud;
    private RenderTexture target;
    private FullRibbonHUDStyle style;
    private Vector2 scroll;
    private double last;
    private float initial=1,goal=.5f;
    private string error;
    [MenuItem("Tools/Musical-Sprite/V11/08 Full Ribbon HUD And FX")]
    public static void Open(){var w=GetWindow<FullRibbonHUDReview>("整段血条 / 动效");w.minSize=new Vector2(880,620);}
    private void OnEnable(){last=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}
    private void OnDisable(){EditorApplication.update-=Tick;Dispose();}
    private void Tick(){double now=EditorApplication.timeSinceStartup;float dt=(float)Math.Min(.05,now-last);last=now;if(hud!=null){hud.Advance(dt);Repaint();}}
    private void OnGUI(){
        EditorGUILayout.HelpBox("使用新游戏组件与真实素材的双侧预览；不修改当前场景。参数修改位于 FullRibbonHUDStyle，修改后立即影响本窗口和 Play。旧 UI 保留，关闭 useFullRibbon 后重新进入 Play 可回退。",MessageType.Info);
        using(new EditorGUILayout.HorizontalScope()){
            if(GUILayout.Button("载入 / 重建",GUILayout.Height(28)))Build();
            if(GUILayout.Button("选中参数资产",GUILayout.Height(28))){style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);Selection.activeObject=style;EditorGUIUtility.PingObject(style);}
        }
        initial=EditorGUILayout.Slider("起始血量",initial,0,1);goal=EditorGUILayout.Slider("目标血量",goal,0,1);
        using(new EditorGUILayout.HorizontalScope()){
            if(GUILayout.Button("设起始（无动画）"))Run(()=>hud.PreviewReset(initial));
            if(GUILayout.Button("变化到目标"))Run(()=>hud.PreviewChange(goal));
            if(GUILayout.Button("损血 100→50%"))Run(()=>{hud.PreviewReset(1);hud.PreviewChange(.5f);});
            if(GUILayout.Button("回血 35→75%"))Run(()=>{hud.PreviewReset(.35f);hud.PreviewChange(.75f);});
        }
        using(new EditorGUILayout.HorizontalScope()){
            if(GUILayout.Button("入场白光"))Run(()=>hud.PreviewEntrance());
            if(GUILayout.Button("濒危暗闪"))Run(()=>hud.PreviewReset(.15f));
            if(GUILayout.Button("归零熄火"))Run(()=>{hud.PreviewReset(.4f);hud.PreviewChange(0);});
            if(GUILayout.Button("过热"))Run(()=>hud.PreviewFever(1));
            if(GUILayout.Button("超级过热"))Run(()=>hud.PreviewFever(2));
            if(GUILayout.Button("扑灭"))Run(()=>hud.PreviewFever(0));
        }
        using(new EditorGUILayout.HorizontalScope()){
            EditorGUILayout.LabelField("分数位数",GUILayout.Width(65));
            foreach(int value in new[]{9,58,584,5842,58420,584200})
                if(GUILayout.Button(value.ToString()))Run(()=>hud.PreviewScores(value,value));
        }
        if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
        if(target!=null){Rect area=GUILayoutUtility.GetRect(100,180,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));if(Event.current.type==EventType.Repaint){Render();GUI.DrawTexture(area,target,ScaleMode.ScaleToFit,false);}}
    }
    private void Run(Action action){if(hud==null)Build();if(hud!=null)action();}
    private void Build(){
        Dispose();error=null;
        try {
            style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
            if(style==null||!style.HasArtwork)throw new Exception("FullRibbonHUDStyle 或素材引用缺失。");
            scene=EditorSceneManager.NewPreviewScene();
            var cam=new GameObject("FullRibbonPreviewCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cam,scene);camera=cam.GetComponent<Camera>();
            camera.enabled=false;camera.cameraType=CameraType.Game;camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
            camera.orthographic=true;camera.orthographicSize=3;camera.aspect=3.2f;
            camera.backgroundColor=new Color(.045f,.075f,.11f);camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.transform.position=new Vector3(0,0,-10);
            cam.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            target=new RenderTexture(1600,500,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var root=new GameObject("FullRibbonPreview");SceneManager.MoveGameObjectToScene(root,scene);hud=root.AddComponent<FullRibbonBattleHUD>();
            hud.BuildPreview(style,camera);Canvas.ForceUpdateCanvases();hud.Advance(0);
        }catch(Exception e){error=e.Message;Debug.LogException(e);Dispose();}
    }
    private void Render(){
        if(camera==null)return;
        Canvas.ForceUpdateCanvases();
        hud.RebuildForPreview();
        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
    }
    public static void CaptureValidation(string directory) {
        Directory.CreateDirectory(directory);
        var w=CreateInstance<FullRibbonHUDReview>();
        try {
            w.Build();if(w.hud==null)throw new Exception(w.error??"Preview failed");
            var shader=Resources.Load<Shader>("Shaders/FullRibbonHUD");
            foreach(var candidate in new[]{shader,Resources.Load<Shader>("Shaders/RibbonOpticsV7"),Resources.Load<Shader>("Shaders/UnifiedLoadingV7")}){
                if(candidate==null)throw new Exception("Missing V7 shader");
                if(ShaderUtil.ShaderHasError(candidate)){
                    var detail=new System.Text.StringBuilder(candidate.name);
                    foreach(var message in ShaderUtil.GetShaderMessages(candidate))detail.AppendLine(message.message+" line="+message.line);
                    throw new Exception(detail.ToString());
                }
            }
            w.hud.PreviewEntrance();w.hud.Advance(w.style.entranceDuration-.001f);
            if(w.hud.EntranceComplete)throw new Exception("Entrance completed before end");
            w.hud.Advance(.0011f);
            if(!w.hud.EntranceComplete)throw new Exception("Entrance still waits for resolve after end");
            var sideArray=(Array)typeof(FullRibbonBattleHUD).GetField("sides",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(w.hud);
            foreach(var side in sideArray){float age=(float)side.GetType().GetField("entryBurstAge").GetValue(side);if(age<0||age>.001f)throw new Exception("Completion burst is delayed or advanced twice");}
            w.hud.PreviewReset(1);w.hud.PreviewScores(5842,4580);w.Capture(directory,"01-idle.png");
            w.hud.PreviewChange(.5f);w.hud.Advance(.035f);w.Capture(directory,"02-damage-flash.png");
            w.hud.Advance(1.32f);w.Capture(directory,"03-yellow-moving-tick.png");
            foreach(var optical in Resources.FindObjectsOfTypeAll<RibbonOpticalGraphic>())if(optical.gameObject.scene==w.scene){
                var bar=optical.transform.parent.Find("BloodRibbon").GetComponent<FullRibbonImage>();
                if(Vector4.Distance(bar.InstanceMaterial.GetVector("_HighBoundary"),optical.InstanceMaterial.GetVector("_CursorBoundary"))>.001f)throw new Exception("Solid boundary and optical core disagree");
                if(bar.InstanceMaterial.shader.name==optical.InstanceMaterial.shader.name)throw new Exception("Solid and optical layers were not separated");
            }

            var opticals=new System.Collections.Generic.List<RibbonOpticalGraphic>();
            foreach(var optical in Resources.FindObjectsOfTypeAll<RibbonOpticalGraphic>())if(optical.gameObject.scene==w.scene){optical.enabled=false;opticals.Add(optical);}
            w.Capture(directory,"03b-solid-only.png");
            foreach(var optical in opticals)optical.enabled=true;
            w.Capture(directory,"03c-solid-plus-optics.png");
            var solidPixels=new Texture2D(2,2);var litPixels=new Texture2D(2,2);
            try{
                solidPixels.LoadImage(File.ReadAllBytes(Path.Combine(directory,"03b-solid-only.png")));litPixels.LoadImage(File.ReadAllBytes(Path.Combine(directory,"03c-solid-plus-optics.png")));
                var a=solidPixels.GetPixels32();var b=litPixels.GetPixels32();int changed=0;
                for(int k=0;k<a.Length;k++)if(Mathf.Max(Mathf.Abs(a[k].r-b[k].r),Mathf.Abs(a[k].g-b[k].g),Mathf.Abs(a[k].b-b[k].b))>20)changed++;
                if(changed<40)throw new Exception("Optical layer has no visible emitted pixels");
                File.WriteAllText(Path.Combine(directory,"optical-layer-proof.txt"),"Visible pixels changed by independent optical layer: "+changed+". Same time, material colours and camera; solids held constant.");
            }finally{DestroyImmediate(solidPixels);DestroyImmediate(litPixels);}
            AssertEventColor(w,false);
            w.hud.PreviewReset(.35f);w.hud.PreviewChange(.75f);w.hud.Advance(.26f);w.Capture(directory,"04-green-moving-tick.png");
            AssertEventColor(w,true);
            w.hud.PreviewReset(.15f);w.hud.Advance(.6f);w.Capture(directory,"05-danger-dark.png");
            w.hud.PreviewChange(.10f);w.hud.Advance(.24f);w.Capture(directory,"05b-danger-damage-overlay.png");
            w.hud.PreviewReset(1);w.hud.PreviewFever(2);w.hud.Advance(.4f);w.Capture(directory,"06-super-fever.png");
            w.hud.PreviewEntrance();w.hud.Advance(.3f);w.Capture(directory,"07-loading-head.png");
            int loadingGroups=0;
            foreach(var group in Resources.FindObjectsOfTypeAll<FullRibbonLoadingGraphic>())if(group.gameObject.scene==w.scene){if(!group.HasMask||!group.IsVisible)throw new Exception("Unified entry mask / loading head missing at full HP");loadingGroups++;}
            if(loadingGroups!=2)throw new Exception("Expected one loading union per team");
            w.hud.Advance(.5f);w.Capture(directory,"08-loading-complete.png");
            w.hud.PreviewReset(.4f);w.hud.PreviewChange(0);w.hud.Advance(.02f);w.Capture(directory,"09-zero-impact.png");
            ValidateMotion(w.style);
            var manager=w.hud.gameObject.AddComponent<ScoreManager>();manager.maxHP=326;
            var left=new GameObject("FullRibbon_ProbeScoreLeft",typeof(RectTransform),typeof(ScoreDisplay));SceneManager.MoveGameObjectToScene(left,w.scene);
            var right=new GameObject("FullRibbon_ProbeScoreRight",typeof(RectTransform),typeof(ScoreDisplay));SceneManager.MoveGameObjectToScene(right,w.scene);
            manager.leftScoreDisplay=left.GetComponent<ScoreDisplay>();manager.rightScoreDisplay=right.GetComponent<ScoreDisplay>();
            w.hud.Configure(manager,w.style);manager.ResetScores();manager.AddScore(0,5842);manager.AddScore(1,4580);w.hud.Advance(1);
            manager.TakeDamage(1,100);w.hud.Advance(.24f);w.Capture(directory,"10-score-manager-damage-event.png");
            if(manager.GetRightHP()!=226)throw new Exception("ScoreManager HP event fixture is inconsistent");
            w.hud.Advance(1);manager.Heal(1,50);w.hud.Advance(.27f);w.Capture(directory,"11-score-manager-healing-event.png");
            if(manager.GetRightHP()!=276)throw new Exception("ScoreManager healing event fixture is inconsistent");
            w.hud.PreviewReset(1);
            foreach(int value in new[]{9,58,584,5842,58420,584200,111111,222222,999999}){
                w.hud.PreviewScores(value,value);w.Capture(directory,"score-"+value+".png");
                foreach(var root in Resources.FindObjectsOfTypeAll<RectTransform>()) {
                    if(root.gameObject.scene!=w.scene||root.name!="Team_Red"&&root.name!="Team_Blue")continue;
                    var score=root.Find("ScoreDigit_0") as RectTransform;var hp=root.Find("HPDigit_2") as RectTransform;
                    var scoreLast=root.Find("ScoreDigit_"+(value.ToString().Length-1)) as RectTransform;
                    var hpLabel=root.Find("HP") as RectTransform;
                    if(w.style.masterArtwork&&Mathf.Abs(score.rect.height-w.style.scoreHeight)>.01f)
                        throw new Exception("Master score height changed to fit digit count at "+value);
                    if(root.name=="Team_Red"&&score.anchoredPosition.x<hp.anchoredPosition.x+hp.rect.width)
                        throw new Exception("Score overlaps HP at "+value);
                    if(root.name=="Team_Blue"&&scoreLast.anchoredPosition.x+scoreLast.rect.width>hpLabel.anchoredPosition.x)
                        throw new Exception("Mirrored score overlaps HP at "+value);
                }
            }
            if(ShaderUtil.ShaderHasError(shader))throw new Exception("Shader error after rendering");
            File.WriteAllText(Path.Combine(directory,"unity-validation.txt"),"PASS: V7 solid / additive optics / unified load shaders rendered in isolated Unity preview scene; shared boundary geometry and all shader compilation checked; damage/heal holds, reversals and zero-impact timing checked; actual ScoreManager.ResetScores/AddScore/TakeDamage/Heal events drove the renderer. This does not replace live match testing.");
        } finally {w.Dispose();DestroyImmediate(w);}
    }
    public static void Batch() {
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../DesignPreviews/FullRibbonLive-20261028"));
        try{RepairArtworkReferences();CaptureValidation(directory);}
        catch(Exception e){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"unity-validation.txt"),"FAIL: "+e);Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
    }
    [MenuItem("Tools/Musical-Sprite/V11/Repair Full Ribbon Artwork References")]
    public static void RepairArtworkReferences(){
        const string profilePath="Assets/Resources/BattleVisualV11/FullRibbonHUDStyle.asset";
        const string art="Assets/Art/UI/V11-Handpainted/FullRibbonStaticV1/";
        var profile=AssetDatabase.LoadAssetAtPath<FullRibbonHUDStyle>(profilePath);
        if(profile==null)throw new Exception("Cannot load style asset: "+profilePath);
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../DesignPreviews/FullRibbonLive-20261028"));
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"profile-before-reference-repair.json"),EditorJsonUtility.ToJson(profile,true));
        Sprite[] Pair(string prefix)=>new[]{AssetDatabase.LoadAssetAtPath<Sprite>(art+prefix+"-red.png"),AssetDatabase.LoadAssetAtPath<Sprite>(art+prefix+"-blue.png")};
        profile.ribbon=Pair("ribbon");profile.avatarFrame=Pair("avatar-frame");profile.crescent=Pair("identity-crescent");
        profile.outerLeaves=Pair("outer-leaves");profile.innerLeaves=Pair("inner-leaves");profile.marks=Pair("score-marks");profile.dot=Pair("outer-dot");
        profile.avatarMask=AssetDatabase.LoadAssetAtPath<Sprite>(art+"avatar-content-mask.png");profile.hpLabel=AssetDatabase.LoadAssetAtPath<Sprite>(art+"label-hp-cream.png");
        profile.digits=new Sprite[10];for(int i=0;i<10;i++)profile.digits[i]=AssetDatabase.LoadAssetAtPath<Sprite>(art+"digit-"+i+"-cream.png");
        if(profile.avatars==null||profile.avatars.Length!=2)profile.avatars=new Sprite[2];
        for(int side=0;side<2;side++)if(profile.avatars[side]==null)profile.avatars[side]=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/V11-Handpainted/FullRibbonAvatarsV1/default-"+(side==0?"red":"blue")+".png");
        if(!profile.HasArtwork)throw new Exception("Sprite importer references are incomplete: "+EditorJsonUtility.ToJson(profile));
        EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
    }
    private void Capture(string directory,string name) {
        Render();var old=RenderTexture.active;RenderTexture.active=target;
        int captureHeight=Mathf.Min(210,target.height);
        var pixels=new Texture2D(target.width,captureHeight,TextureFormat.RGBA32,false);
        try {
            pixels.ReadPixels(new Rect(0,target.height-captureHeight,target.width,captureHeight),0,0);pixels.Apply();
            var data=pixels.GetPixels32();int visible=0;var background=data[data.Length-1];
            foreach(var p in data)if(Math.Abs(p.r-background.r)+Math.Abs(p.g-background.g)+Math.Abs(p.b-background.b)>35)visible++;
            if(visible<100){File.WriteAllText(Path.Combine(directory,"renderer-diagnostics.txt"),hud.Diagnostics());throw new Exception("Rendered image is blank: "+name);}
            File.WriteAllBytes(Path.Combine(directory,name),pixels.EncodeToPNG());
        }
        finally {RenderTexture.active=old;DestroyImmediate(pixels);}
    }
    private static void AssertEventColor(FullRibbonHUDReview review,bool healing){
        foreach(var image in Resources.FindObjectsOfTypeAll<FullRibbonImage>())
            if(image.gameObject.scene==review.scene&&image.name=="BloodRibbon"&&image.InstanceMaterial.GetFloat("_Healing")!=(healing?1:0))throw new Exception("Renderer inferred the wrong health event colour");
    }
    private static void ValidateMotion(FullRibbonHUDStyle profile) {
        var m=new FullRibbonHealthMotion();m.Reset(1);m.Change(.5f);m.Tick(profile.flashDuration*.5f,profile);
        if(m.Cursor!=1||m.ZeroImpact)throw new Exception("Damage cursor skipped flash hold");
        m.Tick(profile.flashDuration+profile.holdDuration+profile.lossDuration+1,profile);
        if(Mathf.Abs(m.Cursor-.5f)>.0001f||m.Active)throw new Exception("Damage cursor failed to settle");
        m.Reset(.35f);m.Change(.75f);m.Tick(profile.flashDuration*.5f,profile);
        if(Mathf.Abs(m.Cursor-.35f)>.0001f||!m.Healing)throw new Exception("Healing interval skipped hold");
        m.Tick(profile.flashDuration+profile.holdDuration+profile.healDuration*.4f,profile);float prior=m.Cursor;m.Change(.1f);
        if(m.Cursor<prior||m.Healing)throw new Exception("Interrupted healing retained the wrong colour/direction");
        m.Reset(.4f);m.Change(0);
        if(!m.ZeroImpact||m.BurstAge!=0||m.Cursor<=0)throw new Exception("Death explosion did not trigger at real HP zero");
        m.Tick(profile.flashDuration+profile.holdDuration,profile);
        m.Tick(profile.lossDuration+1,profile);
        if(!m.ZeroImpact||m.Cursor!=0)throw new Exception("Zero impact did not follow cursor");
    }
    private void Dispose(){
        if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        scene=default;camera=null;hud=null;
        if(target!=null){target.Release();DestroyImmediate(target);target=null;}
    }
}

// One-shot diagnostic request after compilation; no scene/play-mode mutation.
[InitializeOnLoad]
internal static class FullRibbonValidationRequest
{
    static FullRibbonValidationRequest(){EditorApplication.delayCall+=Run;}
    private static void Run(){
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../DesignPreviews/FullRibbonLive-20261028"));
        string request=Path.Combine(root,"capture.request");if(!File.Exists(request))return;
        File.Delete(request);
        try{FullRibbonHUDReview.CaptureValidation(root);}
        catch(Exception e){File.WriteAllText(Path.Combine(root,"unity-validation.txt"),"FAIL: "+e);Debug.LogException(e);}
    }
}
