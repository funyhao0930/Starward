namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环「斯卡布罗集市」的卡池。
/// <para/>
/// 异环没有抽卡接口，记录只能从 nte-exporter 导出的文件读入，
/// 因此这里的编号是 Starward 自己定的，与游戏无关，但一旦定下就不能再改：
/// 它会写进数据库的 GachaType 列。文件里对应的是字符串 pool id，
/// 两者的映射见 <see cref="FromPoolId"/>。
/// </summary>
public readonly record struct HottaGachaType(int Value) : IGachaType
{


    /// <summary>
    /// 常驻棋盘 Standard Board
    /// </summary>
    public const int StandardBoard = 1;

    /// <summary>
    /// 限定角色棋盘 Limited Character Board
    /// </summary>
    public const int LimitedCharacterBoard = 2;

    /// <summary>
    /// 弧盘奇迹盒 Arc Miracle Box，异环的「武器」池
    /// </summary>
    public const int ArcMiracleBox = 3;

    /// <summary>
    /// 神秘盒子 Mystery Box，按轮次开放的单抽池
    /// </summary>
    public const int MysteryBox = 4;


    /// <summary>
    /// nte-exporter 文件里的 pool id
    /// </summary>
    public const string PoolIdStandardBoard = "Lottery_Permanent";
    public const string PoolIdLimitedCharacterBoard = "Lottery_LimitedCharacter";
    public const string PoolIdArcMiracleBox = "Arc_MiracleBox";
    public const string PoolIdMysteryBox = "Gashapon_MysteryBox";


    /// <summary>
    /// 把导出文件里的 pool id 换成本地编号，认不出来时返回 0。
    /// <para/>
    /// 导出文件里每条记录都自带 pool_group_id，顶层的 banner 只是取第一条记录推断出来的，
    /// 记录为空时还会谎报成常驻池，因此只认记录自己的那个字段。
    /// </summary>
    public static HottaGachaType FromPoolId(string? poolId) => poolId switch
    {
        PoolIdStandardBoard => StandardBoard,
        PoolIdLimitedCharacterBoard => LimitedCharacterBoard,
        PoolIdArcMiracleBox => ArcMiracleBox,
        PoolIdMysteryBox => MysteryBox,
        _ => new HottaGachaType(0),
    };


    /// <summary>
    /// 这一行算不算一抽。
    /// <para/>
    /// 斯卡布罗集市是掷骰子走棋盘：掷一次是一抽（<c>dice</c>），
    /// 落格与积分给的奖励（<c>points_gift</c>、<c>chase_reward</c>）
    /// 与那次投掷共用时间戳，只是奖励。弧盘奇迹盒与神秘盒子每行都是一抽。
    /// <para/>
    /// 用真实记录验证过：只数掷骰时，两次 S 角色之间最多 78 抽，不超过 90 抽的保底；
    /// 把奖励行也算进去就会冒出 94 抽这种不可能的间隔。
    /// </summary>
    public static bool IsPull(int gachaType, string? resultType)
    {
        if (gachaType is StandardBoard or LimitedCharacterBoard)
        {
            return string.IsNullOrEmpty(resultType) || resultType is "dice";
        }
        return true;
    }


    /// <summary>
    /// 这一行的稀有度算不算数。
    /// <para/>
    /// 异环给每种奖励都标了稀有度，棋盘上掉的「S 级骰子道具」跟抽到 S 角色是两回事，
    /// 所以棋盘卡池只认角色（<paramref name="rewardType"/> 为 <c>character</c>）。
    /// 弧盘奇迹盒每行都是弧盘，神秘盒子认它给的东西，都照单全收。
    /// <para/>
    /// 顺带一提，角色几乎都不是掷骰子掉出来的：实测限定棋盘 42 个角色里
    /// 39 个来自 <c>points_gift</c>，所以「只留 dice」会把 S 角色全丢光。
    /// </summary>
    public static bool IsRankSubject(int gachaType, string? rewardType)
    {
        if (gachaType is StandardBoard or LimitedCharacterBoard)
        {
            return rewardType is "character";
        }
        return true;
    }


    public string ToLocalization() => Value switch
    {
        StandardBoard => CoreLang.GachaType_StandardBoard,
        LimitedCharacterBoard => CoreLang.GachaType_LimitedCharacterBoard,
        ArcMiracleBox => CoreLang.GachaType_ArcMiracleBox,
        MysteryBox => CoreLang.GachaType_MysteryBox,
        _ => "",
    };


    public override string ToString() => Value.ToString();
    public static implicit operator HottaGachaType(int value) => new(value);
    public static implicit operator int(HottaGachaType gachaType) => gachaType.Value;


}
