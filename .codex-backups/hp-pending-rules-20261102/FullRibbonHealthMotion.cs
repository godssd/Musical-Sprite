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
    private float start, travelAge, flashAge=100, waitRemaining, velocity;
    private bool waitUninitialized, handoff, kickPending;
    private float handoffFrom, handoffTarget, handoffAge;

    public void Reset(float value){
        Target=Cursor=start=Mathf.Clamp01(value);
        Active=Danger=ZeroImpact=handoff=waitUninitialized=Healing=false;
        Elapsed=BurstAge=flashAge=100;travelAge=waitRemaining=velocity=0;
        FlashLow=FlashHigh=Target;kickPending=false;
    }
    public void Change(float value){
        value=Mathf.Clamp01(value);if(Mathf.Abs(value-Target)<.00001f)return;
        float oldTarget=Target;bool healing=value>oldTarget;
        FlashLow=Mathf.Min(value,oldTarget);FlashHigh=Mathf.Max(value,oldTarget);flashAge=0;
        if(!Active){Elapsed=0;waitUninitialized=true;velocity=0;kickPending=true;}
        else if(healing!=Healing){
            FlashLow=Mathf.Min(FlashLow,Cursor);FlashHigh=Mathf.Max(FlashHigh,Cursor);
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
            Damp(handoffTarget,used,duration);
            waitRemaining=Mathf.Max(0,waitRemaining-used);
            if(handoffAge<duration)return;
            handoff=false;Cursor=handoffTarget;start=Cursor;travelAge=0;velocity=0;kickPending=true;
        }
        float held=Mathf.Min(available,waitRemaining);waitRemaining-=held;available-=held;
        if(available<=0)return;
        travelAge+=available;
        float durationSeconds=Healing?s.healDuration:s.lossDuration;
        if(kickPending){velocity=(Target-Cursor)*6f/Mathf.Max(.04f,durationSeconds);kickPending=false;}
        Damp(Target,available,durationSeconds);
        if(travelAge<durationSeconds && (Mathf.Abs(Cursor-Target)>.0001f||Mathf.Abs(velocity)>.003f))return;
        Cursor=Target;velocity=0;Active=false;
        if(Target<=0&&!ZeroImpact){ZeroImpact=true;BurstAge=0;}
    }
    // Analytic critically damped spring: exact for a constant target at any dt.
    // Initial velocity is matched to exponential ease-out: fast immediately, then slow.
    // Same-direction HP events retain velocity, so rapid notes never restart the ease.
    private void Damp(float goal,float dt,float seconds){
        float omega=6f/Mathf.Max(.04f,seconds),delta=Cursor-goal;
        float j=velocity+omega*delta,decay=Mathf.Exp(-omega*dt);
        float next=goal+(delta+j*dt)*decay;
        velocity=(velocity-omega*j*dt)*decay;
        if((Cursor-goal)*(next-goal)<0){next=goal;velocity=0;}
        Cursor=Mathf.Clamp01(next);
    }

}
