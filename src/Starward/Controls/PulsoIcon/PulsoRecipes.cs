using System.Collections.Generic;
using System.Numerics;

namespace Starward.Controls;


/// <summary>
/// PulsoIcon 的动作名称，对应 pulsoicon-core 的 recipes.ts
/// </summary>
public enum PulsoRecipe
{
    Draw, DrawBack, DrawOut, Redraw, Pop, Press, Spin, Turn, Ring, Beat, Pulse, Slide, Shoot, Bounce, Shake,
    Tilt, Swing, Lift, Blink, Flip, Open, Fold, Fade, Twinkle, Float, Grow, Drop, Rise, Stamp, Jelly, Roll,
    Sway, Flicker, Wobble, GrowX,
}


/// <summary>
/// 一个形状上的一个动作：延迟 D 秒、方向 (X, Y)、幅度 A、支点 (OX, OY)，坐标都在 24×24 的格子里
/// </summary>
internal sealed record PulsoMotion(PulsoRecipe Recipe, float D, float X, float Y, float A, float OX, float OY);


/// <summary>
/// 一个形状：路径操作码（0 M x y、1 L x y、2 C x1 y1 x2 y2 x y、3 Z）、是否填色、是否闭合（PulsoIcon.IsFilled 只填满闭合的形状）、动作
/// </summary>
internal sealed record PulsoNode(float[] Ops, bool Filled, bool Closed, PulsoMotion? Motion);


/// <summary>
/// 一个图标：整个图标的动作和各个形状
/// </summary>
internal sealed record PulsoIconDefinition(PulsoMotion? Svg, PulsoNode[] Nodes);


/// <summary>
/// 动作会改的值
/// </summary>
internal enum PulsoChannel { X, Y, Rotate, Scale, ScaleX, ScaleY, Opacity, PathLength, PathOffset }


/// <summary>
/// 一条关键帧轨道，时间单位是秒，Ease 是三次贝塞尔的两个控制点
/// </summary>
internal readonly record struct PulsoTrack(PulsoChannel Channel, float[] Values, float Duration, float Delay, Vector4 Ease, float[]? Times = null);


/// <summary>
/// 把 recipes.ts 的每个动作翻成关键帧轨道。数值和时长照搬原版，只有 open 的 skewY 没做（Composition 形状没有斜切）。
/// </summary>
internal static class PulsoRecipes
{

    public static readonly Vector4 EaseInOut = new(0.42f, 0, 0.58f, 1);

    public static readonly Vector4 EaseOut = new(0, 0, 0.58f, 1);


