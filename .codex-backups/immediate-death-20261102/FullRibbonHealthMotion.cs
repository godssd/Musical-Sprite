using UnityEngine;

/// <summary>Actual HP, pending HP and event flash have independent clocks.</summary>
public sealed class FullRibbonHealthMotion {
    public const float LossPauseSeconds=1.2f;
    public float Target {get;private set;}=1;
    public float Cursor {get;private set;}=1;
    public float FlashLow {get;private set;}
    public float FlashHigh {get;private set;}
    public float Elapsed {get;private set;}=100;
    public bool Active {get;private set;}
    // Colour describes the remaining interval, not the most recent event.
    public bool Healing=>Target>Cursor+.00001f;
    public bool Danger {get;private set;}
    public bool ZeroImpact {get;private set;}
    public float BurstAge {get;private set;}=100;
    public float LossPauseRemaining=>!Healing&&!handoff?(float)waitRemaining:0;
    float travelAge,flashAge=100,velocity,handoffTarget,handoffAge;
    double waitRemaining;
    bool initializeHealWait,handoff,kickPending;

    public void Reset(float value){
        Target=Cursor=Mathf.Clamp01(value);FlashLow=FlashHigh=Target;
        Active=Danger=ZeroImpact=initializeHealWait=handoff=kickPending=false;
        Elapsed=BurstAge=flashAge=100;travelAge=velocity=handoffAge=0;waitRemaining=0;
    }
    public void Change(float value){
        value=Mathf.Clamp01(value);if(Mathf.Abs(value-Target)<.00001f)return;
        float oldTarget=Target;bool gained=value>oldTarget,wasActive=Active,wasGreen=Healing;
        FlashLow=Mathf.Min(value,oldTarget);FlashHigh=Mathf.Max(value,oldTarget);flashAge=0;
        Target=value;Elapsed=travelAge=0;ZeroImpact=false;BurstAge=100;
        if(!gained){
            initializeHealWait=false;handoff=false;
            if(wasGreen&&value>=Cursor){
                // Damage consumes only uncommitted green HP. The solid boundary
                // stays where it is; any remaining green continues to settle.
                waitRemaining=0;
                if(value==Cursor){Finish();return;}
                Active=true;kickPending=velocity<=0;return;
            }
            // An existing yellow drain stops at its current position immediately.
            // Every further loss restarts the entire inclusive 1.2s pause.
            Active=true;waitRemaining=value<=0?0:LossPauseSeconds;
            velocity=0;kickPending=true;
        }else{
            Active=true;
            if(!wasActive){initializeHealWait=true;velocity=0;kickPending=true;}
            else if(!wasGreen&&!handoff){
                // Keep the established recovery handoff for healing during yellow.
                // First reconcile the old pending loss, then show the new heal.
                handoff=true;handoffTarget=oldTarget;handoffAge=0;
                waitRemaining=0;initializeHealWait=false;
            }
            // Additional heals extend Target without restarting green motion/wait.
        }
        if(value<=0){waitRemaining=0;initializeHealWait=handoff=false;}
        if(Mathf.Abs(Cursor-Target)<.00001f)Finish();
    }
    public bool Moving(FullRibbonHUDStyle s)=>Active&&travelAge>0&&!initializeHealWait&&waitRemaining<=0&&!handoff;
    public float Flash(FullRibbonHUDStyle s){
        float peak=Mathf.Min(.025f,s.flashDuration*.25f);
        return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(peak,Mathf.Max(peak+.001f,s.flashDuration),flashAge));
    }
    public void Tick(float dt,FullRibbonHUDStyle s){
        dt=Mathf.Max(0,dt);BurstAge+=dt;flashAge+=dt;
        if(Target<=s.dangerEnter)Danger=true;else if(Target>=s.dangerExit)Danger=false;
        if(!Active)return;Elapsed+=dt;
        if(initializeHealWait){waitRemaining=s.flashDuration+s.holdDuration;initializeHealWait=false;}
        float available=dt;
        if(handoff){
            float duration=Mathf.Clamp(s.flashDuration,.04f,.12f),used=Mathf.Min(available,Mathf.Max(0,duration-handoffAge));
            handoffAge+=used;available-=used;Damp(handoffTarget,used,duration);
            if(handoffAge<duration)return;
            handoff=false;Cursor=handoffTarget;travelAge=velocity=0;kickPending=true;
        }
        double held=System.Math.Min(available,waitRemaining);waitRemaining-=held;available-=(float)held;
        if(available<=.000001f)return;
        travelAge+=available;
        float seconds=Healing?s.healDuration:s.lossDuration;
        if(kickPending){velocity=(Target-Cursor)*6f/Mathf.Max(.04f,seconds);kickPending=false;}
        Damp(Target,available,seconds);
        if(travelAge<seconds&&(Mathf.Abs(Cursor-Target)>.0001f||Mathf.Abs(velocity)>.003f))return;
        Finish();
    }
    void Finish(){
        Cursor=Target;velocity=0;Active=handoff=initializeHealWait=kickPending=false;waitRemaining=0;
        if(Target<=0&&!ZeroImpact){ZeroImpact=true;BurstAge=0;}
    }
    // Analytic critically damped ease-out. Pending losses restart only their pause;
    // accumulated green keeps its continuous position and velocity.
    void Damp(float goal,float dt,float seconds){
        float omega=6f/Mathf.Max(.04f,seconds),delta=Cursor-goal,j=velocity+omega*delta,decay=Mathf.Exp(-omega*dt);
        float next=goal+(delta+j*dt)*decay;velocity=(velocity-omega*j*dt)*decay;
        if((Cursor-goal)*(next-goal)<0){next=goal;velocity=0;}
        Cursor=Mathf.Clamp01(next);
    }
}
