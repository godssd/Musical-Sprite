using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Spine.Unity;

public static class UIFxRebuildV7Regression
{
    public static void Batch(){
        string output=Path.GetFullPath("DesignPreviews/UIFxRebuildV7-20261101");Directory.CreateDirectory(output);
        var report=new StringBuilder();var scene=EditorSceneManager.NewPreviewScene();
        try{
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Shit/Shit.prefab");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var skel=model.GetComponentInChildren<SkeletonAnimation>(true);skel.Initialize(true);
            var driver=model.GetComponentInChildren<CharacterAnimator>(true);if(driver==null)driver=model.AddComponent<CharacterAnimator>();
            driver.animationPrefix="Aibo_2_Shit";
            void InvokeDriver(string method)=>typeof(CharacterAnimator).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(driver,null);
            InvokeDriver("Awake");InvokeDriver("Start");
            void Step(float dt){skel.AnimationState.Update(dt);skel.AnimationState.Apply(skel.Skeleton);InvokeDriver("Update");}
            string Pose(){var s=new StringBuilder();foreach(var bone in skel.Skeleton.Bones)s.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"{0:F4}/{1:F4}/{2:F4};",bone.Pose.X,bone.Pose.Y,bone.Pose.Rotation);return s.ToString();}
            Step(2);driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayFever);Step(.1f);
            if(!driver.PlayOnce(CharacterAnimator.CharacterAnimationState.TargetFever))throw new Exception("Fever target failed");
            driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlaySuperFever);Step(1);
            var entry=skel.AnimationState.GetTrack(0);string before=Pose();Step(.37f);
            if(entry==null||!entry.Loop||before==Pose())throw new Exception("Shit still stalls when super fever interrupts one-shot");
            report.AppendLine("Super loop fallback: "+entry.Animation.Name+", animated bone pose changed.");
            if(!driver.PlayOnce(CharacterAnimator.CharacterAnimationState.TargetSuperFever))throw new Exception("Super target fallback missing");
            Step(1);driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayNormal);Step(1);
            foreach(var state in new[]{CharacterAnimator.CharacterAnimationState.Hit,CharacterAnimator.CharacterAnimationState.SkillSelect,CharacterAnimator.CharacterAnimationState.SkillStart,CharacterAnimator.CharacterAnimationState.SkillAttak,CharacterAnimator.CharacterAnimationState.SkillEnd}){
                if(!driver.PlayOnce(state))throw new Exception("Character alias / priority failed: "+state);Step(2);
            }
            driver.PlayTimedLoop(CharacterAnimator.CharacterAnimationState.Dizziness,.2f);
            if(!skel.AnimationState.GetTrack(0).Animation.Name.EndsWith("09_Dizziness"))throw new Exception("Dizziness alias incorrect");
            driver.CancelTimedLoop(CharacterAnimator.CharacterAnimationState.PlayNormal);Step(1);
            driver.SetLoopState(CharacterAnimator.CharacterAnimationState.Victory);Step(1);
            if(!skel.AnimationState.GetTrack(0).Animation.Name.EndsWith("16_Victory01"))throw new Exception("Victory alias incorrect");
            report.AppendLine("PASS: super loop / target, hit, dizziness, skills, return-to-normal and victory aliases.");

            var style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
            foreach(float interval in new[]{.05f,.10f,.15f}){
                var m=new FullRibbonHealthMotion();m.Reset(1);
                for(int i=1;i<=8;i++){float prior=m.Cursor;m.Change(1-i*.04f);if(m.Cursor!=prior)throw new Exception("Loss cursor teleported on event");m.Tick(interval,style);}
                if(m.Cursor!=1)throw new Exception("Repeated loss moved before refreshed 1.2s pause at "+interval);
                m.Tick(2,style);if(Mathf.Abs(m.Cursor-.68f)>.0001f||m.Active)throw new Exception("Loss failed to settle");
                m.Reset(.2f);for(int i=1;i<=8;i++){m.Change(.2f+i*.06f);m.Tick(interval,style);}
                if(m.Cursor<.21f)throw new Exception("Repeated healing starves cursor at "+interval);
                m.Tick(2,style);if(Mathf.Abs(m.Cursor-.68f)>.0001f||m.Active)throw new Exception("Heal failed to settle");
                report.AppendLine("PASS: continuous losses/heals, interval="+interval+", finite settlement and no event-time jumps.");
            }
            var slow=new FullRibbonHealthMotion();var fast=new FullRibbonHealthMotion();slow.Reset(1);fast.Reset(1);slow.Change(.5f);fast.Change(.5f);
            for(int k=0;k<10;k++)slow.Tick(.035f,style);
            for(int k=0;k<40;k++)fast.Tick(.00875f,style);
            if(Mathf.Abs(slow.Cursor-fast.Cursor)>.0002f)throw new Exception("Damping changed with frame rate");
            var speedField=typeof(FullRibbonHealthMotion).GetField("velocity",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            slow.Tick(1.0f,style);if(slow.Cursor>=1)throw new Exception("Loss did not start after 1.2s");slow.Change(.2f);
            if((float)speedField.GetValue(slow)!=0)throw new Exception("Repeated loss did not stop motion immediately");
            float previous=slow.Cursor;for(int k=0;k<100;k++){slow.Tick(.01f,style);if(slow.Cursor>previous+.00001f||slow.Cursor<.19999f)throw new Exception("Damping overshot / reversed");previous=slow.Cursor;}
            report.AppendLine("PASS: analytic damping frame-rate invariance, loss interrupts and refreshes hold, monotonic finite settlement.");
            var motion=new FullRibbonHealthMotion();motion.Reset(1);motion.Change(.5f);motion.Tick(.29f,style);
            float old=motion.Cursor;motion.Change(.75f);if(motion.Cursor!=old)throw new Exception("Loss-to-heal reversal jumps");
            motion.Tick(2,style);if(Mathf.Abs(motion.Cursor-.75f)>.0001f)throw new Exception("Reversed healing did not settle");
            motion.Change(.9f);motion.Tick(.25f,style);old=motion.Cursor;motion.Change(.1f);if(motion.Cursor!=old)throw new Exception("Heal-to-loss reversal jumps");
            motion.Tick(2,style);motion.Change(0);motion.Tick(style.flashDuration+style.holdDuration,style);
            if(motion.ZeroImpact||motion.Cursor<=0)throw new Exception("Zero explosion occurs before cursor drain");
            motion.Tick(style.lossDuration+.01f,style);if(!motion.ZeroImpact||motion.Cursor!=0)throw new Exception("Zero impact missing");
            report.AppendLine("PASS: both reversal directions, white-flash peak, and zero impact after cursor reaches zero.");
            var pulse=new DampedNumberPulse();pulse.Trigger();pulse.Advance(.04f);
            if(pulse.Scale>.93f)throw new Exception("Number compression phase absent");
            pulse.Advance(.07f);if(pulse.Scale<1.07f)throw new Exception("Number expansion phase absent");
            float scaleBefore=pulse.Scale;pulse.Trigger();if(pulse.Scale!=scaleBefore)throw new Exception("Retrigger teleported number scale");
            pulse.Advance(.5f);if(pulse.Scale!=1||pulse.Active)throw new Exception("Number feedback failed to settle");
            var p30=new DampedNumberPulse();var p120=new DampedNumberPulse();p30.Trigger();p120.Trigger();
            for(int i=0;i<6;i++)p30.Advance(1f/30);for(int i=0;i<24;i++)p120.Advance(1f/120);
            if(Mathf.Abs(p30.Scale-p120.Scale)>.0001f)throw new Exception("Number feedback is frame-rate dependent");
            var ease=new FullRibbonHealthMotion();ease.Reset(1);ease.Change(.5f);ease.Tick(FullRibbonHealthMotion.LossPauseSeconds,style);
            float c0=ease.Cursor;ease.Tick(.05f,style);float first=c0-ease.Cursor;ease.Tick(.2f,style);
            c0=ease.Cursor;ease.Tick(.05f,style);float tail=c0-ease.Cursor;
            if(first<tail*3)throw new Exception("Health motion is not visibly fast then slow");
            report.AppendLine("PASS: number compress / expand / damp, continuous retrigger, frame-rate invariance; health first 50ms displacement >3x tail 50ms.");
            var pending=new FullRibbonHealthMotion();pending.Reset(1);pending.Change(.8f);
            pending.Tick(1.2f,style);if(pending.Cursor!=1||pending.Moving(style))throw new Exception("Inclusive 1.2s damage boundary moved");
            pending.Tick(.001f,style);if(pending.Cursor>=1)throw new Exception("Damage did not move strictly after 1.2s");
            float paused=pending.Cursor;pending.Change(.6f);pending.Tick(.7f,style);pending.Change(.5f);pending.Tick(1.2f,style);
            if(pending.Cursor!=paused)throw new Exception("Damage in-flight pause or refresh failed");
            pending.Tick(.01f,style);if(pending.Cursor>=paused)throw new Exception("Refreshed yellow did not resume");
            pending.Change(0);paused=pending.Cursor;pending.Tick(.001f,style);
            if(pending.Cursor>=paused||pending.LossPauseRemaining!=0)throw new Exception("Lethal damage retained pause");
            pending.Tick(1,style);if(!pending.ZeroImpact||pending.Cursor!=0)throw new Exception("Lethal drain did not finish before explosion");
            pending.Reset(.3f);pending.Change(.7f);pending.Tick(.4f,style);float solidHP=pending.Cursor;
            pending.Change(.9f);if(pending.Cursor!=solidHP||!pending.Healing)throw new Exception("Repeated heal did not stack green");
            pending.Change((solidHP+.9f)*.5f);
            if(pending.Cursor!=solidHP||!pending.Healing||pending.LossPauseRemaining!=0)throw new Exception("Damage within green created yellow or moved solid HP");
            pending.Change(solidHP-.1f);
            if(pending.Cursor!=solidHP||pending.Healing||pending.LossPauseRemaining!=1.2f)throw new Exception("Damage beyond green did not create only excess yellow");
            pending.Tick(1.2f,style);if(pending.Cursor!=solidHP)throw new Exception("Excess yellow skipped hold");
            pending.Tick(1,style);if(pending.Active)throw new Exception("Excess yellow failed to settle");
            pending.Reset(.3f);pending.Change(.8f);pending.Change(.3f);
            if(pending.Active||pending.Cursor!=.3f||pending.Healing)throw new Exception("Exactly consuming green created phantom yellow");
            report.AppendLine("PASS: inclusive 1.2s / refresh / pause while draining / lethal bypass / green stacking / green-first damage / exact green cancellation / excess-only yellow.");
            string solid=File.ReadAllText("Assets/Resources/Shaders/FullRibbonHUD.shader");
            if(solid.Contains("innerGlow")||solid.Contains("pendingHalo")||solid.Contains("_DistanceTex"))throw new Exception("Legacy mixed light paths remain in the solid shader");
            FullRibbonHUDReview.CaptureValidation(Path.Combine(output,"hud"));
            ComboReviewV11.CaptureValidation(Path.Combine(output,"combo"),true);
            File.WriteAllText(Path.Combine(output,"regression.txt"),report.ToString());
        }catch(Exception e){report.AppendLine("FAIL: "+e);File.WriteAllText(Path.Combine(output,"regression.txt"),report.ToString());Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
