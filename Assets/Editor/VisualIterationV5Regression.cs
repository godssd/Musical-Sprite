using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Spine.Unity;

public static class VisualIterationV5Regression
{
    public static void Batch(){
        string output=Path.GetFullPath("DesignPreviews/VisualIterationV5-20261030");Directory.CreateDirectory(output);
        var report=new StringBuilder();var scene=EditorSceneManager.NewPreviewScene();
        try{
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Shit/Shit.prefab");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var skel=model.GetComponentInChildren<SkeletonAnimation>(true);skel.Initialize(true);
            var driver=model.GetComponentInChildren<CharacterAnimator>(true);if(driver==null)driver=model.AddComponent<CharacterAnimator>();
            driver.animationPrefix="Aibo_2_Shit";driver.SendMessage("Awake");driver.SendMessage("Start");
            void Step(float dt){skel.AnimationState.Update(dt);skel.AnimationState.Apply(skel.Skeleton);driver.SendMessage("Update");}
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
                if(m.Cursor>.99f)throw new Exception("Repeated loss starves cursor at "+interval);
                m.Tick(2,style);if(Mathf.Abs(m.Cursor-.68f)>.0001f||m.Active)throw new Exception("Loss failed to settle");
                m.Reset(.2f);for(int i=1;i<=8;i++){m.Change(.2f+i*.06f);m.Tick(interval,style);}
                if(m.Cursor<.21f)throw new Exception("Repeated healing starves cursor at "+interval);
                m.Tick(2,style);if(Mathf.Abs(m.Cursor-.68f)>.0001f||m.Active)throw new Exception("Heal failed to settle");
                report.AppendLine("PASS: continuous losses/heals, interval="+interval+", finite settlement and no event-time jumps.");
            }
            var motion=new FullRibbonHealthMotion();motion.Reset(1);motion.Change(.5f);motion.Tick(.29f,style);
            float old=motion.Cursor;motion.Change(.75f);if(motion.Cursor!=old)throw new Exception("Loss-to-heal reversal jumps");
            motion.Tick(2,style);if(Mathf.Abs(motion.Cursor-.75f)>.0001f)throw new Exception("Reversed healing did not settle");
            motion.Change(.9f);motion.Tick(.25f,style);old=motion.Cursor;motion.Change(.1f);if(motion.Cursor!=old)throw new Exception("Heal-to-loss reversal jumps");
            motion.Tick(2,style);motion.Change(0);motion.Tick(style.flashDuration+style.holdDuration,style);
            if(motion.ZeroImpact||motion.Cursor<=0)throw new Exception("Zero explosion occurs before cursor drain");
            motion.Tick(style.lossDuration+.01f,style);if(!motion.ZeroImpact||motion.Cursor!=0)throw new Exception("Zero impact missing");
            report.AppendLine("PASS: both reversal directions, white-flash peak, and zero impact after cursor reaches zero.");
            FourVisualCorrectionsReview.BatchV5();
            File.WriteAllText(Path.Combine(output,"regression.txt"),report.ToString());
        }catch(Exception e){report.AppendLine("FAIL: "+e);File.WriteAllText(Path.Combine(output,"regression.txt"),report.ToString());Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
