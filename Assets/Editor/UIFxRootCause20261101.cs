using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Isolated ablation evidence only. Never installs a diagnostic material in the game.</summary>
public static class UIFxRootCause20261101 {
    public static void Batch(){
        string output=Path.GetFullPath("DesignPreviews/UIFxRootCause-20261101");Directory.CreateDirectory(output);
        string folder="Assets/Editor/Diagnostics/UIFxRootCause";Directory.CreateDirectory(folder);
        string source=File.ReadAllText("Assets/Resources/Shaders/FullRibbonHUD.shader");
        if(!source.Contains("float pendingHalo="))throw new InvalidOperationException("This ablation targets the archived V6 shader; V7 removed those layers.");
        string[] names={"00-current","01-no-band-halo","02-no-band-light","03-no-legacy-inner-glow"};
        for(int mode=0;mode<names.Length;mode++){
            string shader=source.Replace("MusicalSprite/UI/FullRibbonHUD","Hidden/Diagnostics/UIFxAblation"+mode);
            if(mode>=1)shader=shader.Replace("float pendingHalo=interval*pendingSoft*_PendingGlow*.8;","float pendingHalo=0;");
            if(mode>=2)shader=shader.Replace("float bandLight=_PendingInnerLight*(.30*exp(-crossSection*crossSection*5)+rim*.45);","float bandLight=0;");
            if(mode>=3)shader=shader.Replace("rgb=lerp(rgb,_FlashColor.rgb,innerGlow);","// legacy inner glow disabled for diagnosis");
            File.WriteAllText(folder+"/Ablation"+mode+".shader",shader);
        }
        AssetDatabase.Refresh();
        var type=typeof(FullRibbonHUDReview);var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var window=ScriptableObject.CreateInstance<FullRibbonHUDReview>();
        try{
            type.GetMethod("Build",flags).Invoke(window,null);
            var hud=(FullRibbonBattleHUD)type.GetField("hud",flags).GetValue(window);
            if(hud==null)throw new Exception("Diagnostic preview failed");
            var style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);
            for(int mode=0;mode<names.Length;mode++){
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(folder+"/Ablation"+mode+".shader");
                if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Diagnostic shader failed at "+mode);
                foreach(bool healing in new[]{false,true}){
                    hud.PreviewReset(healing?.35f:1);hud.PreviewChange(healing?.75f:.5f);hud.Advance(.30f);
                    // The HUD canvas is a separate root, scoped by the preview scene.
                    foreach(var image in Resources.FindObjectsOfTypeAll<FullRibbonImage>())if(image.gameObject.scene==hud.gameObject.scene){
                        // Runtime uniforms are not serialized shader Properties; changing
                        // shader alone discards them. Rebind exactly as the runtime builder.
                        var old=image.InstanceMaterial;image.Configure(image.artwork,shader);UnityEngine.Object.DestroyImmediate(old);
                        int side=image.transform.parent.name=="Team_Blue"?1:0;
                        var m=image.InstanceMaterial;m.SetColor("_Tint",Color.white);m.SetFloat("_Brightness",1);m.SetFloat("_Load",2);
                        bool lettering=image.name=="HP"||image.name.StartsWith("ScoreDigit_");
                        m.SetFloat("_Monochrome",lettering?1:0);
                        Color Linear(Color c)=>QualitySettings.activeColorSpace==ColorSpace.Linear?c.linear:c;
                        if(lettering)m.SetColor("_Tint",Linear(style.letteringColor));
                        if(image.name=="AvatarLoadingShape")m.SetColor("_Tint",Linear(new Color(.13f,.17f,.19f,1)));
                        m.SetFloat("_Flip",side);m.SetColor("_FlashColor",Linear(side==0?new Color(1,.94f,.91f):new Color(.84f,.97f,1)));
                        m.SetFloat("_Identity",image.name=="AvatarLoadingShape"||image.name=="AvatarFrame"||image.name=="OuterLeaves"||image.name=="OuterDot"?1:0);
                        m.SetFloat("_Mode",image.name=="BloodRibbon"?1:0);
                        if(image.name=="BloodRibbon"){
                            m.SetTexture("_CurveTex",style.ribbonProfiles[side]);m.SetFloat("_HasProfile",1);m.SetVector("_MeterRange",new Vector4(.066f,.98f,0,0));
                        }
                    }
                    hud.Advance(0);
                    type.GetMethod("Capture",flags).Invoke(window,new object[]{output,(healing?"heal-":"loss-")+names[mode]+".png"});
                    var check=new Texture2D(2,2);check.LoadImage(File.ReadAllBytes(Path.Combine(output,(healing?"heal-":"loss-")+names[mode]+".png")));
                    int red=0;for(int y=40;y<100;y++)for(int x=180;x<350;x++){var c=check.GetPixel(x,y);if(c.r>.6f&&c.r>c.g*1.3f&&c.r>c.b*1.3f)red++;}
                    UnityEngine.Object.DestroyImmediate(check);if(red<400)throw new Exception("Ablation lost the bar's material bindings");
                }
            }
            File.WriteAllText(Path.Combine(output,"ablation.txt"),"PASS: same production renderer, artwork, camera, time and palette; only diagnostic shader terms cumulatively disabled. 00 current; 01 remove full-band outer halo; 02 also remove band centre/rim light; 03 also remove legacy inner glow. Diagnostic alternatives are evidence, not approved replacement designs. No runtime shader, style or scene changed.");
        }finally{UnityEngine.Object.DestroyImmediate(window);}
    }
}
