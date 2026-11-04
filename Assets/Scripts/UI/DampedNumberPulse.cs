using UnityEngine;

/// <summary>Compress, expand, then damp to rest. Phase boundaries are integrated exactly.</summary>
public sealed class DampedNumberPulse {
    public float Scale { get; private set; } = 1;
    public bool Active { get; private set; }
    float age, velocity, peak=1.13f, minimum=.88f, speed=1;
    const float PressEnd=.04f, ExpandEnd=.11f, End=.42f;
    public void Trigger(float maximum=1.13f,float compressed=.88f,float seconds=.42f){peak=maximum;minimum=compressed;speed=End/Mathf.Max(.12f,seconds);age=0;Active=true;}
    public void Reset(){Scale=1;age=velocity=0;Active=false;}
    public void Advance(float dt){
        float remaining=Mathf.Max(0,dt)*speed;
        while(Active&&remaining>0){
            float boundary=age<PressEnd?PressEnd:age<ExpandEnd?ExpandEnd:End;
            float goal=age<PressEnd?minimum:age<ExpandEnd?peak:1;
            float omega=age<PressEnd?95:age<ExpandEnd?65:25;
            float step=Mathf.Min(remaining,boundary-age),delta=Scale-goal,j=velocity+omega*delta,e=Mathf.Exp(-omega*step);
            Scale=goal+(delta+j*step)*e;velocity=(velocity-omega*j*step)*e;
            remaining-=step;age+=step;
            if(age>=End){Reset();return;}
            // Assign the exact boundary to avoid a tiny remainder loop.
            if(boundary-age<.000001f)age=boundary;
        }
    }
}
