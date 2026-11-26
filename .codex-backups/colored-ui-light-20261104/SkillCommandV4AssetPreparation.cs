using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in import only. Does not replace runtime styles or save scenes.</summary>
public static class SkillCommandV4AssetPreparation {
    public static void Activate(){
        Prepare();var style=Resources.Load<SkillCommandStyle>(SkillCommandStyle.Path);
        if(style==null)throw new Exception("Skill command style missing");
        style.artworkFrame=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/SkillCommandsV4/command-frame.png");
        var sprites=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/SkillCommandsV4/command-icons.png").OfType<Sprite>().ToArray();
        style.icons=new[]{sprites.Single(s=>s.name=="courage"),sprites.Single(s=>s.name=="guardian"),sprites.Single(s=>s.name=="tenacity")};
        style.useSemanticArtwork=true;style.cream=Color.white;style.artworkHeight=600;style.iconSize=112;
        style.artworkIconUV=new[]{new Vector2(.504f,.818f),new Vector2(.482f,.500f),new Vector2(.449f,.206f)};
        style.pressGlow=.8f;style.releaseGlow=2.4f;EditorUtility.SetDirty(style);AssetDatabase.SaveAssets();
    }
    public static void Prepare(){
        const string root="Assets/Art/UI/SkillCommandsV4/";
        foreach(string name in new[]{"command-frame","command-icons"}){
            string path=root+name+".png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=100;
            importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;
            importer.spriteImportMode=name=="command-frame"?SpriteImportMode.Single:SpriteImportMode.Multiple;
            if(name=="command-icons"){
                var data=new SpriteMetaData[3];string[] names={"courage","guardian","tenacity"};
                for(int i=0;i<3;i++)data[i]=new SpriteMetaData{name=names[i],rect=new Rect(i*724,0,724,724),alignment=(int)SpriteAlignment.Center,pivot=new Vector2(.5f,.5f)};
#pragma warning disable 0618
                importer.spritesheet=data;
#pragma warning restore 0618
            }
            importer.SaveAndReimport();
        }
        var sprites=AssetDatabase.LoadAllAssetsAtPath(root+"command-icons.png").OfType<Sprite>().ToArray();
        if(sprites.Length!=3||sprites.Any(s=>s.rect.width!=724||s.rect.height!=724))throw new Exception("V4 icon slicing failed");
        if(AssetDatabase.LoadAssetAtPath<Sprite>(root+"command-frame.png")==null)throw new Exception("V4 frame missing");
        File.WriteAllText("DesignPreviews/SkillCommands-Motion-20261103/import-validation.txt","PASS: transparent RGBA frame imported at 724x2172, uncompressed; three named 724x724 icon sprites imported from 2172x724 atlas; sRGB, bilinear, no mipmaps. Runtime style and scene untouched. Motion board is concept art, not implemented animation.");
    }
}
