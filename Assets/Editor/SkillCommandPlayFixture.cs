using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Play-only isolated skill owners. No scene assets or player preferences are changed.</summary>
public sealed class SkillCommandPlayFixture : IDisposable {
    readonly SkillInputUI ui;readonly ScoreManager score;
    readonly List<ActiveSkillRuntime> original=new List<ActiveSkillRuntime>(),fixtures=new List<ActiveSkillRuntime>();
    readonly List<bool> wasEnabled=new List<bool>();readonly List<SkillSO> skills=new List<SkillSO>();
    readonly HashSet<int> previousCasting;readonly HashSet<int> casting;
    readonly object previousBindings;readonly int leftScore,rightScore,leftHP,rightHP;
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static readonly FieldInfo bindingsField=typeof(SkillInputUI).GetField("bindings",Private);
    public SkillCommandPlayFixture(SkillInputUI input,ScoreManager owner){
        ui=input;score=owner;ui.GetBinding(0);previousBindings=bindingsField.GetValue(ui);bindingsField.SetValue(ui,new SkillCommandBindings());
        leftScore=score.GetLeftScore();rightScore=score.GetRightScore();leftHP=score.GetHP(0);rightHP=score.GetHP(1);
        foreach(var rt in UnityEngine.Object.FindObjectsByType<ActiveSkillRuntime>(FindObjectsSortMode.None)){original.Add(rt);wasEnabled.Add(rt.enabled);rt.enabled=false;}
        casting=(HashSet<int>)typeof(ActiveSkillRuntime).GetField("CastingSides",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);previousCasting=new HashSet<int>(casting);
        foreach(var ai in UnityEngine.Object.FindObjectsByType<OpponentInput>(FindObjectsSortMode.None))ai.StopAllCoroutines();
        Create(0,new[]{SkillInputStep.Left,SkillInputStep.Down,SkillInputStep.Right},false);
        Create(0,new[]{SkillInputStep.Left,SkillInputStep.Right,SkillInputStep.Left},false);
        Create(1,new[]{SkillInputStep.Down,SkillInputStep.Right,SkillInputStep.Down},false);
        Create(0,new[]{SkillInputStep.Right,SkillInputStep.Right,SkillInputStep.Right},true);
    }
    void Create(int side,SkillInputStep[] sequence,bool needsEnergy){
        var skill=ScriptableObject.CreateInstance<SkillSO>();skill.displayName="ReviewFixture";skill.charmedNoteCount=1;skill.effectType="ReviewFixture";skills.Add(skill);
        var owner=new CharacterClass{characterId=10000+fixtures.Count,activeSkill=skill,maxEnergies=new[]{needsEnergy?10f:0},currentEnergies=new[]{0f},isFullyChargedArr=new[]{false},skillBusyArr=new[]{false}};
        var go=new GameObject("SkillCommandReviewFixture");var rt=go.AddComponent<ActiveSkillRuntime>();rt.Setup(owner,null,null,null,side,null,skill,0,needsEnergy,sequence,0);fixtures.Add(rt);
    }
    void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    void RestoreCasting(){casting.Clear();foreach(int side in previousCasting)casting.Add(side);}
    public void Ready(){ui.ResetInput();RestoreCasting();foreach(var rt in fixtures){rt.phase=ActiveSkillRuntime.Phase.Standby;rt.owner.skillBusyArr[0]=false;rt.owner.currentEnergies[0]=0;}}
    public void Validate(){
        Ready();Assert(ui.FeedInput(0,SkillInputStep.Right)==FeedResult.Rejected,"Uncharged / unmatched input was accepted");
        Assert(ui.IsInputLocked(0)&&ui.BufferedCount(0)==0&&ui.Feedback(0)==SkillCommandFeedback.Reset,"Unmatched input did not reset and lock");
        int presses=ui.AcceptedPresses;Assert(ui.FeedInput(0,SkillInputStep.Left)==FeedResult.Locked&&ui.BufferedCount(0)==0&&ui.AcceptedPresses==presses,"Reset lock still accepted input");
        Assert(ui.FeedInput(1,SkillInputStep.Down)==FeedResult.Accepted&&ui.BufferedCount(1)==1,"One team's reset locked the other team");
        ui.AdvanceFeedback(ui.style.resetDuration+.01f);Assert(!ui.IsInputLocked(0),"Reset lock did not expire");
        Assert(ui.FeedInput(0,SkillInputStep.Left)==FeedResult.Accepted&&ui.MatchedCharacters==2,"Shared prefix did not match multiple characters");
        Assert(ui.FeedInput(0,SkillInputStep.Down)==FeedResult.Accepted,"Second prefix step failed");
        Assert(ui.FeedInput(0,SkillInputStep.Right)==FeedResult.Triggered&&fixtures[0].phase==ActiveSkillRuntime.Phase.Grow,"Complete sequence did not start actual runtime");
        Assert(ui.IsInputLocked(0)&&ui.BufferedCount(0)==0,"Release did not clear and lock");
        presses=ui.AcceptedPresses;Assert(ui.FeedInput(0,SkillInputStep.Left)==FeedResult.Locked&&ui.AcceptedPresses==presses,"Release lock processed another press");
        ui.AdvanceFeedback(ui.style.releaseDuration+.01f);Assert(!ui.IsInputLocked(0),"Release feedback lock did not end");
        Ready();ui.FeedInput(0,SkillInputStep.Left);ui.AdvanceFeedback(ui.inputInterval+.01f);
        Assert(ui.FeedInput(0,SkillInputStep.Down)==FeedResult.Rejected,"Timeout reused old prefix");
        Ready();fixtures[3].owner.currentEnergies[0]=10;
        Assert(ui.FeedInput(0,SkillInputStep.Right)==FeedResult.Accepted,"Charged runtime did not become eligible");ui.FeedInput(0,SkillInputStep.Right);
        Assert(ui.FeedInput(0,SkillInputStep.Right)==FeedResult.Triggered&&fixtures[3].owner.currentEnergies[0]==0,"Release did not consume the eligible slot exactly once");
        Ready();
        Assert(ui.SetBindings(new[]{KeyCode.F9,KeyCode.F10,KeyCode.F11},false),"Runtime key mapping failed");
        Assert(!ui.SetBindings(new[]{KeyCode.F9,KeyCode.F9,KeyCode.F11},false),"Duplicate mapping accepted");
        foreach(var spawner in UnityEngine.Object.FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None))if(spawner.side==0&&spawner.keys.Length>0){Assert(!ui.SetBinding(0,spawner.keys[0],false),"Skill mapping overlapped note input");break;}
        ui.ResetBindings(false);
        var proxy=ui.GetComponentInChildren<SkillButtonProxy>();var hit=(RectTransform)proxy.transform;Canvas.ForceUpdateCanvases();
        Vector2 center=RectTransformUtility.WorldToScreenPoint(null,hit.position);Assert(ui.ContainsScreenPoint(center),"Stable button hit rect missed center");
        var eventData=new PointerEventData(EventSystem.current){position=center,button=PointerEventData.InputButton.Left};var raycasts=new List<RaycastResult>();EventSystem.current.RaycastAll(eventData,raycasts);
        Assert(raycasts.Exists(r=>r.gameObject==proxy.gameObject),"Transparent skill hitbox did not receive UI raycasts");
        presses=ui.AcceptedPresses;proxy.OnPointerDown(eventData);Assert(ui.AcceptedPresses==presses+1&&ui.BufferedCount(0)==1,"Pointer down did not use shared semantic input");
        Vector2 size=hit.rect.size;ui.AdvanceFeedback(.045f);Assert(hit.rect.size==size&&ui.ContainsScreenPoint(center),"Visual compression changed touch hitbox");
        var touch=UnityEngine.Object.FindFirstObjectByType<TouchInputManager>();if(touch!=null){
            typeof(TouchInputManager).GetField("skillInput",Private).SetValue(touch,ui);
            var method=typeof(TouchInputManager).GetMethod("CaptureSkillPointer",Private);
            bool Capture(Vector2 point,TouchPhase phase)=>(bool)method.Invoke(touch,new object[]{90210,point,phase});
            Assert(Capture(center,TouchPhase.Began)&&Capture(new Vector2(-500,-500),TouchPhase.Moved)&&Capture(new Vector2(-500,-500),TouchPhase.Ended),"Skill gesture leaked when dragged away");
            Assert(!Capture(new Vector2(-500,-500),TouchPhase.Began),"Pointer ownership was not released");
        }
        if(ui.style.artworkFrame!=null){
            Assert(ui.UsesMasterArtwork&&ui.GetComponentsInChildren<SkillCommandFrameGraphic>().Length==0,"V4 artwork was replaced by approximate geometry");
            var frame=Array.Find(ui.GetComponentsInChildren<Image>(),image=>image.name=="MasterArtworkFrame");
            Assert(frame!=null&&frame.sprite==ui.style.artworkFrame,"V4 master sprite not bound");
            var mat=frame.material;Assert(mat!=null&&mat.shader.name=="MusicalSprite/UI/SkillCommandArtwork"&&!UnityEditor.ShaderUtil.ShaderHasError(mat.shader),"Artwork glow shader unavailable");
            Show("Press");ui.AdvanceFeedback(.055f);float press=mat.GetVector("_Press").x;
            Show("Release");ui.AdvanceFeedback(.11f);var glow=mat.GetVector("_Release");
            Assert(glow.x*glow.z>press*2&&ui.IsInputLocked(0),"Full release was not brighter than ordinary input");
            ui.ResetInput();Assert(mat.GetVector("_Release").x==0&&mat.GetVector("_Press").sqrMagnitude==0,"Reset retained emitted light");
        }
        ui.ResetInput();Assert(!ui.IsInputLocked(0)&&!ui.IsInputLocked(1)&&ui.BufferedCount(0)==0&&ui.BufferedCount(1)==0,"Restart retained command state");
        Assert(score.GetLeftScore()==leftScore&&score.GetRightScore()==rightScore&&score.GetHP(0)==leftHP&&score.GetHP(1)==rightHP,"UI-only fixture changed real score or HP");
    }
    public void Show(string kind){
        Ready();if(kind=="Press")ui.FeedInput(0,SkillInputStep.Left);
        else if(kind=="Reset")ui.FeedInput(0,SkillInputStep.Right);
        else if(kind=="Release"){ui.FeedInput(0,SkillInputStep.Left);ui.FeedInput(0,SkillInputStep.Down);ui.FeedInput(0,SkillInputStep.Right);}
    }
    public void Dispose(){
        ui.ResetInput();bindingsField.SetValue(ui,previousBindings);RestoreCasting();
        for(int i=0;i<original.Count;i++)if(original[i]!=null)original[i].enabled=wasEnabled[i];
        foreach(var rt in fixtures)if(rt!=null)UnityEngine.Object.DestroyImmediate(rt.gameObject);
        foreach(var skill in skills)if(skill!=null)UnityEngine.Object.DestroyImmediate(skill);
    }
}
