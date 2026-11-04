using UnityEditor;
using UnityEngine;

/// <summary>Full transparent production layers; never apply the legacy bottom-30% crop.</summary>
public sealed class FeverSceneryArtworkImporter : AssetPostprocessor
{
    private void OnPreprocessTexture() {
        if(!assetPath.StartsWith("Assets/Resources/BattleVisualV11/FeverSceneryV2/"))return;
        var importer=(TextureImporter)assetImporter;
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
        importer.spritePixelsPerUnit=100;importer.spritePivot=new Vector2(.5f,0);
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
        settings.spriteAlignment=(int)SpriteAlignment.BottomCenter;settings.spriteMeshType=SpriteMeshType.FullRect;importer.SetTextureSettings(settings);
        importer.alphaIsTransparency=true;importer.sRGBTexture=true;importer.mipmapEnabled=true;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;
    }
}