    public static List<PulsoTrack> Build(PulsoMotion m)
    {
        float d = m.D, x = m.X, y = m.Y;
        // 原版写的是 p.a || 默认值，0 也当成没给
        float A(float fallback) => m.A != 0 ? m.A : fallback;
        var list = new List<PulsoTrack>(3);
        void Add(PulsoChannel channel, float duration, Vector4? ease = null, float[]? times = null, params float[] values)
            => list.Add(new PulsoTrack(channel, values, duration, d, ease ?? EaseInOut, times));
        void DrawOn(float duration)
        {
            Add(PulsoChannel.PathLength, duration, values: [0, 1]);
            Add(PulsoChannel.Opacity, 0.1f, values: [0, 1]);
        }

        switch (m.Recipe)
        {
            case PulsoRecipe.Draw:
                DrawOn(0.55f);
                break;
            case PulsoRecipe.DrawBack:
                DrawOn(0.55f);
                Add(PulsoChannel.PathOffset, 0.55f, values: [1, 0]);
                break;
            case PulsoRecipe.DrawOut:
                DrawOn(0.55f);
                Add(PulsoChannel.PathOffset, 0.55f, values: [0.5f, 0]);
                break;
            case PulsoRecipe.Redraw:
                Add(PulsoChannel.PathLength, 0.7f, values: [1, 0, 1]);
                Add(PulsoChannel.PathOffset, 0.7f, values: [0, 1, 0]);
                break;
            case PulsoRecipe.Pop:
                Add(PulsoChannel.Scale, 0.45f, values: [0, 1 + A(0.25f), 1]);
                Add(PulsoChannel.Opacity, 0.45f, values: [0, 1, 1]);
                break;
            case PulsoRecipe.Press:
                Add(PulsoChannel.Scale, 0.4f, values: [1, 1 - A(0.15f), 1.05f, 1]);
                break;
            case PulsoRecipe.Spin:
                Add(PulsoChannel.Rotate, 0.8f, values: [0, A(360)]);
                break;
            case PulsoRecipe.Turn:
                Add(PulsoChannel.Rotate, 0.7f, values: [0, A(90), 0]);
                break;
            case PulsoRecipe.Ring:
                {
                    float a = A(14);
                    Add(PulsoChannel.Rotate, 0.7f, values: [0, -a, a * 0.85f, -a * 0.55f, a * 0.3f, 0]);
                    break;
                }
            case PulsoRecipe.Beat:
                {
                    float a = A(0.16f);
                    Add(PulsoChannel.Scale, 0.65f, values: [1, 1 + a, 1 - a * 0.3f, 1 + a * 0.6f, 1]);
                    break;
                }
            case PulsoRecipe.Pulse:
                Add(PulsoChannel.Scale, 0.6f, values: [1, 1 + A(0.08f), 1]);
                break;
            case PulsoRecipe.Slide:
                {
                    float a = A(2.5f);
                    Add(PulsoChannel.X, 0.5f, values: [0, x * a, 0]);
                    Add(PulsoChannel.Y, 0.5f, values: [0, y * a, 0]);
                    break;
                }
            case PulsoRecipe.Shoot:
                {
                    float a = A(7);
                    float[] times = [0, 0.4f, 0.41f, 1];
                    Add(PulsoChannel.X, 0.6f, times: times, values: [0, x * a, -x * a, 0]);
                    Add(PulsoChannel.Y, 0.6f, times: times, values: [0, y * a, -y * a, 0]);
                    Add(PulsoChannel.Opacity, 0.6f, times: times, values: [1, 0, 0, 1]);
                    break;
                }
            case PulsoRecipe.Bounce:
                {
                    float a = A(3);
                    Add(PulsoChannel.X, 0.6f, values: [0, x * a, 0, x * a * 0.4f, 0]);
                    Add(PulsoChannel.Y, 0.6f, values: [0, y * a, 0, y * a * 0.4f, 0]);
                    break;
                }
            case PulsoRecipe.Shake:
                {
                    float a = A(1.5f);
                    Add(PulsoChannel.X, 0.5f, values: [0, -a, a, -a * 0.6f, a * 0.6f, 0]);
                    break;
                }
            case PulsoRecipe.Tilt:
                {
                    float a = A(12);
                    Add(PulsoChannel.Rotate, 0.7f, values: [0, -a, a * 0.6f, -a * 0.3f, 0]);
                    break;
                }
            case PulsoRecipe.Swing:
                {
                    float a = A(10);
                    Add(PulsoChannel.Rotate, 0.8f, values: [0, a, -a * 0.75f, a * 0.4f, 0]);
                    break;
                }
            case PulsoRecipe.Lift:
                Add(PulsoChannel.X, 0.55f, values: [0, x * 2.5f, 0]);
                Add(PulsoChannel.Y, 0.55f, values: [0, (y != 0 ? y : -1) * 2.5f, 0]);
                Add(PulsoChannel.Rotate, 0.55f, values: [0, m.A, 0]);
                break;
            case PulsoRecipe.Blink:
                Add(PulsoChannel.ScaleY, 0.35f, values: [1, 0.1f, 1]);
                break;
            case PulsoRecipe.Flip:
                Add(PulsoChannel.ScaleX, 0.6f, values: [1, -1, 1]);
                break;
            case PulsoRecipe.Open:
                Add(PulsoChannel.ScaleX, 0.6f, values: [1, 1 - A(0.35f), 1]);
                break;
            case PulsoRecipe.Fold:
                Add(PulsoChannel.ScaleY, 0.6f, values: [1, -1, 1]);
                break;
            case PulsoRecipe.Fade:
                Add(PulsoChannel.Opacity, 0.5f, values: [1, 0.15f, 1]);
                break;
            case PulsoRecipe.Twinkle:
                {
                    float a = A(18);
                    Add(PulsoChannel.Rotate, 0.7f, values: [0, -a, a, 0]);
                    Add(PulsoChannel.Scale, 0.7f, values: [1, 0.85f, 1.1f, 1]);
                    break;
                }
            case PulsoRecipe.Float:
                Add(PulsoChannel.Y, 0.6f, values: [0, -A(1.5f), 0]);
                break;
            case PulsoRecipe.Grow:
                Add(PulsoChannel.ScaleY, 0.45f, values: [0, 1]);
                break;
            case PulsoRecipe.Drop:
                {
                    float a = A(6);
                    Vector4 ease = new(0.34f, 1.56f, 0.64f, 1);
                    Add(PulsoChannel.X, 0.45f, ease, values: [x * a, 0]);
                    Add(PulsoChannel.Y, 0.45f, ease, values: [(y != 0 ? y : -1) * a, 0]);
                    Add(PulsoChannel.Opacity, 0.45f, ease, values: [0, 1]);
                    break;
                }
            case PulsoRecipe.Rise:
                {
                    Vector4 ease = new(0.22f, 1, 0.36f, 1);
                    Add(PulsoChannel.Y, 0.5f, ease, values: [A(3), 0]);
                    Add(PulsoChannel.Opacity, 0.5f, ease, values: [0, 1]);
                    break;
                }
            case PulsoRecipe.Stamp:
                Add(PulsoChannel.Scale, 0.45f, values: [1 + A(0.6f), 0.92f, 1]);
                Add(PulsoChannel.Opacity, 0.45f, values: [0, 1, 1]);
                break;
            case PulsoRecipe.Jelly:
                {
                    float a = A(0.18f);
                    Add(PulsoChannel.ScaleX, 0.7f, values: [1, 1 + a, 1 - a * 0.6f, 1 + a * 0.3f, 1]);
                    Add(PulsoChannel.ScaleY, 0.7f, values: [1, 1 - a, 1 + a * 0.6f, 1 - a * 0.3f, 1]);
                    break;
                }
            case PulsoRecipe.Roll:
                {
                    float a = A(3);
                    float dir = x != 0 ? x : 1;
                    Add(PulsoChannel.X, 0.8f, values: [0, dir * a, 0]);
                    Add(PulsoChannel.Rotate, 0.8f, values: [0, dir * 180, 360 * dir]);
                    break;
                }
            case PulsoRecipe.Sway:
                {
                    float a = A(6);
                    Add(PulsoChannel.Rotate, 1, values: [0, a, -a, a * 0.5f, 0]);
                    break;
                }
            case PulsoRecipe.Flicker:
                Add(PulsoChannel.Opacity, 0.6f, times: [0, 0.2f, 0.4f, 0.6f, 1], values: [1, 0.3f, 1, 0.5f, 1]);
                break;
            case PulsoRecipe.Wobble:
                {
                    float a = A(8);
                    Add(PulsoChannel.Rotate, 0.6f, values: [0, -a, a, 0]);
                    Add(PulsoChannel.Scale, 0.6f, values: [1, 1.08f, 1.08f, 1]);
                    break;
                }
            case PulsoRecipe.GrowX:
                Add(PulsoChannel.ScaleX, 0.45f, values: [0, 1]);
                break;
        }
        return list;
    }

}
