using UnityEngine;
using System.Collections;

/// <summary>
/// 控制音符从生成点沿轨道移动到判定线，并在越过中线后变为可见。
/// 命中时：变黑方块 → 白方块 → 变大 → 消失。
/// </summary>
[RequireComponent(typeof(Note))]
public class NoteMover : MonoBehaviour
{
    private Note note;
    private Conductor conductor;
    private BattleCenterLine centerLine;

    private Vector3 spawnPos;
    private Vector3 hitPos;
    private float hitTime;
    private float leadTime;
    private float startTime;

    private MeshRenderer rend;
    private Material noteMaterial;
    private Color originalColor;

    private bool animationPlaying = false;

    public void Init(Vector3 spawnPos, Vector3 hitPos, float hitTime, float leadTime,
                     Conductor conductor, BattleCenterLine centerLine)
    {
        this.spawnPos = spawnPos;
        this.hitPos = hitPos;
        this.hitTime = hitTime;
        this.leadTime = leadTime;
        this.conductor = conductor;
        this.centerLine = centerLine;

        note = GetComponent<Note>();
        note.hitTime = hitTime;
        startTime = hitTime - leadTime;

        rend = GetComponentInChildren<MeshRenderer>();
        if (rend != null)
        {
            // 复制一份材质实例，避免影响其他音符
            noteMaterial = new Material(rend.material);
            rend.material = noteMaterial;
            originalColor = Color.black;
            noteMaterial.color = Color.black;
            // 初始不可见：越过粉杠前完全隐藏
            rend.enabled = false;
        }
    }

    void Update()
    {
        if (conductor == null || animationPlaying) return;

        // 按歌曲时间做线性插值
        float t = (conductor.songPosition - startTime) / leadTime;
        t = Mathf.Clamp01(t);
        transform.position = Vector3.Lerp(spawnPos, hitPos, t);

        // 过中线后变为可见
        float centerX = centerLine != null ? centerLine.currentX : 0f;
        bool shouldBeVisible = false;

        if (note.side == 0) // 左玩家：从右向左飞，X < 中线时进入自己半场
        {
            shouldBeVisible = transform.position.x < centerX;
        }
        else // 右玩家：从左向右飞，X > 中线时进入自己半场
        {
            shouldBeVisible = transform.position.x > centerX;
        }

        if (shouldBeVisible && !note.isVisible)
        {
            note.isVisible = true;
            if (rend != null) rend.enabled = true;
            SetAlpha(1f);
        }
    }

    /// <summary>
    /// 播放命中反馈：变黑 → 白 → 变大 → 消失。
    /// </summary>
    public void PlayHitAnimation(string rank)
    {
        if (animationPlaying) return;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(HitCoroutine(rank));
    }

    /// <summary>
    /// 播放 Miss 消失动画。
    /// </summary>
    public void PlayMissAnimation()
    {
        if (animationPlaying) return;
        animationPlaying = true;
        StopAllCoroutines();
        StartCoroutine(MissCoroutine());
    }

    private IEnumerator HitCoroutine(string rank)
    {
        float duration = 0.25f;
        float timer = 0f;

        // 第一阶段：变黑（强调被击中）
        if (noteMaterial != null)
        {
            noteMaterial.color = Color.black;
        }

        yield return new WaitForSeconds(0.05f);

        // 第二阶段：变白并放大
        Vector3 startScale = transform.localScale;
        Vector3 endScale = startScale * 1.4f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                noteMaterial.color = Color.Lerp(Color.white, new Color(1f, 1f, 1f, 0f), t);
            }

            transform.localScale = Vector3.Lerp(startScale, endScale, Mathf.Sin(t * Mathf.PI * 0.5f));
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator MissCoroutine()
    {
        float duration = 0.3f;
        float timer = 0f;
        Vector3 startScale = transform.localScale;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            if (noteMaterial != null)
            {
                Color c = Color.Lerp(Color.black, new Color(0.2f, 0.2f, 0.2f, 0f), t);
                noteMaterial.color = c;
            }

            transform.localScale = Vector3.Lerp(startScale, startScale * 0.5f, t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void SetAlpha(float alpha)
    {
        if (noteMaterial == null) return;

        Color c = Color.black;
        c.a = alpha;
        noteMaterial.color = c;

        if (rend != null && note.isVisible)
        {
            rend.enabled = alpha > 0.01f;
        }
    }
}
