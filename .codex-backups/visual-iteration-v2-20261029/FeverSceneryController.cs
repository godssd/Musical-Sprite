using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>每方三层布景：背景常驻，过热与超级过热独立升降并同时存在。</summary>
    [DisallowMultipleComponent]
    public sealed class FeverSceneryController : MonoBehaviour
    {
        [Range(0, 1)] public int side;
        public Transform background;
        public Transform feverLayer;
        public Transform superFeverLayer;
        [Min(0.01f)] public float riseDuration = 0.5f;
        [Min(0.01f)] public float fallDuration = 0.45f;
        public Vector3 feverHiddenLocalPosition;
        public Vector3 feverShownLocalPosition;
        public Vector3 superHiddenLocalPosition;
        public Vector3 superShownLocalPosition;
        [Header("Temporary artwork framing")]
        [Range(0.1f, 1f)] public float visibleImageHeight = 0.30f;
        public Color feverTint = new Color(1f, 0.48f, 0.3f, 1f);
        public Color superTint = new Color(1f, 0.78f, 0.35f, 1f);

        [Header("过热布景定位：按场地后沿，而非固定世界高度")]
        [InspectorName("自动贴合场地后沿")] public bool fitRisingLayersToArena = true;
        [InspectorName("场地（留空自动读取中线引用）")] public Transform arenaGround;
        [InspectorName("每侧宽度占半场比例"),Range(.3f,1.2f)] public float halfArenaWidth = .96f;
        [InspectorName("后沿后退距离"),Min(0)] public float rearPadding = .08f;
        [InspectorName("根部下沉距离"),Min(0)] public float rootInset = .16f;
        [InspectorName("顶部屏幕高度上限"),Range(.6f,.98f)] public float topViewportLimit = .92f;
        [InspectorName("普通过热布景画面高度"),Range(.05f,.35f)] public float feverViewportHeight = .20f;
        [InspectorName("超级过热布景画面高度"),Range(.05f,.35f)] public float superViewportHeight = .23f;

        private FeverManager manager;
        private Coroutine riseAnimation;
        private Sprite croppedBackground;
        private Sprite croppedFever;
        private Sprite croppedSuper;
        private readonly List<GameObject> backdropObjects = new List<GameObject>();
        private readonly List<Mesh> backdropMeshes = new List<Mesh>();
        private readonly List<Material> backdropMaterials = new List<Material>();

        private void Awake()
        {
            // Also cover an already-open editor scene whose serialized instance has
            // not reloaded from disk since the framing change.
            if (Mathf.Abs(feverShownLocalPosition.y - 2.4f) < 0.01f &&
                Mathf.Abs(feverHiddenLocalPosition.y + 4f) < 0.01f)
            {
                feverShownLocalPosition.y = 2.25f;
                feverHiddenLocalPosition.y = -4.8f;
                superShownLocalPosition.y = 1.5f;
                superHiddenLocalPosition.y = -4.8f;
            }
            if (side == 1 && feverTint.r > 0.9f && feverTint.b < 0.4f)
            {
                feverTint = new Color(0.31f, 0.61f, 1f, 1f);
                superTint = new Color(0.47f, 0.91f, 1f, 1f);
            }
            ReframeScale(background);
            ReframeScale(feverLayer);
            ReframeScale(superFeverLayer);
            FaceMainCamera(background);
            FaceMainCamera(feverLayer);
            FaceMainCamera(superFeverLayer);
            // Current placeholders have artwork only in their bottom 28%. Frame that area
            // without changing the source textures, so each layer can rise visibly.
            croppedBackground = FrameSprite(background);
            croppedFever = FrameSprite(feverLayer);
            croppedSuper = FrameSprite(superFeverLayer);
            SetOrder(background, -70);
            SetOrder(feverLayer, -60);
            SetOrder(superFeverLayer, -50);
            TintLayer(feverLayer, feverTint);
            TintLayer(superFeverLayer, superTint);
            CreateBackdrop(feverLayer, false);
            CreateBackdrop(superFeverLayer, true);
            FitRisingLayers();
            SetLayerVisible(feverLayer,false);
            SetLayerVisible(superFeverLayer,false);
        }

        private void FitRisingLayers()
        {
            if(!fitRisingLayersToArena||Camera.main==null)return;
            if(arenaGround==null){var center=FindFirstObjectByType<BattleCenterLine>();if(center!=null)arenaGround=center.ground;}
            var groundRenderer=arenaGround!=null?arenaGround.GetComponent<Renderer>():null;
            if(groundRenderer==null)return; // Retain explicitly authored positions in non-arena scenes.
            Bounds ground=groundRenderer.bounds;
            float x=Mathf.Lerp(ground.min.x,ground.max.x,side==0?.25f:.75f);
            Vector3 root=new Vector3(x,ground.max.y-rootInset,ground.max.z+rearPadding);
            FitCard(feverLayer,root,ground.size.x*.5f*halfArenaWidth,feverViewportHeight,out feverShownLocalPosition,out feverHiddenLocalPosition);
            // Super layer sits in front of the first card, with a slightly lower crown.
            FitCard(superFeverLayer,root+Vector3.back*.12f,ground.size.x*.5f*halfArenaWidth*.96f,superViewportHeight,out superShownLocalPosition,out superHiddenLocalPosition);
        }

        private void FitCard(Transform card,Vector3 root,float width,float viewportHeight,out Vector3 shown,out Vector3 hidden)
        {
            shown=hidden=Vector3.zero;if(card==null)return;
            var renderer=card.GetComponent<SpriteRenderer>();if(renderer==null||renderer.sprite==null)return;
            var camera=Camera.main;
            card.rotation=camera.transform.rotation;
            Vector3 parentScale=card.parent!=null?card.parent.lossyScale:Vector3.one;
            Vector3 rootViewport=camera.WorldToViewportPoint(root);
            float leftLimit=side==0?.025f:.505f,rightLimit=side==0?.495f:.975f;
            float viewportWidth=Mathf.Max(.05f,2*Mathf.Min(rootViewport.x-leftLimit,rightLimit-rootViewport.x));
            float maximumWidth=Vector3.Distance(camera.ViewportToWorldPoint(rootViewport),camera.ViewportToWorldPoint(rootViewport+Vector3.right*viewportWidth));
            float scale=Mathf.Min(width,maximumWidth)/Mathf.Max(.001f,renderer.sprite.bounds.size.x);
            float worldHeight=Vector3.Distance(camera.ViewportToWorldPoint(rootViewport),camera.ViewportToWorldPoint(rootViewport+Vector3.up*viewportHeight));
            float scaleY=worldHeight/Mathf.Max(.001f,renderer.sprite.bounds.size.y);
            // Width belongs to this team's arena half; height has a separate explicit
            // composition control for the existing cropped billboard artwork.
            card.localScale=new Vector3(scale/Mathf.Max(.001f,Mathf.Abs(parentScale.x)),scaleY/Mathf.Max(.001f,Mathf.Abs(parentScale.y)),1);
            foreach(var mesh in card.GetComponentsInChildren<MeshFilter>(true)) {
                if(mesh.sharedMesh==null||!mesh.name.EndsWith("PaintedBackdrop"))continue;
                Bounds panel=mesh.sharedMesh.bounds,art=renderer.sprite.bounds;
                mesh.transform.localScale=new Vector3(art.size.x/Mathf.Max(.001f,panel.size.x),art.size.y/Mathf.Max(.001f,panel.size.y),1);
            }
            card.position=root;
            Bounds sprite=renderer.sprite.bounds;
            Vector3 left=card.TransformPoint(new Vector3(sprite.min.x,sprite.max.y,0));
            Vector3 right=card.TransformPoint(new Vector3(sprite.max.x,sprite.max.y,0));
            float top=Mathf.Max(camera.WorldToViewportPoint(left).y,camera.WorldToViewportPoint(right).y);
            if(top>topViewportLimit) {
                Vector3 p=camera.WorldToViewportPoint(card.position);
                Vector3 a=camera.ViewportToWorldPoint(p),b=camera.ViewportToWorldPoint(new Vector3(p.x,p.y-(top-topViewportLimit),p.z));
                card.position+=b-a;
            }
            shown=card.localPosition;
            Vector3 concealed=card.position-camera.transform.up*(worldHeight+.6f);
            hidden=card.parent!=null?card.parent.InverseTransformPoint(concealed):concealed;
        }

        private static void SetLayerVisible(Transform card,bool visible)
        {
            if(card==null)return;
            foreach(var renderer in card.GetComponentsInChildren<Renderer>(true))renderer.enabled=visible;
        }

        private void CreateBackdrop(Transform layer, bool super)
        {
            if (layer == null) return;
            var shader = Resources.Load<Shader>("Shaders/LaneGraphic");
            if (shader == null) return;
            var panel = new GameObject(super ? "SuperFever_PaintedBackdrop" : "Fever_PaintedBackdrop");
            panel.transform.SetParent(layer, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            var mesh = BuildBackdrop(side == 1, super);
            panel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(shader) { renderQueue = 3001 };
            material.SetColor("_Tint", super
                ? side == 0 ? new Color(0.59f, 0.42f, 0.43f, 1f) : new Color(0.48f, 0.53f, 0.66f, 1f)
                : side == 0 ? new Color(0.37f, 0.27f, 0.35f, 1f) : new Color(0.31f, 0.33f, 0.47f, 1f));
            var renderer = panel.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = super ? -51 : -61;
            backdropObjects.Add(panel);
            backdropMeshes.Add(mesh);
            backdropMaterials.Add(material);
        }

        private static Mesh BuildBackdrop(bool rightSide, bool super)
        {
            const int segments = 24;
            var vertices = new List<Vector3>((segments + 1) * 4);
            var colors = new List<Color>(vertices.Capacity);
            var triangles = new List<int>(segments * 18);
            for (int row = 0; row < 4; row++)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float towardOuterEdge = rightSide ? t : 1f - t;
                    float rise = towardOuterEdge * towardOuterEdge * (3f - 2f * towardOuterEdge);
                    float contour = 0.11f * Mathf.Sin(t * 14f) + 0.065f * Mathf.Sin(t * 31f);
                    float top = (super ? 1.15f : 1.4f) + (super ? 1.25f : 1.55f) * rise + contour;
                    float y = row == 0 ? 0f : row == 1 ? top * 0.47f : row == 2 ? top : top + 0.24f;
                    vertices.Add(new Vector3(Mathf.Lerp(-5.35f, 5.35f, t), y, 0f));
                    float alpha = row == 3 ? 0f : row == 2 ? 0.72f : 0.82f;
                    float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 14f);
                    colors.Add(new Color(1f, 1f, 1f, alpha * edgeFade));
                }
            }
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = row * (segments + 1) + i;
                    int b = a + segments + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
            var mesh = new Mesh { name = super ? "SuperFeverBackdropShape" : "FeverBackdropShape" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void FaceMainCamera(Transform layer)
        {
            if (layer == null || Camera.main == null) return;
            // A sprite's front is its local -Z. Match the angled battle camera so
            // its painted face, rather than an edge, is shown in Game view.
            layer.rotation = Camera.main.transform.rotation;
        }

        private static void ReframeScale(Transform layer)
        {
            if (layer == null || layer.localScale.x > 0.65f) return;
            layer.localScale = new Vector3(0.76f, 1.32f, 1f);
        }

        private static void SetOrder(Transform layer, int order)
        {
            if (layer == null) return;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.sortingOrder = order;
        }

        private Sprite FrameSprite(Transform layer)
        {
            if (layer == null) return null;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return null;
            var source = renderer.sprite;
            var rect = source.rect;
            float height = Mathf.Max(1f, rect.height * visibleImageHeight);
            var framed = Sprite.Create(source.texture,
                new Rect(rect.x, rect.y, rect.width, height),
                new Vector2(0.5f, 0f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            framed.name = source.name + "_VisibleBottom";
            renderer.sprite = framed;
            return framed;
        }

        private static void TintLayer(Transform layer, Color tint)
        {
            if (layer == null) return;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            renderer.color = tint;
            // These layers are scenery, not territory props. Keep their alpha and let
            // the ground geometry cover their lower edge naturally.
        }

        private IEnumerator Start()
        {
            if (feverLayer != null) feverLayer.localPosition = feverHiddenLocalPosition;
            if (superFeverLayer != null) superFeverLayer.localPosition = superHiddenLocalPosition;
            // Let the battle camera finish its aspect/viewport setup before final framing.
            yield return null;
            FitRisingLayers();
            while (FeverManager.Instance == null) yield return null;
            manager = FeverManager.Instance;
            manager.OnStateChanged += OnStateChanged;
            ApplyImmediately(manager.GetState(side));
        }

        private void OnDestroy()
        {
            if (manager != null) manager.OnStateChanged -= OnStateChanged;
            if (croppedBackground != null) Destroy(croppedBackground);
            if (croppedFever != null) Destroy(croppedFever);
            if (croppedSuper != null) Destroy(croppedSuper);
            foreach (var backdrop in backdropObjects) if (backdrop != null) Destroy(backdrop);
            foreach (var mesh in backdropMeshes) if (mesh != null) Destroy(mesh);
            foreach (var material in backdropMaterials) if (material != null) Destroy(material);
        }

        private void OnStateChanged(FeverState oldState, FeverState state, int changedSide)
        {
            if (changedSide != side) return;
            if (riseAnimation != null) StopCoroutine(riseAnimation);
            riseAnimation = StartCoroutine(MoveTo(state));
        }

        private void ApplyImmediately(FeverState state)
        {
            if (feverLayer != null)
                feverLayer.localPosition = state == FeverState.None ? feverHiddenLocalPosition : feverShownLocalPosition;
            if (superFeverLayer != null)
                superFeverLayer.localPosition = state == FeverState.SuperFever ? superShownLocalPosition : superHiddenLocalPosition;
            SetLayerVisible(feverLayer,state!=FeverState.None);
            SetLayerVisible(superFeverLayer,state==FeverState.SuperFever);
        }

        // 临时图片可能完全相同；这些菜单可在 Inspector 直接检查三层位置，
        // 无需先打出连击。编辑模式预览后选 Normal 可恢复隐藏位置。
        [ContextMenu("Preview/Normal")]
        private void PreviewNormal() => Preview(FeverState.None);

        [ContextMenu("Preview/Fever")]
        private void PreviewFever() => Preview(FeverState.Fever);

        [ContextMenu("Preview/Super Fever")]
        private void PreviewSuperFever() => Preview(FeverState.SuperFever);

        private void Preview(FeverState state)
        {
            if (Application.isPlaying)
            {
                if (riseAnimation != null) StopCoroutine(riseAnimation);
                riseAnimation = StartCoroutine(MoveTo(state));
            }
            else ApplyImmediately(state);
        }

        private IEnumerator MoveTo(FeverState state)
        {
            if(state!=FeverState.None)SetLayerVisible(feverLayer,true);
            if(state==FeverState.SuperFever)SetLayerVisible(superFeverLayer,true);
            Vector3 feverFrom = feverLayer != null ? feverLayer.localPosition : Vector3.zero;
            Vector3 superFrom = superFeverLayer != null ? superFeverLayer.localPosition : Vector3.zero;
            Vector3 feverTo = state == FeverState.None ? feverHiddenLocalPosition : feverShownLocalPosition;
            Vector3 superTo = state == FeverState.SuperFever ? superShownLocalPosition : superHiddenLocalPosition;
            float duration = state == FeverState.None ? fallDuration : riseDuration;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float k = Mathf.Clamp01(elapsed / duration);
                k = 1f - Mathf.Pow(1f - k, 3f);
                if (feverLayer != null) feverLayer.localPosition = Vector3.LerpUnclamped(feverFrom, feverTo, k);
                if (superFeverLayer != null) superFeverLayer.localPosition = Vector3.LerpUnclamped(superFrom, superTo, k);
                yield return null;
            }
            if (feverLayer != null) feverLayer.localPosition = feverTo;
            if (superFeverLayer != null) superFeverLayer.localPosition = superTo;
            SetLayerVisible(feverLayer,state!=FeverState.None);
            SetLayerVisible(superFeverLayer,state==FeverState.SuperFever);
            riseAnimation = null;
        }
    }
}
