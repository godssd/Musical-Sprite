using UnityEngine;

/// <summary>Skill-specific .04 compression / .10 rebound timing; preserves current velocity on retrigger.</summary>
public sealed class SkillCommandPulse {
    public float Scale {get;private set;}=1;
    float age=100,velocity,peak=1.07f,minimum=.91f,duration=.26f;
    public void Trigger(float maximum,float compressed,float seconds){peak=maximum;minimum=compressed;duration=Mathf.Max(.12f,seconds);age=0;}
    public void Reset(){Scale=1;age=100;velocity=0;}
    public void Advance(float dt){
        float remaining=Mathf.Max(0,dt);
        while(age<duration&&remaining>0){
            float boundary=age<.04f?.04f:age<.10f?.10f:duration;
            float target=age<.04f?minimum:age<.10f?peak:1;
            float omega=age<.04f?110:age<.10f?85:30;
            float step=Mathf.Min(remaining,boundary-age),delta=Scale-target,j=velocity+omega*delta,e=Mathf.Exp(-omega*step);
            Scale=target+(delta+j*step)*e;velocity=(velocity-omega*j*step)*e;remaining-=step;age+=step;
            if(boundary-age<.000001f)age=boundary;
            if(age>=duration)Reset();
        }
    }
}
