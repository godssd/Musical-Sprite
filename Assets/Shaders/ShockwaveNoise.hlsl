// 冲击波墙 / 拖尾 共用的程序化噪声（URP 17 / Unity 6）。
// 用法：在 HLSLPROGRAM 里 #include "ShockwaveNoise.hlsl"
//
// ★为什么不用噪声贴图：先用程序化 value noise 跑通效果，避免临时素材进版本库；
//   等自绘/购买的噪声贴图就位后，只需把 FlowNoise 内部换成一次 SAMPLE_TEXTURE2D，
//   对外接口 (p, time, speed, warp, detail, stretchX, stretchY, swirl, scrollDirX, scrollDirY) 保持不变，调用方一行都不用改。
//
// 设计目标：做出「分形杂色 → 漩涡 → 上下拉很长 → 沿指定方向移动」的能量流动/溶解感。
//   - stretchX/Y：各向异性缩放采样坐标。拖尾用 stretchY 大 ⇒ 长条上下方向，流动沿 +X（从左到右）。
//   - swirl：低频噪声驱动的旋转位移场，形成 AE 里 Turbulent Noise 的漩涡扭曲。
//   - scrollDirX/Y：★【图案看起来的移动方向】。(0,-1)=向下，(1,0)=向右。墙面默认 (1,0)=从中缝往己方半场；拖尾默认 (1,0)=从贴墙端往远端。
//
// 成本：FlowNoise 在 detail>0 时约 4~6 次 VNoise（每次 4 次 hash）。
//      移动端若吃紧，把 detail 设 0（省一次）、warp 设 0（省两次）、swirl 设 0（省一次）。
#ifndef MUSICAL_SPRITE_SHOCKWAVE_NOISE_INCLUDED
#define MUSICAL_SPRITE_SHOCKWAVE_NOISE_INCLUDED

float ShockHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ShockVNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = ShockHash21(i);
    float b = ShockHash21(i + float2(1.0, 0.0));
    float c = ShockHash21(i + float2(0.0, 1.0));
    float d = ShockHash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

/// <summary>
/// 分形杂色 + 漩涡扭曲 + 各向异性拉伸 + 沿 scrollDir 方向滚动。
/// 用于墙面流体 / 拖尾流体 / 溶解噪声。
///
/// ★方向约定（2026-10-12 修正，之前是反的）：
///     scrollDir = 图案【看起来】往哪边移动。(0,-1)=向下，(1,0)=向右，(-1,0)=向左。
///     旧实现是 uv = p*stretch 之后【加】scrollDir*time，导致两件事同时出错：
///       ① 视觉方向与参数相反（填 (0,-1) 实际往上跑）→ 反复改不对方向；
///       ② 滚动量落在拉伸之后的坐标里，真实速度被 stretch 除掉（stretchY=4 ⇒ 只有 1/4 速度）→ "看不出在动"。
///     现在改成：先在 p 空间滚动（速度与 stretch 无关），再拉伸（只决定花纹形状）。
/// </summary>
float ShockFlowNoise(float2 p, float time, float speed, float warp, float detail,
                     float stretchX, float stretchY, float swirl,
                     float scrollDirX, float scrollDirY)
{
    // 1) 先在 p 空间滚动：★滚动量与 stretch 解耦，speed 就是眼睛看到的真实速度
    //    图案出现在 p = q0/stretch + scrollDir*time*speed ⇒ 视觉移动方向 = scrollDir
    float2 pp = p - float2(scrollDirX, scrollDirY) * time * speed;

    // 2) 各向异性拉伸：只决定花纹【形状】（拉成长条），不影响移动速度
    float2 flow = pp * float2(stretchX, stretchY);

    // 3) 漩涡扭曲：低频噪声驱动的旋转位移场，把条纹卷成涡状。
    //    ★不再去旋转"滚动向量"本身（旧实现把随时间增长的滚动量按随机角度旋转，方向感被彻底抹平）
    if (abs(swirl) > 0.001)
    {
        float low = ShockVNoise(flow * 0.35 + float2(0.0, time * speed * 0.1));
        float ang = (low * 2.0 - 1.0) * 6.283185;
        flow += float2(cos(ang), sin(ang)) * (abs(swirl) * 0.7);
    }

    // 4) domain-warp：低频噪声扭曲高频采样坐标 ⇒ 翻滚细节
    if (warp > 0.001)
    {
        float2 w = float2(ShockVNoise(flow * 0.6), ShockVNoise(flow * 0.6 + float2(3.7, 1.3)));
        flow += (w - 0.5) * 2.0 * warp;
    }

    float n = ShockVNoise(flow);
    if (detail > 0.001)
        n = lerp(n, n * 0.7 + ShockVNoise(flow * 2.1 + float2(7.3, 2.7) - time * speed) * 0.3, detail);
    return saturate(n);
}

/// <summary>溶解带权重：t 越小（越靠判定线/尾部）越接近 1。</summary>
float ShockDissolveBand(float t, float pointC, float width)
{
    return 1.0 - smoothstep(max(0.0, pointC - width), saturate(pointC + width), t);
}

/// <summary>
/// 溶解：用分形杂色噪声作为侵蚀图，柔和地衰减 alpha，并在侵蚀边缘产生淡淡的发光。
/// 输出：keep = 保留部分（0=完全烧掉），glow = 边缘发光强度（用于叠加能量色）。
/// edge 越小发光带越窄（0.001 接近硬边），越大越柔和。
/// </summary>
void ShockDissolve(float2 p, float band, float amount, float edge, float scale,
                   float speed, float time, float stretchX, float stretchY, float swirl,
                   float scrollDirX, float scrollDirY,
                   out float keep, out float glow)
{
    // 溶解噪声：与流体同源，但可独立控制拉伸/漩涡/滚动方向
    float n = ShockFlowNoise(p * scale, time, speed, 0.0, 0.0, stretchX, stretchY, swirl, scrollDirX, scrollDirY);

    // 侵蚀强度：越靠近尾部、整体 amount 越大、噪声越亮 ⇒ 烧蚀越多
    float erode = band * amount * n;

    // alpha 保留：从 1 柔和地衰减到 0，不再用硬阈值切出斑块
    keep = saturate(1.0 - erode);

    // 边缘发光：只在侵蚀带中间（erode ≈ e~2.5e）产生一圈淡淡的能量边
    float e = max(0.001, edge);
    glow = smoothstep(0.0, e, erode) * (1.0 - smoothstep(e, e * 2.5, erode)) * band;
    glow *= 0.5; // 默认亮度压低，避免白色斑块过曝
}

#endif
