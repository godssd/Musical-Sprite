using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Spine.Unity;

/// <summary>Explicit, non-mutating reproduction of the reported character stall and HP burst timing.</summary>
public static class VisualRegressionDiagnosis20261030
{
    public static void Batch(){
        string output=Path.GetFullPath("DesignPreviews/VideoReview-20261030-122324");Directory.CreateDirectory(output);
        var report=new StringBuilder();var scene=EditorSceneManager.NewPreviewScene();
        try{
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Shit/Shit.prefab");
            var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            var skel=model.GetComponentInChildren<SkeletonAnimation>(true);skel.Initialize(true);
            var driver=model.GetComponentInChildren<CharacterAnimator>(true);
            if(driver==null)driver=model.AddComponent<CharacterAnimator>();
            driver.animationPrefix="Aibo_2_Shit";driver.SendMessage("Awake");driver.SendMessage("Start");
            void Step(float dt){skel.AnimationState.Update(dt);skel.AnimationState.Apply(skel.Skeleton);driver.SendMessage("Update");}
            string State(){var cur=skel.AnimationState.GetTrack(0);return cur==null?"none":cur.Animation.Name+" loop="+cur.Loop+" complete="+cur.IsComplete+" time="+cur.TrackTime;}
            string Pose(){var s=new StringBuilder();foreach(var bone in skel.Skeleton.Bones)s.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"{0:F4}/{1:F4}/{2:F4};",bone.Pose.X,bone.Pose.Y,bone.Pose.Rotation);return s.ToString();}
            Step(2);driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayFever);Step(.1f);
            report.AppendLine("Fever: "+State());
            if(!driver.PlayOnce(CharacterAnimator.CharacterAnimationState.TargetFever))throw new Exception("Fever target fixture failed");
            Step(.8f);
            // Put the driver into a non-loop target as a fever transition does during judgement.
            driver.PlayOnce(CharacterAnimator.CharacterAnimationState.TargetFever);
            driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlaySuperFever);
            Step(1);string before=Pose();report.AppendLine("After switching to missing SuperFever: "+State());
            Step(.4f);string after=Pose();var entry=skel.AnimationState.GetTrack(0);
            bool target=driver.PlayOnce(CharacterAnimator.CharacterAnimationState.TargetSuperFever);
            report.AppendLine("Super target accepted="+target+"; applied bone pose unchanged="+(before==after));
            if(entry==null||entry.Loop||!entry.IsComplete||before!=after||target)throw new Exception("The hypothesised stall was not reproduced");
            driver.SetLoopState(CharacterAnimator.CharacterAnimationState.PlayNormal);Step(.1f);
            report.AppendLine("After return to existing Normal loop: "+State());
            if(!skel.AnimationState.GetTrack(0).Loop)throw new Exception("Normal loop failed to recover");
            report.AppendLine("PASS: missing SuperFever loop leaves a finished one-shot at its last pose; missing Super target cannot recover it; Normal state recovers.");

            var style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
            var health=new FullRibbonHealthMotion();health.Reset(1);float minimum=1;
            for(int hit=1;hit<=6;hit++){
                health.Change(1-hit*10/326f);
                for(int frame=0;frame<5;frame++){health.Tick(.03f,style);minimum=Mathf.Min(minimum,health.Cursor);}
                report.AppendLine("HP event "+hit+": target="+health.Target.ToString("F4")+" cursor="+health.Cursor.ToString("F4")+" moving="+health.Moving(style));
            }
            if(minimum<.9999f)throw new Exception("Continuous-hit cursor starvation fixture did not reproduce");
            report.AppendLine("PASS: 0.15s repeated losses keep resetting the 0.19s flash+hold, so delayed cursor never starts while hits continue.");
            health.Reset(.30f);
            for(int hit=1;hit<=4;hit++){health.Change(.30f+hit*.10f);for(int frame=0;frame<5;frame++)health.Tick(.03f,style);}
            report.AppendLine("Continuous heal: target="+health.Target.ToString("F4")+" cursor="+health.Cursor.ToString("F4"));
            if(Mathf.Abs(health.Cursor-.30f)>.0001f)throw new Exception("Repeated-heal starvation fixture did not reproduce");
            health.Reset(1);health.Change(.5f);health.Tick(.28f,style);float beforeReverse=health.Cursor;health.Change(.75f);
            report.AppendLine("Loss interrupted by heal: cursor jumps from "+beforeReverse.ToString("F4")+" to "+health.Cursor.ToString("F4")+"; actual target="+health.Target.ToString("F4"));
            if(beforeReverse-health.Cursor<.1f)throw new Exception("Reverse-event cursor jump fixture did not reproduce");
            report.AppendLine("PASS: repeated heals also starve the cursor; damage-to-heal reversal changes the cursor instantly.");
            File.WriteAllText(Path.Combine(output,"runtime-diagnosis.txt"),report.ToString());
        }catch(Exception e){report.AppendLine("FAIL: "+e);File.WriteAllText(Path.Combine(output,"runtime-diagnosis.txt"),report.ToString());Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
