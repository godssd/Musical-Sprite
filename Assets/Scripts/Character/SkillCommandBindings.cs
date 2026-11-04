using UnityEngine;

/// <summary>Semantic commands survive key changes; saving is explicit and optional for tests.</summary>
public sealed class SkillCommandBindings {
    const string Prefix="MusicalSprite.SkillCommand.";
    readonly KeyCode[] defaults={KeyCode.LeftArrow,KeyCode.DownArrow,KeyCode.RightArrow};
    readonly KeyCode[] keys=new KeyCode[3];
    public SkillCommandBindings(){for(int i=0;i<3;i++){int value=PlayerPrefs.GetInt(Prefix+i,(int)defaults[i]);keys[i]=Valid((KeyCode)value)?(KeyCode)value:defaults[i];}for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)if(keys[i]==keys[j]){System.Array.Copy(defaults,keys,3);return;}}
    public KeyCode Get(int command)=>keys[Mathf.Clamp(command,0,2)];
    static bool Valid(KeyCode key)=>key!=KeyCode.None&&System.Enum.IsDefined(typeof(KeyCode),key)&&!(key>=KeyCode.Mouse0&&key<=KeyCode.Mouse6)&&key<KeyCode.JoystickButton0;
    public bool TrySet(int command,KeyCode key,bool persist=true){if(command<0||command>2)return false;var values=(KeyCode[])keys.Clone();values[command]=key;return TrySetAll(values,persist);}
    public bool TrySetAll(KeyCode[] values,bool persist=true){
        if(values==null||values.Length!=3)return false;for(int i=0;i<3;i++){if(!Valid(values[i]))return false;for(int j=0;j<i;j++)if(values[i]==values[j])return false;}
        System.Array.Copy(values,keys,3);if(persist){for(int i=0;i<3;i++)PlayerPrefs.SetInt(Prefix+i,(int)keys[i]);PlayerPrefs.Save();}return true;
    }
    public void Reset(bool persist=true){System.Array.Copy(defaults,keys,3);if(persist){for(int i=0;i<3;i++)PlayerPrefs.DeleteKey(Prefix+i);PlayerPrefs.Save();}}
}
