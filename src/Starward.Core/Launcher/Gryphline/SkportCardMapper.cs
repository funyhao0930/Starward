namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 资料卡里几个需要换算的数值。不依赖任何 IO，值得单独测。
/// </summary>
public static class SkportCardMapper
{

    /// <summary>
    /// 毫秒级的 Unix 时间戳至少有 13 位；秒级的要到 33658 年才会长到这么大
    /// </summary>
    private const long MillisecondsThreshold = 100_000_000_000;


    /// <summary>
    /// 理智回满的时刻，已经满了或给不出时返回 null。
    /// <para/>
    /// <c>maxTs</c> 的单位没有公开说明：SKPort 其余时间字段都是秒，但不排除这一个是毫秒，
    /// 所以按数量级判断，两种都认。已经满了的时候接口给的可能是 0、也可能是过去的某一刻，
    /// 一律当成没有倒计时。
    /// </summary>
    public static DateTimeOffset? ToFullTime(SkportCardDungeon? dungeon, DateTimeOffset now)
    {
        if (dungeon is null || dungeon.MaxStamina <= 0 || dungeon.CurStamina >= dungeon.MaxStamina || dungeon.MaxTs <= 0)
        {
            return null;
        }
        DateTimeOffset time = dungeon.MaxTs >= MillisecondsThreshold
            ? DateTimeOffset.FromUnixTimeMilliseconds(dungeon.MaxTs)
            : DateTimeOffset.FromUnixTimeSeconds(dungeon.MaxTs);
        return time > now ? time : null;
    }

}
