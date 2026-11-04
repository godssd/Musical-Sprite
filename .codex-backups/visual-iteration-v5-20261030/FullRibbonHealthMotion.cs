using UnityEngine;

/// <summary>Display-only cursor. Real HP remains owned by ScoreManager.</summary>
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
    private float start;
    public void Reset(float value) {
        Target = Cursor = start = Mathf.Clamp01(value);
        Active = Danger = ZeroImpact = false; Elapsed = BurstAge = 100;
        FlashLow = FlashHigh = Target;
    }
    public void Change(float value) {
        value = Mathf.Clamp01(value);
        if(Mathf.Abs(value-Target)<.00001f)return;
        FlashLow = Mathf.Min(value,Target); FlashHigh = Mathf.Max(value,Target);
        bool nextHealing=value>Target;
        // Reverse events cancel the obsolete coloured interval. Resume from its real fill boundary,
        // rather than mislabelling a heal as yellow just because an old loss cursor is still ahead.
        if(Active&&Healing!=nextHealing)Cursor=nextHealing?Mathf.Min(Cursor,Target):Mathf.Max(Cursor,Target);
        start = Cursor; Target = value; Healing = nextHealing;
        Active = true; Elapsed = 0; ZeroImpact = false; BurstAge = 100;
    }
    public bool Moving(FullRibbonHUDStyle s) => Active && Elapsed >= s.flashDuration+s.holdDuration;
    public float Flash(FullRibbonHUDStyle s) => Active ? Mathf.Pow(1-Mathf.Clamp01(Elapsed/Mathf.Max(.001f,s.flashDuration)),.6f) : 0;
    public void Tick(float dt, FullRibbonHUDStyle s) {
        BurstAge += dt;
        if(Target<=s.dangerEnter)Danger=true;
        else if(Target>=s.dangerExit)Danger=false;
        if(!Active)return;
        Elapsed += Mathf.Max(0,dt);
        float duration = Healing?s.healDuration:s.lossDuration;
        float t = Mathf.Clamp01((Elapsed-s.flashDuration-s.holdDuration)/Mathf.Max(.01f,duration));
        Cursor = Mathf.Lerp(start,Target,1-Mathf.Pow(1-t,3));
        if(t<1)return;
        Cursor=Target; Active=false;
        if(Target<=0 && !ZeroImpact){ZeroImpact=true;BurstAge=0;}
    }
}
