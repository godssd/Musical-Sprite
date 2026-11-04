using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Approved lettering and independently tinted strokes, driven by real judge events.</summary>
public sealed class HandpaintedJudgementHUD : MonoBehaviour {
    [Serializable] public sealed class Region {public string rank;public int x,y,width,height,glyphHeight;}
    [Serializable] sealed class Layout {public Region[] regions;}
    sealed class Art {public Sprite letters,decoration;public Region region;}
    sealed class Item {
        public RectTransform root;public Image letters,decoration;public CanvasGroup alpha;
        public readonly DampedNumberPulse pulse=new DampedNumberPulse();
        public Vector3 world;public Vector2 screen;public Camera camera;public bool fixedScreen,active;
        public int side,lane;public float age,life,yieldAt=-1;public string rank;
    }
    static readonly Dictionary<string,Art> artwork=new Dictionary<string,Art>();
    readonly List<Item> items=new List<Item>();
    Canvas canvas;RectTransform canvasRect;ScoreManager score;HandpaintedJudgementStyle style;
    public int SpawnCount {get;private set;}
    public string LastRank {get;private set;}
    public int ActiveCount {get{int n=0;foreach(var i in items)if(i.active)n++;return n;}}
    public static bool LoadArtwork(){
        if(artwork.Count==6)return true;
        var letters=Resources.Load<Texture2D>("BattleVisualV11/Judgements/lettering");
        var strokes=Resources.Load<Texture2D>("BattleVisualV11/Judgements/decoration");
        var json=Resources.Load<TextAsset>("BattleVisualV11/Judgements/layout");
        if(letters==null||strokes==null||json==null)return false;
        foreach(var r in JsonUtility.FromJson<Layout>(json.text).regions){
            Rect rect=new Rect(r.x,letters.height-r.y-r.height,r.width,r.height);
            artwork[r.rank]=new Art{region=r,letters=Sprite.Create(letters,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect),decoration=Sprite.Create(strokes,rect,new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect)};
        }
        return artwork.Count==6;
    }
    public static bool TryShow(JudgeFeedbackManager owner,int side,int lane,string rank,Vector3 position){
        var style=Resources.Load<HandpaintedJudgementStyle>(HandpaintedJudgementStyle.Path);
        if(style==null||!style.enabledInGame||!LoadArtwork()||Camera.main==null)return false;
        string key=rank=="BREAK"?"MISS":rank;if(!artwork.ContainsKey(key))return false;
        var hud=owner.GetComponent<HandpaintedJudgementHUD>();if(hud==null)hud=owner.gameObject.AddComponent<HandpaintedJudgementHUD>();
        hud.Configure(style);hud.Spawn(side,lane,key,position,Camera.main,false,Vector2.zero);return true;
    }
    public void Configure(HandpaintedJudgementStyle profile){
        style=profile;if(canvas!=null)return;
        var go=new GameObject("HandpaintedJudgements_V1",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        go.transform.SetParent(transform,false);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=97;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        canvasRect=(RectTransform)go.transform;Canvas.ForceUpdateCanvases();
        score=FindFirstObjectByType<ScoreManager>();if(score!=null)score.OnBattleReset+=ResetFeedback;
        for(int i=0;i<16;i++)items.Add(Create());
    }
    public void PreviewAtScreen(int side,int lane,string rank,Vector2 pixels){if(LoadArtwork())Spawn(side,lane,rank=="BREAK"?"MISS":rank,Vector3.zero,null,true,pixels);}
    Item Create(){
        var go=new GameObject("JudgeItem",typeof(RectTransform),typeof(CanvasGroup));go.transform.SetParent(canvasRect,false);
        var item=new Item{root=(RectTransform)go.transform,alpha=go.GetComponent<CanvasGroup>()};
        item.alpha.blocksRaycasts=false;item.alpha.interactable=false;
        Image Layer(string name){var x=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));x.transform.SetParent(go.transform,false);var image=x.GetComponent<Image>();image.raycastTarget=false;var rect=image.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return image;}
        item.decoration=Layer("TeamStroke");item.letters=Layer("CreamLettering");go.SetActive(false);return item;
    }
    void Spawn(int side,int lane,string rank,Vector3 world,Camera camera,bool fixedScreen,Vector2 screen){
        if(!artwork.TryGetValue(rank,out var art))return;
        // Shorten old visual tails only. Every legitimate event still creates its own feedback.
        foreach(var old in items)if(old.active&&old.side==side&&old.lane==lane&&old.yieldAt<0)old.yieldAt=old.age;
        Item item=null;foreach(var candidate in items)if(!candidate.active){item=candidate;break;}
        if(item==null){item=Create();items.Add(item);}
        item.active=true;item.side=side;item.lane=lane;item.rank=rank;item.world=world;item.camera=camera;item.fixedScreen=fixedScreen;item.screen=screen;
        item.age=0;item.life=style.lifetime*(rank=="YES"?.74f:rank=="MISS"?.85f:1);item.yieldAt=-1;
        float scale=rank=="PERFECT"?1:rank=="YES"?.72f:rank=="PASS"?.82f:.9f;
        float unit=style.glyphHeight*scale/Mathf.Max(1,art.region.glyphHeight);
        item.root.sizeDelta=new Vector2(art.region.width,art.region.height)*unit;
        item.root.localScale=Vector3.one;item.letters.sprite=art.letters;item.letters.color=Color.white;
        item.decoration.sprite=art.decoration;item.decoration.color=rank=="MISS"?style.miss:side==0?style.red:style.blue;
        item.pulse.Reset();item.pulse.Trigger(rank=="MISS"?1:1.08f,rank=="MISS"?.95f:.90f,rank=="MISS"?.24f:.32f);
        item.alpha.alpha=1;item.root.gameObject.SetActive(true);item.root.SetAsLastSibling();Position(item);
        SpawnCount++;LastRank=rank;
    }
    void LateUpdate()=>Advance(Time.deltaTime);
    public void Advance(float dt){
        dt=Mathf.Max(0,dt);
        foreach(var item in items){if(!item.active)continue;item.age+=dt;
            if(item.age>=item.life||(item.yieldAt>=0&&item.age-item.yieldAt>=.12f)){item.active=false;item.root.gameObject.SetActive(false);continue;}
            item.pulse.Advance(dt);item.root.localScale=Vector3.one*item.pulse.Scale;
            item.alpha.alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(item.life*.38f,item.life,item.age));
            if(item.yieldAt>=0)item.alpha.alpha=Mathf.Min(item.alpha.alpha,1-(item.age-item.yieldAt)/.12f);
            Position(item);
        }
    }
    void Position(Item item){
        Vector3 pixels=item.fixedScreen?(Vector3)item.screen:item.camera!=null?item.camera.WorldToScreenPoint(item.world):Vector3.zero;
        if(!item.fixedScreen&&pixels.z<=0){item.alpha.alpha=0;return;}
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,pixels,null,out var local);
        local.y+=(1-Mathf.Exp(-item.age*7))*style.rise;
        var bounds=canvasRect.rect;var half=item.root.sizeDelta*.5f;
        local.x=Mathf.Clamp(local.x,bounds.xMin+half.x+12,bounds.xMax-half.x-12);
        local.y=Mathf.Clamp(local.y,bounds.yMin+half.y+12,bounds.yMax-half.y-12);item.root.anchoredPosition=local;
    }
    public void ResetFeedback(){foreach(var item in items){item.active=false;if(item.root!=null)item.root.gameObject.SetActive(false);}}
    void OnDisable()=>ResetFeedback();
    void OnDestroy(){if(score!=null)score.OnBattleReset-=ResetFeedback;}
}
