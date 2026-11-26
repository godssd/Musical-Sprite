using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>Semantic commands: courage / guardian / tenacity. Shared keyboard, touch and AI gate.</summary>
public class SkillInputUI : MonoBehaviour {
    public float inputInterval=1.2f;
    public CharacterBattleSystem battle;
    public SkillCommandStyle style;
    // Retained scene fields and fallback visuals.
    public Color pressedColor=new Color(1,.9f,.2f),readyColor=new Color(1,1,1,.18f),idleColor=new Color(1,1,1,.08f),errorColor=new Color(1,.35f,.35f);
    [HideInInspector] public float flashDuration=.18f,popScale=1.35f;
    static readonly SkillInputStep[] steps={SkillInputStep.Left,SkillInputStep.Down,SkillInputStep.Right};
    static readonly string[] names={"勇气","守护","坚韧"};
    readonly SkillCommandSession[] sessions={new SkillCommandSession(),new SkillCommandSession()};
    readonly DampedNumberPulse[] pulses={new DampedNumberPulse(),new DampedNumberPulse(),new DampedNumberPulse()};
    readonly RectTransform[] hits=new RectTransform[3],visuals=new RectTransform[3];
    readonly Image[] roots=new Image[3],icons=new Image[3];
    readonly Graphic[] rings=new Graphic[3];
    readonly float[] pressAges={100,100,100};
    readonly List<ActiveSkillRuntime> matching=new List<ActiveSkillRuntime>();
    readonly Material[] artworkMaterials=new Material[4];
    Image artwork;SkillCommandBurstGraphic burst;
    bool masterArtwork;
    public bool UsesMasterArtwork=>masterArtwork;
    SkillCommandBindings bindings;Canvas canvas;RectTransform safeArea;SkillCommandLinkGraphic link;ScoreManager score;
    bool initialized,semantic;
    public FeedResult LastResult {get;private set;}=FeedResult.Accepted;
    public int AcceptedPresses {get;private set;}
    public int MatchedCharacters {get;private set;}
    public bool IsInputLocked(int side)=>side<0||side>1||sessions[side].IsLocked;
    public int BufferedCount(int side)=>side>=0&&side<2?sessions[side].Buffer.Count:0;
    public SkillCommandFeedback Feedback(int side)=>sessions[Mathf.Clamp(side,0,1)].Feedback;
    public KeyCode GetBinding(int command){EnsureInitialized();return bindings.Get(command);}
    public bool SetBinding(int command,KeyCode key,bool persist=true){EnsureInitialized();if(command<0||command>2)return false;var values=new[]{bindings.Get(0),bindings.Get(1),bindings.Get(2)};values[command]=key;return SetBindings(values,persist);}
    public bool SetBindings(KeyCode[] values,bool persist=true){
        EnsureInitialized();if(values==null||values.Length!=3)return false;
        foreach(var spawner in FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None))if(spawner.side==0&&spawner.keys!=null)foreach(var key in values)if(System.Array.IndexOf(spawner.keys,key)>=0)return false;
        return bindings.TrySetAll(values,persist);
    }
    public void ResetBindings(bool persist=true){EnsureInitialized();bindings.Reset(persist);}
    void Start()=>EnsureInitialized();
    void EnsureInitialized(){
        if(initialized)return;initialized=true;
        if(style==null)style=Resources.Load<SkillCommandStyle>(SkillCommandStyle.Path);
        semantic=style!=null&&style.useSemanticArtwork&&style.HasIcons;
        bindings=new SkillCommandBindings();
        if(battle==null)battle=FindFirstObjectByType<CharacterBattleSystem>();
        BuildButtons();score=FindFirstObjectByType<ScoreManager>();if(score!=null)score.OnBattleReset+=ResetInput;
    }
    GameObject Child(string name,Transform parent,params System.Type[] components){var go=new GameObject(name,components);go.transform.SetParent(parent,false);return go;}
    void BuildButtons(){
        if(FindFirstObjectByType<EventSystem>()==null){var go=new GameObject("EventSystem");go.AddComponent<EventSystem>();go.AddComponent<StandaloneInputModule>();}
        var host=Child("SkillCommandCanvas",transform,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        canvas=host.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=60;
        var scaler=host.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        safeArea=(RectTransform)Child("CameraSafeArea",host.transform,typeof(RectTransform)).transform;
        safeArea.anchorMin=Vector2.zero;safeArea.anchorMax=Vector2.one;safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        float size=style!=null?style.hitSize:160,spacing=style!=null?style.spacing:180,inset=style!=null?style.rightInset:32;
        var group=(RectTransform)Child("SemanticCommands",safeArea,typeof(RectTransform)).transform;
        group.anchorMin=group.anchorMax=new Vector2(1,.5f);group.pivot=new Vector2(.5f,.5f);group.sizeDelta=new Vector2(size,spacing*2+size);group.anchoredPosition=new Vector2(-inset-size*.5f,0);
        masterArtwork=semantic&&style.artworkFrame!=null;
        Vector2 artworkSize=Vector2.zero;
        if(masterArtwork){
            artworkSize=new Vector2(style.artworkHeight*style.artworkFrame.rect.width/style.artworkFrame.rect.height,style.artworkHeight);
            artwork=ImageAt("MasterArtworkFrame",group,style.artworkFrame,style.artworkHeight);
            artwork.rectTransform.sizeDelta=artworkSize;artwork.preserveAspect=false;
            artworkMaterials[0]=ArtworkMaterial(artwork,artworkSize,false);
        }else if(semantic){
            link=Child("CommandLink",group,typeof(RectTransform),typeof(CanvasRenderer),typeof(SkillCommandLinkGraphic)).GetComponent<SkillCommandLinkGraphic>();
            link.rectTransform.sizeDelta=group.sizeDelta;link.spacing=spacing;link.radius=style.frameSize*.5f;link.raycastTarget=false;
        }
        for(int i=0;i<3;i++){
            var root=Child("SkillCommand_"+names[i],group,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(SkillButtonProxy));
            hits[i]=(RectTransform)root.transform;hits[i].sizeDelta=Vector2.one*size;hits[i].anchoredPosition=new Vector2(0,(1-i)*spacing);
            if(masterArtwork){var uv=style.artworkIconUV!=null&&style.artworkIconUV.Length==3?style.artworkIconUV[i]:new Vector2(.5f,.818f-i*.306f);hits[i].anchoredPosition=Vector2.Scale(uv-Vector2.one*.5f,artworkSize);}
            roots[i]=root.GetComponent<Image>();roots[i].color=semantic?Color.clear:idleColor;roots[i].raycastTarget=true;
            var proxy=root.GetComponent<SkillButtonProxy>();proxy.buttonIndex=i;proxy.onClick=OnButtonPushed;
            visuals[i]=(RectTransform)Child("VisualFeedback",hits[i],typeof(RectTransform)).transform;visuals[i].sizeDelta=Vector2.one*size;
            if(semantic){
                if(!masterArtwork){
                    var ribbon=Child("CommandRibbon",visuals[i],typeof(RectTransform),typeof(CanvasRenderer),typeof(SkillCommandFrameGraphic)).GetComponent<SkillCommandFrameGraphic>();
                    ribbon.rectTransform.sizeDelta=Vector2.one*style.frameSize;ribbon.command=i;ribbon.raycastTarget=false;rings[i]=ribbon;
                }
                icons[i]=ImageAt(names[i],visuals[i],style.icons[i],style.iconSize);
                if(masterArtwork)artworkMaterials[i+1]=ArtworkMaterial(icons[i],Vector2.one*style.iconSize,true);
            }else{
                var go=Child("LegacyArrow",visuals[i],typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));var rt=(RectTransform)go.transform;rt.sizeDelta=Vector2.one*size;
                var label=go.GetComponent<Text>();label.text=new[]{"←","↓","→"}[i];label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=110;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
            }
        }
        if(masterArtwork){
            burst=Child("ReleasePaintSparks",group,typeof(RectTransform),typeof(CanvasRenderer),typeof(SkillCommandBurstGraphic)).GetComponent<SkillCommandBurstGraphic>();
            burst.rectTransform.anchoredPosition=new Vector2(artworkSize.x*.23f,-artworkSize.y*.405f);burst.rectTransform.sizeDelta=new Vector2(180,160);burst.raycastTarget=false;
        }
        Canvas.ForceUpdateCanvases();ApplyVisuals();
    }
    Image ImageAt(string name,Transform parent,Sprite sprite,float size){var image=Child(name,parent,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)).GetComponent<Image>();image.sprite=sprite;image.rectTransform.sizeDelta=Vector2.one*size;image.preserveAspect=true;image.raycastTarget=false;return image;}
    Material ArtworkMaterial(Image image,Vector2 size,bool isIcon){
        var shader=Shader.Find("MusicalSprite/UI/SkillCommandArtwork");if(shader==null)return null;
        var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        var sprite=image.sprite;float u=sprite.rect.width/sprite.texture.width,v=sprite.rect.height/sprite.texture.height;
        material.SetVector("_GlowUV",new Vector4(u/size.x,v/size.y,0,0));material.SetFloat("_IsIcon",isIcon?1:0);image.material=material;return material;
    }
    void Update(){
        EnsureInitialized();AdvanceFeedback(Time.deltaTime);
        for(int i=0;i<3;i++)if(Input.GetKeyDown(bindings.Get(i)))FeedInput(0,steps[i]);
    }
    public void AdvanceFeedback(float dt){
        dt=Mathf.Max(0,dt);foreach(var session in sessions)session.Advance(dt);
        for(int i=0;i<3;i++){pulses[i].Advance(dt);pressAges[i]+=dt;}
        ApplyVisuals();
    }
    public FeedResult FeedInput(int side,SkillInputStep step){
        EnsureInitialized();
        if(!isActiveAndEnabled||side<0||side>1||IsInputLocked(side)||(GameManager.Instance!=null&&GameManager.Instance.isGameOver))return LastResult=FeedResult.Locked;
        var session=sessions[side];session.Append(step,inputInterval);matching.Clear();
        foreach(var rt in FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None)){
            if(!rt.isActiveAndEnabled||rt.ownerSide!=side||rt.owner==null||!rt.owner.HasActiveSkill||rt.SkillRef==null||rt.phase!=ActiveSkillRuntime.Phase.Standby)continue;
            if(rt.NeedsEnergyGate&&!rt.IsSlotFull())continue;
            if(rt.inputSequence==null||rt.inputSequence.Length==0)continue;
            int lane=rt.marker!=null?rt.marker.laneIndex:-1;
            if(SleepController.Instance!=null&&SleepController.Instance.IsCharacterSleeping(side,lane))continue;
            if(SkillCommandSession.IsPrefix(rt.inputSequence,session.Buffer))matching.Add(rt);
        }
        MatchedCharacters=matching.Count;
        if(matching.Count==0){session.Lock(SkillCommandFeedback.Reset,style!=null?style.resetDuration:.28f);ApplyVisuals();return LastResult=FeedResult.Rejected;}
        foreach(var rt in matching)if(rt.marker!=null)rt.marker.PlaySkillStep(CharacterAnimator.CharacterAnimationState.SkillSelect);
        AcceptedPresses++;
        if(side==0){int index=System.Array.IndexOf(steps,SkillCommandSession.Normalize(step));if(index>=0){pulses[index].Trigger(1.07f,.91f,.26f);pressAges[index]=0;}}
        foreach(var rt in matching)if(session.Buffer.Count==rt.inputSequence.Length){
            // Preserve the existing first-complete-runtime selection rule.
            rt.BeginCast();session.Clear();
            if(rt.phase!=ActiveSkillRuntime.Phase.Standby){
                session.Lock(SkillCommandFeedback.Release,style!=null?style.releaseDuration:.38f);
                if(side==0)for(int i=0;i<3;i++)pulses[i].Trigger(1.07f,.94f,.32f);
                ApplyVisuals();return LastResult=FeedResult.Triggered;
            }
            // A role animation may reject the cast. The prefix was still valid:
            // keep the accepted press, never invent an unmatched-input failure.
            break;
        }
        ApplyVisuals();return LastResult=FeedResult.Accepted;
    }
    public void OnButtonPushed(int index){if(index>=0&&index<3)FeedInput(0,steps[index]);}
    public bool ContainsScreenPoint(Vector2 point){
        if(!isActiveAndEnabled||canvas==null||!canvas.gameObject.activeInHierarchy)return false;
        for(int i=0;i<3;i++)if(hits[i]!=null&&RectTransformUtility.RectangleContainsScreenPoint(hits[i],point,null))return true;return false;
    }
    public void ResetInput(){foreach(var session in sessions)session.Reset();foreach(var pulse in pulses)pulse.Reset();for(int i=0;i<3;i++)pressAges[i]=100;MatchedCharacters=0;LastResult=FeedResult.Accepted;ApplyVisuals();}
    void ApplyVisuals(){
        if(canvas==null)return;
        Rect safe=Screen.safeArea;if(Camera.main!=null){Rect r=Camera.main.pixelRect;float l=Mathf.Max(safe.xMin,r.xMin),b=Mathf.Max(safe.yMin,r.yMin),rr=Mathf.Min(safe.xMax,r.xMax),t=Mathf.Min(safe.yMax,r.yMax);if(rr>l&&t>b)safe=Rect.MinMaxRect(l,b,rr,t);}
        safeArea.anchorMin=new Vector2(safe.xMin/Mathf.Max(1,Screen.width),safe.yMin/Mathf.Max(1,Screen.height));safeArea.anchorMax=new Vector2(safe.xMax/Mathf.Max(1,Screen.width),safe.yMax/Mathf.Max(1,Screen.height));safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        var session=sessions[0];float resetDuration=style!=null?style.resetDuration:.28f,releaseDuration=style!=null?style.releaseDuration:.38f;
        float reset=session.Feedback==SkillCommandFeedback.Reset?Mathf.Sin(Mathf.Clamp01(session.FeedbackAge/resetDuration)*Mathf.PI):0;
        float release=session.Feedback==SkillCommandFeedback.Release?Mathf.Sin(Mathf.Clamp01(session.FeedbackAge/releaseDuration)*Mathf.PI):0;
        for(int i=0;i<3;i++){
            if(visuals[i]==null)continue;
            visuals[i].localScale=Vector3.one*pulses[i].Scale*(1-reset*.055f+release*.015f);
            visuals[i].anchoredPosition=masterArtwork?Vector2.zero:new Vector2((i-1)*reset*2,0);
            if(semantic){
                icons[i].color=masterArtwork?Color.white:Color.Lerp(style.cream,new Color(.55f,.55f,.60f),reset*.8f);
                var tint=Color.Lerp(style.team,Color.white,Mathf.Clamp01(Mathf.Exp(-pressAges[i]*16)*.20f+release*.35f));
                tint.a=1-reset*.4f;if(rings[i]!=null)rings[i].color=tint;
            }else roots[i].color=Color.Lerp(Color.Lerp(idleColor,pressedColor,Mathf.Exp(-pressAges[i]*16)+release),errorColor,reset);
        }
        if(link!=null){link.color=Color.Lerp(style.team,Color.white,release*.35f)*new Color(1,1,1,1-reset*.4f);}
        if(masterArtwork){
            // Sharper attack, longer soft tail: complete release is visibly stronger than a press.
            float t=Mathf.Clamp01(session.FeedbackAge/releaseDuration);
            float energy=session.Feedback==SkillCommandFeedback.Release&&t<1?Mathf.SmoothStep(0,1,t/.10f)*Mathf.Pow(1-t,1.1f):0;
            var releaseData=new Vector4(energy,Mathf.Lerp(.12f,.94f,Mathf.SmoothStep(0,1,t/.68f)),style.releaseGlow,0);
            var pressData=new Vector4(Mathf.Exp(-pressAges[0]*17),Mathf.Exp(-pressAges[1]*17),Mathf.Exp(-pressAges[2]*17),0)*style.pressGlow*(1-reset);
            for(int i=0;i<artworkMaterials.Length;i++){
                var material=artworkMaterials[i];if(material==null)continue;
                material.SetFloat("_Reset",reset);material.SetVector("_Release",releaseData);material.SetVector("_Press",pressData);
                material.SetFloat("_LocalPress",i>0?pressData[i-1]:0);
            }
            if(burst!=null)burst.SetTime(session.Feedback==SkillCommandFeedback.Release?session.FeedbackAge:100,releaseDuration);
        }
    }
    void OnEnable(){if(canvas!=null)canvas.gameObject.SetActive(true);}
    void OnDisable(){ResetInput();if(canvas!=null)canvas.gameObject.SetActive(false);}
    void OnDestroy(){if(score!=null)score.OnBattleReset-=ResetInput;foreach(var material in artworkMaterials)if(material!=null)Destroy(material);}
}

public enum FeedResult { Accepted, Triggered, Rejected, Locked }

/// <summary>Press starts input immediately; visual scaling never changes this hit rectangle.</summary>
public class SkillButtonProxy : MonoBehaviour,IPointerDownHandler {
    public int buttonIndex;public System.Action<int> onClick;
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)onClick?.Invoke(buttonIndex);}
}
