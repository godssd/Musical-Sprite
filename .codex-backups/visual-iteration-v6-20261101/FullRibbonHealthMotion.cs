using UnityEngine;

/// <summary>Real HP target, event flash and continuous display cursor have independent clocks.</summary>
public sealed class FullRibbonHealthMotion
{
    public float Target { get; private set; } = 1;
    public float Cursor { get; private set; } = 1;
    public float FlashLow { get; private set; }
    public float FlashHigh { get; private set; }
    public float Elapsed { get; private set; } = 100;
    public bool Active { get; private set; }
    public bool Healing { get; private set; }
    public bool Danger { get; private set; }
    public bool ZeroImpact { get; private set; }
    public float BurstAge { get; private set; } = 100;
    private float start, travelAge, flashAge=100, waitRemaining;
    private bool waitUninitialized, handoff;
    private float handoffFrom, handoffTarget, handoffAge;

    public void Reset(float value){
        Target=Cursor=start=Mathf.Clamp01(value);
        Active=Danger=ZeroImpact=handoff=waitUninitialized=false;
        Elapsed=BurstAge=flashAge=100;travelAge=waitRemaining=0;
        FlashLow=FlashHigh=Target;
    }
    public void Change(float value){
        value=Mathf.Clamp01(value);if(Mathf.Abs(value-Target)<.00001f)return;
        float oldTarget=Target;bool healing=value>oldTarget;
        FlashLow=Mathf.Min(value,oldTarget);FlashHigh=Mathf.Max(value,oldTarget);flashAge=0;
        if(!Active){Elapsed=0;waitUninitialized=true;}
        else if(healing!=Healing){
            // Reconcile the old display during the new local white flash, never
            // teleport Cursor. This also makes the following interval use the
            // correct event direction, rather than inventing yellow healing.
            handoff=true;handoffAge=0;handoffFrom=Cursor;handoffTarget=oldTarget;
        }
        start=Cursor;travelAge=0;Target=value;Healing=healing;
        Active=true;ZeroImpact=false;BurstAge=100;
    }
    public bool Moving(FullRibbonHUDStyle s)=>Active&&!waitUninitialized&&waitRemaining<=0&&!handoff;
    public float Flash(FullRibbonHUDStyle s){
        // A readable peak followed by one fade, independent from the cursor hold.
        float peak=Mathf.Min(.025f,s.flashDuration*.25f);
        return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(peak,Mathf.Max(peak+.001f,s.flashDuration),flashAge));
    }
    public void Tick(float dt,FullRibbonHUDStyle s){
        dt=Mathf.Max(0,dt);BurstAge+=dt;flashAge+=dt;
        if(Target<=s.dangerEnter)Danger=true;else if(Target>=s.dangerExit)Danger=false;
        if(!Active)return;Elapsed+=dt;
        if(waitUninitialized){waitRemaining=s.flashDuration+s.holdDuration;waitUninitialized=false;}
        float available=dt;
        if(handoff){
            float duration=Mathf.Clamp(s.flashDuration,.04f,.12f),used=Mathf.Min(available,Mathf.Max(0,duration-handoffAge));
            handoffAge+=used;available-=used;
            Cursor=Mathf.Lerp(handoffFrom,handoffTarget,Mathf.SmoothStep(0,1,handoffAge/duration));
            waitRemaining=Mathf.Max(0,waitRemaining-used);
            if(handoffAge<duration)return;
            handoff=false;start=Cursor;travelAge=0;
        }
        float held=Mathf.Min(available,waitRemaining);waitRemaining-=held;available-=held;
        if(available<=0)return;
        travelAge+=available;
        float durationSeconds=Healing?s.healDuration:s.lossDuration;
        float t=Mathf.Clamp01(travelAge/Mathf.Max(.01f,durationSeconds));
        Cursor=Mathf.Lerp(start,Target,1-Mathf.Pow(1-t,3));
        if(t<1)return;Cursor=Target;Active=false;
        if(Target<=0&&!ZeroImpact){ZeroImpact=true;BurstAge=0;}
    }
}
