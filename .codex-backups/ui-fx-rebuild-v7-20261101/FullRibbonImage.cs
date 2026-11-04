using UnityEngine;
using UnityEngine.UI;

/// <summary>One unwarped sprite with padded geometry for an external light halo.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class FullRibbonImage : MaskableGraphic
{
    public Sprite artwork;
    public float padding = 14;
    public Material InstanceMaterial { get; private set; }
    public override Texture mainTexture => artwork != null ? artwork.texture : Texture2D.whiteTexture;
    public void Configure(Sprite sprite, Shader shader) {
        artwork = sprite;
        InstanceMaterial = new Material(shader){ hideFlags=HideFlags.HideAndDontSave };
        material=InstanceMaterial;raycastTarget=false;
        Rect r=sprite.rect;
        InstanceMaterial.SetVector("_SpriteUV",new Vector4(r.x/sprite.texture.width,r.y/sprite.texture.height,r.width/sprite.texture.width,r.height/sprite.texture.height));
        var distance=Resources.Load<Texture2D>("BattleVisualV11/MasterV6Glow/"+sprite.texture.name);
        if(distance!=null){
            InstanceMaterial.SetTexture("_DistanceTex",distance);InstanceMaterial.SetFloat("_HasDistance",1);
            InstanceMaterial.SetVector("_DistanceUV",new Vector4(128f/distance.width,128f/distance.height,r.width/distance.width,r.height/distance.height));
        }
        SetAllDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();Rect r=GetPixelAdjustedRect();
        float px=padding/Mathf.Max(1,r.width),py=padding/Mathf.Max(1,r.height);
        vh.AddVert(new Vector3(r.xMin-padding,r.yMin-padding),color,new Vector2(-px,-py));
        vh.AddVert(new Vector3(r.xMin-padding,r.yMax+padding),color,new Vector2(-px,1+py));
        vh.AddVert(new Vector3(r.xMax+padding,r.yMax+padding),color,new Vector2(1+px,1+py));
        vh.AddVert(new Vector3(r.xMax+padding,r.yMin-padding),color,new Vector2(1+px,-py));
        vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);
    }
    protected override void OnDestroy() {
        if(InstanceMaterial!=null){if(Application.isPlaying)Destroy(InstanceMaterial);else DestroyImmediate(InstanceMaterial);}
        base.OnDestroy();
    }
}
