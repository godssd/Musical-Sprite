using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Reversible, data-driven replacement for the old header; never reparents/deletes old UI.</summary>
public sealed class FullRibbonBattleHUD : MonoBehaviour
{
    public FullRibbonHUDStyle style;
    public bool IsReady => canvas != null;
    public bool EntranceComplete => introAge >= style.entranceDuration+style.entranceResolve;
    private ScoreManager owner;
    private Canvas canvas;
    private RectTransform safeArea;
    private Shader shader;
    private bool preview, subscribed, chimed;
    private float introAge, clock;
    private AudioSource chimeSource;
    private AudioClip chime;
    private readonly List<FullRibbonImage> images=new List<FullRibbonImage>();
    private readonly List<CanvasGroup> hiddenLegacy=new List<CanvasGroup>();
    private readonly List<float> legacyAlpha=new List<float>();
    private readonly List<Behaviour> disabledLegacy=new List<Behaviour>();
    private readonly Side[] sides={new Side(),new Side()};
    private sealed class Side {
        public RectTransform root;
        public FullRibbonImage bar, avatarBase;
        public RibbonOpticalGraphic optics;
        public FullRibbonLoadingGraphic loading;
        public bool loadingDirty=true;
        public Image avatar;
        public FullRibbonImage[] hp=new FullRibbonImage[8], score=new FullRibbonImage[6];
        public FullRibbonImage marks;
        public FullRibbonImage hpLabel, innerLeaves;
        public FullRibbonAccentGraphic flame,entryBurst,deathBurst;
        public FullRibbonHealthMotion health=new FullRibbonHealthMotion();
        public int total, hpValue, maxHP=326;
        public bool defeated;
        public string hpString="",scoreString="";
        public float scorePulse,level,targetLevel,entryBurstAge=100,layoutAge=100,marksX,marksWidth=33,scoreY,tickActivity;
        public float[] digitX=new float[6];
        public float[] digitAlpha=new float[6];
    }
    public static bool TryEnable(ScoreManager manager) {
        var profile=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
        if(profile==null||!profile.useFullRibbon||!profile.HasArtwork||Resources.Load<Shader>("Shaders/FullRibbonHUD")==null||Resources.Load<Shader>("Shaders/RibbonOpticsV7")==null||Resources.Load<Shader>("Shaders/UnifiedLoadingV7")==null)return false;
        var hud=manager.GetComponent<FullRibbonBattleHUD>();if(hud==null)hud=manager.gameObject.AddComponent<FullRibbonBattleHUD>();
        hud.Configure(manager,profile);return true;
    }
    public void Configure(ScoreManager manager,FullRibbonHUDStyle profile) {
        owner=manager;style=profile;
        if(manager!=null&&!subscribed) {
            owner.OnHPChanged+=HPChanged;owner.OnScoreChanged+=ScoreChanged;owner.OnScoreGained+=ScoreGained;owner.OnBattleReset+=ResetBattle;owner.OnPlayerDefeated+=Defeated;
            if(owner.feverManager!=null)owner.feverManager.OnStateChanged+=FeverChanged;
            subscribed=true;
        }
    }
    public void BuildPreview(FullRibbonHUDStyle profile,Camera camera) {
        style=profile;preview=true;Build();canvas.renderMode=RenderMode.WorldSpace;
        canvas.worldCamera=camera;canvas.GetComponent<CanvasScaler>().enabled=false;
        var root=(RectTransform)canvas.transform;root.sizeDelta=new Vector2(1920,600);
        root.position=Vector3.zero;root.rotation=Quaternion.identity;root.localScale=Vector3.one*.01f;
        PreviewReset(1);introAge=100;Apply(0);
    }
    private void LateUpdate() {
        if(preview||style==null)return;
        if(canvas==null) {
            if(owner==null||owner.GetMaxHP(0)<=0)return;
            Build();ResetBattle();HideLegacy();
        }
        Advance(Time.deltaTime);
    }
    public void Advance(float dt) {
        if(!IsReady)return;
        dt=Mathf.Max(0,dt);introAge+=dt;clock+=dt;
        bool completed=EntranceComplete;
        if(completed&&!chimed) {
            chimed=true;foreach(var s in sides)s.entryBurstAge=0;
            if(!preview&&style.chimeVolume>0&&chimeSource!=null)chimeSource.PlayOneShot(chime,style.chimeVolume);
        }
        for(int i=0;i<2;i++) {
            Side s=sides[i];s.health.Tick(dt,style);s.entryBurstAge+=dt;s.scorePulse=Mathf.Max(0,s.scorePulse-dt);
            float target=s.health.ZeroImpact?0:s.targetLevel;
            s.level=Mathf.MoveTowards(s.level,target,dt*2/Mathf.Max(.01f,style.extinguishDuration));
        }
        Apply(dt);
    }
    private void Build() {
        shader=Resources.Load<Shader>("Shaders/FullRibbonHUD");
        var host=New("FullRibbonBattleHUD_V1",null);canvas=host.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=101;
        var scaler=host.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        safeArea=(RectTransform)New("CameraSafeArea",host.transform).transform;
        for(int side=0;side<2;side++) {
            Side s=sides[side];s.root=(RectTransform)New(side==0?"Team_Red":"Team_Blue",safeArea).transform;
            Make(side,"OuterLeaves",style.outerLeaves[side],style.outerLeafPosition,style.outerLeafSize);
            Make(side,"OuterDot",style.dot[side],new Vector2(20,98),new Vector2(10,13));
            s.avatarBase=Make(side,"AvatarLoadingShape",style.avatarMask,style.masterArtwork?new Vector2(46,37):new Vector2(43,45),style.masterArtwork?new Vector2(112,112):new Vector2(105,105));
            s.avatarBase.color=Color.white;s.avatarBase.InstanceMaterial.SetColor("_Tint",ShaderColor(new Color(.13f,.17f,.19f,1)));
            var maskRoot=New("ReplaceableAvatar",s.root);Place((RectTransform)maskRoot.transform,style.masterArtwork?new Vector2(46,37):new Vector2(43,45),style.masterArtwork?new Vector2(112,112):new Vector2(105,105),side);
            var maskImage=maskRoot.AddComponent<Image>();maskImage.sprite=style.avatarMask;maskImage.raycastTarget=false;
            maskRoot.AddComponent<Mask>().showMaskGraphic=false;
            var portrait=New("Content",maskRoot.transform);s.avatar=portrait.AddComponent<Image>();s.avatar.raycastTarget=false;s.avatar.preserveAspect=true;
            var ar=(RectTransform)portrait.transform;ar.anchorMin=Vector2.zero;ar.anchorMax=Vector2.one;ar.offsetMin=ar.offsetMax=Vector2.zero;
            SetTeamAvatar(side,style.avatars!=null&&style.avatars.Length>side?style.avatars[side]:null);
            Make(side,"AvatarFrame",style.avatarFrame[side],style.masterArtwork?new Vector2(31,22):new Vector2(30,32),style.masterArtwork?new Vector2(144,140):new Vector2(132,132));
            if(!style.masterArtwork)Make(side,"IdentityCrescent",style.crescent[side],style.crescentPosition,style.crescentSize);
            s.bar=Make(side,"BloodRibbon",style.ribbon[side],style.ribbonPosition,style.ribbonSize);s.bar.InstanceMaterial.SetFloat("_Mode",1);
            if(style.ribbonProfiles!=null&&style.ribbonProfiles.Length>side&&style.ribbonProfiles[side]!=null)s.bar.InstanceMaterial.SetTexture("_CurveTex",style.ribbonProfiles[side]);
            s.bar.InstanceMaterial.SetFloat("_HasProfile",style.masterArtwork?1:0);
            s.bar.InstanceMaterial.SetVector("_MeterRange",style.masterArtwork?new Vector4(.066f,.98f,0,0):new Vector4(0,1,0,0));
            s.optics=New("RibbonOptical_V7",s.root).AddComponent<RibbonOpticalGraphic>();s.optics.Configure();
            s.innerLeaves=Make(side,"InnerLeaves",style.innerLeaves[side],new Vector2(655,76),new Vector2(44,48));
            var label=s.hpLabel=Make(side,"HP",style.hpLabel,new Vector2(171,58),new Vector2(46,34));
            // Text stays readable; blue HP label sits before its HP digits.
            if(side==1)Place(label.rectTransform,new Vector2(405,58),new Vector2(46,34),0);
            s.flame=Accent("FeverLoop",s.root,false);Place(s.flame.rectTransform,new Vector2(side==0?330:75,10),new Vector2(315,85),0);
            for(int k=0;k<s.hp.Length;k++)s.hp[k]=Make(side,"HPDigit_"+k,style.digits[0],Vector2.zero,new Vector2(20,36));
            for(int k=0;k<s.score.Length;k++)s.score[k]=Make(side,"ScoreDigit_"+k,style.digits[0],Vector2.zero,new Vector2(40,64));
            s.marks=Make(side,"ScoreMarks",style.marks[side],new Vector2(310,99),new Vector2(33,20));
            s.loading=New("UnifiedLoading_V7",s.root).AddComponent<FullRibbonLoadingGraphic>();s.loading.Configure();
            s.entryBurst=Accent("LoadingComplete",s.root,true);s.deathBurst=Accent("ZeroImpact",s.root,true);
            Place(s.entryBurst.rectTransform,new Vector2(641,112),new Vector2(40,40),side);
            Place(s.deathBurst.rectTransform,style.masterArtwork?new Vector2(style.ribbonPosition.x+style.ribbonSize.x*.066f-20,RibbonCenterY(style.ribbonPosition.x+style.ribbonSize.x*.066f)-20):new Vector2(136,110),new Vector2(40,40),side);
        }
        if(!preview)CreateChime();
    }
    private void CreateChime() {
        chimeSource=gameObject.AddComponent<AudioSource>();chimeSource.playOnAwake=false;chimeSource.spatialBlend=0;
        const int rate=24000;int count=(int)(rate*.16f);var samples=new float[count];
        for(int i=0;i<count;i++){float t=i/(float)rate;samples[i]=(Mathf.Sin(t*2*Mathf.PI*1568)+.25f*Mathf.Sin(t*2*Mathf.PI*3136))*.22f*Mathf.Exp(-t*35)*Mathf.Min(1,t*500);}
        chime=AudioClip.Create("HUD_LoadComplete",count,1,rate,false);chime.SetData(samples,0);
    }
    private FullRibbonAccentGraphic Accent(string name,Transform parent,bool burst) {
        var graphic=New(name,parent).AddComponent<FullRibbonAccentGraphic>();graphic.style=style;graphic.burstOnly=burst;graphic.raycastTarget=false;return graphic;
    }
    private FullRibbonImage Make(int side,string name,Sprite sprite,Vector2 offset,Vector2 size) {
        var image=New(name,sides[side].root).AddComponent<FullRibbonImage>();image.Configure(sprite,shader);
        Place(image.rectTransform,offset,size,side);images.Add(image);
        var m=image.InstanceMaterial;m.SetColor("_Tint",Color.white);m.SetFloat("_Brightness",1);m.SetFloat("_Visibility",1);
        // One palette for pigment, frame and text. Source textures supply shape and paper grain.
        bool lettering=name=="HP"||name.StartsWith("ScoreDigit_");
        bool teamPigment=name=="BloodRibbon"||name=="AvatarFrame"||name=="IdentityCrescent"||name=="OuterDot"||name=="ScoreMarks";
        if(lettering||(teamPigment&&!style.masterArtwork)){m.SetFloat("_Monochrome",1);m.SetColor("_Tint",ShaderColor(lettering?style.letteringColor:style.Team(side)));}
        m.SetColor("_FlashColor",ShaderColor(FlashColor(side)));m.SetFloat("_Flip",side);
        m.SetFloat("_Identity",name=="AvatarLoadingShape"||name=="AvatarFrame"||name=="OuterLeaves"||name=="OuterDot"?1:0);
        return image;
    }
    private Color FlashColor(int side) => side==0?new Color(1,.94f,.91f):new Color(.84f,.97f,1);
    private static Color ShaderColor(Color c) => QualitySettings.activeColorSpace==ColorSpace.Linear?c.linear:c;
    private GameObject New(string name,Transform parent) {
        var go=new GameObject(name,typeof(RectTransform));SceneManager.MoveGameObjectToScene(go,gameObject.scene);
        if(parent!=null)go.transform.SetParent(parent,false);return go;
    }
    private void Place(RectTransform rt,Vector2 offset,Vector2 size,int side) {
        rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.sizeDelta=size;
        rt.anchoredPosition=new Vector2(side==0?offset.x:style.teamSize.x-offset.x-size.x,-offset.y);
    }
    private void Apply(float dt) {
        Rect safe=preview?new Rect(0,0,Screen.width,Screen.height):Screen.safeArea;
        if(!preview&&Camera.main!=null){Rect r=Camera.main.pixelRect;float l=Mathf.Max(safe.xMin,r.xMin),b=Mathf.Max(safe.yMin,r.yMin),rr=Mathf.Min(safe.xMax,r.xMax),t=Mathf.Min(safe.yMax,r.yMax);if(rr>l&&t>b)safe=Rect.MinMaxRect(l,b,rr,t);}
        safeArea.anchorMin=preview?Vector2.zero:new Vector2(safe.xMin/Mathf.Max(1,Screen.width),safe.yMin/Mathf.Max(1,Screen.height));
        safeArea.anchorMax=preview?Vector2.one:new Vector2(safe.xMax/Mathf.Max(1,Screen.width),safe.yMax/Mathf.Max(1,Screen.height));safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        float available=((RectTransform)canvas.transform).rect.width*(preview?1:safe.width/Mathf.Max(1,Screen.width))*.5f-style.screenInset.x-style.centerGap*.5f;
        float scale=Mathf.Min(style.scale,Mathf.Max(.1f,available/style.teamSize.x));
        float phase=Mathf.Clamp01(introAge/Mathf.Max(.01f,style.entranceDuration));
        // Single distance clock: no long pause between identity and ribbon.
        float load=1-Mathf.Pow(1-phase,2.2f);
        float resolve=Mathf.Clamp01((introAge-style.entranceDuration)/Mathf.Max(.01f,style.entranceResolve));
        float blend=1-Mathf.SmoothStep(0,1,resolve);
        for(int i=0;i<2;i++) {
            Side s=sides[i];s.root.anchorMin=s.root.anchorMax=new Vector2(i,1);s.root.pivot=new Vector2(i,1);s.root.sizeDelta=style.teamSize;
            s.root.anchoredPosition=new Vector2(i==0?style.screenInset.x:-style.screenInset.x,-style.screenInset.y);s.root.localScale=Vector3.one*scale;
            Place(s.bar.rectTransform,style.ribbonPosition,style.ribbonSize,i);
            LayoutNumbers(i,dt);
            // Colour handoff is a single completion beat; no double-alpha crossfade.
            float baseVisibility=EntranceComplete?1:0;
            s.avatar.enabled=EntranceComplete&&s.avatar.sprite!=null;s.avatar.color=Color.white;
            float danger=s.health.Danger&&s.health.Target>0?Mathf.Lerp(style.dangerMinBrightness,1,Mathf.Pow(.5f+.5f*Mathf.Cos(clock*2*Mathf.PI/Mathf.Max(.2f,style.dangerPeriod)),2)):1;
            float low=Mathf.Min(s.health.Cursor,s.health.Target),high=Mathf.Max(s.health.Cursor,s.health.Target);
            Vector4 lowBoundary=RibbonVisualGeometry.Boundary(style,i,low),highBoundary=RibbonVisualGeometry.Boundary(style,i,high);
            Vector4 cursorBoundary=RibbonVisualGeometry.Boundary(style,i,s.health.Cursor);
            Vector4 f0=RibbonVisualGeometry.Boundary(style,i,s.health.FlashLow),f1=RibbonVisualGeometry.Boundary(style,i,s.health.FlashHigh);
            bool interval=Mathf.Abs(high-low)>.00001f;
            var m=s.bar.InstanceMaterial;m.SetVector("_BarSize",new Vector4(style.ribbonSize.x,style.ribbonSize.y,0,0));
            m.SetVector("_LowBoundary",lowBoundary);m.SetVector("_HighBoundary",highBoundary);m.SetVector("_FlashBoundary0",f0);m.SetVector("_FlashBoundary1",f1);
            m.SetFloat("_SideSign",i==0?1:-1);m.SetFloat("_Bow",style.opticalBow);m.SetFloat("_Interval",interval?1:0);m.SetFloat("_Full",low>=.9999f?1:0);
            m.SetFloat("_Healing",s.health.Healing?1:0);m.SetColor("_LossColor",ShaderColor(style.lossColor));m.SetColor("_HealColor",ShaderColor(style.healingColor));m.SetColor("_EmptyColor",ShaderColor(style.Empty(i)));
            m.SetColor("_FlashColor",ShaderColor(FlashColor(i)));m.SetFloat("_Flash",s.health.Flash(style));m.SetFloat("_Brightness",danger);
            bool moving=s.health.Moving(style);float tickTarget=moving?1:0;
            s.tickActivity=dt<=0?tickTarget:Mathf.Lerp(s.tickActivity,tickTarget,1-Mathf.Exp(-dt*(tickTarget>s.tickActivity?32:18)));
            float tickVisible=s.health.Cursor>.0001f&&(s.health.Target<.9999f||s.health.Cursor<.9999f)?1:0;
            Place(s.optics.rectTransform,style.ribbonPosition,style.ribbonSize,i);
            s.optics.SetState(style,i,cursorBoundary,lowBoundary,highBoundary,f0,f1,tickVisible,s.tickActivity,interval,s.health.Healing,s.health.Flash(style),baseVisibility);
            if(!EntranceComplete&&s.loadingDirty){s.loading.BuildMask(images,s.root,style,i);s.loadingDirty=false;}
            Place(s.loading.rectTransform,new Vector2(-FullRibbonLoadingGraphic.Padding,-FullRibbonLoadingGraphic.Padding),style.teamSize+Vector2.one*FullRibbonLoadingGraphic.Padding*2,0);
            s.loading.SetState(style,i,load,resolve,!EntranceComplete&&introAge>.015f?1:0);
            s.flame.level=EntranceComplete&&s.total>0?s.level:0;s.flame.clock=clock;s.flame.SetVerticesDirty();
            s.entryBurst.burstAge=s.entryBurstAge;s.entryBurst.burstDuration=.44f;s.entryBurst.forwardBurst=true;s.entryBurst.forwardSign=i==0?1:-1;s.entryBurst.SetVerticesDirty();
            s.deathBurst.burstAge=s.health.BurstAge;s.deathBurst.burstDuration=style.zeroBurstDuration;s.deathBurst.SetVerticesDirty();
        }
        foreach(var image in images){
            image.InstanceMaterial.SetFloat("_Visibility",EntranceComplete?1:0);
            if(image.padding!=0){image.padding=0;image.SetVerticesDirty();}
        }
    }
    private void LayoutNumbers(int side,float dt) {
        Side s=sides[side];string hp=Mathf.Max(0,s.hpValue).ToString();
        float aspect=Mathf.Clamp(style.digitWidthScale,.7f,1);
        float hh=Mathf.Min(style.hpHeight,128f/Mathf.Max(1,hp.Length)/(.8f*aspect));
        float end=style.ribbonPosition.x+style.ribbonSize.x;
        float hpLeft=style.masterArtwork?184:style.ribbonPosition.x+15;
        float hpAdvance=style.masterArtwork?75:50;
        float widest=0;foreach(var digit in style.digits)widest=Mathf.Max(widest,digit.rect.width/digit.rect.height);
        float hpDigitsWidth=0;
        foreach(char digit in hp)hpDigitsWidth+=hh*aspect*style.digits[digit-'0'].rect.width/style.digits[digit-'0'].rect.height+1;
        int reservedHPDigits=Mathf.Max(3,hp.Length);
        float hpGroupWidth=hpAdvance+Mathf.Max(hpDigitsWidth-1,hh*aspect*widest*reservedHPDigits+reservedHPDigits-1);
        float labelX=side==0?hpLeft:style.teamSize.x-hpLeft-hpGroupWidth;
        float hpTop=style.masterArtwork?36:style.ribbonPosition.y-hh-6;
        Place(s.hpLabel.rectTransform,new Vector2(labelX,hpTop+(style.masterArtwork?16:8)),style.masterArtwork?new Vector2(54,38):new Vector2(46,36),0);
        Place(s.innerLeaves.rectTransform,new Vector2(end+style.innerLeafOffset.x,style.ribbonPosition.y+style.innerLeafOffset.y),style.innerLeafSize,side);
        for(int k=0;k<s.hp.Length;k++) {
            var image=s.hp[k];image.enabled=k<hp.Length;
            if(k>=hp.Length)continue;
            SetDigit(image,hp[k]-'0');float width=hh*aspect*image.artwork.rect.width/image.artwork.rect.height;
            float hx=labelX+hpAdvance;
            for(int j=0;j<k;j++)hx+=hh*aspect*style.digits[hp[j]-'0'].rect.width/style.digits[hp[j]-'0'].rect.height+1;
            Place(image.rectTransform,new Vector2(hx,hpTop),new Vector2(width,hh),0);
            image.InstanceMaterial.SetColor("_Tint",ShaderColor(style.Team(side)));image.InstanceMaterial.SetFloat("_Monochrome",1);
        }
        // Actual scores are never clamped. The six-digit display uses 999999 as its visual ceiling.
        string score=s.total>0?Mathf.Min(999999,s.total).ToString():"";
        // Reserve enough room for SIX widest glyphs from the beginning. Use actual
        // glyph advances, so a narrow 1 does not leave a monospace hole. Marks and HP
        // must never collide when the score grows to six digits.
        float scoreHeight=Mathf.Min(style.scoreHeight,(end-16-(hpLeft+hpGroupWidth+22))/(6*widest*aspect));
        float widthTotal=0;
        foreach(char digit in score)widthTotal+=scoreHeight*aspect*style.digits[digit-'0'].rect.width/style.digits[digit-'0'].rect.height;
        float scoreEdge=end-16;
        float start=side==0?scoreEdge-widthTotal:style.teamSize.x-scoreEdge;
        float scoreMid=side==0?start+widthTotal*.5f:style.teamSize.x-(start+widthTotal*.5f);
        float scoreTop=style.masterArtwork?22:RibbonCenterY(scoreMid)-14-scoreHeight;
        int oldLength=s.scoreString.Length;
        s.scoreY=oldLength==0||dt<=0?scoreTop:Mathf.Lerp(s.scoreY,scoreTop,1-Mathf.Exp(-dt*24));
        if(score.Length!=oldLength)s.layoutAge=0;else s.layoutAge+=dt;
        for(int k=0;k<s.score.Length;k++) {
            var image=s.score[k];bool visible=k<score.Length&&EntranceComplete;image.enabled=visible;
            if(k>=score.Length){s.digitAlpha[k]=0;continue;}
            SetDigit(image,score[k]-'0');float w=scoreHeight*aspect*image.artwork.rect.width/image.artwork.rect.height;
            float x=start;
            for(int j=0;j<k;j++)x+=scoreHeight*aspect*style.digits[score[j]-'0'].rect.width/style.digits[score[j]-'0'].rect.height;
            if(oldLength==0||dt<=0)s.digitX[k]=x;
            else if(k>=oldLength)s.digitX[k]=x+(side==0?8:-8);
            else s.digitX[k]=Mathf.Lerp(s.digitX[k],x,1-Mathf.Exp(-dt*24));
            Place(image.rectTransform,new Vector2(s.digitX[k],s.scoreY),new Vector2(w,scoreHeight),0);
            s.digitAlpha[k]=oldLength==0||dt<=0?1:Mathf.MoveTowards(s.digitAlpha[k],1,dt/.09f);
            image.color=new Color(1,1,1,s.digitAlpha[k]);
            image.rectTransform.localScale=Vector3.one*(1+.035f*Mathf.Sin(Mathf.Clamp01(s.scorePulse/.16f)*Mathf.PI));
        }
        s.scoreString=score;
        s.marks.enabled=score.Length>0&&EntranceComplete;
        float availableGap=side==0?start-(labelX+hpGroupWidth):labelX-(start+widthTotal);
        float marksWidth=style.masterArtwork?Mathf.Min(60,Mathf.Max(18,availableGap-4)):Mathf.Clamp(availableGap-6,12,33);
        s.marksWidth=oldLength==0||dt<=0?marksWidth:Mathf.Lerp(s.marksWidth,marksWidth,1-Mathf.Exp(-dt*24));
        float markTarget=side==0?start-s.marksWidth-4:start+widthTotal+4;
        s.marksX=oldLength==0||dt<=0?markTarget:Mathf.Lerp(s.marksX,markTarget,1-Mathf.Exp(-dt*24));
        float marksCenter=side==0?s.marksX+s.marksWidth*.5f:style.teamSize.x-s.marksX-s.marksWidth*.5f;
        float marksHeight=style.masterArtwork?s.marksWidth*s.marks.artwork.rect.height/Mathf.Max(1,s.marks.artwork.rect.width):18;
        Place(s.marks.rectTransform,new Vector2(s.marksX,style.masterArtwork?scoreTop+scoreHeight-marksHeight+3:RibbonCenterY(marksCenter)-31),new Vector2(s.marksWidth,marksHeight),0);
        // Flame bases follow the actual score's upper third, rather than a fixed HUD y.
        float flameHeight=style.superFeverHeight+30;
        Place(s.flame.rectTransform,new Vector2(start,s.scoreY+scoreHeight*.08f-flameHeight),new Vector2(Mathf.Max(40,widthTotal),flameHeight),0);
        Place(s.entryBurst.rectTransform,new Vector2(end-28,RibbonCenterY(end-8)-20),new Vector2(40,40),side);
    }
    private float RibbonCenterY(float redLayoutX){
        float u=Mathf.Clamp01((redLayoutX-style.ribbonPosition.x)/Mathf.Max(1,style.ribbonSize.x));
        if(style.masterArtwork&&style.ribbonProfiles!=null&&style.ribbonProfiles.Length>0&&style.ribbonProfiles[0]!=null&&style.ribbonProfiles[0].isReadable){
            Color section=style.ribbonProfiles[0].GetPixelBilinear(u,.5f);
            return style.ribbonPosition.y+(1-(section.r+section.g)*.5f)*style.ribbonSize.y;
        }
        float y=((6.26993f*u-8.66934f)*u+2.87552f)*u+.3613f;
        return style.ribbonPosition.y+(1-y)*style.ribbonSize.y;
    }
    private void SetDigit(FullRibbonImage image,int number) {
        Sprite sprite=style.digits[Mathf.Clamp(number,0,9)];if(image.artwork==sprite)return;
        image.artwork=sprite;Rect r=sprite.rect;
        image.InstanceMaterial.SetVector("_SpriteUV",new Vector4(r.x/sprite.texture.width,r.y/sprite.texture.height,r.width/sprite.texture.width,r.height/sprite.texture.height));image.SetAllDirty();
    }
    private void HPChanged(int left,int right) {for(int i=0;i<2;i++){Side s=sides[i];if(s.defeated)continue;s.hpValue=i==0?left:right;s.maxHP=Mathf.Max(1,owner.GetMaxHP(i));s.health.Change(s.hpValue/(float)s.maxHP);}}
    private void Defeated(int side){if(side>=0&&side<2)sides[side].defeated=true;}
    private void ScoreChanged(int left,int right) {sides[0].total=left;sides[1].total=right;}
    private void ScoreGained(int side,int delta){if(side>=0&&side<2&&delta>0)sides[side].scorePulse=.16f;}
    private void FeverChanged(FeverState oldState,FeverState state,int side) {if(side>=0&&side<2)sides[side].targetLevel=state==FeverState.SuperFever?2:state==FeverState.Fever?1:0;}
    public bool ZeroImpactReady(int side) => !IsReady||sides[Mathf.Clamp(side,0,1)].health.ZeroImpact;
    public float DisplayedFill01(int side) => sides[Mathf.Clamp(side,0,1)].health.Cursor;
    public void SetTeamAvatar(int side,Sprite picture) {
        if(side<0||side>1||sides[side].avatar==null)return;
        var avatar=sides[side].avatar;avatar.sprite=picture;avatar.enabled=picture!=null&&EntranceComplete;
        if(picture==null)return;
        float aspect=picture.rect.width/Mathf.Max(1,picture.rect.height);
        var rt=avatar.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;
        rt.sizeDelta=aspect>=1?new Vector2(105*aspect,105):new Vector2(105,105/aspect);
    }
    public void ResetBattle() {
        introAge=0;clock=0;chimed=false;
        for(int i=0;i<2;i++){Side s=sides[i];s.defeated=false;s.hpValue=owner.GetHP(i);s.maxHP=Mathf.Max(1,owner.GetMaxHP(i));s.health.Reset(s.hpValue/(float)s.maxHP);s.loadingDirty=true;s.total=i==0?owner.GetLeftScore():owner.GetRightScore();s.scoreString="";s.scorePulse=s.level=s.targetLevel=0;s.entryBurstAge=100;}
    }
    public void PreviewReset(float value) {introAge=100;chimed=true;foreach(var s in sides){s.maxHP=326;s.hpValue=Mathf.RoundToInt(value*326);s.health.Reset(value);s.total=5842;s.scoreString="";s.level=s.targetLevel=0;s.entryBurstAge=100;}Apply(0);}
    public void PreviewChange(float value){foreach(var s in sides){s.hpValue=Mathf.CeilToInt(value*326);s.health.Change(value);}}
    public void PreviewFever(int level){foreach(var s in sides)s.targetLevel=level;}
    public void PreviewScores(int red,int blue){sides[0].total=Mathf.Max(0,red);sides[1].total=Mathf.Max(0,blue);Apply(0);}
    public void PreviewEntrance(){PreviewReset(1);foreach(var s in sides){s.total=0;s.loadingDirty=true;}introAge=0;chimed=false;Apply(0);}
    public void RebuildForPreview(){
        foreach(var image in images)if(image.isActiveAndEnabled){image.SetVerticesDirty();image.SetMaterialDirty();image.Rebuild(CanvasUpdate.PreRender);}
        foreach(var s in sides){if(s.optics!=null&&s.optics.isActiveAndEnabled)s.optics.Rebuild(CanvasUpdate.PreRender);if(s.loading!=null&&s.loading.isActiveAndEnabled)s.loading.Rebuild(CanvasUpdate.PreRender);}
        foreach(var s in sides)foreach(var g in new[]{s.flame,s.entryBurst,s.deathBurst})if(g!=null&&g.isActiveAndEnabled)g.Rebuild(CanvasUpdate.PreRender);
    }
    public string Diagnostics(){
        var text=new System.Text.StringBuilder();text.AppendLine("Canvas="+canvas.renderMode+" rect="+((RectTransform)canvas.transform).rect+" scale="+canvas.transform.localScale+" intro="+introAge);
        foreach(var image in images){
            var mesh=image.canvasRenderer.GetMesh();
            text.AppendLine(image.name+" active="+image.isActiveAndEnabled+" rect="+image.rectTransform.rect+" world="+image.transform.position+" verts="+(mesh!=null?mesh.vertexCount:0)+" cull="+image.canvasRenderer.cull+" color="+image.color+" tint="+image.InstanceMaterial.GetColor("_Tint")+" visibility="+image.InstanceMaterial.GetFloat("_Visibility")+" uv="+image.InstanceMaterial.GetVector("_SpriteUV"));
        }
        foreach(var side in sides){text.AppendLine("V7 optics verts="+side.optics.canvasRenderer.GetMesh()?.vertexCount+" cull="+side.optics.canvasRenderer.cull+" visible="+side.optics.InstanceMaterial.GetFloat("_Visibility"));text.AppendLine("V7 loading mask="+side.loading.HasMask+" visible="+side.loading.IsVisible+" verts="+side.loading.canvasRenderer.GetMesh()?.vertexCount+" cull="+side.loading.canvasRenderer.cull+" cache="+side.loading.UsedBakedMask+" maskBuildMs="+side.loading.LastBuildMilliseconds);}
        return text.ToString();
    }
    private void HideLegacy() {
        foreach(var behaviour in new Behaviour[]{owner.GetComponent<ConnectedBattleHeaderLayout>(),owner.GetComponent<BattleHeaderLayout>()})DisableLegacy(behaviour);
        foreach(var bar in FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None))if(bar.gameObject.scene==gameObject.scene&&(bar.scoreManager==null||bar.scoreManager==owner)){Hide(bar.gameObject);DisableLegacy(bar);}
        if(owner.leftScoreDisplay!=null)Hide(owner.leftScoreDisplay.gameObject);
        if(owner.rightScoreDisplay!=null)Hide(owner.rightScoreDisplay.gameObject);
    }
    private void DisableLegacy(Behaviour b){if(b==null||!b.enabled)return;disabledLegacy.Add(b);b.enabled=false;}
    private void Hide(GameObject go){var group=go.GetComponent<CanvasGroup>();if(group==null)group=go.AddComponent<CanvasGroup>();hiddenLegacy.Add(group);legacyAlpha.Add(group.alpha);group.alpha=0;}
    private void OnDestroy() {
        if(subscribed&&owner!=null){owner.OnHPChanged-=HPChanged;owner.OnScoreChanged-=ScoreChanged;owner.OnScoreGained-=ScoreGained;owner.OnBattleReset-=ResetBattle;owner.OnPlayerDefeated-=Defeated;if(owner.feverManager!=null)owner.feverManager.OnStateChanged-=FeverChanged;}
        for(int i=0;i<hiddenLegacy.Count;i++)if(hiddenLegacy[i]!=null)hiddenLegacy[i].alpha=legacyAlpha[i];
        foreach(var b in disabledLegacy)if(b!=null)b.enabled=true;
        if(canvas!=null){if(Application.isPlaying)Destroy(canvas.gameObject);else DestroyImmediate(canvas.gameObject);}
        if(chime!=null)Destroy(chime);if(chimeSource!=null)Destroy(chimeSource);
    }
}
