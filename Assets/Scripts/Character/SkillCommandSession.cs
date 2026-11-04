using System.Collections.Generic;
using UnityEngine;

public enum SkillCommandFeedback { None, Reset, Release }

/// <summary>One team's buffer and UI lock. No rendering or character skill rules.</summary>
public sealed class SkillCommandSession {
    public readonly List<SkillInputStep> Buffer=new List<SkillInputStep>(8);
    public SkillCommandFeedback Feedback {get;private set;}
    public float FeedbackAge {get;private set;}=100;
    public float GapAge {get;private set;}=100;
    public float LockRemaining {get;private set;}
    public bool IsLocked=>LockRemaining>0;
    public void Advance(float dt){dt=Mathf.Max(0,dt);GapAge+=dt;FeedbackAge+=dt;LockRemaining=Mathf.Max(0,LockRemaining-dt);if(LockRemaining<.000001f)LockRemaining=0;}
    public bool Append(SkillInputStep step,float timeout){if(IsLocked)return false;if(Buffer.Count>0&&GapAge>timeout)Buffer.Clear();Buffer.Add(Normalize(step));GapAge=0;return true;}
    public void Lock(SkillCommandFeedback feedback,float seconds){Buffer.Clear();GapAge=100;Feedback=feedback;FeedbackAge=0;LockRemaining=Mathf.Max(0,seconds);}
    public void Clear(){Buffer.Clear();GapAge=100;}
    public void Reset(){Clear();Feedback=SkillCommandFeedback.None;FeedbackAge=100;LockRemaining=0;}
    public static SkillInputStep Normalize(SkillInputStep step)=>step==SkillInputStep.A?SkillInputStep.Left:step==SkillInputStep.B?SkillInputStep.Down:step==SkillInputStep.C?SkillInputStep.Right:step;
    public static bool IsPrefix(SkillInputStep[] sequence,List<SkillInputStep> buffer){if(sequence==null||buffer.Count>sequence.Length)return false;for(int i=0;i<buffer.Count;i++)if(Normalize(sequence[i])!=buffer[i])return false;return true;}
}
