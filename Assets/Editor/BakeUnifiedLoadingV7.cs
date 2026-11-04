using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BakeUnifiedLoadingV7 {
    public static void Batch(){
        const string folder="Assets/Resources/BattleVisualV11/V7Loading";Directory.CreateDirectory(folder);
        const string output="DesignPreviews/UIFxRebuildV7-20261101";Directory.CreateDirectory(output);
        var window=ScriptableObject.CreateInstance<FullRibbonHUDReview>();var type=typeof(FullRibbonHUDReview);var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        try{
            type.GetMethod("Build",flags).Invoke(window,null);var hud=(FullRibbonBattleHUD)type.GetField("hud",flags).GetValue(window);if(hud==null)throw new Exception("Cannot build loading masks");
            hud.PreviewEntrance();
            var report=new System.Text.StringBuilder();
            foreach(var group in Resources.FindObjectsOfTypeAll<FullRibbonLoadingGraphic>())if(group.gameObject.scene==hud.gameObject.scene){
                int side=group.transform.parent.name=="Team_Blue"?1:0;
                var source=group.MaskForBaking;var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;
                var copy=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
                try{Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();File.WriteAllBytes(folder+"/mask-"+side+".png",copy.EncodeToPNG());}
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
                File.WriteAllText(folder+"/key-"+side+".txt",group.BuildKey);report.AppendLine("Team "+side+" union build="+group.LastBuildMilliseconds.ToString("F2")+"ms, prior cache="+group.UsedBakedMask+", key="+group.BuildKey);
            }
            AssetDatabase.Refresh();foreach(var path in Directory.GetFiles(folder,"*.png")){
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
            }
            hud.PreviewEntrance();foreach(var group in Resources.FindObjectsOfTypeAll<FullRibbonLoadingGraphic>())if(group.gameObject.scene==hud.gameObject.scene){if(!group.UsedBakedMask)throw new Exception("Baked union key failed to reuse");report.AppendLine(group.transform.parent.name+" cached lookup="+group.LastBuildMilliseconds.ToString("F2")+"ms");}
            var style=Resources.Load<FullRibbonHUDStyle>(FullRibbonHUDStyle.ResourcePath);EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();File.WriteAllText(output+"/loading-cache.txt",report.ToString());
        }finally{UnityEngine.Object.DestroyImmediate(window);}
    }
}
