namespace Starward.Core.Gacha;

/// <summary>
/// 为没有服务器端记录 ID 的游戏合成稳定的 <see cref="GachaLogItem.Id"/> 与
/// <see cref="GachaLogItem.ItemId"/>。
/// <para/>
/// 米哈游的每条抽卡记录都带一个单调递增的 id，去重与增量获取都靠它。
/// 鸣潮完全没有这个字段，终末地的物品 ID 是字符串，因此需要在本地合成，
/// 且必须满足两点：
/// <list type="number">
/// <item>同一条记录每次重算都得到相同的值，否则重复获取会插入重复记录；</item>
/// <item>按 ID 升序排列等于按时间升序排列，因为界面与保底计算都依赖这个顺序。</item>
/// </list>
/// </summary>
public static class GachaSyntheticId
{


    /// <summary>
    /// 每秒可容纳的记录数，十连也只占十个
    /// </summary>
    private const long SequencePerSecond = 100;

    /// <summary>
    /// 同一秒内用于区分不同记录的散列空间
    /// </summary>
    private const long HashSpace = 10000;


    /// <summary>
    /// 用时间与记录内容合成一个既稳定又按时间递增的 ID。
    /// </summary>
    /// <param name="time">记录时间</param>
    /// <param name="sequenceInSecond">同一秒内按时间升序的序号，从 0 开始</param>
    /// <param name="content">同一秒内用于区分记录的内容，例如物品 ID 与名称</param>
    public static long FromTime(DateTime time, int sequenceInSecond, string content)
    {
        long seconds = new DateTimeOffset(time.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();
        long sequence = Math.Clamp(sequenceInSecond, 0, SequencePerSecond - 1);
        return ((seconds * SequencePerSecond) + sequence) * HashSpace + (Fnv1a(content) % HashSpace);
    }


    /// <summary>
    /// 把字符串形式的物品 ID 变成非负整数。
    /// 能直接解析成整数的就用原值，这样与游戏内的 ID 一致；
    /// 否则散列，只要求同一物品得到同一个值（界面按此分组统计）。
    /// </summary>
    public static int ToItemId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return 0;
        }
        if (int.TryParse(id, out int value) && value >= 0)
        {
            return value;
        }
        return (int)(Fnv1a(id) & 0x7FFFFFFF);
    }


    /// <summary>
    /// FNV-1a，选它是因为实现短、无依赖，且结果在不同版本间不会变。
    /// 这里只用来去重与分组，不用于任何安全用途。
    /// </summary>
    public static long Fnv1a(string content)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in content)
        {
            hash ^= c;
            hash *= 1099511628211;
        }
        return (long)(hash & 0x7FFFFFFFFFFFFFFF);
    }


}
